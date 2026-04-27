// =============================================================================
// Cena Platform — TASK-E2E-A-04-BE parent-bind endpoint tests
//
// Pure-unit tests around the boundary mapping IssueInvite + ConsumeInvite
// owns. Business logic (jti store, signature, expiry, replay, tenant
// match) is exercised by the e2e-flow Playwright suite via real Marten,
// matching the same pattern PRR-436 set up.
//
// Endpoint contract verified here:
//   * Missing uid claim                 -> 401
//   * Missing tenant_id claim           -> 401 with parent_bind_no_tenant
//   * Missing parentEmail               -> 400 parent_bind_email_required
//   * Service returns InvalidStudent    -> 400 parent_bind_issue_failed
//   * Service returns Issued            -> 200 with token + jti + expiresAt
//
// Consume:
//   * Service returns Verified          -> binding store called + 200
//   * Service returns Expired           -> 401 parent_bind_expired
//   * Service returns InvalidSignature  -> 401 parent_bind_invalidsignature
//   * Service returns AlreadyConsumed   -> 409 parent_bind_already_consumed
//   * Service returns Unknown           -> 401 parent_bind_unknown
//   * Service returns TenantMismatch    -> 403 parent_bind_tenantmismatch
//   * Service returns EmailMismatch     -> 403 parent_bind_emailmismatch
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Parent;
using Cena.Infrastructure.Errors;
using Cena.Student.Api.Host.Tests.Endpoints.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NATS.Client.Core;
using static Cena.Api.Host.Endpoints.ParentBindEndpoints;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class ParentBindEndpointsTests
{
    private static HttpContext WithStudentClaims(string uid, string tenantId, string? email = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uid),
            new("sub", uid),
            new("tenant_id", tenantId),
        };
        if (email is not null)
            claims.Add(new Claim("email", email));
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };
        return ctx;
    }

    // ── Issue ──

    [Fact]
    public async Task Issue_NoUid_Returns401()
    {
        var ctx = HttpContextBuilder.WithEmptyPrincipal();
        var invites = new Mock<IParentBindInviteService>(MockBehavior.Strict);

        var result = await IssueInvite(
            new IssueInviteRequest(ParentEmail: "p@cena.test", Relationship: null),
            invites.Object, NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsAssignableFrom<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Issue_NoTenant_Returns401WithCode()
    {
        var ctx = HttpContextBuilder.WithUid("uid-1", "s@cena.test"); // no tenant_id claim
        var invites = new Mock<IParentBindInviteService>(MockBehavior.Strict);

        var result = await IssueInvite(
            new IssueInviteRequest(ParentEmail: "p@cena.test", Relationship: null),
            invites.Object, NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<CenaError>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, json.StatusCode);
        Assert.Equal("parent_bind_no_tenant", json.Value!.Code);
    }

    [Fact]
    public async Task Issue_MissingEmail_Returns400()
    {
        var ctx = WithStudentClaims("uid-1", "t_x");
        var invites = new Mock<IParentBindInviteService>(MockBehavior.Strict);

        var result = await IssueInvite(
            request: null,
            invites.Object, NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        var bad = Assert.IsType<BadRequest<CenaError>>(result);
        Assert.Equal("parent_bind_email_required", bad.Value!.Code);
    }

    [Fact]
    public async Task Issue_HappyPath_ReturnsTokenJtiExp()
    {
        var ctx = WithStudentClaims("student-uid", "t_x");
        var invites = new Mock<IParentBindInviteService>(MockBehavior.Strict);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        invites.Setup(s => s.IssueAsync(
                "student-uid", "t_x", "parent@cena.test", "guardian", It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParentBindIssueResult(
                ParentBindIssueOutcome.Issued, "token.jwt", "jti-abc", expiresAt));

        var result = await IssueInvite(
            new IssueInviteRequest(ParentEmail: "parent@cena.test", Relationship: "guardian"),
            invites.Object, NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        var ok = Assert.IsType<Ok<IssueInviteResponse>>(result);
        Assert.Equal("token.jwt", ok.Value!.Token);
        Assert.Equal("jti-abc", ok.Value.Jti);
        Assert.Equal(expiresAt, ok.Value.ExpiresAt);
    }

    [Fact]
    public async Task Issue_InvalidStudent_Returns400()
    {
        var ctx = WithStudentClaims("student-uid", "t_x");
        var invites = new Mock<IParentBindInviteService>(MockBehavior.Strict);
        invites.Setup(s => s.IssueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParentBindIssueResult(
                ParentBindIssueOutcome.InvalidStudent, null, null, null));

        var result = await IssueInvite(
            new IssueInviteRequest(ParentEmail: "p@cena.test", Relationship: null),
            invites.Object, NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        var bad = Assert.IsType<BadRequest<CenaError>>(result);
        Assert.Equal("parent_bind_issue_failed", bad.Value!.Code);
    }

    // ── Consume ──

    private static (Mock<IParentBindInviteService> invites,
                    Mock<IParentChildBindingStore> bindingStore,
                    Mock<INatsConnection> nats) MockServices()
    {
        var invites = new Mock<IParentBindInviteService>(MockBehavior.Strict);
        var bindingStore = new Mock<IParentChildBindingStore>(MockBehavior.Strict);
        var nats = new Mock<INatsConnection>(MockBehavior.Loose);
        return (invites, bindingStore, nats);
    }

    [Fact]
    public async Task Consume_NoUid_Returns401()
    {
        var (invites, store, nats) = MockServices();
        var ctx = HttpContextBuilder.WithEmptyPrincipal();

        var result = await ConsumeInvite(
            "tok", invites.Object, store.Object, nats.Object,
            NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        Assert.IsAssignableFrom<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Consume_NoTenant_Returns401WithCode()
    {
        var (invites, store, nats) = MockServices();
        var ctx = HttpContextBuilder.WithUid("parent-uid", "p@cena.test");

        var result = await ConsumeInvite(
            "tok", invites.Object, store.Object, nats.Object,
            NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<CenaError>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, json.StatusCode);
        Assert.Equal("parent_bind_no_tenant", json.Value!.Code);
    }

    [Theory]
    [InlineData(ParentBindConsumeOutcome.InvalidSignature, StatusCodes.Status401Unauthorized, "parent_bind_invalidsignature")]
    [InlineData(ParentBindConsumeOutcome.Expired,          StatusCodes.Status401Unauthorized, "parent_bind_expired")]
    [InlineData(ParentBindConsumeOutcome.Unknown,          StatusCodes.Status401Unauthorized, "parent_bind_unknown")]
    [InlineData(ParentBindConsumeOutcome.AlreadyConsumed,  StatusCodes.Status409Conflict,     "parent_bind_already_consumed")]
    [InlineData(ParentBindConsumeOutcome.TenantMismatch,   StatusCodes.Status403Forbidden,    "parent_bind_tenantmismatch")]
    [InlineData(ParentBindConsumeOutcome.EmailMismatch,    StatusCodes.Status403Forbidden,    "parent_bind_emailmismatch")]
    public async Task Consume_RejectsWithMappedStatusAndCode(
        ParentBindConsumeOutcome outcome, int expectedStatus, string expectedCode)
    {
        var (invites, store, nats) = MockServices();
        invites.Setup(s => s.ConsumeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParentBindConsumeResult(outcome, null));
        var ctx = WithStudentClaims("parent-uid", "t_x", email: "p@cena.test");

        var result = await ConsumeInvite(
            "tok", invites.Object, store.Object, nats.Object,
            NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<CenaError>>(result);
        Assert.Equal(expectedStatus, json.StatusCode);
        Assert.Equal(expectedCode, json.Value!.Code);

        // The binding store must NOT be called on a rejection path.
        store.Verify(s => s.GrantAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_Verified_GrantsBindingAndReturns200()
    {
        var (invites, store, nats) = MockServices();
        var ctx = WithStudentClaims("parent-uid", "t_x", email: "p@cena.test");
        var consumedAt = DateTimeOffset.UtcNow;
        var invite = new ParentBindInviteDocument
        {
            Id = "jti-abc",
            StudentSubjectId = "student-uid",
            InstituteId = "t_x",
            ParentEmail = "p@cena.test",
            Relationship = "parent",
            IssuedAt = consumedAt.AddDays(-1),
            ExpiresAt = consumedAt.AddDays(6),
            ConsumedAt = consumedAt,
            ConsumedByParentUid = "parent-uid",
        };

        invites.Setup(s => s.ConsumeAsync(
                "tok", "parent-uid", "t_x", "p@cena.test",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParentBindConsumeResult(ParentBindConsumeOutcome.Verified, invite));

        store.Setup(s => s.GrantAsync(
                "parent-uid", "student-uid", "t_x", consumedAt,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParentChildBinding(
                "parent-uid", "student-uid", "t_x", consumedAt));

        var result = await ConsumeInvite(
            "tok", invites.Object, store.Object, nats.Object,
            NullLogger<ParentBindLoggerMarker>.Instance, ctx, CancellationToken.None);

        var ok = Assert.IsType<Ok<BindResponse>>(result);
        Assert.Equal("parent-uid", ok.Value!.ParentUid);
        Assert.Equal("student-uid", ok.Value.StudentSubjectId);
        Assert.Equal("t_x", ok.Value.TenantId);
        Assert.Equal("parent", ok.Value.Relationship);
        Assert.Equal("jti-abc", ok.Value.InviteJti);

        store.Verify(s => s.GrantAsync(
            "parent-uid", "student-uid", "t_x", consumedAt, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
