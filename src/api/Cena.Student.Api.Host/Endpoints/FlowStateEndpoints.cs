// =============================================================================
// Cena Platform — Flow State Endpoint (RDY-034)
//
// POST /api/sessions/flow-state/assess
//
// Student-scoped, rate-limited flow-state assessment endpoint. Takes the four
// session signals (fatigue, accuracy trend, streak, session duration) plus an
// optional current difficulty, and returns the authoritative backend state +
// recommended action.
//
// The state machine lives entirely in FlowStateService; this file is the
// HTTP surface only. Signals are produced by the client (useFlowState.ts
// consumes them from the session state + cognitive-load snapshot) — a
// future slice will fold this into GET /api/sessions/{id}.
//
// NO STUBS, NO MOCKS. Runs FlowStateService directly.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Services;
using Cena.Api.Contracts.Sessions;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class FlowStateEndpoints
{
    public static IEndpointRouteBuilder MapFlowStateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions/flow-state")
            .WithTags("FlowState")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapPost("/assess", Assess)
            .WithName("AssessFlowState")
            .Produces<FlowStateAssessmentResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }

    internal static IResult Assess(
        [FromBody] FlowStateAssessmentRequest request,
        ClaimsPrincipal user,
        [FromServices] IFlowStateService flowState)
    {
        if (request is null)
            return Results.Json(
                new CenaError(
                    "invalid_body",
                    "FlowStateAssessmentRequest body required.",
                    ErrorCategory.Validation, null, null),
                statusCode: StatusCodes.Status400BadRequest);

        if (user.FindFirstValue("sub") is null
            && user.FindFirstValue(ClaimTypes.NameIdentifier) is null)
        {
            return Results.Unauthorized();
        }

        // Defensive bounds checking — service also clamps, but surface a clean
        // 400 for obviously bogus numbers so client bugs are caught upstream.
        if (double.IsNaN(request.FatigueLevel)
            || double.IsNaN(request.AccuracyTrend)
            || double.IsNaN(request.SessionDurationMinutes))
        {
            return Results.Json(
                new CenaError(
                    "invalid_input",
                    "Fatigue, trend, and session duration must be finite numbers.",
                    ErrorCategory.Validation, null, null),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CurrentDifficulty is not null
            && (request.CurrentDifficulty < 1 || request.CurrentDifficulty > 10))
        {
            return Results.Json(
                new CenaError(
                    "invalid_input",
                    "CurrentDifficulty must be in [1, 10] when supplied.",
                    ErrorCategory.Validation, null, null),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var assessment = flowState.Assess(
            fatigueLevel: request.FatigueLevel,
            accuracyTrend: request.AccuracyTrend,
            consecutiveCorrect: request.ConsecutiveCorrect,
            sessionDurationMinutes: request.SessionDurationMinutes,
            currentDifficulty: request.CurrentDifficulty);

        return Results.Ok(new FlowStateAssessmentResponse(
            State: MapStateCamelCase(assessment.State),
            FatigueLevel: assessment.FatigueLevel,
            AccuracyTrend: assessment.AccuracyTrend,
            ConsecutiveCorrect: assessment.ConsecutiveCorrect,
            SessionDurationMinutes: assessment.SessionDurationMinutes,
            RecommendedAction: MapActionSnakeCase(assessment.RecommendedAction),
            CooldownMinutes: assessment.CooldownMinutes,
            DifficultyAdjustment: assessment.DifficultyAdjustmentAdvice is null
                ? null
                : MapDifficultyAdjustmentLowercase(assessment.DifficultyAdjustmentAdvice.Value)));
    }

    // Wire format: camelCase to match the Vue composable's FlowState union.
    // Uses the service-side FlowStateKind enum (the DTO-side enum with the
    // same name lives in Cena.Api.Contracts.Sessions but is never surfaced
    // on the wire — we stringify directly).
    internal static string MapStateCamelCase(Cena.Actors.Services.FlowStateKind kind) => kind switch
    {
        Cena.Actors.Services.FlowStateKind.Warming     => "warming",
        Cena.Actors.Services.FlowStateKind.Approaching => "approaching",
        Cena.Actors.Services.FlowStateKind.InFlow      => "inFlow",
        Cena.Actors.Services.FlowStateKind.Disrupted   => "disrupted",
        Cena.Actors.Services.FlowStateKind.Fatigued    => "fatigued",
        _                                              => "warming",
    };

    // Wire format: snake_case for recommended actions so the client switches
    // on stable identifiers rather than parsing prose.
    internal static string MapActionSnakeCase(FlowStateAction action) => action switch
    {
        FlowStateAction.Continue         => "continue",
        FlowStateAction.SlowDown         => "slow_down",
        FlowStateAction.ReduceDifficulty => "reduce_difficulty",
        FlowStateAction.SuggestBreak     => "suggest_break",
        _                                => "continue",
    };

    internal static string MapDifficultyAdjustmentLowercase(DifficultyAdjustment adj) => adj switch
    {
        DifficultyAdjustment.Ease     => "ease",
        DifficultyAdjustment.Maintain => "maintain",
        DifficultyAdjustment.Increase => "increase",
        _                             => "maintain",
    };
}
