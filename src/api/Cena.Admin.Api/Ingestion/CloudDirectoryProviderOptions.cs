// =============================================================================
// Cena Platform — Cloud Directory Provider config POCOs (ADR-0058)
// =============================================================================

namespace Cena.Admin.Api.Ingestion;

/// <summary>
/// Root options bound from the <c>Ingestion</c> configuration section.
/// Populated in <c>Program.cs</c> via
/// <c>services.Configure&lt;IngestionOptions&gt;(configuration.GetSection("Ingestion"))</c>.
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>
    /// Filesystem paths allowed as list/ingest roots for the
    /// <c>local</c> provider. Directory traversal outside these roots
    /// is rejected with 401.
    /// </summary>
    public List<string> CloudWatchDirs { get; set; } = new();

    /// <summary>
    /// S3-specific provider configuration. When absent or
    /// <see cref="S3Options.Enabled"/>=false, the S3 provider is
    /// registered as disabled and dispatch throws a curator-readable
    /// error.
    /// </summary>
    public S3Options? S3 { get; set; }

    /// <summary>
    /// Max batch size enforced at the ingest dispatch boundary across
    /// all providers. Null = no limit (not recommended).
    /// </summary>
    public int? MaxBatchFiles { get; set; } = 1000;

    /// <summary>
    /// Max total bytes across all files in a single ingest batch.
    /// </summary>
    public long? MaxBatchBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10 GB
}

public sealed class S3Options
{
    /// <summary>Feature-flag. Defaults to false so S3 is opt-in per env.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// AWS region. Required when Enabled=true. Example: <c>"us-east-1"</c>.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Buckets admins are allowed to list/ingest from. An admin request
    /// targeting a bucket outside this list returns 401. Empty list =
    /// S3 provider disabled even if Enabled=true.
    /// </summary>
    public List<string> AllowedBuckets { get; set; } = new();

    /// <summary>
    /// Override the S3 endpoint URL. Production: leave empty — SDK
    /// resolves to the regional endpoint. Dev with LocalStack: set to
    /// <c>"http://localhost:4566"</c>.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Force path-style addressing (bucket in URL path, not subdomain).
    /// Required for LocalStack and many S3-compatible endpoints.
    /// Production S3: leave false.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Static access key. Leave empty in prod (IRSA / default credential
    /// chain is preferred — see ADR-0058). Dev / non-EKS clusters can
    /// populate via K8s Secret.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// Static secret key. Leave empty in prod (IRSA).
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Max continuation-token-based page size. S3 caps at 1000; we
    /// default to the same.
    /// </summary>
    public int PageSize { get; set; } = 1000;
}
