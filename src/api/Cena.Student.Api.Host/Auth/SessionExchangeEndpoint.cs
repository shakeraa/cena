// =============================================================================
// Cena Platform — BFF Session-Exchange Endpoint (prr-011)
//
// Trades a short-lived Firebase ID token (presented ONCE, via Authorization:
// Bearer) for an httpOnly+Secure+SameSite=Strict cookie that carries a server-
// minted session JWT. Subsequent API traffic authenticates via the cookie
// only — the Firebase ID token stays in memory on the client and is never
// persisted or re-transmitted. This is the only endpoint in the platform that
// is allowed to read the Authorization header directly; all other endpoints
// rely on the cookie-auth middleware populating HttpContext.User.
//
// Why BFF instead of trying to make Firebase set the cookie?
//   Firebase Auth does not natively issue httpOnly cookies. We need server-
//   minted JWTs so we can (a) ship an httpOnly cookie and (b) maintain our
//   own revocation list (SessionRevocationList) keyed on jti. This trades
//   one more "moving part" for closing the entire XSS session-theft class.
//
// Why HS256 and not RS256?
//   Simplicity for pilot. A single host, a single key, derived from
//   SessionJwt:SigningKey configuration. The cookie never leaves our
//   origin so verifying signatures with anything other than the mint-side
//   key is unnecessary. RS256 + JWKS rotation is a follow-up for multi-
//   host production.
//
// Firebase ID token verification:
//   Uses Firebase Admin SDK FirebaseAuth.DefaultInstance.VerifyIdTokenAsync
//   when the SDK is initialized (production path). In the Firebase Auth
//   Emulator path (local dev), the SDK reads FIREBASE_AUTH_EMULATOR_HOST
//   automatically and accepts unsigned tokens the emulator mints — no
//   additional code here needed.
// =============================================================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Auth;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Cena.Student.Api.Host.Auth;

public static class SessionExchangeEndpoint
{
    /// <summary>
    /// Name of the httpOnly cookie that carries the server-minted session JWT.
    /// Referenced by <see cref="CookieAuthMiddleware"/> and by the logout
    /// clear-cookie response.
    ///
    /// prr-011 Phase 1B: upgraded from "cena_session" to "__Host-cena_session".
    /// The __Host- prefix is browser-enforced — cookies with this prefix MUST
    /// have Secure, MUST have Path=/, and MUST NOT carry Domain. This kills
    /// the subdomain-cookie-shadowing class of attack (threat model T2,
    /// ADR-0046 §1) at the browser layer rather than the application layer.
    /// </summary>
    public const string CookieName = "__Host-cena_session";

    /// <summary>
    /// Legacy cookie name from Phase 1A. <see cref="CookieAuthMiddleware"/>
    /// still reads this name so users with an in-flight Phase 1A cookie are
    /// not force-logged-out on deploy; on their next refresh or re-login we
    /// issue the __Host- cookie and clear the legacy one. Safe to remove
    /// after <see cref="DefaultSessionLifetime"/> + a safety margin past the
    /// deploy. Remove by prr-011i (cleanup task).
    /// </summary>
    public const string LegacyCookieName = "cena_session";

    /// <summary>
    /// Default session lifetime. Matches the cookie Max-Age, the JWT exp claim,
    /// and the revocation-list TTL. 24 hours balances convenience (one cookie
    /// per school-day) against exposure if a host is compromised.
    /// </summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(24);

    // Marker type for the endpoint-scoped logger so the static handler methods
    // can declare ILogger<T> without pulling in the outer Program class.
    internal sealed class SessionExchangeLoggerMarker { }

    public sealed record SessionExchangeResponse(
        string UserId,
        DateTimeOffset ExpiresAt);

    public sealed record SessionExchangeError(string Error);

    public static IEndpointRouteBuilder MapSessionExchangeEndpoints(this IEndpointRouteBuilder app)
    {
        // NOTE: AllowAnonymous on the exchange endpoint itself — the Firebase
        // ID token is carried in Authorization: Bearer and verified by hand
        // inside the handler, bypassing the default JwtBearer middleware so
        // cookie-auth can remain the default for every other endpoint.
        var group = app.MapGroup("/api/auth/session")
            .WithTags("Auth")
            .AllowAnonymous();

        group.MapPost("", Exchange)
            .WithName("PostSessionExchange")
            .WithSummary("Exchange a Firebase ID token for an httpOnly session cookie.")
            .Produces<SessionExchangeResponse>(StatusCodes.Status200OK)
            .Produces<SessionExchangeError>(StatusCodes.Status400BadRequest)
            .Produces<SessionExchangeError>(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", Logout)
            .WithName("PostSessionLogout")
            .WithSummary("Clear the session cookie and revoke the server session JWT.")
            .Produces(StatusCodes.Status204NoContent);

        // prr-011 Phase 1B: refresh endpoint lives in SessionRefreshEndpoint.cs
        // so this file stays under its 500 LOC budget. Wires into the same
        // /api/auth/session group for URL consistency.
        SessionRefreshEndpoint.MapRefresh(group);

        return app;
    }

    /// <summary>
    /// POST /api/auth/session
    /// Authorization: Bearer &lt;Firebase ID token&gt;
    /// →
    /// Set-Cookie: cena_session=&lt;server JWT&gt;; HttpOnly; Secure; SameSite=Strict; ...
    /// Body: { userId, expiresAt }  (session JWT itself is NEVER in the body)
    /// </summary>
    internal static async Task<IResult> Exchange(
        HttpContext ctx,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        [Microsoft.AspNetCore.Mvc.FromServices] ILogger<SessionExchangeLoggerMarker> logger,
        CancellationToken cancellationToken)
    {
        // ── 1. Pull the Firebase ID token from the Authorization header. ──
        // This is the ONLY endpoint that is allowed to read the header
        // directly — see NoBearerTokenEndpointsTest for the arch enforcement.
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader)
            || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Unauthorized();
        }

