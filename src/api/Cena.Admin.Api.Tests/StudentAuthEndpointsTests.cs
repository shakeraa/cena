// =============================================================================
// FIND-ux-006b — Student host AuthEndpoints tests (password-reset)
//
// Covers the POST /api/auth/password-reset handler on Cena.Student.Api.Host:
//   1. Wiring — MapAuthEndpoints registers the expected anonymous route.
//   2. Handler — uniform 204 for LinkGenerated + UserNotFound (OWASP), 503
//      on Firebase outage, 400 for malformed input.
//   3. Rate-limiter policy name is applied to the anonymous group.
//
// Hosted inside Cena.Admin.Api.Tests so the existing backend CI workflow
// picks them up without needing a new test csproj. The Student host exposes
// AuthEndpoints as internal and allows Cena.Admin.Api.Tests via
// InternalsVisibleTo.
// =============================================================================

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Firebase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests;

public class StudentAuthEndpointsTests
{
    // ---- Test double ------------------------------------------------------

    private sealed class FakeFirebaseAdminService : IFirebaseAdminService
    {
        public PasswordResetOutcome NextOutcome { get; set; } = PasswordResetOutcome.LinkGenerated;
        public int CallCount { get; private set; }
        public string? LastEmail { get; private set; }

        public Task<PasswordResetOutcome> GeneratePasswordResetLinkAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastEmail = email;
            return Task.FromResult(NextOutcome);
        }

        // Remaining surface is not exercised by AuthEndpoints; stubs throw so
        // an accidental call fails the test loudly instead of silently.
        public Task<string> CreateUserAsync(string email, string fullName, string? password)
            => throw new System.NotSupportedException();
        public Task UpdateEmailAsync(string uid, string newEmail)
            => throw new System.NotSupportedException();
        public Task SetCustomClaimsAsync(string uid, System.Collections.Generic.Dictionary<string, object> claims)
            => throw new System.NotSupportedException();
        public Task DisableUserAsync(string uid) => throw new System.NotSupportedException();
        public Task EnableUserAsync(string uid) => throw new System.NotSupportedException();
        public Task DeleteUserAsync(string uid) => throw new System.NotSupportedException();
        public Task<string> GenerateSignInLinkAsync(string email) => throw new System.NotSupportedException();
    }

    private static ILogger<AuthEndpoints.AuthLoggerMarker> NullLogger() =>
        NullLogger<AuthEndpoints.AuthLoggerMarker>.Instance;

    private static DefaultHttpContext CreateHttpContext()
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        return ctx;
    }

    // ---- Handler behavior (OWASP uniform 204) -----------------------------

    [Fact]
    public async Task PasswordReset_ValidEmail_Returns204()
    {
        var firebase = new FakeFirebaseAdminService
        {
            NextOutcome = PasswordResetOutcome.LinkGenerated,
        };

        var result = await AuthEndpoints.PasswordReset(
            new AuthEndpoints.PasswordResetRequest("student@example.com"),
            firebase,
            NullLogger(),
            CreateHttpContext(),
            CancellationToken.None);

        Assert.IsType<NoContent>(result);
        Assert.Equal(1, firebase.CallCount);
        Assert.Equal("student@example.com", firebase.LastEmail);
    }

    [Fact]
    public async Task PasswordReset_UnknownEmail_Returns204_ToPreventEnumeration()
    {
        // OWASP Authentication Cheat Sheet — "Forgot password / password
        // reset": return a consistent message regardless of whether the
        // account exists. If this test ever flips to 404, the endpoint is
        // leaking account existence to unauthenticated callers.
        var firebase = new FakeFirebaseAdminService
        {
            NextOutcome = PasswordResetOutcome.UserNotFound,
        };

        var result = await AuthEndpoints.PasswordReset(
            new AuthEndpoints.PasswordResetRequest("ghost@example.com"),
            firebase,
            NullLogger(),
            CreateHttpContext(),
            CancellationToken.None);

        Assert.IsType<NoContent>(result);
        Assert.Equal(1, firebase.CallCount);
    }

    [Fact]
    public async Task PasswordReset_FirebaseUnavailable_Returns503()
    {
        var firebase = new FakeFirebaseAdminService
        {
            NextOutcome = PasswordResetOutcome.FirebaseUnavailable,
        };

        var result = await AuthEndpoints.PasswordReset(
            new AuthEndpoints.PasswordResetRequest("student@example.com"),
            firebase,
            NullLogger(),
            CreateHttpContext(),
            CancellationToken.None);

        var statusResult = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
    }

    // ---- Input validation -------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@missing-local")]
    [InlineData("missing-domain@")]
    public async Task PasswordReset_MalformedEmail_Returns400_WithoutCallingFirebase(string email)
    {
        var firebase = new FakeFirebaseAdminService();

        var result = await AuthEndpoints.PasswordReset(
            new AuthEndpoints.PasswordResetRequest(email),
            firebase,
            NullLogger(),
            CreateHttpContext(),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<AuthEndpoints.AuthErrorResponse>>(result);
        Assert.NotNull(badRequest.Value);
        Assert.False(string.IsNullOrWhiteSpace(badRequest.Value!.Error));
        Assert.Equal(0, firebase.CallCount);
    }

    // ---- Route wiring -----------------------------------------------------

    [Fact]
    public void MapAuthEndpoints_RegistersPasswordResetRoute_Anonymous_AndRateLimited()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        // AuthEndpoints.PasswordReset has [FromServices] parameters; we need
        // a dummy registration so RequestDelegateFactory can build the
        // delegate during endpoint enumeration.
        builder.Services.AddSingleton<IFirebaseAdminService>(new FakeFirebaseAdminService());

        var app = builder.Build();
        app.MapAuthEndpoints();

        var routeBuilder = (IEndpointRouteBuilder)app;
        var endpoints = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is not null &&
                        e.RoutePattern.RawText.StartsWith("/api/auth", System.StringComparison.Ordinal))
            .ToList();

        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/auth/password-reset");

        var passwordReset = endpoints.Single(e => e.RoutePattern.RawText == "/api/auth/password-reset");

        // Must be anonymous — students using the forgot-password form are
        // by definition unauthenticated.
        var allowAnonymous = passwordReset.Metadata.GetMetadata<IAllowAnonymous>();
        Assert.NotNull(allowAnonymous);

        // Must be rate-limited under the dedicated policy so the generic
        // "api" bucket isn't drained by unauthed abuse.
        var rateLimiter = passwordReset.Metadata
            .OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            .FirstOrDefault();
        Assert.NotNull(rateLimiter);
        Assert.Equal("password-reset", rateLimiter!.PolicyName);
    }
}
