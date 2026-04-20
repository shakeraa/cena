// =============================================================================
// Cena Platform — /api/me/study-plan endpoints (prr-148)
//
// GET /api/me/study-plan     → current StudentPlanConfig (or empty defaults)
// PUT /api/me/study-plan     → update deadline + weekly-budget, emits
//                              ExamDateSet_V1 and WeeklyTimeBudgetSet_V1
//                              events on the studentplan-{studentId} stream.
//
// Validation rules (DoD):
//   - DeadlineUtc must be > now + 7 days (gives the scheduler room to plan).
//   - WeeklyBudget must be in [1h, 40h] inclusive.
//   - At least one of DeadlineUtc / WeeklyBudgetHours must be non-null on
//     PUT (empty PUT returns 400).
//
// Auth: student-owned; the student_id claim on the JWT keys the stream.
// Rate limit: standard "api" policy (60 req/min per user).
//
// NOTE: A follow-up StudyPlanSettings.vue (editable post-onboarding) will
// call the same PUT endpoint; that file is scoped out of this PR per the
// prr-148 time-box note. This endpoint IS already the contract the
// settings page will hit unchanged.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>
/// Minimal-API endpoints for the student-input study-plan surface (prr-148).
/// </summary>
public static class StudyPlanSettingsEndpoints
{
    /// <summary>Minimum lead time between now and the exam date.</summary>
    internal static readonly TimeSpan MinDeadlineLead = TimeSpan.FromDays(7);

    /// <summary>Minimum weekly study commitment the scheduler accepts.</summary>
    internal static readonly TimeSpan MinWeeklyBudget = TimeSpan.FromHours(1);

    /// <summary>Maximum weekly study commitment the scheduler accepts.</summary>
    internal static readonly TimeSpan MaxWeeklyBudget = TimeSpan.FromHours(40);

    private sealed class LoggerMarker { }

    public static IEndpointRouteBuilder MapStudyPlanSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/study-plan")
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapGet("", GetAsync)
            .WithName("GetStudyPlan")
            .Produces<StudyPlanResponseDto>(StatusCodes.Status200OK);

        group.MapPut("", PutAsync)
            .WithName("PutStudyPlan")
            .Produces<StudyPlanResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest);

        return app;
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        IStudentPlanInputsService service,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        var config = await service.GetAsync(studentId, ct).ConfigureAwait(false);
        return Results.Ok(MapToDto(config));
    }

    private static async Task<IResult> PutAsync(
        [FromBody] StudyPlanRequestDto req,
        ClaimsPrincipal user,
        IStudentPlanAggregateStore store,
        IStudentPlanInputsService service,
        ILogger<LoggerMarker> logger,
        CancellationToken ct)
    {
        var studentId = GetStudentId(user);
        if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

        var now = DateTimeOffset.UtcNow;
        if (!TryValidate(req, now, out var err))
        {
            return Results.BadRequest(new CenaError(
                ErrorCodes.CENA_INTERNAL_VALIDATION, err,
                ErrorCategory.Validation, null, null));
        }

        if (req!.DeadlineUtc is { } deadline)
        {
            await store.AppendAsync(
                studentId,
                new ExamDateSet_V1(studentId, deadline, now),
                ct).ConfigureAwait(false);
        }

        if (req.WeeklyBudgetHours is { } weeklyHours)
        {
            await store.AppendAsync(
                studentId,
                new WeeklyTimeBudgetSet_V1(studentId, TimeSpan.FromHours(weeklyHours), now),
                ct).ConfigureAwait(false);
        }

        logger.LogInformation(
            "[STUDY_PLAN] student={StudentId} deadlineSet={DeadlineSet} weeklyHoursSet={WeeklyHoursSet}",
            studentId,
            req.DeadlineUtc.HasValue,
            req.WeeklyBudgetHours.HasValue);

        var updated = await service.GetAsync(studentId, ct).ConfigureAwait(false);
        return Results.Ok(MapToDto(updated));
    }

    // ── Validation (exposed internal for unit tests) ─────────────────────

    internal static bool TryValidate(StudyPlanRequestDto? req, DateTimeOffset nowUtc, out string error)
    {
        error = "";
        if (req is null) { error = "Request body is required."; return false; }

        if (req.DeadlineUtc is null && req.WeeklyBudgetHours is null)
        {
            error = "At least one of deadlineUtc or weeklyBudgetHours must be provided.";
            return false;
        }

        if (req.DeadlineUtc is { } d)
        {
            if (d <= nowUtc + MinDeadlineLead)
            {
                error = $"deadlineUtc must be more than {MinDeadlineLead.TotalDays:F0} days in the future.";
                return false;
            }
        }

        if (req.WeeklyBudgetHours is { } h)
        {
            if (h < MinWeeklyBudget.TotalHours || h > MaxWeeklyBudget.TotalHours)
            {
                error =
                    $"weeklyBudgetHours must be between {MinWeeklyBudget.TotalHours:F0} " +
                    $"and {MaxWeeklyBudget.TotalHours:F0} hours (inclusive).";
                return false;
            }
        }

        return true;
    }

    internal static StudyPlanResponseDto MapToDto(StudentPlanConfig c) =>
        new(
            DeadlineUtc: c.DeadlineUtc,
            WeeklyBudgetHours: c.WeeklyBudget?.TotalHours,
            UpdatedAt: c.UpdatedAt);

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id")
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

/// <summary>PUT body. Both fields optional; at least one required.</summary>
/// <param name="DeadlineUtc">Target exam date (UTC). Must be &gt; now+7d.</param>
/// <param name="WeeklyBudgetHours">Weekly study commitment in hours. Must be ∈ [1, 40].</param>
public sealed record StudyPlanRequestDto(
    DateTimeOffset? DeadlineUtc,
    double? WeeklyBudgetHours);

/// <summary>GET / PUT response. All fields null when the student has not set a plan.</summary>
public sealed record StudyPlanResponseDto(
    DateTimeOffset? DeadlineUtc,
    double? WeeklyBudgetHours,
    DateTimeOffset? UpdatedAt);