        var idToken = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return TypedResults.Unauthorized();
        }

        // ── 2. Verify the Firebase ID token. ──
        // Firebase Admin SDK handles both real Google-signed tokens and
        // emulator-issued tokens (via FIREBASE_AUTH_EMULATOR_HOST).
        string userId;
        string? email;
        try
        {
            var firebaseToken = await VerifyFirebaseIdTokenAsync(idToken, configuration, logger, cancellationToken)
                .ConfigureAwait(false);
            if (firebaseToken is null)
            {
                return TypedResults.Unauthorized();
            }

            userId = firebaseToken.Uid;
            firebaseToken.Claims.TryGetValue("email", out var emailClaim);
            email = emailClaim as string;
        }
        catch (FirebaseAuthException ex)
        {
            logger.LogWarning(
                "[SESSION_EXCHANGE] Firebase verification failed: {Code}",
                ex.AuthErrorCode);
            return TypedResults.Unauthorized();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SESSION_EXCHANGE] unexpected verification failure");
            return TypedResults.Unauthorized();
        }

        // ── 3. Mint the server session JWT. ──
        var lifetime = GetSessionLifetime(configuration);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(lifetime);
        var jti = GenerateJti();
        var sessionJwt = MintSessionJwt(
            configuration, userId, email, jti, now, expiresAt);

        // ── 4. Set the __Host- httpOnly + Secure + SameSite=Strict cookie. ──
        // Secure is ALWAYS true — in local dev the student dev server runs
        // over http on localhost, which browsers treat as secure for cookie
        // purposes. Do not relax this flag.
        //
        // __Host- prefix: browser rejects cookies with this name unless
        // Secure=true, Path="/", and Domain is absent. We set Domain=null
        // explicitly to make the contract obvious at the call site, even
        // though CookieOptions.Domain defaults to null.
        //
        // If an in-flight Phase 1A cookie exists on the client, clear it so
        // the migrated client does not carry both names forever.
        ctx.Response.Cookies.Append(CookieName, sessionJwt, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Domain = null, // __Host- forbids Domain
            Expires = expiresAt,
            MaxAge = lifetime,
            IsEssential = true,
        });
        if (ctx.Request.Cookies.ContainsKey(LegacyCookieName))
        {
            ctx.Response.Cookies.Delete(LegacyCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
            });
        }

        logger.LogInformation(
            "[SESSION_EXCHANGE] minted session for {UserId} expiring {ExpiresAt}",
            userId, expiresAt);

        // ── 5. Return userId + expiresAt. Session JWT stays in the cookie. ──
        return TypedResults.Ok(new SessionExchangeResponse(userId, expiresAt));
    }

    /// <summary>
    /// POST /api/auth/session/logout
    /// Clears the cookie and marks the session JWT's jti as revoked.
    /// Idempotent — invoking without a cookie still returns 204.
    /// </summary>
    internal static IResult Logout(
        HttpContext ctx,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        [Microsoft.AspNetCore.Mvc.FromServices] SessionRevocationList revocationList,
        [Microsoft.AspNetCore.Mvc.FromServices] ILogger<SessionExchangeLoggerMarker> logger)
    {
        // If the request carried a valid cookie (new or legacy name), extract
        // its jti + exp so we can add the session to the revocation list. We
        // cannot simply trust the client-side "delete this cookie" because an
        // attacker with the cookie value can ignore our clear-cookie header
        // and keep replaying.
        if ((ctx.Request.Cookies.TryGetValue(CookieName, out var sessionJwt)
                || ctx.Request.Cookies.TryGetValue(LegacyCookieName, out sessionJwt))
            && !string.IsNullOrWhiteSpace(sessionJwt))
        {
            try
            {
                var validationParameters = BuildValidationParameters(configuration);
                var handler = new JwtSecurityTokenHandler();
                handler.ValidateToken(sessionJwt, validationParameters, out var validatedToken);

                if (validatedToken is JwtSecurityToken jwt)
                {
                    var jti = jwt.Id;
                    var exp = jwt.ValidTo == DateTime.MinValue
                        ? DateTimeOffset.UtcNow.Add(GetSessionLifetime(configuration))
                        : new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);

                    if (!string.IsNullOrWhiteSpace(jti))
                    {
                        revocationList.Add(jti, exp);
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't fail logout on a bad cookie — the whole point of
                // /logout is to wipe state. Just log and continue.
                logger.LogDebug(ex, "[SESSION_LOGOUT] cookie validation failed during logout");
            }
        }

        // Tell the browser to drop both cookie names (new + legacy) regardless
        // of what the server state was. Browsers only honour cookie deletion
        // with matching Path / Secure / SameSite attributes, so mirror the
        // exchange handler. Deleting legacy is cheap insurance during the
        // rollout window.
        ctx.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
        ctx.Response.Cookies.Delete(LegacyCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });

        return TypedResults.NoContent();
    }

    // =========================================================================
    // Helpers — Firebase verification, JWT mint/validate, configuration
    // =========================================================================

    /// <summary>
    /// Verify a Firebase ID token using the Admin SDK. Returns null when the
    /// SDK is not initialized (dev-without-credentials path) AND the
    /// configuration has SessionJwt:AllowFirebaseStubInDev=true, enabling a
    /// token-shape-only fallback so local dev without Firebase creds still
    /// exercises the cookie pipeline. Production MUST have the SDK
    /// initialized or this method returns null (→ 401).
    /// </summary>
    private static async Task<FirebaseToken?> VerifyFirebaseIdTokenAsync(
        string idToken,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (FirebaseApp.DefaultInstance is not null)
        {
            return await FirebaseAuth.DefaultInstance
                .VerifyIdTokenAsync(idToken, cancellationToken)
                .ConfigureAwait(false);
        }

        logger.LogError(
            "[SESSION_EXCHANGE] Firebase Admin SDK not initialized — cannot verify ID token. " +
            "Configure Firebase:ServiceAccountKeyPath or run with FIREBASE_AUTH_EMULATOR_HOST set.");
        return null;
    }

    /// <summary>
    /// Mint the HS256-signed server session JWT. Claims:
    ///   sub: Firebase uid (used by ClaimTypes.NameIdentifier downstream)
    ///   email: optional
    ///   jti: unique id for revocation keying
    ///   iss: "cena-session"
    ///   iat / nbf / exp
    /// </summary>
    internal static string MintSessionJwt(
        IConfiguration configuration,
        string userId,
        string? email,
        string jti,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var signingKey = GetSigningKey(configuration);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new("user_id", userId),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
            claims.Add(new Claim("email", email));
        }

        var token = new JwtSecurityToken(
            issuer: "cena-session",
            audience: "cena-session",
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Validation parameters used both for logout-time jti extraction and for
    /// the CookieAuthMiddleware request-time validation. Centralized here so
    /// signing key + issuer + clock skew stay in lock-step.
    /// </summary>
    public static TokenValidationParameters BuildValidationParameters(IConfiguration configuration)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "cena-session",
            ValidateAudience = true,
            ValidAudience = "cena-session",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetSigningKey(configuration),
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
        };
    }

    internal static SymmetricSecurityKey GetSigningKey(IConfiguration configuration)
    {
        var keyMaterial = configuration["SessionJwt:SigningKey"]
            ?? Environment.GetEnvironmentVariable("CENA_SESSION_JWT_SIGNING_KEY");
        if (string.IsNullOrWhiteSpace(keyMaterial))
        {
            // Dev-only fallback so the host boots without extra configuration.
            // The key is derived from a fixed constant XOR'd with the machine
            // name so multiple concurrent dev hosts on the same box share a
            // key (both test runners see the same session JWTs). Production
            // MUST set SessionJwt:SigningKey — enforcement is deferred to a
            // configuration-validation task.
            keyMaterial = "cena-dev-session-jwt-key-" + Environment.MachineName;
        }

        // Pad the HS256 key to at least 32 bytes (256 bits) per RFC 7518 §3.2.
        // SHA-256 the input deterministically to get exactly 32 bytes every
        // time without depending on the length of the config value.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        return new SymmetricSecurityKey(bytes);
    }

    internal static TimeSpan GetSessionLifetimeInternal(IConfiguration configuration)
        => GetSessionLifetime(configuration);

    private static TimeSpan GetSessionLifetime(IConfiguration configuration)
    {
        var hours = configuration.GetValue<int?>("SessionJwt:LifetimeHours");
        return hours is > 0 ? TimeSpan.FromHours(hours.Value) : DefaultSessionLifetime;
    }

    internal static string GenerateJtiInternal() => GenerateJti();

    private static string GenerateJti()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexString(buffer);
    }

    /// <summary>
    /// Emit the __Host- session cookie using the exact attributes locked in
    /// ADR-0046 §1. Centralised so the exchange and refresh endpoints speak
    /// the same cookie language — a mismatch would be a deletion-won't-honour
    /// bug.
    /// </summary>
    internal static void AppendSessionCookie(
        HttpContext ctx, string sessionJwt, DateTimeOffset expiresAt, TimeSpan lifetime)
    {
        ctx.Response.Cookies.Append(CookieName, sessionJwt, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Domain = null, // __Host- forbids Domain
            Expires = expiresAt,
            MaxAge = lifetime,
            IsEssential = true,
        });
    }
}
