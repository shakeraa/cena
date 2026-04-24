// =============================================================================
// Cena Platform — Hint-Ladder Endpoint (prr-203, ADR-0045)
//
// POST /api/sessions/{sessionId}/question/{questionId}/hint/next
//
// Server-authoritative hint-ladder progression. Per ADR-0045 §3 the ladder
// is:
//
//   L1 — template only (no LLM)            — IL1TemplateHintGenerator
//   L2 — Haiku "here's the method"         — IL2HaikuHintGenerator
//   L3 — Sonnet "here's a worked example"  — IL3WorkedExampleHintGenerator
//
// The client has no request body and cannot request a specific rung — the
// server advances the per-(session, question) rung state held on the
// LearningSessionQueueProjection (prr-203) by exactly one rung per call,
// up to the max rung (3). A client that tries to "skip to L2" by calling
// the endpoint twice is given L1 then L2, in order.
//
// Fallback chain (persona-sre): failures at L2 and L3 degrade gracefully
// to the L1 static template with RungSource = "template-fallback" so the
// client always gets a useful hint and the admin dashboard can see the
// degradation. The ladder never errors on LLM outage.
//
// SLOs (persona-sre, per ADR-0045 DoD):
//   L1 p99 ≤  50ms
//   L2 p99 ≤ 800ms
//   L3 p99 ≤ 2500ms
//
// Architecture ratchet: HintLadderEndpointUsesLadderTest asserts this
// endpoint file wires IHintLadderOrchestrator and does NOT import the
// old inline IHintGenerator path that SessionEndpoints.cs still uses for
// the /hint (non-ladder) route. Keeping the two endpoints distinct lets
// the deprecated inline route be retired in a follow-up PR without
// touching ladder logic.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Accommodations;
using Cena.Actors.Hints;
using Cena.Actors.Projections;
using Cena.Actors.Serving;
using Cena.Api.Contracts.Sessions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Api.Host.Endpoints;

public static class HintLadderEndpoint
{
    private sealed class HintLadderLogMarker { }

