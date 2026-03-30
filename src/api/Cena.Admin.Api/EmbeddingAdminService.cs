// =============================================================================
// Cena Platform -- Embedding Admin Service (ADM-020)
// Direct Npgsql queries against cena.content_embeddings (pgvector).
// Same data-access pattern as EmbeddingService in Cena.Actors.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Configuration;
using Cena.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cena.Admin.Api;

public interface IEmbeddingAdminService
{
    Task<EmbeddingCorpusStatsResponse> GetCorpusStatsAsync();
    Task<EmbeddingSearchResponse> SearchAsync(EmbeddingSearchRequest request);
    Task<EmbeddingDuplicateResponse> GetDuplicatesAsync(float threshold, int page, int pageSize);
    Task<ReindexResponse> RequestReindexAsync(ReindexRequest request);
}

public sealed class EmbeddingAdminService : IEmbeddingAdminService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<EmbeddingAdminService> _logger;

    public EmbeddingAdminService(
        NpgsqlDataSource dataSource,
        ILogger<EmbeddingAdminService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    // ── Corpus Stats ──

    public async Task<EmbeddingCorpusStatsResponse> GetCorpusStatsAsync()
    {
        try
        {
        await using var conn = await _dataSource.OpenConnectionAsync();

        // Check if table exists
        await using var checkCmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'cena' AND table_name = 'content_embeddings')", conn);
        var tableExists = (bool)(await checkCmd.ExecuteScalarAsync() ?? false);
        if (!tableExists)
        {
            _logger.LogWarning("content_embeddings table does not exist — pgvector may not be installed");
            return new EmbeddingCorpusStatsResponse(0, new(), new(), new(), new(), 0f);
        }

        // Total blocks
        await using var countCmd = new NpgsqlCommand(
            "SELECT count(*) FROM cena.content_embeddings", conn);
        var totalBlocks = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // Subject counts
        var subjectCounts = new Dictionary<string, int>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT subject, count(*) FROM cena.content_embeddings GROUP BY subject", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var key = reader.IsDBNull(0) ? "(null)" : reader.GetString(0);
                subjectCounts[key] = Convert.ToInt32(reader.GetInt64(1));
            }
        }

        // Concept counts (top 50)
        var conceptCounts = new Dictionary<string, int>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT unnest(concept_ids) AS concept, count(*) FROM cena.content_embeddings GROUP BY concept ORDER BY count DESC LIMIT 50", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var key = reader.IsDBNull(0) ? "(null)" : reader.GetString(0);
                conceptCounts[key] = Convert.ToInt32(reader.GetInt64(1));
            }
        }

        // Content type counts
        var contentTypeCounts = new Dictionary<string, int>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT content_type, count(*) FROM cena.content_embeddings GROUP BY content_type", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var key = reader.IsDBNull(0) ? "(null)" : reader.GetString(0);
                contentTypeCounts[key] = Convert.ToInt32(reader.GetInt64(1));
            }
        }

        // Language counts
        var languageCounts = new Dictionary<string, int>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT language, count(*) FROM cena.content_embeddings GROUP BY language", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var key = reader.IsDBNull(0) ? "(null)" : reader.GetString(0);
                languageCounts[key] = Convert.ToInt32(reader.GetInt64(1));
            }
        }

        // Index size in MB
        await using var sizeCmd = new NpgsqlCommand(
            "SELECT pg_total_relation_size('cena.content_embeddings') / (1024.0 * 1024.0) AS size_mb", conn);
        var indexSizeMb = Convert.ToSingle(await sizeCmd.ExecuteScalarAsync());

        _logger.LogDebug("Corpus stats: {Total} blocks, {Size:F2} MB", totalBlocks, indexSizeMb);

        return new EmbeddingCorpusStatsResponse(
            totalBlocks, subjectCounts, conceptCounts,
            contentTypeCounts, languageCounts, indexSizeMb);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query corpus stats — table may not exist");
            return new EmbeddingCorpusStatsResponse(0, new(), new(), new(), new(), 0f);
        }
    }

    // ── Search ──
    // NOTE: This is a text-based ILIKE search, not vector similarity.
    // The Admin API does not have access to the embedding generation service,
    // so we search on text_preview as a pragmatic admin search fallback.

    public async Task<EmbeddingSearchResponse> SearchAsync(EmbeddingSearchRequest request)
    {
        var sw = Stopwatch.StartNew();

        await using var conn = await _dataSource.OpenConnectionAsync();

        var topK = request.TopK > 0 ? request.TopK : 20;

        // SEC-004: Sanitize query string before embedding it in the parameterized ILIKE pattern.
        // The query is already bound as $1 (safe from SQL injection), but stripping control
        // characters prevents malformed bytes reaching the Postgres text-matching engine.
        var sanitizedQuery = InputSanitizer.SanitizeSearchQuery(request.Query);

        const string sql = """
            SELECT content_block_id, text_preview, subject, content_type,
                   concept_ids, language, 1.0 AS similarity
            FROM cena.content_embeddings
            WHERE text_preview ILIKE '%' || $1 || '%'
              AND ($2::text IS NULL OR subject = $2)
              AND ($3::text IS NULL OR $3 = ANY(concept_ids))
            LIMIT $4
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(sanitizedQuery);
        cmd.Parameters.AddWithValue((object?)request.SubjectFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)request.ConceptFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue(topK);

        var results = new List<EmbeddingSearchResultDto>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var conceptIds = reader.IsDBNull(4)
                ? new List<string>()
                : ((string[])reader.GetValue(4)).ToList();

            results.Add(new EmbeddingSearchResultDto(
                ContentBlockId: reader.GetString(0),
                TextPreview: reader.IsDBNull(1) ? "" : reader.GetString(1),
                Subject: reader.IsDBNull(2) ? "" : reader.GetString(2),
                ContentType: reader.IsDBNull(3) ? "" : reader.GetString(3),
                ConceptIds: conceptIds,
                Similarity: reader.GetFloat(5),
                Language: reader.IsDBNull(6) ? "" : reader.GetString(6)));
        }

        sw.Stop();
        _logger.LogDebug("Embedding search for '{Query}' returned {Count} results in {Ms}ms",
            request.Query, results.Count, sw.ElapsedMilliseconds);

        return new EmbeddingSearchResponse(results, sw.ElapsedMilliseconds);
    }

    // ── Duplicates ──

    public async Task<EmbeddingDuplicateResponse> GetDuplicatesAsync(
        float threshold, int page, int pageSize)
    {
        var offset = (page - 1) * pageSize;

        await using var conn = await _dataSource.OpenConnectionAsync();

        try
        {
            // Count total duplicates above threshold
            const string countSql = """
                SELECT count(*)
                FROM cena.content_embeddings a
                JOIN cena.content_embeddings b
                  ON a.content_block_id < b.content_block_id
                 AND a.subject = b.subject
                WHERE 1 - (a.embedding <=> b.embedding) >= $1
                """;

            await using var countCmd = new NpgsqlCommand(countSql, conn);
            countCmd.Parameters.AddWithValue(threshold);
            countCmd.CommandTimeout = 120; // cross-join can be slow
            var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            // Fetch page of duplicates
            const string sql = """
                SELECT a.content_block_id, a.text_preview,
                       b.content_block_id, b.text_preview,
                       1 - (a.embedding <=> b.embedding) AS similarity,
                       a.subject
                FROM cena.content_embeddings a
                JOIN cena.content_embeddings b
                  ON a.content_block_id < b.content_block_id
                 AND a.subject = b.subject
                WHERE 1 - (a.embedding <=> b.embedding) >= $1
                ORDER BY similarity DESC
                OFFSET $2 LIMIT $3
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(threshold);
            cmd.Parameters.AddWithValue(offset);
            cmd.Parameters.AddWithValue(pageSize);
            cmd.CommandTimeout = 120;

            var duplicates = new List<DuplicatePairDto>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                duplicates.Add(new DuplicatePairDto(
                    Block1Id: reader.GetString(0),
                    Block1Preview: reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Block2Id: reader.GetString(2),
                    Block2Preview: reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Similarity: reader.GetFloat(4),
                    Subject: reader.IsDBNull(5) ? "" : reader.GetString(5)));
            }

            _logger.LogInformation("Duplicate scan: {Total} pairs >= {Threshold}, page {Page}",
                totalCount, threshold, page);

            return new EmbeddingDuplicateResponse(duplicates, totalCount, page, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Duplicate detection failed (threshold={Threshold}). " +
                "This can be slow on large corpora.", threshold);
            return new EmbeddingDuplicateResponse([], 0, page, pageSize);
        }
    }

    // ── Reindex ──

    public async Task<ReindexResponse> RequestReindexAsync(ReindexRequest request)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        // Count blocks matching scope
        var countSql = request.Scope.ToLowerInvariant() switch
        {
            "subject" when !string.IsNullOrEmpty(request.Filter)
                => "SELECT count(*) FROM cena.content_embeddings WHERE subject = $1",
            "concept" when !string.IsNullOrEmpty(request.Filter)
                => "SELECT count(*) FROM cena.content_embeddings WHERE $1 = ANY(concept_ids)",
            _ => "SELECT count(*) FROM cena.content_embeddings"
        };

        await using var cmd = new NpgsqlCommand(countSql, conn);
        if (!string.IsNullOrEmpty(request.Filter) && request.Scope is "subject" or "concept")
        {
            cmd.Parameters.AddWithValue(request.Filter);
        }

        var estimatedBlocks = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        var jobId = Guid.NewGuid().ToString("N");

        // In production, this would publish a NATS message to trigger the
        // reindex pipeline. For now, we log the request and return the job ID.
        _logger.LogInformation(
            "Reindex requested: scope={Scope} filter={Filter} estimatedBlocks={Est} jobId={JobId}",
            request.Scope, request.Filter ?? "(none)", estimatedBlocks, jobId);

        return new ReindexResponse(jobId, estimatedBlocks);
    }
}
