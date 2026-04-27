// =============================================================================
// Cena Platform — TASK-E2E-A-01-BE-01 endpoint handler unit tests
//
// Pure-unit tests for AuthEndpoints.OnFirstSignIn. The handler itself owns
// boundary validation (auth, body shape, env-gate, error mapping) and
// delegates business logic to IStudentOnboardingService. We mock the service
// and assert the handler's mapping invariants:
//
//   * Missing/blank uid in claims              -> 401
//   * Missing email claim                       -> 400
//   * Missing TenantId in body                  -> 400
//   * No CENA_E2E_TRUSTED_REGISTRATION env var  -> 501 (production path
//                                                  not yet wired)
//   * Happy path with WasNewlyOnboarded=true    -> 200 with that field
//   * Idempotent re-call (WasNewlyOnboarded=false) -> 200 with that field
//   * Role-collision exception from service     -> 409
//   * SchoolId omitted in body                  -> defaults to TenantId
//   * X-Correlation-Id header used when present -> service receives parsed Guid
//
// Service-layer behaviour (Firebase claims push, Marten append, NATS publish,
// dedup gate) is exercised by the e2e-flow spec student-register.spec.ts,
// which is the integration contract this code unblocks.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Onboarding;
using Cena.Infrastructure.Errors;
using Cena.Student.Api.Host.Tests.Endpoints.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Cena.Api.Host.Endpoints.AuthEndpoints;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public class AuthOnFirstSignInTests : IDisposable
{
    private readonly string? _originalTrustedRegistration;

    public AuthOnFirstSignInTests()
    {
        _originalTrustedRegistration = Environment.GetEnvironmentVariable(TrustedRegistrationEnvVar);
        // Default: enable trusted mode for every test except the explicit
        // production-path test below.
        Environment.SetEnvironmentVariable(TrustedRegistrationEnvVar, "true");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TrustedRegistrationEnvVar, _originalTrustedRegistration);
    }

    [Fact]
    public async Task OnFirstSignIn_NoUidClaim_Returns401()
    {
        var ctx = HttpContextBuilder.WithEmptyPrincipal();
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);

        var result = await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_x", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        Assert.IsAssignableFrom<UnauthorizedHttpResult>(result);
        onboarding.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnFirstSignIn_NoEmailClaim_Returns400()
    {
        var ctx = HttpContextBuilder.WithUid("uid-123", email: null);
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);

        var result = await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_x", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        var bad = Assert.IsType<BadRequest<AuthErrorResponse>>(result);
        Assert.Contains("email claim", bad.Value!.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnFirstSignIn_NullBody_Returns400()
    {
        var ctx = HttpContextBuilder.WithUid("uid-123", "u@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);

        var result = await OnFirstSignIn(
            request: null,
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        var bad = Assert.IsType<BadRequest<AuthErrorResponse>>(result);
        Assert.Contains("TenantId", bad.Value!.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnFirstSignIn_BlankTenantId_Returns400()
    {
        var ctx = HttpContextBuilder.WithUid("uid-123", "u@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);

        var result = await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "   ", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        Assert.IsType<BadRequest<AuthErrorResponse>>(result);
    }

    [Fact]
    public async Task OnFirstSignIn_TrustedRegistrationDisabled_Returns501()
    {
        Environment.SetEnvironmentVariable(TrustedRegistrationEnvVar, null);
        var ctx = HttpContextBuilder.WithUid("uid-123", "u@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);

        var result = await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_x", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<CenaError>>(result);
        Assert.Equal(StatusCodes.Status501NotImplemented, json.StatusCode);
        Assert.Equal("on_first_sign_in_invite_path_not_implemented", json.Value!.Code);
        onboarding.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnFirstSignIn_HappyPath_Returns200_WasNewlyOnboardedTrue()
    {
        var ctx = HttpContextBuilder.WithUid("uid-abc", "alice@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);
        onboarding.Setup(o => o.OnboardAsync(It.IsAny<StudentOnboardingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StudentOnboardingRequest req, CancellationToken _) =>
                new StudentOnboardingResult(req.Uid, req.TenantId, req.SchoolId, "student", WasNewlyOnboarded: true));

        var result = await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_e2e_0_run1", SchoolId: "school-001", DisplayName: "Alice"),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<OnFirstSignInResponse>>(result);
        Assert.Equal("uid-abc", ok.Value!.Uid);
        Assert.Equal("t_e2e_0_run1", ok.Value.TenantId);
        Assert.Equal("school-001", ok.Value.SchoolId);
        Assert.Equal("student", ok.Value.Role);
        Assert.True(ok.Value.WasNewlyOnboarded);

        onboarding.Verify(o => o.OnboardAsync(
            It.Is<StudentOnboardingRequest>(r =>
                r.Uid == "uid-abc" &&
                r.Email == "alice@cena.test" &&
                r.TenantId == "t_e2e_0_run1" &&
                r.SchoolId == "school-001" &&
                r.DisplayName == "Alice"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnFirstSignIn_IdempotentRecall_WasNewlyOnboardedFalse()
    {
        var ctx = HttpContextBuilder.WithUid("uid-existing", "bob@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);
        onboarding.Setup(o => o.OnboardAsync(It.IsAny<StudentOnboardingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StudentOnboardingRequest req, CancellationToken _) =>
                new StudentOnboardingResult(req.Uid, req.TenantId, req.SchoolId, "student", WasNewlyOnboarded: false));

        var result = await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_x", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<OnFirstSignInResponse>>(result);
        Assert.False(ok.Value!.WasNewlyOnboarded);
    }

    [Fact]
    public async Task OnFirstSignIn_RoleCollision_Returns409()
    {
        var ctx = HttpContextBuilder.WithUid("uid-collision", "c@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);
        onboarding.Setup(o => o.OnboardAsync(It.IsAny<StudentOnboardingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Uid uid-collision already exists as TEACHER, refusing to re-bind as STUDENT."));

        var result = await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_x", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<CenaError>>(result);
        Assert.Equal(StatusCodes.Status409Conflict, json.StatusCode);
        Assert.Equal("on_first_sign_in_role_collision", json.Value!.Code);
    }

    [Fact]
    public async Task OnFirstSignIn_OmittedSchoolId_DefaultsToTenantId()
    {
        var ctx = HttpContextBuilder.WithUid("uid-default-school", "d@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);
        StudentOnboardingRequest? captured = null;
        onboarding.Setup(o => o.OnboardAsync(It.IsAny<StudentOnboardingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StudentOnboardingRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync((StudentOnboardingRequest req, CancellationToken _) =>
                new StudentOnboardingResult(req.Uid, req.TenantId, req.SchoolId, "student", true));

        await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "tenant-eq-school", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("tenant-eq-school", captured!.SchoolId);
    }

    [Fact]
    public async Task OnFirstSignIn_CorrelationHeader_ParsedAndForwarded()
    {
        var corrId = Guid.NewGuid();
        var ctx = HttpContextBuilder.WithUid("uid-corr", "e@cena.test");
        ctx.Request.Headers["X-Correlation-Id"] = corrId.ToString();

        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);
        StudentOnboardingRequest? captured = null;
        onboarding.Setup(o => o.OnboardAsync(It.IsAny<StudentOnboardingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StudentOnboardingRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync((StudentOnboardingRequest req, CancellationToken _) =>
                new StudentOnboardingResult(req.Uid, req.TenantId, req.SchoolId, "student", true));

        await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_x", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(corrId, captured!.CorrelationId);
    }

    [Fact]
    public async Task OnFirstSignIn_NoCorrelationHeader_GeneratesGuid()
    {
        var ctx = HttpContextBuilder.WithUid("uid-no-corr", "f@cena.test");
        var onboarding = new Mock<IStudentOnboardingService>(MockBehavior.Strict);
        StudentOnboardingRequest? captured = null;
        onboarding.Setup(o => o.OnboardAsync(It.IsAny<StudentOnboardingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StudentOnboardingRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync((StudentOnboardingRequest req, CancellationToken _) =>
                new StudentOnboardingResult(req.Uid, req.TenantId, req.SchoolId, "student", true));

        await OnFirstSignIn(
            new OnFirstSignInRequest(TenantId: "t_x", SchoolId: null, DisplayName: null),
            onboarding.Object,
            NullLogger<AuthLoggerMarker>.Instance,
            ctx,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, captured!.CorrelationId);
    }
}
