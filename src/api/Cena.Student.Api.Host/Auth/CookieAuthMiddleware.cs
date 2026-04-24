// =============================================================================
// Cena Platform — Cookie Auth Middleware (prr-011)
//
// Reads the httpOnly `__Host-cena_session` cookie (or legacy `cena_session`
// during the Phase 1A→1B rollout window), validates the server-minted JWT
// signature + lifetime + revocation, and — if all three pass — populates
// HttpContext.User with the subject claim.
//
// WHY: cookie runs AFTER UseAuthentication() (Firebase bearer) during the
// transition window. If a request arrives with BOTH a Firebase bearer AND
// a cookie, the bearer-scheme principal takes precedence and this
// middleware becomes a no-op. This is correct for the Phase 1B backend:
// once the Vue SPA migration (sibling task prr-011f) lands, the bearer
// path shrinks to just the exchange endpoint, and this middleware becomes
// the sole authoritative source. See ADR-0046 §3.
//
// This middleware short-circuits on:
//   • Missing cookie  → no user attached, pipeline continues so
//     AllowAnonymous endpoints still work.
//   • Invalid cookie  → user stripped, 401 on RequireAuthorization endpoints.
//   • Revoked jti     → user stripped, 401 on RequireAuthorization endpoints.
//
// It does NOT write directly to the response. The endpoint's authorization
// policy decides what a missing/invalid identity means (401 vs anonymous),
// keeping concerns separated.
// =============================================================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Auth;

/// <summary>
/// Request-scoped validator for the <c>cena_session</c> cookie. Not registered
/// as ASP.NET authentication scheme on purpose — we want cookie auth to be an
/// additive pass that runs AFTER the Firebase JwtBearer default scheme so the
/// transition window supports both. Once the client is fully cookie-only we
/// can switch to a full AddAuthentication scheme.
/// </summary>
public sealed class CookieAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly SessionRevocationList _revocationList;
    private readonly ILogger<CookieAuthMiddleware> _logger;

    public CookieAuthMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        SessionRevocationList revocationList,
        ILogger<CookieAuthMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _revocationList = revocationList;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // If JwtBearer already attached a principal (transition window: client
        // still sending Firebase Bearer on a legacy endpoint), don't overwrite
        // it. Cookie is additive, not authoritative.
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // WHY: try the new __Host- name first, then fall back to the legacy
        // name for in-flight Phase 1A cookies during the rollout window.
        // Once the rollout window closes, the legacy read is removed by
        // prr-011i. Do NOT reverse the order — we want new-name clients to
        // skip the legacy lookup entirely.
        if (!(context.Request.Cookies.TryGetValue(SessionExchangeEndpoint.CookieName, out var sessionJwt)
              || context.Request.Cookies.TryGetValue(SessionExchangeEndpoint.LegacyCookieName, out sessionJwt))
            || string.IsNullOrWhiteSpace(sessionJwt))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        try
        {
            var parameters = SessionExchangeEndpoint.BuildValidationParameters(_configuration);
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(sessionJwt, parameters, out var validatedToken);
            var jwt = (JwtSecurityToken)validatedToken;

            // Revocation check — even a perfectly-signed token is rejected if
            // the server has marked it revoked (logout, forced sign-out).
            if (!string.IsNullOrWhiteSpace(jwt.Id)
                && _revocationList.IsRevoked(jwt.Id, DateTimeOffset.UtcNow))
            {
                _logger.LogInformation(
                    "[COOKIE_AUTH] rejected revoked session jti={JtiPrefix}...",
                    jwt.Id.Length > 8 ? jwt.Id[..8] : jwt.Id);
                await _next(context).ConfigureAwait(false);
                return;
            }

            // Set the principal onto the request. "Cookie" identity type is
            // chosen so downstream middleware that inspects
            // User.Identity.AuthenticationType can distinguish cookie auth
            // from JwtBearer when both are in play.
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(principal.Claims, "Cookie"));
        }
        catch (Exception ex)
        {
            // Swallow validation failures — the endpoint's RequireAuthorization
            // will 401 when the user is still unauthenticated. Log at Debug so
            // an attacker spraying garbage cookies doesn't flood the warn log.
            _logger.LogDebug(ex, "[COOKIE_AUTH] cookie validation failed");
        }

        await _next(context).ConfigureAwait(false);
    }
}
