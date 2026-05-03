// =============================================================================
// Cena Platform — BFF Session Refresh Endpoint (prr-011 Phase 1B)
//
// Rotates the httpOnly session cookie in place. Splits out of
// SessionExchangeEndpoint to keep that file under its 500-LOC budget
// (CLAUDE.md project rule).
//
// Contract:
//   POST /api/auth/session/refresh
//   Request: carries __Host-cena_session cookie (or legacy cena_session
//            during Phase 1A→1B rollout window).
//   Success: 204 + Set-Cookie replacing the session JWT with a freshly-minted
//            one whose jti is different from the presented one. The old jti
//            is on the revocation list until its natural expiry; the new jti
//            is recorded as the successor.
//   Failure: 401 if the cookie is missing / invalid / revoked / mid-race.
//
// Rotation-race semantics (ADR-0046 §4, threat-model T4/T5):
//   If the presented cookie's jti is already on the revocation list AND the
//   SessionRevocationList.GetSuccessor(jti) returns a non-null successor, the
//   refresh is rejected with CENA_AUTH_SESSION_RACE and the successor is
//   ALSO revoked — both cookies are now suspect (either an attacker is
//   replaying the pre-rotation cookie, or a double-refresh victim has raced
//   themselves). The user is forced back to Firebase for re-auth.
//
//   In normal double-refresh scenarios (slow network, SPA debouncing bug)
//   the SessionJwt:RotationGraceSeconds configuration allows a narrow
//   window where a just-rotated jti is still accepted once. Defaults to 0 —
//   exact rotation. Ops can widen temporarily during an incident without a
//   redeploy.
// =============================================================================

using System.IdentityModel.Tokens.Jwt;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Auth;

public static class SessionRefreshEndpoint
{
    public sealed record RefreshErrorResponse(string Error, string Code);

    public static void MapRefresh(RouteGroupBuilder group)
    {
        group.MapPost("/refresh", Refresh)
            .WithName("PostSessionRefresh")
            .WithSummary("Rotate the session cookie in place.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<RefreshErrorResponse>(StatusCodes.Status401Unauthorized);
    }

    internal static IResult Refresh(
        HttpContext ctx,
        [FromServices] IConfiguration configuration,
        [FromServices] SessionRevocationList revocationList,
        [FromServices] ILogger<SessionExchangeEndpoint.SessionExchangeLoggerMarker> logger)
    {
        // ── 1. Read the cookie (new name, then legacy). ──
        if (!(ctx.Request.Cookies.TryGetValue(SessionExchangeEndpoint.CookieName, out var currentJwt)
              || ctx.Request.Cookies.TryGetValue(SessionExchangeEndpoint.LegacyCookieName, out currentJwt))
            || string.IsNullOrWhiteSpace(currentJwt))
        {
            return TypedResults.Unauthorized();
        }

        // ── 2. Validate signature + expiry. ──
        var parameters = SessionExchangeEndpoint.BuildValidationParameters(configuration);
        JwtSecurityToken parsed;
        string userId;
        string? email;
        string currentJti;
        DateTimeOffset currentExp;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(currentJwt, parameters, out var validated);
            parsed = (JwtSecurityToken)validated;
            currentJti = parsed.Id;
            currentExp = parsed.ValidTo == DateTime.MinValue
                ? DateTimeOffset.UtcNow.Add(SessionExchangeEndpoint.GetSessionLifetimeInternal(configuration))
                : new DateTimeOffset(parsed.ValidTo, TimeSpan.Zero);
            userId = parsed.Subject
                     ?? parsed.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                     ?? parsed.Claims.FirstOrDefault(c => c.Type == "user_id")?.Value
                     ?? string.Empty;
            email = parsed.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[SESSION_REFRESH] cookie validation failed");
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(currentJti) || string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        // ── 3. Rotation-race detection. ──
        var now = DateTimeOffset.UtcNow;
        if (revocationList.IsRevoked(currentJti, now))
        {
            var successor = revocationList.GetSuccessor(currentJti);
            if (successor is not null)
            {
                // Race: old cookie is replayed AFTER we already rotated to a
                // successor. Revoke the successor too — it is now suspect.
                // Force re-auth.
                revocationList.Add(successor, currentExp);
                logger.LogWarning(
                    "[SESSION_ROTATION_RACE] uid={UidPrefix}... oldJti={OldPrefix}... "
                    + "successor={SuccessorPrefix}... — revoking successor and forcing re-auth",
                    userId.Length > 8 ? userId[..8] : userId,
                    currentJti.Length > 8 ? currentJti[..8] : currentJti,
                    successor.Length > 8 ? successor[..8] : successor);
                return TypedResults.Json(
                    new RefreshErrorResponse(
                        "Session rotation race detected — re-authenticate.",
                        "CENA_AUTH_SESSION_RACE"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            // Revoked but no successor recorded — a plain "this jti is dead"
            // (normal logout). 401, no race signal.
            return TypedResults.Unauthorized();
        }

        // ── 4. Mint the successor JWT. ──
        var lifetime = SessionExchangeEndpoint.GetSessionLifetimeInternal(configuration);
        var newIssuedAt = now;
        var newExpiresAt = now.Add(lifetime);
        var newJti = SessionExchangeEndpoint.GenerateJtiInternal();
        var newJwt = SessionExchangeEndpoint.MintSessionJwt(
            configuration, userId, email, newJti, newIssuedAt, newExpiresAt);

        // Atomic rotation: record old→new mapping + mark old revoked. We
        // revoke using the old exp (not the new one) because the old cookie
        // is only dangerous until it would have expired anyway.
        revocationList.RecordRotation(currentJti, newJti, currentExp);

        // ── 5. Emit the replacement cookie. ──
        SessionExchangeEndpoint.AppendSessionCookie(ctx, newJwt, newExpiresAt, lifetime);

        // If the presented cookie was legacy-named, clear the legacy name so
        // the client stops sending both.
        if (ctx.Request.Cookies.ContainsKey(SessionExchangeEndpoint.LegacyCookieName)
            && !ctx.Request.Cookies.ContainsKey(SessionExchangeEndpoint.CookieName))
        {
            ctx.Response.Cookies.Delete(SessionExchangeEndpoint.LegacyCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
            });
        }

        logger.LogInformation(
            "[SESSION_REFRESH] uid={UidPrefix}... oldJti={OldPrefix}... → newJti={NewPrefix}... exp={ExpiresAt}",
            userId.Length > 8 ? userId[..8] : userId,
            currentJti.Length > 8 ? currentJti[..8] : currentJti,
            newJti.Length > 8 ? newJti[..8] : newJti,
            newExpiresAt);

        return TypedResults.NoContent();
    }
}