    public static IEndpointRouteBuilder MapHintLadderEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions")
            .WithTags("Sessions")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapPost("/{sessionId}/question/{questionId}/hint/next", async (
            string sessionId,
            string questionId,
            HttpContext ctx,
            IDocumentStore store,
            [FromServices] IQuestionBank questionBank,
            [FromServices] IHintLadderOrchestrator orchestrator,
            [FromServices] IAccommodationProfileService accommodationProfiles,
            ILogger<HintLadderLogMarker> logger) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            // 1. Verify the caller owns the session (tenant + ownership).
            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.LightweightSession();

            // 2. Load the session queue projection; 404 if missing.
            var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
            if (queue is null)
                return Results.NotFound(new { error = "Session not found" });

            // 3. Verify the caller owns this session.
            if (queue.StudentId != studentId)
                return Results.Forbid();

            // 4. Verify the question is the one currently in flight. The
            //    ladder is scoped to the current question — a stale client
            //    asking about a completed question gets a 400 rather than
            //    silently rewinding ladder state.
            if (queue.CurrentQuestionId != questionId)
            {
                logger.LogWarning(
                    "[SIEM] HintLadderRequested: question mismatch for student "
                    + "{StudentId}, session {SessionId}. Expected {Expected}, got {Actual}",
                    studentId, sessionId, queue.CurrentQuestionId, questionId);
                return Results.BadRequest(new
                {
                    error = "Question is not the current active question"
                });
            }

            // 5. Load the question for L2/L3 prompt context.
            var questionDoc = await questionBank.GetQuestionAsync(questionId);
            if (questionDoc is null)
                return Results.NotFound(new { error = "Question not found" });

            // 6. Read the current rung from session state. 0 = never served;
            //    1/2/3 = highest rung the student has already seen for THIS
            //    question in THIS session. Server-authoritative: the client
            //    has no input here.
            var currentRung = queue.LadderRungByQuestion.GetValueOrDefault(questionId, 0);

            if (currentRung >= HintLadderOrchestrator.MaxRung)
            {
                // Student has already exhausted the ladder for this question.
                // Re-serve L3 deterministically with NextRungAvailable=false
                // so the UI retires the "next hint" affordance — no new LLM
                // call; the orchestrator will return the static fallback copy
                // when the L3 generator declines (cap exhausted or otherwise).
                logger.LogDebug(
                    "HintLadder: rung already at max (3) for session {SessionId} "
                    + "question {QuestionId}; replaying L3.",
                    sessionId, questionId);
            }

            // 7. Resolve the student's accommodation profile for the L1
            //    governor. Failure is fail-open — LD-anxious rewrite is an
            //    accessibility enhancement, not a correctness requirement.
            AccommodationProfile? profile = null;
            try
            {
                profile = await accommodationProfiles.GetCurrentAsync(studentId, ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Accommodation profile lookup failed for student {StudentId}; "
                    + "L1 governor will be skipped for this call.",
                    studentId);
            }

            // 8. Institute scope — read from claims the same way the inline
            //    hint endpoint does. "unknown" semantics live in the cost-
            //    metric layer (prr-046); the orchestrator passes through
            //    whatever we hand it.
            var instituteId =
                ctx.User.FindFirstValue("institute_id")
                ?? ctx.User.FindFirstValue("tenant_id")
                ?? string.Empty;

            // 9. Pull the first prerequisite name if the question carries one.
            //    The inline hint endpoint already works with names resolved
            //    upstream via the session actor; for REST we do not traverse
            //    the graph (PSI=1.0 in the non-ladder path) so we just thread
            //    whatever is on the QuestionDocument directly.
            IReadOnlyList<string> prereqNames = Array.Empty<string>();
            if (questionDoc.Prerequisites is { Count: > 0 })
            {
                // Use concept ids as names — the L1/L2/L3 generators tolerate
                // either (they only need a label the student can re-read).
                prereqNames = new[] { questionDoc.Prerequisites[0] };
            }

            var input = new HintLadderInput(
                SessionId: sessionId,
                QuestionId: questionId,
                ConceptId: questionDoc.ConceptId ?? string.Empty,
                Subject: questionDoc.Subject,
                QuestionStem: questionDoc.Prompt,
                Explanation: questionDoc.Explanation,
                Methodology: null,
                PrerequisiteConceptNames: prereqNames,
                InstituteId: string.IsNullOrWhiteSpace(instituteId) ? null : instituteId);

            // 10. Advance the ladder. The orchestrator owns the tier dispatch
            //     + fallback chain; the endpoint just persists the new rung.
            var output = await orchestrator.AdvanceAsync(
                input, currentRung, profile, ctx.RequestAborted);

            // 11. Persist the new rung state. Idempotent by design — the rung
            //     never goes backwards, so concurrent clients cannot corrupt
            //     each other's state.
            queue.LadderRungByQuestion[questionId] = Math.Max(currentRung, output.Rung);
            session.Store(queue);
            await session.SaveChangesAsync();

            logger.LogInformation(
                "[SIEM] HintLadderRung: student {StudentId} session {SessionId} "
                + "question {QuestionId} rung {Rung} source {RungSource}",
                studentId, sessionId, questionId, output.Rung, output.RungSource);

            return Results.Ok(new HintLadderResponseDto(
                Rung: output.Rung,
                Body: output.Body,
                RungSource: output.RungSource,
                MaxRungReached: output.MaxRungReached,
                NextRungAvailable: output.NextRungAvailable));
        })
        .WithName("RequestNextHint")
        .Produces<HintLadderResponseDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // GetStudentId mirrors SessionEndpoints.GetStudentId — duplicated here
    // so this endpoint file stays independent and the architecture ratchet
    // can assert its boundary without cross-file compilation coupling.
    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");
}
