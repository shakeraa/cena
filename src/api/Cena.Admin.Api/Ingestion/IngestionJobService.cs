// =============================================================================
// Cena Platform — IngestionJobService
//
// CRUD + enqueue surface over IngestionJobDocument. The runner
// (IngestionJobRunnerHostedService) reads a Channel<string> of job ids
// produced by EnqueueAsync; this service owns Marten persistence + the
// channel write.
//
// Concurrency: EnqueueAsync stores the doc and writes the id to the
// channel atomically from the caller's perspective — if Marten succeeds
// but the channel write fails (process shutdown), the doc stays in
// Queued and the runner will pick it up on the next boot via
// RehydrateAsync.
// =============================================================================

using System.Text.Json;
using System.Threading.Channels;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api.Ingestion;

public interface IIngestionJobService
{
    Task<string> EnqueueAsync(
        IngestionJobType type,
        string label,
        object payload,
        string? createdBy,
        CancellationToken ct = default);

    Task<IngestionJobDocument?> GetAsync(string id, CancellationToken ct = default);

    Task<(IReadOnlyList<IngestionJobDocument> jobs, int total)> ListAsync(
        IngestionJobStatus? statusFilter,
        int limit,
        CancellationToken ct = default);

    Task<bool> RequestCancelAsync(string id, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    // Runner-only: marks the job as Running and stamps StartedAt.
    Task MarkStartedAsync(string id, CancellationToken ct = default);

    // Runner-only: progress update (intermediate, non-terminal).
    Task UpdateProgressAsync(
        string id, int pct, string? message, CancellationToken ct = default);

    // Append a single log line. Capped at IngestionJobDocument.LogCap.
    Task AppendLogAsync(
        string id, string level, string message, CancellationToken ct = default);

    // Returns the trailing N log entries (newest last).
    Task<IReadOnlyList<JobLogEntry>> GetLogsAsync(
        string id, int tail, CancellationToken ct = default);

    // Runner-only: terminal state writer.
    Task MarkTerminalAsync(
        string id,
        IngestionJobStatus status,
        string? error,
        object? result,
        CancellationToken ct = default);

    // Runner-only: read leftover Queued/Running rows on boot for reload.
    Task<IReadOnlyList<string>> RehydrateAsync(CancellationToken ct = default);
}

public sealed class IngestionJobService : IIngestionJobService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly IDocumentStore _store;
    private readonly Channel<string> _channel;
    private readonly ILogger<IngestionJobService> _logger;

    public IngestionJobService(
        IDocumentStore store,
        Channel<string> channel,
        ILogger<IngestionJobService> logger)
    {
        _store = store;
        _channel = channel;
        _logger = logger;
    }

