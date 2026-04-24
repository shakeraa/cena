// =============================================================================
// Cena Platform — Teacher Schedule Override endpoints (prr-150)
//
// Three POST endpoints behind the ADMIN/MODERATOR/SUPER_ADMIN policy that
// let a teacher or admin:
//   - POST /api/admin/teacher/override/pin-topic
//   - POST /api/admin/teacher/override/budget
//   - POST /api/admin/teacher/override/motivation
//
// Every endpoint:
//   1. Extracts the teacher's actor id + institute id from claims.
//   2. Delegates to TeacherOverrideCommands, which enforces the ADR-0001
//      tenant invariant (throws CrossTenantOverrideDeniedException on
//      mismatch — mapped to 403 here).
//   3. Rides on AdminActionAuditMiddleware for the audit trail: every
//      POST to /api/admin/** is already captured there, so no bespoke
//      audit plumbing is needed beyond the route prefix.
//
// NOTE: The endpoint is behind ModeratorOrAbove (teachers in the Cena
// role map are MODERATOR — see InstituteRoleClaims). If a dedicated
// TeacherOrAbove policy lands in a future sprint, swap it in here; the
// endpoint contract does not change.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Security.Claims;
using Cena.Actors.Mastery;
using Cena.Actors.Teacher.ScheduleOverride;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.Teacher;

// ---- Wire DTOs -------------------------------------------------------------

/// <summary>Request body for POST /api/admin/teacher/override/pin-topic.</summary>
public sealed record PinTopicRequest(
    string StudentAnonId,
    string TopicSlug,
    int PinnedSessionCount,
    string Rationale);

/// <summary>Request body for POST /api/admin/teacher/override/budget.</summary>
public sealed record AdjustBudgetRequest(
    string StudentAnonId,
    int WeeklyBudgetHours,
    string Rationale);

/// <summary>Request body for POST /api/admin/teacher/override/motivation.</summary>
public sealed record OverrideMotivationRequest(
    string StudentAnonId,
    string SessionTypeScope,
    MotivationProfile OverrideProfile,
    string Rationale);

/// <summary>Canonical 200 response.</summary>
public sealed record OverrideAppliedResponse(
    string OverrideKind,
    string StudentAnonId,
    string TeacherActorId,
    DateTimeOffset AppliedAt);

// ---- Endpoint --------------------------------------------------------------

public static class ScheduleOverrideEndpoints
{
    private static readonly Meter Meter = new("Cena.Teacher.Override", "1.0");
    private static readonly Counter<long> AppliedCounter = Meter.CreateCounter<long>(
        "cena_teacher_override_applied_total",
        description: "Teacher/mentor schedule overrides successfully applied");
    private static readonly Counter<long> DeniedCounter = Meter.CreateCounter<long>(
        "cena_teacher_override_cross_tenant_denied_total",
        description: "Teacher override attempts denied due to cross-tenant mismatch");

    public const string RoutePrefix = "/api/admin/teacher/override";

    public static IEndpointRouteBuilder MapScheduleOverrideEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api")
            .WithTags("Teacher Override");

        group.MapPost("/pin-topic", HandlePinAsync)
            .WithName("TeacherOverridePinTopic")
            .Produces<OverrideAppliedResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        group.MapPost("/budget", HandleBudgetAsync)
            .WithName("TeacherOverrideBudget")
            .Produces<OverrideAppliedResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        group.MapPost("/motivation", HandleMotivationAsync)
            .WithName("TeacherOverrideMotivation")
            .Produces<OverrideAppliedResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    // ---- Handlers ----------------------------------------------------------

    private static async Task<IResult> HandlePinAsync(
        PinTopicRequest request,
        HttpContext ctx,
        TeacherOverrideCommands commands,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TeacherOverride.PinTopic");
        var (teacherActorId, teacherInstituteId, rejection) = ExtractIdentity(ctx.User);
        if (rejection is not null) return rejection;

        var command = new PinTopicCommand(
            StudentAnonId: request.StudentAnonId,
            TopicSlug: request.TopicSlug,
            PinnedSessionCount: request.PinnedSessionCount,
            TeacherActorId: teacherActorId,
            TeacherInstituteId: teacherInstituteId,
            Rationale: request.Rationale ?? string.Empty,
            SetAt: DateTimeOffset.UtcNow);

        return await ExecuteAsync(
            kind: "pin-topic",
            run: () => commands.PinTopicAsync(command, ct),
            studentAnonId: command.StudentAnonId,
            teacherActorId: teacherActorId,
            logger: logger);
    }

