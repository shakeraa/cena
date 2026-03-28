// =============================================================================
// Cena Platform — 3-Level Deduplication Service
// Level 1: Exact SHA-256 hash (O(1) Redis SET)
// Level 2: Structural AST hash (same math, different variables)
// Level 3: Semantic embedding cosine similarity (future: mE5-large)
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Ingest;

public enum DedupResult
{
    Unique,
    ExactDuplicate,
    StructuralDuplicate,
    SemanticNearDuplicate
}

public sealed record DedupOutcome(
    DedupResult Result,
    string? MatchedItemId,
    float? SimilarityScore,
    string ExactHash,
    string? StructuralHash);

public interface IDeduplicationService
{
    Task<DedupOutcome> CheckAsync(string stemText, Dictionary<string, string> mathExpressions, CancellationToken ct = default);
    Task RegisterAsync(string itemId, string exactHash, string? structuralHash, CancellationToken ct = default);
}

public sealed partial class DeduplicationService : IDeduplicationService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DeduplicationService> _logger;

    private const string ExactHashSet = "ingest:dedup:exact";
    private const string StructuralHashSet = "ingest:dedup:structural";
    private const string HashToItemMap = "ingest:dedup:hash_to_item";

    public DeduplicationService(
        IConnectionMultiplexer redis,
        ILogger<DeduplicationService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<DedupOutcome> CheckAsync(
        string stemText,
        Dictionary<string, string> mathExpressions,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        // Level 1: Exact content hash
        var normalizedContent = NormalizeForExactHash(stemText, mathExpressions);
        var exactHash = ComputeSha256(normalizedContent);

        var existingExact = await db.HashGetAsync(HashToItemMap, $"exact:{exactHash}");
        if (existingExact.HasValue)
        {
            _logger.LogDebug("Exact duplicate found: {Hash}", exactHash);
            return new DedupOutcome(DedupResult.ExactDuplicate, existingExact.ToString(), 1.0f, exactHash, null);
        }

        // Level 2: Structural AST hash (normalize variables/constants)
        var structuralHash = ComputeStructuralHash(mathExpressions);
        if (structuralHash is not null)
        {
            var existingStructural = await db.HashGetAsync(HashToItemMap, $"structural:{structuralHash}");
            if (existingStructural.HasValue)
            {
                _logger.LogDebug("Structural duplicate found: {Hash}", structuralHash);
                return new DedupOutcome(DedupResult.StructuralDuplicate, existingStructural.ToString(), 0.85f, exactHash, structuralHash);
            }
        }

        // Level 3: Semantic embedding (placeholder — requires vector DB)
        // TODO: Integrate mE5-large embeddings with pgvector or Redis VSS
        // For now, skip semantic dedup until corpus > 10K items

        return new DedupOutcome(DedupResult.Unique, null, null, exactHash, structuralHash);
    }

    public async Task RegisterAsync(
        string itemId, string exactHash, string? structuralHash, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var batch = db.CreateBatch();

        batch.SetAddAsync(ExactHashSet, exactHash);
        batch.HashSetAsync(HashToItemMap, $"exact:{exactHash}", itemId);

        if (structuralHash is not null)
        {
            batch.SetAddAsync(StructuralHashSet, structuralHash);
            batch.HashSetAsync(HashToItemMap, $"structural:{structuralHash}", itemId);
        }

        batch.Execute();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Normalize content for exact hashing: lowercase, strip whitespace, sort math keys.
    /// </summary>
    private static string NormalizeForExactHash(string stemText, Dictionary<string, string> math)
    {
        var sb = new StringBuilder();
        sb.Append(WhitespaceRegex().Replace(stemText.Trim(), " "));
        foreach (var kv in math.OrderBy(k => k.Key))
        {
            sb.Append('|');
            sb.Append(WhitespaceRegex().Replace(kv.Value.Trim(), ""));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Structural hash: replace variable names with placeholders, constants with C.
    /// Catches "same problem, different numbers/variables."
    /// </summary>
    private static string? ComputeStructuralHash(Dictionary<string, string> mathExpressions)
    {
        if (mathExpressions.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var kv in mathExpressions.OrderBy(k => k.Key))
        {
            var normalized = NormalizeMathAst(kv.Value);
            sb.Append(normalized);
            sb.Append('|');
        }
        return ComputeSha256(sb.ToString());
    }

    /// <summary>
    /// Normalize a LaTeX expression by replacing variable names and numeric constants
    /// with canonical placeholders. This makes "2x² + 3x - 1" and "5y² + 2y - 7"
    /// hash to the same value.
    /// </summary>
    private static string NormalizeMathAst(string latex)
    {
        // Replace all numeric constants with "N"
        var result = NumberRegex().Replace(latex, "N");
        // Replace single-letter variables with "V"
        result = VariableRegex().Replace(result, "V");
        // Strip whitespace
        result = WhitespaceRegex().Replace(result, "");
        return result;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\b\d+\.?\d*\b")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(?<![a-zA-Z\\])[a-zA-Z](?![a-zA-Z])")]
    private static partial Regex VariableRegex();
}
