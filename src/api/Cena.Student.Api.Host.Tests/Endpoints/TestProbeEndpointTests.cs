// =============================================================================
// Cena Platform — PRR-436 TestProbeEndpoint unit tests
//
// Pure-unit tests for the boundary behaviour that does NOT need a live Marten:
//
//   * Token gate: env unset, header missing, header wrong -> 404
//   * Token gate: header right -> proceeds to handler
//   * Input validation: missing type / tenantId / id -> 400
//   * Unknown kind -> 400
//
// Marten-backed coverage (snapshot lookup, stream replay, tenant scoping)
// runs in the e2e-flow Playwright suite which exercises the full stack —
// it's the integration contract this code unblocks.
// =============================================================================

using System.Security.Claims;
using Cena.Student.Api.Host.Tests.Endpoints.Support;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Cena.Api.Host.Endpoints.TestProbeEndpoint;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class TestProbeEndpointTests : IDisposable
{
    private readonly string? _originalToken;

    public TestProbeEndpointTests()
    {
        _originalToken = Environment.GetEnvironmentVariable(TokenEnvVar);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, _originalToken);
    }

    [Fact]
    public async Task TokenEnvUnset_Returns404()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, null);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "anything";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: "studentProfile", tenantId: "t_x", id: "uid-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsAssignableFrom<NotFound>(result);
    }

    [Fact]
    public async Task TokenHeaderMissing_Returns404()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "secret");
        var ctx = new DefaultHttpContext();
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: "studentProfile", tenantId: "t_x", id: "uid-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsAssignableFrom<NotFound>(result);
    }

    [Fact]
    public async Task TokenHeaderWrong_Returns404()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "secret");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "wrong";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: "studentProfile", tenantId: "t_x", id: "uid-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsAssignableFrom<NotFound>(result);
    }

    [Fact]
    public async Task TokenHeaderWrongLength_Returns404()
    {
        // Length-mismatched but otherwise plausible value — the constant-time
        // comparison must still reject.
        Environment.SetEnvironmentVariable(TokenEnvVar, "1234567890");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "1234";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: "studentProfile", tenantId: "t_x", id: "uid-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsAssignableFrom<NotFound>(result);
    }

    [Fact]
    public async Task TokenOk_MissingTypeQuery_Returns400()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "secret");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "secret";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: null, tenantId: "t_x", id: "uid-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task TokenOk_MissingTenantIdQuery_Returns400()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "secret");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "secret";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: "studentProfile", tenantId: null, id: "uid-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task TokenOk_MissingIdQuery_Returns400()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "secret");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "secret";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: "studentProfile", tenantId: "t_x", id: null,
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task TokenOk_UnknownKind_Returns400()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "secret");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "secret";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);

        var result = await HandleProbe(
            type: "wormhole", tenantId: "t_x", id: "id-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        var bad = Assert.IsType<BadRequest<string>>(result);
        Assert.Contains("unknown kind", bad.Value!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenOk_KindCaseInsensitive_NotRejectedByValidator()
    {
        // The dispatcher lowercases the kind before matching. A capitalised
        // "StudentProfile" must not be treated as unknown by the input
        // validator. We don't have Marten here so the actual probe path will
        // fail downstream — this test only proves the validator passed.
        Environment.SetEnvironmentVariable(TokenEnvVar, "secret");
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[TokenHeader] = "secret";
        var store = new Mock<IDocumentStore>(MockBehavior.Strict);
        // Loose mock that lets the call go through and returns null sessions
        // etc — we accept InternalServerError as the proof-of-pass for
        // the validator gate. The point is "not BadRequest with unknown kind".
        store.Setup(s => s.QuerySession()).Throws(new InvalidOperationException("test stub"));

        var result = await HandleProbe(
            type: "StudentProfile", tenantId: "t_x", id: "uid-1",
            store.Object, NullLogger<TestProbeLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsAssignableFrom<StatusCodeHttpResult>(result);
        var sc = (StatusCodeHttpResult)result;
        Assert.Equal(StatusCodes.Status500InternalServerError, sc.StatusCode);
    }
}
