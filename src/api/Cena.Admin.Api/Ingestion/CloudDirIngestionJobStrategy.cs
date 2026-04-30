// =============================================================================
// Cena Platform — IngestionJobCloudDirStrategy (extracted from IngestionJobRunnerHostedService for the LOC ratchet).
// =============================================================================

using System.Text.Json;
using Cena.Admin.Api.QualityGate;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api.Ingestion;


internal sealed class CloudDirIngestionJobStrategy : IIngestionJobStrategy
{
    public IngestionJobType Type => IngestionJobType.CloudDir;

    public async Task<object?> ExecuteAsync(
        IngestionJobDocument job,
        IServiceProvider scoped,
        IJobProgressReporter progress,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CloudDirJobPayload>(job.PayloadJson ?? "{}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Cloud-dir job payload missing");

        await progress.ReportAsync(10, $"Listing {payload.Provider}:{payload.BucketOrPath}…", ct);

        var pipeline = scoped.GetRequiredService<IIngestionPipelineService>();

        var ingestRequest = new Cena.Api.Contracts.Admin.Ingestion.CloudDirIngestRequest(
            Provider: payload.Provider,
            BucketOrPath: payload.BucketOrPath,
            FileKeys: payload.FileKeys ?? Array.Empty<string>(),
            Prefix: payload.Prefix);

        await progress.ReportAsync(30, "Queueing files for ingestion…", ct);

        var resp = await pipeline.IngestCloudDirectoryAsync(ingestRequest);

        await progress.ReportAsync(100,
            $"Queued {resp.FilesQueued}, skipped {resp.FilesSkipped}", ct);

        return new
        {
            filesQueued = resp.FilesQueued,
            filesSkipped = resp.FilesSkipped,
            batchId = resp.BatchId,
        };
    }
}

public sealed record CloudDirJobPayload(
    string Provider,
    string BucketOrPath,
    IReadOnlyList<string>? FileKeys,
    string? Prefix);

// ----- GenerateVariants strategy (option 2) -----

