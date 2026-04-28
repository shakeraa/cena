// =============================================================================
// Cena Platform — IngestionJobDocument
//
// Marten document tracking long-running ingestion operations (Bagrut PDF,
// cloud-dir batch). Lifecycle:
//
//   queued → running → completed | failed | cancelled
//
// Created by IngestionJobService.EnqueueAsync, picked up by
// IngestionJobRunnerHostedService, terminal state surfaced to the admin
// SPA via the IngestionJobsDrawer (3-second poll on /jobs?status=…).
//
// Lives in Cena.Infrastructure.Documents so the actor-host Marten schema
// reconciler (the host with AutoCreate=CreateOrUpdate authority) can
// register the schema. admin-api boots with AutoCreate=None and waits
// on actor-host's healthcheck.
// =============================================================================

namespace Cena.Infrastructure.Documents;

public enum IngestionJobType
{
    Bagrut,
    CloudDir,
    GenerateVariants,
}

public enum IngestionJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed class IngestionJobDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public IngestionJobType Type { get; set; }
    public string Label { get; set; } = "";
    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Queued;

    public int ProgressPct { get; set; }
    public string? ProgressMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }
    public string? CreatedBy { get; set; }
    public bool CancelRequested { get; set; }

    // Strategy-specific payload + result. Stored as serialized JSON so
    // the doc shape stays stable across job-type additions.
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }

    // Append-only log buffer surfaced to the SPA via /jobs/{id}/logs.
    // Capped at LogCap entries to keep doc rows small; oldest entries
    // are dropped when the cap is hit. Strategies write via
    // IJobProgressReporter.LogAsync.
    public List<JobLogEntry> Logs { get; set; } = new();
    public const int LogCap = 200;
}

public sealed class JobLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "info";  // info | warn | error
    public string Message { get; set; } = "";
}
