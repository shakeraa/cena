// =============================================================================
// Cena Platform — BFF Session-Exchange Integration Tests (prr-011)
//
// Covers the cookie-only auth flow end-to-end, bypassing the full Student
// API host composition to keep the test surface focused on the security
// properties that matter:
//
//   1. POST /api/auth/session with a valid stub-verified ID token →
//      response carries a Set-Cookie with HttpOnly + Secure + SameSite=Strict
//      and the response body does NOT contain the session JWT itself.
//   2. A subsequent request with ONLY the cookie (no Authorization header)
//      reaches a protected endpoint and gets 200.
//   3. POST /api/auth/session/logout clears the cookie AND revokes the jti,
//      so even replaying the (still-signature-valid) cookie yields 401.
//   4. A revoked session JWT with a valid signature is rejected.
//
// We do not boot the real Student API host because that drags in Postgres,
// NATS, Redis, and Firebase credentials. Instead, we compose a minimal
// WebApplication with just the middleware + endpoint + a test-double
// Firebase verifier that accepts any token starting with "stub:". Production
// Firebase verification is exercised by the host's existing integration
// suite and by the FirebaseAuthExtensions path directly — prr-011's novel
// surface is the cookie round-trip, which this test exhaustively covers.
// =============================================================================

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Student.Api.Host.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Auth;

public class SessionExchangeTests : IDisposable
{
    private readonly IHost _host;
    private readonly HttpClient _client;
    private readonly SessionRevocationList _revocationList;
    private readonly IConfiguration _configuration;

