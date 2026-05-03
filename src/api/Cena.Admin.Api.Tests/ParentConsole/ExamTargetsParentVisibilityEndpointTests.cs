// =============================================================================
// Cena Platform — ExamTargetsParentVisibilityEndpoint tests (prr-230)
//
// Integration-style tests for GET /api/v1/parent/minors/{studentAnonId}/exam-targets.
// Mirrors the DashboardVisibilityEndpointTests approach — reproduces the
// handler's AuthZ → band → list → filter → DTO path so tests bind to the
// exact code paths without spinning up a full WebApplication.
//
// Covered scenarios from PRR-230 §DoD:
//   (a) student-13 Hidden-by-default    → parent list is empty.
//   (b) student-13 opted-in via toggle  → target appears on parent list.
//   (c) student-12 Visible-by-default   → parent list contains the target.
//   (d) SafetyFlag-tagged target        → always appears regardless of flag.
//   (e) Cross-tenant parent → 403.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Consent;
using Cena.Actors.Parent;
using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;
using Cena.Admin.Api.Features.ParentConsole;
using Cena.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests.ParentConsole;

public sealed class ExamTargetsParentVisibilityEndpointTests
{
    private const string ParentA = "parent-A-prr230";
    private const string InstX   = "institute-X";
    private const string InstY   = "institute-Y";

