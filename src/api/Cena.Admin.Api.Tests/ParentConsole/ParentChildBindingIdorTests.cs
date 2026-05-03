// =============================================================================
// Cena Platform — Parent Console IDOR exhaustion tests (prr-009)
//
// Verifies the ADR-0041 seam on the two parent-facing endpoint files
// that exist today (TimeBudgetEndpoint + AccommodationsEndpoints) by
// exercising the ParentAuthorizationGuard directly and then re-verifying
// each endpoint's caller-facing helper returns 403 on every IDOR shape.
//
// Coverage matrix:
//
//   A. Parent A reads / writes their own bound child — allow.
//   B. Parent B reads / writes child of Parent A — deny (IDOR).
//   C. Unbound parent (no parent_of claim at all) — deny.
//   D. Parent bound at institute X attempts child at institute Y — deny
//      even when a binding exists (tenant-crossing IDOR, ADR-0001).
//   E. Parent whose binding was revoked post-token-issuance — deny
//      (JWT cache is advisory; store is authoritative).
//   F. Parent token missing institute_id claim — deny.
//   G. Audit log emits the structured binding-check line every call.
//
// Tests call the guard + the endpoint helpers directly (matching the
// StudentAuthEndpointsTests pattern in this project). No WebApplication
// bring-up required.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Parent;
using Cena.Admin.Api.Features.ParentConsole;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests.ParentConsole;

[Trait("Category", "IDOR")]
public class ParentChildBindingIdorTests
{
    // ── Canonical test identities ───────────────────────────────────────
    private const string ParentA = "parent-A";
    private const string ParentB = "parent-B";
    private const string ChildA  = "child-of-A";
    private const string ChildB  = "child-of-B";
    private const string InstX   = "institute-X";
    private const string InstY   = "institute-Y";

    // ── Fixture helpers ─────────────────────────────────────────────────

