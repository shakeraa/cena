// =============================================================================
// Cena Platform — IngestionJobs Endpoints
//
// Async tracking surface for long-running ingestion operations:
//
//   GET    /api/admin/ingestion/jobs?status=queued,running&limit=50
//   GET    /api/admin/ingestion/jobs/{id}
//   POST   /api/admin/ingestion/jobs/{id}/cancel
//   DELETE /api/admin/ingestion/jobs/{id}
//   POST   /api/admin/ingestion/jobs/bagrut       (multipart, returns {jobId})
//   POST   /api/admin/ingestion/jobs/cloud-dir    (json, returns {jobId})
//
// Drives the IngestionJobsDrawer in the admin SPA.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api.Ingestion;

public static class IngestionJobsEndpoints
{
    public static IEndpointRouteBuilder MapIngestionJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion/jobs")
            .WithTags("Ingestion Jobs")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        group.MapGet("/", async (
            string? status,
            int? limit,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            IngestionJobStatus? filter = null;
            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<IngestionJobStatus>(status, ignoreCase: true, out var parsed))
            {
                filter = parsed;
            }
            var (rows, total) = await jobs.ListAsync(filter, Math.Clamp(limit ?? 50, 1, 200), ct);
            return Results.Ok(new IngestionDto.IngestionJobListResponse(
                rows.Select(IngestionJobMapper.ToSummary).ToList(),
                total));
        }).WithName("ListIngestionJobs")
            .Produces<IngestionDto.IngestionJobListResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id}", async (
            string id,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            var doc = await jobs.GetAsync(id, ct);
            return doc is null
                ? Results.NotFound(new { error = "Job not found" })
                : Results.Ok(IngestionJobMapper.ToDetail(doc));
        }).WithName("GetIngestionJob")
            .Produces<IngestionDto.IngestionJobDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // Feature-flag readback for the SPA. Lets the Generate Variants
        // dialog render a 'Disabled pending legal sign-off' banner up
        // front instead of failing on submit (ADR-0059 §15.5 + PRR-249
        // gating per claude-code directive m_ed53adeb90d2).
        group.MapGet("/feature-flags", (IConfiguration cfg) =>
        {
            return Results.Ok(new
            {
                bagrutSeedToLlmEnabled =
                    cfg.GetValue<bool>("Cena:Variants:BagrutSeedToLlmEnabled"),
                bagrutSeedDisabledReason =
                    "Source-anchored variant generation is implementation-complete but gated " +
                    "on PRR-249 legal-delta memo sign-off (ADR-0059 §15.5). See docs/engineering/feature-flags.md.",
            });
        }).WithName("GetIngestionJobFeatureFlags")
            .Produces(StatusCodes.Status200OK);

        // Tail of the per-job log stream. SPA polls this every 1.5s while
        // the selected job is active; renders into the bottom-half log
        // panel of the IngestionJobsDrawer.
        group.MapGet("/{id}/logs", async (
            string id,
            int? tail,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            var entries = await jobs.GetLogsAsync(id, tail ?? 200, ct);
            return Results.Ok(new
            {
                jobId = id,
                count = entries.Count,
                entries = entries.Select(e => new
                {
                    timestamp = e.Timestamp,
                    level = e.Level,
                    message = e.Message,
                }),
            });
        }).WithName("GetIngestionJobLogs")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/{id}/cancel", async (
            string id,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            var ok = await jobs.RequestCancelAsync(id, ct);
            return ok
                ? Results.Ok(new { cancelled = true })
                : Results.BadRequest(new { error = "Job not found or already terminal" });
        }).WithName("CancelIngestionJob")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id}", async (
            string id,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            var ok = await jobs.DeleteAsync(id, ct);
            return ok
                ? Results.NoContent()
                : Results.BadRequest(new { error = "Job not found or still in-flight" });
        }).WithName("DeleteIngestionJob")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        // POST — enqueue a Bagrut job from a multipart upload. Returns
        // {jobId} immediately; the runner does the OCR off the request
        // thread and the SPA polls /jobs/{id}.
        group.MapPost("/bagrut", async (
            HttpRequest request,
            ClaimsPrincipal user,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data body required" });

            var form = await request.ReadFormAsync(ct);
            var examCode = form["examCode"].ToString().Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(examCode) || !BagrutIngestHandler.ExamCodeRx.IsMatch(examCode))
            {
                return Results.BadRequest(new
                {
                    error = "examCode must match ^[a-z0-9-_]{3,64}$"
                });
            }

            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file (PDF) is required" });
            if (file.Length > BagrutIngestHandler.MaxPdfBytes)
                return Results.BadRequest(new { error = $"file exceeds {BagrutIngestHandler.MaxPdfBytes / (1024*1024)} MB" });

            using var ms = new MemoryStream(capacity: (int)Math.Min(file.Length, BagrutIngestHandler.MaxPdfBytes));
            await using (var s = file.OpenReadStream())
                await s.CopyToAsync(ms, ct);

            var uploadedBy = form["uploadedBy"].ToString();
            if (string.IsNullOrWhiteSpace(uploadedBy))
            {
                uploadedBy = user.FindFirst("user_id")?.Value
                              ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? "unknown-admin";
            }

            var label = string.IsNullOrWhiteSpace(file.FileName)
                ? $"bagrut · {examCode}"
                : $"{file.FileName} · {examCode}";

            // Ministry metadata — when present, the strategy populates
            // BagrutCorpusItemDocument so ADR-0043 isomorph rejector has
            // reference text to compare against during variant generation.
            // Backwards compatible: missing fields disable only the corpus
            // side-effect; ingest + draft persistence still run.
            var payload = new BagrutJobPayload(
                ExamCode: examCode,
                UploadedBy: uploadedBy,
                SourceFilename: file.FileName,
                FileBytes: ms.ToArray(),
                MinistrySubjectCode: NullIfBlank(form["ministrySubjectCode"].ToString()),
                MinistryQuestionPaperCode: NullIfBlank(form["ministryQuestionPaperCode"].ToString()),
                Units: int.TryParse(form["units"], out var u) ? u : null,
                Year: int.TryParse(form["year"], out var y) ? y : null,
                TopicId: NullIfBlank(form["topicId"].ToString()));

            var id = await jobs.EnqueueAsync(IngestionJobType.Bagrut, label, payload, uploadedBy, ct);
            return Results.Accepted($"/api/admin/ingestion/jobs/{id}",
                new IngestionDto.EnqueueJobResponse(id));
        }).DisableAntiforgery()
            .WithName("EnqueueBagrutJob")
            .Produces<IngestionDto.EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // POST — enqueue a "generate variants from Bagrut draft" job (option 2).
        // Body: GenerateVariantsJobPayload. Returns {jobId} immediately.
        // Drawer surfaces progress; result.sample shows the first 5 variants.
        group.MapPost("/generate-variants", async (
            GenerateVariantsJobPayload payload,
            ClaimsPrincipal user,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(payload.DraftId))
                return Results.BadRequest(new { error = "draftId required" });
            if (payload.Count < 1 || payload.Count > 20)
                return Results.BadRequest(new { error = "count must be between 1 and 20" });
            if (string.IsNullOrWhiteSpace(payload.Subject)
                || string.IsNullOrWhiteSpace(payload.Grade)
                || string.IsNullOrWhiteSpace(payload.Language))
            {
                return Results.BadRequest(new { error = "subject + grade + language required" });
            }

            var label = $"Variants × {payload.Count} from {payload.DraftId.Substring(0, Math.Min(12, payload.DraftId.Length))}";
            var createdBy = user.FindFirst("user_id")?.Value
                            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var id = await jobs.EnqueueAsync(IngestionJobType.GenerateVariants, label,
                payload, createdBy, ct);
            return Results.Accepted($"/api/admin/ingestion/jobs/{id}",
                new IngestionDto.EnqueueJobResponse(id));
        }).WithName("EnqueueGenerateVariantsJob")
            .Produces<IngestionDto.EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // POST — enqueue a cloud-dir job. Body: { provider, bucketOrPath,
        // fileKeys?, prefix? }. Returns {jobId} immediately.
        group.MapPost("/cloud-dir", async (
            CloudDirJobPayload payload,
            ClaimsPrincipal user,
            IIngestionJobService jobs,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(payload.Provider)
                || string.IsNullOrWhiteSpace(payload.BucketOrPath))
            {
                return Results.BadRequest(new { error = "provider + bucketOrPath required" });
            }
            var label = $"{payload.Provider} · {payload.BucketOrPath}"
                + (string.IsNullOrWhiteSpace(payload.Prefix) ? "" : $"/{payload.Prefix}");
            var createdBy = user.FindFirst("user_id")?.Value
                            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var id = await jobs.EnqueueAsync(IngestionJobType.CloudDir, label, payload, createdBy, ct);
            return Results.Accepted($"/api/admin/ingestion/jobs/{id}",
                new IngestionDto.EnqueueJobResponse(id));
        }).WithName("EnqueueCloudDirJob")
            .Produces<IngestionDto.EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