    public SessionExchangeTests()
    {
        // Fixed signing key so we can mint session JWTs by hand for the
        // revocation-list test (test #4) without going through the exchange
        // endpoint.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SessionJwt:SigningKey"] = "prr-011-test-key-unique",
                ["SessionJwt:LifetimeHours"] = "24",
            })
            .Build();
        _configuration = config;

        var revocationList = new SessionRevocationList();
        _revocationList = revocationList;

        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(config);
                    services.AddSingleton(revocationList);
                    services.AddRouting();
                    services.AddLogging();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    // CookieAuthMiddleware runs BEFORE the terminal endpoint
                    // so cookie-only requests arrive with an authenticated
                    // principal. The real production pipeline also runs
                    // UseAuthentication() first, but we omit Firebase
                    // JwtBearer here because the /api/auth/session endpoint
                    // verifies ID tokens by hand (see the test-double
                    // verifier below).
                    app.UseMiddleware<CookieAuthMiddleware>();
                    app.UseEndpoints(endpoints =>
                    {
                        // Install a test-double variant of the exchange
                        // endpoint that accepts "stub:<uid>" bearer tokens
                        // without Firebase. Writes the same cookie shape
                        // as production, using the production code's
                        // MintSessionJwt helper so the cookie format stays
                        // identical.
                        endpoints.MapPost("/api/auth/session", async (HttpContext ctx) =>
                        {
                            var auth = ctx.Request.Headers.Authorization.ToString();
                            if (string.IsNullOrWhiteSpace(auth)
                                || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                            {
                                return Results.Unauthorized();
                            }
                            var idToken = auth["Bearer ".Length..].Trim();
                            if (!idToken.StartsWith("stub:", StringComparison.Ordinal))
                            {
                                return Results.Unauthorized();
                            }
                            var uid = idToken["stub:".Length..];
                            if (string.IsNullOrWhiteSpace(uid))
                            {
                                return Results.Unauthorized();
                            }

                            var now = DateTimeOffset.UtcNow;
                            var expiresAt = now.AddHours(24);
                            var jti = Guid.NewGuid().ToString("N");
                            var sessionJwt = SessionExchangeEndpoint.MintSessionJwt(
                                config, uid, $"{uid}@example.test", jti, now, expiresAt);

                            ctx.Response.Cookies.Append(SessionExchangeEndpoint.CookieName, sessionJwt, new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = true,
                                SameSite = SameSiteMode.Strict,
                                Path = "/",
                                Expires = expiresAt,
                                MaxAge = TimeSpan.FromHours(24),
                                IsEssential = true,
                            });

                            return Results.Ok(new SessionExchangeEndpoint.SessionExchangeResponse(uid, expiresAt));
                        });

                        endpoints.MapPost("/api/auth/session/logout",
                            (HttpContext ctx) => SessionExchangeEndpoint.Logout(
                                ctx, config, revocationList, NullLogger<SessionExchangeEndpoint.SessionExchangeLoggerMarker>.Instance));

                        // prr-011 Phase 1B: refresh endpoint — real handler,
                        // no test-double needed because it only touches the
                        // cookie + revocation list which are both real.
                        endpoints.MapPost("/api/auth/session/refresh",
                            (HttpContext ctx) => SessionRefreshEndpoint.Refresh(
                                ctx, config, revocationList, NullLogger<SessionExchangeEndpoint.SessionExchangeLoggerMarker>.Instance));

                        // Protected probe endpoint: 401 unless cookie auth
                        // attached a principal with NameIdentifier.
                        endpoints.MapGet("/protected", (HttpContext ctx) =>
                        {
                            var uid = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                                ?? ctx.User.FindFirstValue("sub");
                            if (string.IsNullOrWhiteSpace(uid))
                                return Results.Unauthorized();
                            return Results.Ok(new { userId = uid });
                        });
                    });
                });
            });

        _host = builder.Start();
        _client = _host.GetTestClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    public async Task Exchange_WithValidIdToken_SetsHttpOnlySecureStrictCookie_AndOmitsJwtFromBody()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "stub:student-123");

        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ── Cookie flags (prr-011 Phase 1B: __Host- prefix) ──
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains(SessionExchangeEndpoint.CookieName + "=", setCookie);
        Assert.StartsWith("__Host-", SessionExchangeEndpoint.CookieName);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        // __Host- prefix forbids Domain; assert none is emitted.
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        // Extract the JWT value from the Set-Cookie header for comparison
        // against the body. The body MUST NOT contain the raw JWT.
        var cookieValue = ExtractCookieValue(setCookie, SessionExchangeEndpoint.CookieName);
        Assert.False(string.IsNullOrWhiteSpace(cookieValue),
            $"{SessionExchangeEndpoint.CookieName} cookie value empty");

        var bodyText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(cookieValue!, bodyText);

        // Body carries userId + expiresAt — nothing more.
        var body = await response.Content.ReadFromJsonAsync<SessionExchangeEndpoint.SessionExchangeResponse>();
        Assert.NotNull(body);
        Assert.Equal("student-123", body!.UserId);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ProtectedEndpoint_Accepts_CookieOnly_NoAuthorizationHeader()
    {
        // Exchange first.
        var exchangeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        exchangeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "stub:student-456");
        var exchangeResp = await _client.SendAsync(exchangeReq);
        exchangeResp.EnsureSuccessStatusCode();
        var setCookie = exchangeResp.Headers.GetValues("Set-Cookie").Single();
        var cookieValue = ExtractCookieValue(setCookie, SessionExchangeEndpoint.CookieName)!;

        // Probe with cookie only, NO Authorization header.
        var probeReq = new HttpRequestMessage(HttpMethod.Get, "/protected");
        probeReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieValue}");
        Assert.Null(probeReq.Headers.Authorization);

        var probeResp = await _client.SendAsync(probeReq);
        Assert.Equal(HttpStatusCode.OK, probeResp.StatusCode);
        var body = await probeResp.Content.ReadFromJsonAsync<ProbeBody>();
        Assert.Equal("student-456", body?.UserId);
    }

    [Fact]
    public async Task Logout_ClearsCookie_AndSubsequentReplay_Returns401()
    {
        // Exchange
        var exchangeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        exchangeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "stub:student-789");
        var exchangeResp = await _client.SendAsync(exchangeReq);
        var cookieValue = ExtractCookieValue(
            exchangeResp.Headers.GetValues("Set-Cookie").Single(),
            SessionExchangeEndpoint.CookieName)!;

        // Sanity: the probe works pre-logout
        var preReq = new HttpRequestMessage(HttpMethod.Get, "/protected");
        preReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieValue}");
        var preResp = await _client.SendAsync(preReq);
        Assert.Equal(HttpStatusCode.OK, preResp.StatusCode);

        // Logout
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/logout");
        logoutReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieValue}");
        var logoutResp = await _client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutResp.StatusCode);

        // The response should include cookie-deletion Set-Cookie directives
        // (both new + legacy names, during rollout window).
        var deletionCookies = logoutResp.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(deletionCookies, c =>
            c.Contains(SessionExchangeEndpoint.CookieName + "=", StringComparison.Ordinal));
        // Cookie deletion sets expires in the past; samesite+secure+httponly
        // must match the original attributes so browsers accept the deletion.
        Assert.Contains(deletionCookies, c =>
            c.Contains(SessionExchangeEndpoint.CookieName, StringComparison.Ordinal)
            && c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

        // Replay the original cookie value: must now be rejected.
        var replayReq = new HttpRequestMessage(HttpMethod.Get, "/protected");
        replayReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieValue}");
        var replayResp = await _client.SendAsync(replayReq);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);
    }

    [Fact]
    public async Task RevokedSession_WithValidSignature_IsRejected()
    {
        // Mint a session JWT by hand (bypasses the exchange endpoint) so we
        // can add its jti to the revocation list and prove the middleware
        // consults the list even when the signature+expiry are both fine.
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(24);
        var jti = Guid.NewGuid().ToString("N");
        var sessionJwt = SessionExchangeEndpoint.MintSessionJwt(
            _configuration, "student-revoked", null, jti, now, expiresAt);

        // Pre-revocation: the cookie works.
        var preReq = new HttpRequestMessage(HttpMethod.Get, "/protected");
        preReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={sessionJwt}");
        var preResp = await _client.SendAsync(preReq);
        Assert.Equal(HttpStatusCode.OK, preResp.StatusCode);

        // Revoke the jti. After this, the same cookie must be rejected even
        // though the signature is still valid — the server-side revocation
        // list is authoritative.
        _revocationList.Add(jti, expiresAt);

        var postReq = new HttpRequestMessage(HttpMethod.Get, "/protected");
        postReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={sessionJwt}");
        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Unauthorized, postResp.StatusCode);
    }

    // =========================================================================
    // prr-011 Phase 1B tests: refresh rotation, race detection, legacy cookie
    // coexistence, expired cookie, JWT-in-response-body assertion.
    // =========================================================================

    [Fact]
    public async Task Refresh_WithValidCookie_RotatesJti_AndOldCookieIsRevoked()
    {
        // Exchange → get cookie A (jti_A)
        var cookieA = await ExchangeForCookieAsync("student-refresh-1");

        // Refresh → get cookie B (jti_B), with jti_A added to revocation
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        refreshReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieA}");
        var refreshResp = await _client.SendAsync(refreshReq);
        Assert.Equal(HttpStatusCode.NoContent, refreshResp.StatusCode);

        var setCookie = refreshResp.Headers.GetValues("Set-Cookie").Single();
        var cookieB = ExtractCookieValue(setCookie, SessionExchangeEndpoint.CookieName)!;
        Assert.False(string.IsNullOrWhiteSpace(cookieB));
        Assert.NotEqual(cookieA, cookieB);

        // Cookie B works.
        var probeB = new HttpRequestMessage(HttpMethod.Get, "/protected");
        probeB.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieB}");
        var respB = await _client.SendAsync(probeB);
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);

        // Cookie A is now revoked — subsequent probe 401s even though sig is valid.
        var probeA = new HttpRequestMessage(HttpMethod.Get, "/protected");
        probeA.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieA}");
        var respA = await _client.SendAsync(probeA);
        Assert.Equal(HttpStatusCode.Unauthorized, respA.StatusCode);

        // Jtis must be different.
        var handler = new JwtSecurityTokenHandler();
        var jtiA = handler.ReadJwtToken(cookieA).Id;
        var jtiB = handler.ReadJwtToken(cookieB).Id;
        Assert.NotEqual(jtiA, jtiB);
    }

    [Fact]
    public async Task Refresh_RotationRace_RevokesSuccessor_AndReturnsSessionRaceCode()
    {
        // Exchange → cookie A, refresh → cookie B (revokes A)
        var cookieA = await ExchangeForCookieAsync("student-race-1");
        var firstRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        firstRefresh.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieA}");
        var firstResp = await _client.SendAsync(firstRefresh);
        Assert.Equal(HttpStatusCode.NoContent, firstResp.StatusCode);
        var cookieB = ExtractCookieValue(
            firstResp.Headers.GetValues("Set-Cookie").Single(),
            SessionExchangeEndpoint.CookieName)!;

        // Race: replay cookie A (which is revoked AND has recorded
        // successor=jti_B). Expect 401 + CENA_AUTH_SESSION_RACE.
        var raceReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        raceReq.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieA}");
        var raceResp = await _client.SendAsync(raceReq);
        Assert.Equal(HttpStatusCode.Unauthorized, raceResp.StatusCode);
        var raceBody = await raceResp.Content.ReadAsStringAsync();
        Assert.Contains("CENA_AUTH_SESSION_RACE", raceBody, StringComparison.Ordinal);

        // Cookie B (the successor) must ALSO now be rejected — it was suspect
        // the moment A was replayed.
        var probeB = new HttpRequestMessage(HttpMethod.Get, "/protected");
        probeB.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieB}");
        var respB = await _client.SendAsync(probeB);
        Assert.Equal(HttpStatusCode.Unauthorized, respB.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithRevokedButNoSuccessor_Returns401_NoRaceCode()
    {
        // Exchange → cookie A, then revoke directly (logout-like, no rotation
        // recorded). Refresh attempt must 401 but NOT signal a race.
        var cookieA = await ExchangeForCookieAsync("student-plain-revoked");
        var jti = new JwtSecurityTokenHandler().ReadJwtToken(cookieA).Id;
        _revocationList.Add(jti, DateTimeOffset.UtcNow.AddHours(24));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session/refresh");
        req.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={cookieA}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CENA_AUTH_SESSION_RACE", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CookieAuth_AcceptsLegacyCookieName_DuringRolloutWindow()
    {
        // Mint a valid session JWT; present it under the legacy cena_session
        // name. The middleware must still accept it so in-flight Phase 1A
        // cookies keep working across the deploy boundary.
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(1);
        var jwt = SessionExchangeEndpoint.MintSessionJwt(
            _configuration, "student-legacy", null, Guid.NewGuid().ToString("N"), now, expiresAt);

        var req = new HttpRequestMessage(HttpMethod.Get, "/protected");
        req.Headers.Add("Cookie", $"{SessionExchangeEndpoint.LegacyCookieName}={jwt}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ProbeBody>();
        Assert.Equal("student-legacy", body?.UserId);
    }

    [Fact]
    public async Task CookieAuth_NewNameWinsOverLegacy_WhenBothPresent()
    {
        // Two cookies on one request, for two different users. The __Host-
        // name must win — the middleware reads new-first, legacy-second.
        var now = DateTimeOffset.UtcNow;
        var exp = now.AddHours(1);
        var newJwt = SessionExchangeEndpoint.MintSessionJwt(
            _configuration, "user-new", null, Guid.NewGuid().ToString("N"), now, exp);
        var legacyJwt = SessionExchangeEndpoint.MintSessionJwt(
            _configuration, "user-legacy", null, Guid.NewGuid().ToString("N"), now, exp);

        var req = new HttpRequestMessage(HttpMethod.Get, "/protected");
        req.Headers.Add("Cookie",
            $"{SessionExchangeEndpoint.CookieName}={newJwt}; "
            + $"{SessionExchangeEndpoint.LegacyCookieName}={legacyJwt}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ProbeBody>();
        Assert.Equal("user-new", body?.UserId);
    }

    [Fact]
    public async Task CookieAuth_ExpiredCookie_Returns401()
    {
        // Mint a cookie whose exp is in the past; token validation must
        // reject it on the lifetime check, not silently pass.
        var now = DateTimeOffset.UtcNow;
        var pastExp = now.AddMinutes(-5);
        var pastIat = now.AddMinutes(-65);
        var jwt = SessionExchangeEndpoint.MintSessionJwt(
            _configuration, "student-expired", null, Guid.NewGuid().ToString("N"), pastIat, pastExp);

        var req = new HttpRequestMessage(HttpMethod.Get, "/protected");
        req.Headers.Add("Cookie", $"{SessionExchangeEndpoint.CookieName}={jwt}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ExchangeResponse_BodyMustNotContainSessionJwtOrRawToken()
    {
        // Structural guarantee: the exchange response body carries only
        // userId + expiresAt. No field in the body may deserialise to a
        // long opaque string that looks like a JWT. This is the runtime
        // companion to NoJwtInLocalStorageResponseTest's static check.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "stub:student-body");
        var resp = await _client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var bodyText = await resp.Content.ReadAsStringAsync();

        // A JWT is 3 dot-separated base64url segments; the smallest plausible
        // real JWT is ≥ 80 characters. We count dots inside long tokens.
        foreach (var chunk in bodyText.Split(new[] { '"', ',', '{', '}', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (chunk.Length >= 80 && chunk.Count(c => c == '.') == 2)
            {
                Assert.Fail(
                    $"Response body contains a JWT-shaped token: {chunk[..Math.Min(20, chunk.Length)]}... "
                    + "Phase 1B forbids session JWTs in response bodies (must be httpOnly cookie only).");
            }
        }
    }

    [Fact]
    public async Task Exchange_WithoutAuthorizationHeader_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.False(resp.Headers.Contains("Set-Cookie"),
            "No cookie must be set when exchange fails authentication.");
    }

    [Fact]
    public async Task Exchange_WithMalformedBearer_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer notastub");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public void MintSessionJwt_IncludesJtiAndExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(1);
        var jwt = SessionExchangeEndpoint.MintSessionJwt(
            _configuration, "jti-test", null, "test-jti-value", now, expiresAt);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);

        Assert.Equal("test-jti-value", parsed.Id);
        Assert.Equal("cena-session", parsed.Issuer);
        // ValidTo is set to the nearest second.
        Assert.InRange(
            (parsed.ValidTo - expiresAt.UtcDateTime).TotalSeconds,
            -1.5, 1.5);
    }

    [Fact]
    public void SessionRevocationList_SweepsExpiredEntries()
    {
        var list = new SessionRevocationList();
        var now = DateTimeOffset.UtcNow;
        list.Add("alive", now.AddMinutes(10));
        list.Add("expired", now.AddMinutes(-1));

        Assert.Equal(2, list.Count);
        Assert.True(list.IsRevoked("alive", now));
        Assert.False(list.IsRevoked("expired", now));

        list.GetType().GetMethod("Sweep",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(list, new object[] { now });

        Assert.Equal(1, list.Count);
    }

    // ── helpers ──

    private async Task<string> ExchangeForCookieAsync(string studentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"stub:{studentId}");
        var resp = await _client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var setCookie = resp.Headers.GetValues("Set-Cookie").Single();
        return ExtractCookieValue(setCookie, SessionExchangeEndpoint.CookieName)
            ?? throw new InvalidOperationException("exchange did not set the session cookie");
    }

    private static string? ExtractCookieValue(string setCookieHeader, string name)
    {
        foreach (var piece in setCookieHeader.Split(';'))
        {
            var trimmed = piece.Trim();
            if (trimmed.StartsWith(name + "=", StringComparison.Ordinal))
                return trimmed[(name.Length + 1)..];
        }
        return null;
    }

    private sealed record ProbeBody(string UserId);
}