    // ── Harness ──────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public required InMemoryParentChildBindingStore Bindings { get; init; }
        public required InMemoryStudentAgeBandLookup Ages { get; init; }
        public required InMemoryStudentPlanAggregateStore PlanStore { get; init; }
        public required IStudentPlanReader PlanReader { get; init; }
        public required IStudentPlanCommandHandler PlanHandler { get; init; }
        public required IServiceProvider Services { get; init; }
    }

    private static Harness Build()
    {
        var bindings = new InMemoryParentChildBindingStore();
        var ages = new InMemoryStudentAgeBandLookup();
        var planStore = new InMemoryStudentPlanAggregateStore();
        var reader = new StudentPlanReader(planStore);
        var handler = new StudentPlanCommandHandler(
            planStore, () => DateTimeOffset.Parse("2026-04-21T10:00:00Z"));

        var sc = new ServiceCollection();
        sc.AddSingleton<IParentChildBindingStore>(bindings);
        sc.AddSingleton<IParentChildBindingService>(new ParentChildBindingService(bindings));
        sc.AddSingleton<IStudentAgeBandLookup>(ages);
        sc.AddSingleton<IStudentPlanReader>(reader);
        sc.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        sc.AddLogging();

        return new Harness
        {
            Bindings = bindings,
            Ages = ages,
            PlanStore = planStore,
            PlanReader = reader,
            PlanHandler = handler,
            Services = sc.BuildServiceProvider(),
        };
    }

    private static ClaimsPrincipal MakeParent(
        string parentId, string instituteId, params (string sid, string iid)[] boundPairs)
    {
        var claims = new List<Claim>
        {
            new("sub", parentId),
            new("parentAnonId", parentId),
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

    private static HttpContext MakeHttp(ClaimsPrincipal user, IServiceProvider services)
        => new DefaultHttpContext { User = user, RequestServices = services };

    private static DateOnly DobForAge(int yearsOld)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return today.AddYears(-yearsOld).AddDays(-1);
    }

    /// <summary>
    /// Re-implements the production handler's AuthZ → band → plan-reader →
    /// filter → DTO path. Any drift between this and the endpoint is a
    /// bug HERE; it will surface when the endpoint shape evolves.
    /// </summary>
    private static async Task<IResult> InvokeAsync(
        HttpContext ctx, string studentAnonId, Harness h)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        }
        if (!ctx.User.IsInRole("PARENT"))
        {
            return Results.Forbid();
        }

        ParentChildBindingResolution binding;
        try
        {
            binding = await ExamTargetsParentVisibilityEndpoint
                .RequireParentBindingAsync(ctx, studentAnonId, CancellationToken.None);
        }
        catch (Cena.Infrastructure.Errors.ForbiddenException)
        {
            return Results.Forbid();
        }

        var band = await h.Ages.ResolveBandAsync(studentAnonId, DateTimeOffset.UtcNow);
        if (band is null) return Results.Forbid();

        var all = await h.PlanReader.ListTargetsAsync(studentAnonId, includeArchived: false);
        var visible = all
            .Where(t => t.ParentVisibility == ParentVisibility.Visible || t.IsSafetyFlagged)
            .ToList();

        var dto = new ParentExamTargetsResponseDto(
            StudentAnonId: studentAnonId,
            SubjectBand: band.Value.ToString(),
            Targets: visible.Select(t => new ParentExamTargetDto(
                Id: t.Id.Value,
                ExamCode: t.ExamCode.Value,
                Track: t.Track?.Value,
                Sitting: new ParentSittingCodeDto(
                    AcademicYear: t.Sitting.AcademicYear,
                    Season: t.Sitting.Season.ToString(),
                    Moed: t.Sitting.Moed.ToString()),
                WeeklyHours: t.WeeklyHours,
                ReasonTag: t.ReasonTag?.ToString(),
                IsActive: t.IsActive,
                QuestionPaperCodes: t.QuestionPaperCodes,
                CreatedAt: t.CreatedAt)).ToList());

        return TypedResults.Ok(dto);
    }

    private static AddExamTargetCommand MakeAdd(
        string studentId, AgeBand? band, ReasonTag? reason = null)
        => new(
            StudentAnonId: studentId,
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(studentId),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: new SittingCode("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 5,
            ReasonTag: reason,
            QuestionPaperCodes: new[] { "035581" },
            StudentAgeBand: band);

    // ── (a) Student-13: Hidden-by-default → parent sees empty list ──────

    [Fact]
    public async Task Student13_default_Hidden_parent_sees_empty_list()
    {
        var h = Build();
        const string child = "stu-13";
        await h.Bindings.GrantAsync(ParentA, child, InstX, DateTimeOffset.UtcNow);
        h.Ages.Set(child, DobForAge(13));

        // Student adds a target → defaults to Hidden at 13.
        var add = await h.PlanHandler.HandleAsync(MakeAdd(child, AgeBand.Teen13to15));
        Assert.True(add.Success);

        var ctx = MakeHttp(MakeParent(ParentA, InstX, (child, InstX)), h.Services);
        var result = await InvokeAsync(ctx, child, h);

        var ok = Assert.IsType<Ok<ParentExamTargetsResponseDto>>(result);
        Assert.Empty(ok.Value!.Targets);
        Assert.Equal(nameof(AgeBand.Teen13to15), ok.Value.SubjectBand);
    }

    // ── (b) Student-13 opts in → target appears ─────────────────────────

    [Fact]
    public async Task Student13_opts_in_parent_sees_target()
    {
        var h = Build();
        const string child = "stu-13b";
        await h.Bindings.GrantAsync(ParentA, child, InstX, DateTimeOffset.UtcNow);
        h.Ages.Set(child, DobForAge(13));

        var add = await h.PlanHandler.HandleAsync(MakeAdd(child, AgeBand.Teen13to15));
        var targetId = add.TargetId!.Value;

        // Student toggles to Visible.
        var toggle = await h.PlanHandler.HandleAsync(new SetParentVisibilityCommand(
            StudentAnonId: child,
            TargetId: targetId,
            Visibility: ParentVisibility.Visible,
            Initiator: ParentVisibilityChangeInitiator.Student,
            InitiatorActorId: child,
            Reason: "student-opt-in"));
        Assert.True(toggle.Success);

        var ctx = MakeHttp(MakeParent(ParentA, InstX, (child, InstX)), h.Services);
        var result = await InvokeAsync(ctx, child, h);

        var ok = Assert.IsType<Ok<ParentExamTargetsResponseDto>>(result);
        var seen = Assert.Single(ok.Value!.Targets);
        Assert.Equal(targetId.Value, seen.Id);
    }

    // ── (c) Student-12: Visible-by-default → parent sees target ─────────

    [Fact]
    public async Task Student12_default_Visible_parent_sees_target()
    {
        var h = Build();
        const string child = "stu-12";
        await h.Bindings.GrantAsync(ParentA, child, InstX, DateTimeOffset.UtcNow);
        h.Ages.Set(child, DobForAge(12));

        var add = await h.PlanHandler.HandleAsync(MakeAdd(child, AgeBand.Under13));
        Assert.True(add.Success);

        var ctx = MakeHttp(MakeParent(ParentA, InstX, (child, InstX)), h.Services);
        var result = await InvokeAsync(ctx, child, h);

        var ok = Assert.IsType<Ok<ParentExamTargetsResponseDto>>(result);
        var seen = Assert.Single(ok.Value!.Targets);
        Assert.Equal(nameof(AgeBand.Under13), ok.Value.SubjectBand);
        Assert.Equal(add.TargetId!.Value.Value, seen.Id);
    }

    // ── (d) SafetyFlag target always visible ────────────────────────────

    [Fact]
    public async Task SafetyFlag_target_visible_to_parent_at_any_age()
    {
        var h = Build();
        const string child = "stu-17-safety";
        await h.Bindings.GrantAsync(ParentA, child, InstX, DateTimeOffset.UtcNow);
        h.Ages.Set(child, DobForAge(17));

        var add = await h.PlanHandler.HandleAsync(
            MakeAdd(child, AgeBand.Teen16to17, reason: ReasonTag.SafetyFlag));
        Assert.True(add.Success);

        var ctx = MakeHttp(MakeParent(ParentA, InstX, (child, InstX)), h.Services);
        var result = await InvokeAsync(ctx, child, h);

        var ok = Assert.IsType<Ok<ParentExamTargetsResponseDto>>(result);
        var seen = Assert.Single(ok.Value!.Targets);
        Assert.Equal(nameof(ReasonTag.SafetyFlag), seen.ReasonTag);
    }

    // ── (e) Cross-tenant parent → 403 ───────────────────────────────────

    [Fact]
    public async Task Cross_tenant_parent_gets_403()
    {
        var h = Build();
        const string child = "stu-cross";
        // Child is bound to parent in InstX.
        await h.Bindings.GrantAsync(ParentA, child, InstX, DateTimeOffset.UtcNow);
        h.Ages.Set(child, DobForAge(14));

        var add = await h.PlanHandler.HandleAsync(MakeAdd(child, AgeBand.Teen13to15));
        Assert.True(add.Success);

        // Parent presents an institute_id=InstY claim → binding guard fails.
        var ctx = MakeHttp(MakeParent(ParentA, InstY, (child, InstY)), h.Services);
        var result = await InvokeAsync(ctx, child, h);

        Assert.IsType<ForbidHttpResult>(result);
    }

    // ── Bonus: non-PARENT role → 403 ───────────────────────────────────

    [Fact]
    public async Task Non_PARENT_role_gets_403()
    {
        var h = Build();
        const string child = "stu-x";
        h.Ages.Set(child, DobForAge(12));

        var claims = new List<Claim>
        {
            new("sub", "admin-1"),
            new(ClaimTypes.Role, "ADMIN"),
            new("institute_id", InstX),
        };
        var ctx = MakeHttp(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), h.Services);

        var result = await InvokeAsync(ctx, child, h);

        Assert.IsType<ForbidHttpResult>(result);
    }
}
