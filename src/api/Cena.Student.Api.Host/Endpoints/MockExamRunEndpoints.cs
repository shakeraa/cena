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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Student.Api.Host.Endpoints;

public static class MockExamRunEndpoints
{
    public static IEndpointRouteBuilder MapMockExamRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/exam-prep/runs")
            .WithTags("ExamPrepMockRuns")
            .RequireAuthorization()
            // PRR-302 — exam-prep specific limiter (30/min/user) instead
            // of the broader 60/min api shared with other endpoints.
            // The runner emits ~25 calls/min during a busy multi-part
            // exam; 30 leaves headroom while preventing scripted-loop
            // abuse from pinning the runner pool.
            .RequireRateLimiting("exam-prep");

        // Phase 1G — runner feature flag (Cena:ExamPrep:RunnerEnabled).
        // Default ON; admin can flip to OFF via env var
        // Cena__ExamPrep__RunnerEnabled=false to drain the runner without
        // a redeploy. Returns 503 with a structured error when off so
        // the SPA shows a banner instead of crashing.
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var enabled = config.GetValue<bool?>("Cena:ExamPrep:RunnerEnabled") ?? true;
            if (!enabled)
            {
                return Results.Json(
                    new
                    {
                        error = "Mock-exam runner is disabled.",
                        code = "EXAM_PREP_RUNNER_DISABLED",
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            return await next(ctx);
        });

        // GET /feature-flags — Phase 3 #9 SPA-cached probe.
        // PRR-282 — tenant-scoped so a future per-institute kill-switch
        // can deny on a specific tenant's runner without touching
        // global config. Today the tenant override is a config lookup;
        // future Marten-doc-backed override is filed in PRR-302.
        app.MapGet("/api/me/exam-prep/feature-flags", (
            HttpContext ctx,
            IConfiguration cfg) =>
        {
            // Global flag.
            var globalEnabled = cfg.GetValue<bool?>("Cena:ExamPrep:RunnerEnabled") ?? true;

            // Per-tenant override (PRR-282). Convention:
            //   Cena:ExamPrep:Tenants:{tenantId}:RunnerEnabled = false
            // disables the runner for that tenant only. The student's
            // tenant claim drives the lookup. An admin can flip a
            // single-tenant override without affecting other tenants.
            var tenantId = ctx.User.FindFirst("tenant_id")?.Value
                ?? ctx.User.FindFirst("school_id")?.Value
                ?? ctx.User.FindFirst("institute_id")?.Value;
            var perTenantEnabled = string.IsNullOrEmpty(tenantId)
                ? (bool?)null
                : cfg.GetValue<bool?>($"Cena:ExamPrep:Tenants:{tenantId}:RunnerEnabled");

            var enabled = (globalEnabled, perTenantEnabled) switch
            {
                (false, _) => false,             // global off → off everywhere
                (_, false) => false,             // tenant override off → off for this tenant
                _          => true,
            };

            return Results.Ok(new
            {
                runnerEnabled = enabled,
                tenantOverride = perTenantEnabled is not null,
            });
        }).RequireAuthorization().RequireRateLimiting("api");

        // POST /  — start a run
        group.MapPost("/", async (
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            StartMockExamRunRequest request,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            // Phase 3 #5 — IDOR / claim-mismatch guard. Other endpoints
            // call this to ensure the bearer token's claims actually
            // identify the student. Throws on mismatch (fast-fail).
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

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
            // Phase 3 #5 — IDOR / claim-mismatch guard. Other endpoints
            // call this to ensure the bearer token's claims actually
            // identify the student. Throws on mismatch (fast-fail).
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

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
            // Phase 3 #5 — IDOR / claim-mismatch guard. Other endpoints
            // call this to ensure the bearer token's claims actually
            // identify the student. Throws on mismatch (fast-fail).
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

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
            // Phase 3 #5 — IDOR / claim-mismatch guard. Other endpoints
            // call this to ensure the bearer token's claims actually
            // identify the student. Throws on mismatch (fast-fail).
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

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

        // POST /{runId}/answers (bulk)  — Phase 3 #8
        group.MapPost("/{runId}/answers", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            SubmitAnswersBulkRequest request,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            try
            {
                var state = await service.SubmitAnswersBulkAsync(studentId, runId, request, ct);
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
            // Phase 3 #5 — IDOR / claim-mismatch guard. Other endpoints
            // call this to ensure the bearer token's claims actually
            // identify the student. Throws on mismatch (fast-fail).
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            try
            {
                var result = await service.SubmitAsync(studentId, runId, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /{runId}/pause + POST /{runId}/resume — PRR-287
        // Save-and-resume. Practice-mode affordance: real Ministry exam
        // day doesn't pause, but exam-prep practice often spans
        // multiple sessions. Pause stops the deadline countdown;
        // resume re-arms it with paused-duration added to the budget.
        group.MapPost("/{runId}/pause", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            try
            {
                return Results.Ok(await service.PauseAsync(studentId, runId, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapPost("/{runId}/resume", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            try
            {
                return Results.Ok(await service.ResumeAsync(studentId, runId, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // GET /history?examCode=...&paperCode=...&limit=...  — PRR-294
        // Recent submitted runs for this student. Powers the
        // "your last N runs on this paper" trend card on the result
        // page. Returns empty list (not 404) when no prior runs.
        group.MapGet("/history", async (
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            string? examCode,
            string? paperCode,
            int? limit,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var runs = await service.GetRecentRunsAsync(
                studentId, examCode, paperCode, limit ?? 5, ct);
            return Results.Ok(new { runs });
        });

        // POST /{runId}/regrade  — PRR-298. Re-runs the grader against
        // the current canonical answers; useful when a seeded answer
        // is corrected after submit. Original Submitted_V2 event is
        // preserved.
        group.MapPost("/{runId}/regrade", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            try
            {
                var result = await service.RegradeAsync(studentId, runId, ct);
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
            // Phase 3 #5 — IDOR / claim-mismatch guard. Other endpoints
            // call this to ensure the bearer token's claims actually
            // identify the student. Throws on mismatch (fast-fail).
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var result = await service.GetResultAsync(studentId, runId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // POST /{runId}/visibility  — Phase-4 #1. Real-exam fidelity:
        // the runner page reports document.visibilityState changes here
        // so the student stream gets ExamVisibilityWarning_V1 events
        // matching what the Ministry would proctor for tab-switches.
        group.MapPost("/{runId}/visibility", async (
            string runId,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            VisibilityEventReport report,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            try
            {
                var state = await service.ReportVisibilityEventAsync(studentId, runId, report, ct);
                return Results.Ok(state);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        });

        // GET /{runId}/question/{qid}  — Phase 1D preview (prompt + topic
        // + bloom). The runner uses this to render question stems in the
        // Part-B picker so students don't pick blind. Delivery gate
        // applies (Ministry-derived items would 403 here).
        group.MapGet("/{runId}/question/{qid}", async (
            string runId,
            string qid,
            HttpContext ctx,
            [FromServices] IMockExamRunService service,
            CancellationToken ct) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId)) return Results.Unauthorized();
            // Phase 3 #5 — IDOR / claim-mismatch guard. Other endpoints
            // call this to ensure the bearer token's claims actually
            // identify the student. Throws on mismatch (fast-fail).
            Cena.Infrastructure.Auth.ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            var preview = await service.GetQuestionPreviewAsync(studentId, runId, qid, ct);
            return preview is null ? Results.NotFound() : Results.Ok(preview);
        });

        return app;
    }

    private static string? GetStudentId(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value;
}