    private static async Task<IResult> HandleBudgetAsync(
        AdjustBudgetRequest request,
        HttpContext ctx,
        TeacherOverrideCommands commands,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TeacherOverride.Budget");
        var (teacherActorId, teacherInstituteId, rejection) = ExtractIdentity(ctx.User);
        if (rejection is not null) return rejection;

        var command = new AdjustBudgetCommand(
            StudentAnonId: request.StudentAnonId,
            NewWeeklyBudget: TimeSpan.FromHours(request.WeeklyBudgetHours),
            TeacherActorId: teacherActorId,
            TeacherInstituteId: teacherInstituteId,
            Rationale: request.Rationale ?? string.Empty,
            SetAt: DateTimeOffset.UtcNow);

        return await ExecuteAsync(
            kind: "budget",
            run: () => commands.AdjustBudgetAsync(command, ct),
            studentAnonId: command.StudentAnonId,
            teacherActorId: teacherActorId,
            logger: logger);
    }

    private static async Task<IResult> HandleMotivationAsync(
        OverrideMotivationRequest request,
        HttpContext ctx,
        TeacherOverrideCommands commands,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TeacherOverride.Motivation");
        var (teacherActorId, teacherInstituteId, rejection) = ExtractIdentity(ctx.User);
        if (rejection is not null) return rejection;

        var command = new OverrideMotivationCommand(
            StudentAnonId: request.StudentAnonId,
            SessionTypeScope: string.IsNullOrWhiteSpace(request.SessionTypeScope)
                ? TeacherOverrideCommands.ScopeAll
                : request.SessionTypeScope,
            OverrideProfile: request.OverrideProfile,
            TeacherActorId: teacherActorId,
            TeacherInstituteId: teacherInstituteId,
            Rationale: request.Rationale ?? string.Empty,
            SetAt: DateTimeOffset.UtcNow);

        return await ExecuteAsync(
            kind: "motivation",
            run: () => commands.OverrideMotivationAsync(command, ct),
            studentAnonId: command.StudentAnonId,
            teacherActorId: teacherActorId,
            logger: logger);
    }

    // ---- Shared execution wrapper ------------------------------------------

    private static async Task<IResult> ExecuteAsync(
        string kind,
        Func<Task> run,
        string studentAnonId,
        string teacherActorId,
        ILogger logger)
    {
        try
        {
            await run().ConfigureAwait(false);
        }
        catch (CrossTenantOverrideDeniedException ex)
        {
            DeniedCounter.Add(1,
                new KeyValuePair<string, object?>("kind", kind));
            logger.LogWarning(
                "[TEACHER_OVERRIDE_DENIED] kind={Kind} student={Sid} teacher={Tid} " +
                "teacher_institute={TInst} student_institute={SInst}",
                kind, ex.StudentAnonId, ex.TeacherActorId,
                ex.TeacherInstituteId, ex.StudentInstituteId);
            return Results.Json(
                new CenaError(
                    "TEACHER_OVERRIDE_CROSS_TENANT_DENIED",
                    ex.Message,
                    ErrorCategory.Authorization, null, null),
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new CenaError(
                "TEACHER_OVERRIDE_INVALID_INPUT",
                ex.Message,
                ErrorCategory.Validation, null, null));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new CenaError(
                "TEACHER_OVERRIDE_INVALID_INPUT",
                ex.Message,
                ErrorCategory.Validation, null, null));
        }

        AppliedCounter.Add(1, new KeyValuePair<string, object?>("kind", kind));
        logger.LogInformation(
            "[TEACHER_OVERRIDE_APPLIED] kind={Kind} student={Sid} teacher={Tid}",
            kind, studentAnonId, teacherActorId);

        return Results.Ok(new OverrideAppliedResponse(
            OverrideKind: kind,
            StudentAnonId: studentAnonId,
            TeacherActorId: teacherActorId,
            AppliedAt: DateTimeOffset.UtcNow));
    }

    // ---- Identity extraction -----------------------------------------------

    /// <summary>
    /// Extract the teacher's actor id + institute id from the claims. A
    /// missing institute claim surfaces an immediate 401/403 rather than
    /// falling through to an ambiguous tenant check.
    /// </summary>
    internal static (string TeacherActorId, string InstituteId, IResult? Rejection) ExtractIdentity(
        ClaimsPrincipal user)
    {
        var teacherActorId = user.FindFirstValue("user_id")
                             ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(teacherActorId))
        {
            return (string.Empty, string.Empty, Results.Unauthorized());
        }

        var instituteId = user.FindFirstValue("institute_id")
                          ?? user.FindFirstValue("school_id");
        if (string.IsNullOrEmpty(instituteId))
        {
            return (teacherActorId, string.Empty, Results.Json(
                new CenaError(
                    "TEACHER_OVERRIDE_MISSING_INSTITUTE",
                    "Teacher caller has no institute_id / school_id claim; cannot apply override.",
                    ErrorCategory.Authorization, null, null),
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (teacherActorId, instituteId, null);
    }
}
