// =============================================================================
// Cena Platform — Ingestion Job DTOs
//
// Async tracking surface for long-running ingestion operations (Bagrut
// PDF, cloud-dir batch). Endpoints under /api/admin/ingestion/jobs.
// =============================================================================

namespace Cena.Api.Contracts.Admin.Ingestion;

public sealed record IngestionJobSummary(
    string Id,
    string Type,            // "bagrut" | "cloud_dir"
    string Label,           // human-readable title (e.g. "35581-Q.pdf · math-5u-1")
    string Status,          // "queued" | "running" | "completed" | "failed" | "cancelled"
    int ProgressPct,        // 0-100
    string? ProgressMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    string? CreatedBy,
    bool CancelRequested);

public sealed record IngestionJobDetail(
    string Id,
    string Type,
    string Label,
    string Status,
    int ProgressPct,
    string? ProgressMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    string? CreatedBy,
    bool CancelRequested,
    string? ResultJson);

public sealed record IngestionJobListResponse(
    IReadOnlyList<IngestionJobSummary> Jobs,
    int Total);

public sealed record EnqueueJobResponse(
    string JobId);