    private static InMemoryParentChildBindingStore NewStore()
    {
        var store = new InMemoryParentChildBindingStore();
        // Parent A ↔ Child A at institute X (legitimate binding)
        store.GrantAsync(ParentA, ChildA, InstX, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
        // Parent B ↔ Child B at institute X (separate legitimate binding)
        store.GrantAsync(ParentB, ChildB, InstX, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
        // Parent A ALSO has Child A bound at institute Y — to prove the
        // guard denies cross-tenant probes even when a same-child binding
        // exists elsewhere.
        // (Intentionally NOT granted here — tenant-crossing test uses the
        // case where the parent is only at X.)
        return store;
    }

    private static IParentChildBindingService NewService(IParentChildBindingStore store)
        => new ParentChildBindingService(store);

    private static ClaimsPrincipal MakeParent(
        string parentActorId, string instituteId, params (string studentId, string instituteId)[] boundPairs)
    {
        var claims = new List<Claim>
        {
            new("sub", parentActorId),
            new("parentAnonId", parentActorId),
            new(ClaimTypes.Role, "PARENT"),
            new("institute_id", instituteId),
        };
        foreach (var (sid, iid) in boundPairs)
        {
            claims.Add(new Claim(
                "parent_of",
                $"{{\"studentId\":\"{sid}\",\"instituteId\":\"{iid}\"}}"));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal MakeStudent(string uid)
        => new(new ClaimsIdentity(new[]
        {
            new Claim("sub", uid),
            new Claim(ClaimTypes.Role, "STUDENT"),
        }, "test"));

    private static ClaimsPrincipal MakeAdmin(string uid, string instituteId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim("sub", uid),
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("institute_id", instituteId),
        }, "test"));

    private static HttpContext MakeHttp(ClaimsPrincipal user, IServiceProvider services)
    {
        var ctx = new DefaultHttpContext
        {
            User = user,
            RequestServices = services,
        };
        return ctx;
    }

    private static IServiceProvider BuildServices(IParentChildBindingService svc, ILoggerFactory? loggers = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(svc);
        services.AddSingleton<ILoggerFactory>(loggers ?? NullLoggerFactory.Instance);
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    // =========================================================================
    // A. Parent A reads their own child — allow
    // =========================================================================

    [Fact]
    public async Task ParentA_AccessingChildA_Passes()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var user = MakeParent(ParentA, InstX, (ChildA, InstX));
        var http = MakeHttp(user, services);

        var resolution = await TimeBudgetEndpoint
            .RequireParentBindingAsync(http, ChildA, CancellationToken.None);

        Assert.Equal(ParentA, resolution.ParentActorId);
        Assert.Equal(ChildA, resolution.StudentSubjectId);
        Assert.Equal(InstX, resolution.InstituteId);
    }

    // =========================================================================
    // B. Parent B probes Parent A's child — deny
    // =========================================================================

    [Fact]
    public async Task ParentB_AccessingChildOfParentA_ThrowsIdor()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        // Parent B only has Child B at institute X. Trying Child A with no
        // matching parent_of claim is the canonical cross-parent IDOR.
        var user = MakeParent(ParentB, InstX, (ChildB, InstX));
        var http = MakeHttp(user, services);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            TimeBudgetEndpoint.RequireParentBindingAsync(http, ChildA, CancellationToken.None));
        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task ParentB_WritingAccommodationsOfParentA_Returns403()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var user = MakeParent(ParentB, InstX, (ChildB, InstX));
        var http = MakeHttp(user, services);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            AccommodationsEndpoints.RequireParentBindingAsync(http, ChildA, CancellationToken.None));
        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    // Exhaustive IDOR matrix — parent B trying every endpoint-shape child id.
    [Theory]
    [InlineData("child-of-A")]
    [InlineData("any-other-student")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("admin-user")]
    [InlineData("")] // Empty route param — not a valid binding anywhere.
    public async Task ParentB_CrossChildMatrix_AllBlocked(string targetChildId)
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var user = MakeParent(ParentB, InstX, (ChildB, InstX));
        var http = MakeHttp(user, services);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            TimeBudgetEndpoint.RequireParentBindingAsync(
                http, string.IsNullOrEmpty(targetChildId) ? " " : targetChildId,
                CancellationToken.None));
    }

    // =========================================================================
    // C. Unbound parent (no parent_of) — deny
    // =========================================================================

    [Fact]
    public async Task UnboundParent_AnyChild_Denied()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var user = MakeParent(ParentA, InstX /* no parent_of claims */);
        var http = MakeHttp(user, services);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            TimeBudgetEndpoint.RequireParentBindingAsync(http, ChildA, CancellationToken.None));
    }

    // =========================================================================
    // D. Cross-tenant (institute-crossing) IDOR — deny
    // =========================================================================

    [Fact]
    public async Task ParentInInstituteX_ProbingChildAtInstituteY_Denied()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        // Parent A is bound to Child A at institute X ONLY. Their session
        // token is scoped to institute X (institute_id claim). An attempt
        // to access Child A under institute Y (say via a forged claim
        // shape that lists (ChildA, InstY)) must still be denied because
        // the authoritative store has no Y-binding.
        var user = MakeParent(ParentA, InstX, (ChildA, InstX), (ChildA, InstY));
        var http = MakeHttp(user, services);

        // The caller's session is scoped to X (institute_id claim). The
        // guard uses that scope, so Child A at X is allowed:
        var ok = await TimeBudgetEndpoint
            .RequireParentBindingAsync(http, ChildA, CancellationToken.None);
        Assert.Equal(InstX, ok.InstituteId);

        // But changing the session's institute claim to Y flips the
        // request into tenant-crossing territory — no authoritative
        // binding exists at Y, so the guard denies.
        var userInY = MakeParent(ParentA, InstY, (ChildA, InstX), (ChildA, InstY));
        var httpY = MakeHttp(userInY, services);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            TimeBudgetEndpoint.RequireParentBindingAsync(httpY, ChildA, CancellationToken.None));
        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    [Fact]
    public async Task ParentClaimedAtInstituteY_ForChildOnlyBoundAtX_Denied()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        // Forged (or stale) claim: parent_of says (ChildA, InstY) but
        // the authoritative store only has (ChildA, InstX). Deny.
        var user = MakeParent(ParentA, InstY, (ChildA, InstY));
        var http = MakeHttp(user, services);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            TimeBudgetEndpoint.RequireParentBindingAsync(http, ChildA, CancellationToken.None));
        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    // =========================================================================
    // E. Revocation post-token — deny on next call
    // =========================================================================

    [Fact]
    public async Task ParentWithRevokedBinding_NextCallDenied()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var user = MakeParent(ParentA, InstX, (ChildA, InstX));
        var http = MakeHttp(user, services);

        // First call: allow.
        var ok = await TimeBudgetEndpoint
            .RequireParentBindingAsync(http, ChildA, CancellationToken.None);
        Assert.Equal(ChildA, ok.StudentSubjectId);

        // Backoffice revokes the binding.
        await store.RevokeAsync(ParentA, ChildA, InstX, DateTimeOffset.UtcNow);

        // Next call on the same (still-valid) token MUST fail — JWT cache
        // is advisory only.
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            TimeBudgetEndpoint.RequireParentBindingAsync(http, ChildA, CancellationToken.None));
        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    // =========================================================================
    // F. Missing institute_id claim — deny
    // =========================================================================

    [Fact]
    public async Task Parent_WithoutInstituteClaim_Denied()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var claims = new List<Claim>
        {
            new("sub", ParentA),
            new("parentAnonId", ParentA),
            new(ClaimTypes.Role, "PARENT"),
            new("parent_of", $"{{\"studentId\":\"{ChildA}\",\"instituteId\":\"{InstX}\"}}"),
            // institute_id deliberately missing
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var http = MakeHttp(user, services);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            TimeBudgetEndpoint.RequireParentBindingAsync(http, ChildA, CancellationToken.None));
        Assert.Equal(ErrorCodes.CENA_AUTH_IDOR_VIOLATION, ex.ErrorCode);
    }

    // =========================================================================
    // G. Guard emits the structured audit line
    // =========================================================================

    [Fact]
    public async Task Guard_EmitsStructuredAuditLog_OnAllowAndDeny()
    {
        var store = NewStore();
        var svc = NewService(store);

        var auditLogger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(auditLogger);
        var services = BuildServices(svc, loggerFactory);

        // Allow case
        var okUser = MakeParent(ParentA, InstX, (ChildA, InstX));
        await ParentAuthorizationGuard.AssertCanAccessAsync(
            okUser, ChildA, InstX, svc, auditLogger, CancellationToken.None);

        // Deny case
        var denyUser = MakeParent(ParentB, InstX, (ChildB, InstX));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ParentAuthorizationGuard.AssertCanAccessAsync(
                denyUser, ChildA, InstX, svc, auditLogger, CancellationToken.None));

        Assert.True(auditLogger.Entries.Count >= 2,
            $"Expected at least two structured log entries (allow + deny); got {auditLogger.Entries.Count}.");
        Assert.Contains(auditLogger.Entries,
            e => e.Message.Contains("parent-binding-check") && e.Level == LogLevel.Information);
        Assert.Contains(auditLogger.Entries,
            e => e.Message.Contains("parent-binding-check") && e.Level == LogLevel.Warning);
    }

    // =========================================================================
    // Non-parent roles
    // =========================================================================

    [Fact]
    public async Task StudentCaller_OnParentEndpoint_Denied()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var student = MakeStudent(ChildA);
        var http = MakeHttp(student, services);

        // The TimeBudget GET handler's AuthorizeParentOrAdminAsync
        // treats a non-parent non-admin as 403.
        var denial = await TimeBudgetEndpoint
            .AuthorizeParentOrAdminAsync(http, ChildA, CancellationToken.None);
        Assert.NotNull(denial);
    }

    [Fact]
    public async Task AdminCaller_OnTimeBudgetGet_BypassesGuard()
    {
        // ADMIN is legitimate for read; tenant scope is enforced at the
        // query layer by Marten's TenantScope — NOT by the parent guard.
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var admin = MakeAdmin("admin-1", InstX);
        var http = MakeHttp(admin, services);

        var denial = await TimeBudgetEndpoint
            .AuthorizeParentOrAdminAsync(http, ChildA, CancellationToken.None);
        Assert.Null(denial);
    }

    [Fact]
    public async Task AdminCaller_OnAccommodationsGet_Passes()
    {
        var store = NewStore();
        var svc = NewService(store);
        var services = BuildServices(svc);

        var admin = MakeAdmin("admin-1", InstX);
        var http = MakeHttp(admin, services);

        var denial = await AccommodationsEndpoints
            .AuthorizeReadOrForbidAsync(http, ChildA, CancellationToken.None);
        Assert.Null(denial);
    }

    // =========================================================================
    // Claim entry parser corner-cases
    // =========================================================================

    [Fact]
    public void ParentOfClaimEntry_TryParse_RejectsMalformed()
    {
        Assert.False(ParentOfClaimEntry.TryParse("", out _));
        Assert.False(ParentOfClaimEntry.TryParse("not-json", out _));
        Assert.False(ParentOfClaimEntry.TryParse("{}", out _));
        Assert.False(ParentOfClaimEntry.TryParse(
            "{\"studentId\":\"s\"}", out _)); // missing instituteId
        Assert.False(ParentOfClaimEntry.TryParse(
            "{\"studentId\":\"\",\"instituteId\":\"i\"}", out _)); // blank id
    }

    [Fact]
    public void ParentOfClaimEntry_TryParse_AcceptsWellFormed()
    {
        Assert.True(ParentOfClaimEntry.TryParse(
            "{\"studentId\":\"s1\",\"instituteId\":\"i1\"}", out var entry));
        Assert.NotNull(entry);
        Assert.Equal("s1", entry!.StudentId);
        Assert.Equal("i1", entry.InstituteId);
    }

    // ── Recording logger utilities ──────────────────────────────────────

    private sealed class RecordingLogger : ILogger
    {
        public List<RecordedEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new RecordedEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record RecordedEntry(LogLevel Level, string Message);

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly ILogger _logger;
        public RecordingLoggerFactory(ILogger logger) => _logger = logger;
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => _logger;
        public void Dispose() { }
    }
}