    public async Task<string> EnqueueAsync(
        IngestionJobType type,
        string label,
        object payload,
        string? createdBy,
        CancellationToken ct = default)
    {
        var doc = new IngestionJobDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Label = label,
            Status = IngestionJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOpts),
        };

        await using (var session = _store.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync(ct);
        }

        // Channel is unbounded; this completes synchronously.
        if (!_channel.Writer.TryWrite(doc.Id))
        {
            _logger.LogWarning(
                "IngestionJob {JobId} enqueued in Marten but channel write failed; will be picked up on next runner rehydrate",
                doc.Id);
        }

        _logger.LogInformation(
            "Ingestion job enqueued: id={JobId} type={Type} label={Label}",
            doc.Id, type, label);
        return doc.Id;
    }

    public async Task<IngestionJobDocument?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.LoadAsync<IngestionJobDocument>(id, ct);
    }

    public async Task<(IReadOnlyList<IngestionJobDocument> jobs, int total)> ListAsync(
        IngestionJobStatus? statusFilter,
        int limit,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var query = session.Query<IngestionJobDocument>();
        var filtered = statusFilter is { } s
            ? query.Where(j => j.Status == s)
            : query;
        var total = await filtered.CountAsync(ct);
        var jobs = await filtered
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return (jobs, total);
    }

    public async Task<bool> RequestCancelAsync(string id, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<IngestionJobDocument>(id, ct);
        if (doc is null) return false;
        if (doc.Status is IngestionJobStatus.Completed or IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled)
        {
            return false;
        }
        doc.CancelRequested = true;
        // Queued jobs that haven't started can transition straight to Cancelled.
        if (doc.Status == IngestionJobStatus.Queued)
        {
            doc.Status = IngestionJobStatus.Cancelled;
            doc.CompletedAt = DateTimeOffset.UtcNow;
            doc.ErrorMessage = "Cancelled before start";
        }
        session.Store(doc);
        await session.SaveChangesAsync(ct);
        _logger.LogInformation("Ingestion job cancel requested: id={JobId}", id);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<IngestionJobDocument>(id, ct);
        if (doc is null) return false;
        if (doc.Status is IngestionJobStatus.Queued or IngestionJobStatus.Running)
        {
            return false;
        }
        session.Delete<IngestionJobDocument>(id);
        await session.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkStartedAsync(string id, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<IngestionJobDocument>(id, ct);
        if (doc is null) return;
        doc.Status = IngestionJobStatus.Running;
        doc.StartedAt = DateTimeOffset.UtcNow;
        doc.ProgressMessage ??= "Starting…";
        session.Store(doc);
        await session.SaveChangesAsync(ct);
    }

    public async Task UpdateProgressAsync(
        string id, int pct, string? message, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<IngestionJobDocument>(id, ct);
        if (doc is null) return;
        doc.ProgressPct = Math.Clamp(pct, 0, 100);
        if (message is not null) doc.ProgressMessage = message;
        session.Store(doc);
        await session.SaveChangesAsync(ct);
    }

    public async Task AppendLogAsync(
        string id, string level, string message, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<IngestionJobDocument>(id, ct);
        if (doc is null) return;
        doc.Logs.Add(new JobLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = string.IsNullOrWhiteSpace(level) ? "info" : level,
            Message = message ?? "",
        });
        // Keep last LogCap entries.
        if (doc.Logs.Count > IngestionJobDocument.LogCap)
        {
            var drop = doc.Logs.Count - IngestionJobDocument.LogCap;
            doc.Logs.RemoveRange(0, drop);
        }
        session.Store(doc);
        await session.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JobLogEntry>> GetLogsAsync(
        string id, int tail, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<IngestionJobDocument>(id, ct);
        if (doc is null) return Array.Empty<JobLogEntry>();
        var t = Math.Clamp(tail, 1, IngestionJobDocument.LogCap);
        return doc.Logs.Count <= t
            ? doc.Logs
            : doc.Logs.Skip(doc.Logs.Count - t).ToList();
    }

    public async Task MarkTerminalAsync(
        string id,
        IngestionJobStatus status,
        string? error,
        object? result,
        CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<IngestionJobDocument>(id, ct);
        if (doc is null) return;
        doc.Status = status;
        doc.CompletedAt = DateTimeOffset.UtcNow;
        doc.ProgressPct = status == IngestionJobStatus.Completed ? 100 : doc.ProgressPct;
        if (error is not null) doc.ErrorMessage = error;
        if (result is not null)
        {
            doc.ResultJson = JsonSerializer.Serialize(result, JsonOpts);
        }
        // Closing log line so the per-job log stream has a clear
        // terminator instead of just trailing off mid-progress.
        doc.Logs.Add(new JobLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = status switch
            {
                IngestionJobStatus.Completed => "info",
                IngestionJobStatus.Cancelled => "warn",
                _ => "error",
            },
            Message = status switch
            {
                IngestionJobStatus.Completed => "Job completed.",
                IngestionJobStatus.Cancelled => "Job cancelled by user.",
                IngestionJobStatus.Failed    => $"Job failed: {error ?? "unknown error"}",
                _ => $"Job ended in status {status}.",
            },
        });
        if (doc.Logs.Count > IngestionJobDocument.LogCap)
            doc.Logs.RemoveRange(0, doc.Logs.Count - IngestionJobDocument.LogCap);
        session.Store(doc);
        await session.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> RehydrateAsync(CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var ids = await session.Query<IngestionJobDocument>()
            .Where(j => j.Status == IngestionJobStatus.Queued
                     || j.Status == IngestionJobStatus.Running)
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .ToListAsync(ct);
        // Running jobs that were mid-flight when the host died: flip them
        // back to Queued so the runner re-enters cleanly. The strategies
        // are idempotent (Bagrut SHA-256 dedup, cloud-dir SHA-256 dedup).
        if (ids.Count > 0)
        {
            await using var write = _store.LightweightSession();
            foreach (var id in ids)
            {
                var doc = await write.LoadAsync<IngestionJobDocument>(id, ct);
                if (doc is null) continue;
                if (doc.Status == IngestionJobStatus.Running)
                {
                    doc.Status = IngestionJobStatus.Queued;
                    doc.StartedAt = null;
                    doc.ProgressMessage = "Re-queued after host restart";
                    write.Store(doc);
                }
            }
            await write.SaveChangesAsync(ct);
        }
        return ids;
    }
}

// ----- Mapper to wire DTOs from documents -----

public static class IngestionJobMapper
{
    public static IngestionDto.IngestionJobSummary ToSummary(IngestionJobDocument doc) =>
        new(
            Id: doc.Id,
            Type: doc.Type.ToString().ToLowerInvariant(),
            Label: doc.Label,
            Status: doc.Status.ToString().ToLowerInvariant(),
            ProgressPct: doc.ProgressPct,
            ProgressMessage: doc.ProgressMessage,
            CreatedAt: doc.CreatedAt,
            StartedAt: doc.StartedAt,
            CompletedAt: doc.CompletedAt,
            ErrorMessage: doc.ErrorMessage,
            CreatedBy: doc.CreatedBy,
            CancelRequested: doc.CancelRequested);

    public static IngestionDto.IngestionJobDetail ToDetail(IngestionJobDocument doc) =>
        new(
            Id: doc.Id,
            Type: doc.Type.ToString().ToLowerInvariant(),
            Label: doc.Label,
            Status: doc.Status.ToString().ToLowerInvariant(),
            ProgressPct: doc.ProgressPct,
            ProgressMessage: doc.ProgressMessage,
            CreatedAt: doc.CreatedAt,
            StartedAt: doc.StartedAt,
            CompletedAt: doc.CompletedAt,
            ErrorMessage: doc.ErrorMessage,
            CreatedBy: doc.CreatedBy,
            CancelRequested: doc.CancelRequested,
            ResultJson: doc.ResultJson);
}
