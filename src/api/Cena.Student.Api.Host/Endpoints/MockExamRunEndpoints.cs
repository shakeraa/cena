// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) HTTP endpoints
//
// Activates the Bagrut exam-prep runner. The student SPA hits this surface
// to start a run, save Part-B selection, save per-question answers, and
// submit for grading.
//
// Auth: standard student-bearer-token (RequireAuthorization defaults).
// Rate-limit: shared "api" policy.
//
// Lifecycle (happy path):
//   1. POST /api/me/exam-prep/runs
//        body: { examCode: "806", paperCode?: "035582" }
//        → 200 { runId, partAQuestionIds[], partBQuestionIds[], deadline, ... }
//   2. GET /api/me/exam-prep/runs/{runId}                 (resume / poll)
//   3. POST /api/me/exam-prep/runs/{runId}/select-part-b  body: { selectedQuestionIds[] }
//   4. POST /api/me/exam-prep/runs/{runId}/answer         body: { questionId, answer }
//   5. POST /api/me/exam-prep/runs/{runId}/submit         (idempotent)
//   6. GET  /api/me/exam-prep/runs/{runId}/result         (mark sheet)
//
// All errors map to:
//   400 — bad input (unsupported examCode, wrong Part-B count, post-submit edits)
//   401 — missing/invalid bearer
//   403 — run belongs to another student
//   404 — runId not found for this student
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Assessment;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

public static class MockExamRunEndpoints
{
    public static IEndpointRouteBuilder MapMockExamRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/exam-prep/runs")
            .WithTags("ExamPrepMockRuns")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        // POST /  — start a run
        group.MapPost("/", async (
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            StartMockExamRunRequest request,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

            try
            {
                var resp = await service.StartAsync(studentId, request, ct);
                return Results.Ok(resp);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // GET /{runId}  — read state
        group.MapGet("/{runId}", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

            var state = await service.GetStateAsync(studentId, runId, ct);
            return state is null ? Results.NotFound() : Results.Ok(state);
        });

        // POST /{runId}/select-part-b
        group.MapPost("/{runId}/select-part-b", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            SelectPartBRequest request,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

            try
            {
                var state = await service.SelectPartBAsync(studentId, runId, request, ct);
                return Results.Ok(state);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /{runId}/answer
        group.MapPost("/{runId}/answer", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            SubmitAnswerRequest request,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

            try
            {
                var state = await service.SubmitAnswerAsync(studentId, runId, request, ct);
                return Results.Ok(state);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /{runId}/submit
        group.MapPost("/{runId}/submit", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

            try
            {
                var result = await service.SubmitAsync(studentId, runId, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // GET /{runId}/result
        group.MapGet("/{runId}/result", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();

            var result = await service.GetResultAsync(studentId, runId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }

    private static string? GetStudentId(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value;
}
