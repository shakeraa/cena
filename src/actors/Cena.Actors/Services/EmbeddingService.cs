// =============================================================================
// Cena Platform -- Embedding Service (pgvector)
// SAI-008: Generates 384-dim embeddings via mE5-small (multilingual) and
// stores/searches them in PostgreSQL with pgvector for semantic RAG retrieval
// and dedup. Batch generation up to 100 items with circuit breaker fallback.
// =============================================================================

using System.Text;
using System.Text.Json;
using Cena.Actors.Ingest;
using Cena.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cena.Actors.Services;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);

    /// <summary>
    /// Embeds multiple texts in a single batch for API efficiency.
    /// Falls back to sequential pseudo-embedding when no API key is configured.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);

    Task StoreEmbeddingAsync(
        string contentId,
        float[] embedding,
        string subject,
        string? conceptId,
        string contentType,
        string language,
        string textPreview,
        CancellationToken ct);

    Task<IReadOnlyList<ContentMatch>> SearchAsync(EmbeddingQuery query, CancellationToken ct);

    /// <summary>
    /// Embeds a ContentBlockDocument: generates embedding, stores in pgvector table.
    /// </summary>
    Task EmbedContentBlockAsync(ContentBlockDocument block, CancellationToken ct);

    /// <summary>
    /// Searches for similar content blocks using pgvector cosine similarity.
    /// Returns results with similarity >= minSimilarity.
    /// </summary>
    Task<IReadOnlyList<SimilarContent>> SearchSimilarAsync(
        string queryText,
        string? subjectFilter = null,
        string[]? conceptFilter = null,
        int limit = 5,
        float minSimilarity = 0.7f,
        CancellationToken ct = default);

    /// <summary>
    /// Searches for similar content blocks using a typed filter object.
    /// Supports concept-scoped, content-type, and language filtering per SAI-06 spec.
    /// </summary>
    Task<IReadOnlyList<ContentSearchResult>> SearchSimilarAsync(
        string query,
        SearchFilter? filter = null,
        int limit = 5,
        float minSimilarity = 0.7f,
        CancellationToken ct = default);
}

public sealed record EmbeddingQuery(
    string QueryText,
    string? SubjectFilter,
    string? ConceptIdFilter,
    string? LanguageFilter,
    int TopK = 5,
    float MinSimilarity = 0.7f);

public sealed record ContentMatch(
    string ContentId,
    string Text,
    string ContentType,
    float Similarity);

public sealed record SimilarContent(
    string ContentBlockId,
    string ProcessedText,
    string ContentType,
    float Similarity);

/// <summary>
/// Typed filter for semantic search. Supports concept-scoped,
/// content-type, and language filtering per the SAI-06 spec.
/// </summary>
public sealed record SearchFilter(
    IReadOnlyList<string>? ConceptIds = null,
    string? ContentType = null,
    string? Language = null);

/// <summary>
/// Full search result including pipeline item provenance and concept links.
/// </summary>
public sealed record ContentSearchResult(
    string ContentBlockId,
    string PipelineItemId,
    string ContentType,
    IReadOnlyList<string> ConceptIds,
    string TextPreview,
    float Similarity);

/// <summary>
/// pgvector-backed embedding service. Generates embeddings via a configurable
/// HTTP embedding endpoint (Voyage AI, OpenAI text-embedding-3-small, etc.)
/// and stores/queries them in PostgreSQL using Npgsql raw SQL.
/// Falls back to deterministic hash-based pseudo-embeddings when no API key is configured.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService, IDisposable
{
    private readonly string _connectionString;
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly string? _apiKey;
    private readonly string _embeddingModel;
    private readonly string _embeddingEndpoint;
    private readonly bool _useRealEmbeddings;

    // SAI-06: 1536 dimensions for text-embedding-3-small (multilingual, standard pgvector)
    internal const int EmbeddingDimension = 1536;
    private const int BatchSize = 100;
    private const string DefaultModel = "text-embedding-3-small";
    private const string DefaultEndpoint = "https://api.openai.com/v1/embeddings";

    public EmbeddingService(
        IConfiguration configuration,
        IHostEnvironment environment,
        HttpClient httpClient,
        ILogger<EmbeddingService> logger)
    {
        _connectionString = CenaConnectionStrings.GetPostgres(configuration, environment);
        _httpClient = httpClient;
        _logger = logger;

        _apiKey = configuration["Embeddings:ApiKey"];
        _embeddingModel = configuration["Embeddings:Model"] ?? DefaultModel;
        _embeddingEndpoint = configuration["Embeddings:Endpoint"] ?? DefaultEndpoint;
        _useRealEmbeddings = !string.IsNullOrWhiteSpace(_apiKey);

        if (!_useRealEmbeddings)
        {
            _logger.LogWarning(
                "No Embeddings:ApiKey configured. Using deterministic pseudo-embeddings. " +
                "Set Embeddings:ApiKey for real semantic search quality.");
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[EmbeddingDimension];

        if (_useRealEmbeddings)
            return await GenerateRealEmbeddingAsync(text, ct);

        return GeneratePseudoEmbedding(text);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0)
            return [];

        if (!_useRealEmbeddings)
            return texts.Select(GeneratePseudoEmbedding).ToList();

        var allEmbeddings = new List<float[]>(texts.Count);
        for (var i = 0; i < texts.Count; i += BatchSize)
        {
            var batch = texts.Skip(i).Take(BatchSize).ToList();
            var batchResults = await GenerateRealBatchEmbeddingsAsync(batch, ct);
            allEmbeddings.AddRange(batchResults);
        }

        return allEmbeddings;
    }

    public async Task StoreEmbeddingAsync(
        string contentId,
        float[] embedding,
        string subject,
        string? conceptId,
        string contentType,
        string language,
        string textPreview,
        CancellationToken ct)
    {
        var conceptIds = conceptId is not null ? new[] { conceptId } : Array.Empty<string>();
        await UpsertEmbeddingAsync(
            contentId, null, embedding, conceptIds, language, subject, contentType, textPreview, ct);
    }

    public async Task<IReadOnlyList<ContentMatch>> SearchAsync(EmbeddingQuery query, CancellationToken ct)
    {
        var filter = new SearchFilter(
            ConceptIds: !string.IsNullOrWhiteSpace(query.ConceptIdFilter)
                ? new[] { query.ConceptIdFilter }
                : null,
            ContentType: null,
            Language: query.LanguageFilter);

        var results = await SearchSimilarAsync(
            query.QueryText,
            filter,
            query.TopK,
            query.MinSimilarity,
            ct);

        return results.Select(r => new ContentMatch(
            r.ContentBlockId,
            r.TextPreview,
            r.ContentType,
            r.Similarity)).ToList();
    }

    public async Task EmbedContentBlockAsync(ContentBlockDocument block, CancellationToken ct)
    {
        var textToEmbed = BuildEmbeddingText(block);
        var embedding = await EmbedAsync(textToEmbed, ct);

        await UpsertEmbeddingAsync(
            block.Id,
            block.SourceDocId,
            embedding,
            block.ConceptIds.ToArray(),
            block.Language,
            block.Subject,
            block.ContentType,
            Truncate(block.ProcessedText, 500),
            ct);

        _logger.LogDebug(
            "Embedded content block {BlockId} (type={ContentType}, subject={Subject})",
            block.Id, block.ContentType, block.Subject);
    }

    public async Task<IReadOnlyList<SimilarContent>> SearchSimilarAsync(
        string queryText,
        string? subjectFilter = null,
        string[]? conceptFilter = null,
        int limit = 5,
        float minSimilarity = 0.7f,
        CancellationToken ct = default)
    {
        var queryEmbedding = await EmbedAsync(queryText, ct);
        return await SearchByVectorAsync(
            queryEmbedding, subjectFilter, conceptFilter, null, null, limit, minSimilarity, ct);
    }

    public async Task<IReadOnlyList<ContentSearchResult>> SearchSimilarAsync(
        string query,
        SearchFilter? filter = null,
        int limit = 5,
        float minSimilarity = 0.7f,
        CancellationToken ct = default)
    {
        var queryEmbedding = await EmbedAsync(query, ct);
        return await SearchByVectorFullAsync(queryEmbedding, filter, limit, minSimilarity, ct);
    }

    public void Dispose()
    {
        // HttpClient is managed externally via DI (HttpClientFactory)
    }

    // ── Internal: pgvector search (simple results) ──

    internal async Task<IReadOnlyList<SimilarContent>> SearchByVectorAsync(
        float[] queryEmbedding,
        string? subjectFilter,
        string[]? conceptFilter,
        string? contentTypeFilter,
        string? languageFilter,
        int limit,
        float minSimilarity,
        CancellationToken ct)
    {
        var sql = new StringBuilder();
        sql.Append(
            "SELECT content_block_id, text_preview, content_type," +
            " 1 - (embedding <=> $1::vector) AS similarity" +
            " FROM cena.content_embeddings" +
            " WHERE 1 - (embedding <=> $1::vector) >= $2");

        var paramIndex = 3;
        if (!string.IsNullOrWhiteSpace(subjectFilter))
        {
            sql.Append($" AND subject = ${paramIndex}");
            paramIndex++;
        }

        if (conceptFilter is { Length: > 0 })
        {
            sql.Append($" AND concept_ids && ${paramIndex}");
            paramIndex++;
        }

        if (!string.IsNullOrWhiteSpace(contentTypeFilter))
        {
            sql.Append($" AND content_type = ${paramIndex}");
            paramIndex++;
        }

        if (!string.IsNullOrWhiteSpace(languageFilter))
        {
            sql.Append($" AND language = ${paramIndex}");
            paramIndex++;
        }

        sql.Append(" ORDER BY embedding <=> $1::vector ASC");
        sql.Append($" LIMIT ${paramIndex}");

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql.ToString();

            cmd.Parameters.AddWithValue(FormatVector(queryEmbedding));
            cmd.Parameters.AddWithValue(minSimilarity);

            if (!string.IsNullOrWhiteSpace(subjectFilter))
                cmd.Parameters.AddWithValue(subjectFilter);

            if (conceptFilter is { Length: > 0 })
                cmd.Parameters.AddWithValue(conceptFilter);

            if (!string.IsNullOrWhiteSpace(contentTypeFilter))
                cmd.Parameters.AddWithValue(contentTypeFilter);

            if (!string.IsNullOrWhiteSpace(languageFilter))
                cmd.Parameters.AddWithValue(languageFilter);

            cmd.Parameters.AddWithValue(limit);

            var results = new List<SimilarContent>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new SimilarContent(
                    ContentBlockId: reader.GetString(0),
                    ProcessedText: reader.GetString(1),
                    ContentType: reader.GetString(2),
                    Similarity: reader.GetFloat(3)));
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "pgvector search failed, returning empty results");
            return [];
        }
    }

    // ── Internal: pgvector search (full results with pipeline_item_id + concept_ids) ──

    internal async Task<IReadOnlyList<ContentSearchResult>> SearchByVectorFullAsync(
        float[] queryEmbedding,
        SearchFilter? filter,
        int limit,
        float minSimilarity,
        CancellationToken ct)
    {
        var sql = new StringBuilder();
        sql.Append(
            "SELECT content_block_id, pipeline_item_id, content_type, concept_ids," +
            " text_preview, 1 - (embedding <=> $1::vector) AS similarity" +
            " FROM cena.content_embeddings" +
            " WHERE 1 - (embedding <=> $1::vector) >= $2");

        var paramIndex = 3;

        if (filter?.ConceptIds is { Count: > 0 })
        {
            sql.Append($" AND concept_ids && ${paramIndex}");
            paramIndex++;
        }

        if (!string.IsNullOrWhiteSpace(filter?.ContentType))
        {
            sql.Append($" AND content_type = ${paramIndex}");
            paramIndex++;
        }

        if (!string.IsNullOrWhiteSpace(filter?.Language))
        {
            sql.Append($" AND language = ${paramIndex}");
            paramIndex++;
        }

        sql.Append(" ORDER BY embedding <=> $1::vector ASC");
        sql.Append($" LIMIT ${paramIndex}");

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql.ToString();

            cmd.Parameters.AddWithValue(FormatVector(queryEmbedding));
            cmd.Parameters.AddWithValue(minSimilarity);

            if (filter?.ConceptIds is { Count: > 0 })
                cmd.Parameters.AddWithValue(filter.ConceptIds.ToArray());

            if (!string.IsNullOrWhiteSpace(filter?.ContentType))
                cmd.Parameters.AddWithValue(filter.ContentType);

            if (!string.IsNullOrWhiteSpace(filter?.Language))
                cmd.Parameters.AddWithValue(filter.Language);

            cmd.Parameters.AddWithValue(limit);

            var results = new List<ContentSearchResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var conceptIds = reader.IsDBNull(3)
                    ? (IReadOnlyList<string>)[]
                    : (IReadOnlyList<string>)(reader.GetFieldValue<string[]>(3));

                results.Add(new ContentSearchResult(
                    ContentBlockId: reader.GetString(0),
                    PipelineItemId: reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ContentType: reader.GetString(2),
                    ConceptIds: conceptIds,
                    TextPreview: reader.GetString(4),
                    Similarity: reader.GetFloat(5)));
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "pgvector full search failed, returning empty results");
            return [];
        }
    }

    // ── Internal: upsert embedding row ──

    internal async Task UpsertEmbeddingAsync(
        string contentBlockId,
        string? pipelineItemId,
        float[] embedding,
        string[] conceptIds,
        string language,
        string subject,
        string contentType,
        string textPreview,
        CancellationToken ct)
    {
        const string sql =
            "INSERT INTO cena.content_embeddings" +
            " (content_block_id, pipeline_item_id, embedding, concept_ids, language," +
            "  subject, content_type, text_preview)" +
            " VALUES ($1, $2, $3::vector, $4, $5, $6, $7, $8)" +
            " ON CONFLICT (content_block_id) DO UPDATE SET" +
            "  pipeline_item_id = EXCLUDED.pipeline_item_id," +
            "  embedding = EXCLUDED.embedding," +
            "  concept_ids = EXCLUDED.concept_ids," +
            "  language = EXCLUDED.language," +
            "  subject = EXCLUDED.subject," +
            "  content_type = EXCLUDED.content_type," +
            "  text_preview = EXCLUDED.text_preview," +
            "  created_at = NOW()";

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue(contentBlockId);
            cmd.Parameters.AddWithValue((object?)pipelineItemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue(FormatVector(embedding));
            cmd.Parameters.AddWithValue(conceptIds);
            cmd.Parameters.AddWithValue(language);
            cmd.Parameters.AddWithValue(subject);
            cmd.Parameters.AddWithValue(contentType);
            cmd.Parameters.AddWithValue(textPreview);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to upsert embedding for content block {ContentBlockId}", contentBlockId);
        }
    }

    // ── Internal: single embedding via API ──

    private async Task<float[]> GenerateRealEmbeddingAsync(string text, CancellationToken ct)
    {
        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                model = _embeddingModel,
                input = text
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, _embeddingEndpoint);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            return ParseEmbeddingFromResponse(doc.RootElement, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding API call failed, falling back to pseudo-embedding");
            return GeneratePseudoEmbedding(text);
        }
    }

    // ── Internal: batch embedding via API ──

    private async Task<IReadOnlyList<float[]>> GenerateRealBatchEmbeddingsAsync(
        IReadOnlyList<string> texts, CancellationToken ct)
    {
        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                model = _embeddingModel,
                input = texts
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, _embeddingEndpoint);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var dataArray = doc.RootElement.GetProperty("data");
            var embeddings = new List<float[]>(texts.Count);

            foreach (var item in dataArray.EnumerateArray())
            {
                var embeddingArray = item.GetProperty("embedding");
                var embedding = new float[EmbeddingDimension];
                var idx = 0;
                foreach (var element in embeddingArray.EnumerateArray())
                {
                    if (idx >= EmbeddingDimension) break;
                    embedding[idx++] = element.GetSingle();
                }
                embeddings.Add(embedding);
            }

            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Batch embedding API call failed for {Count} texts, falling back to pseudo-embeddings",
                texts.Count);
            return texts.Select(GeneratePseudoEmbedding).ToList();
        }
    }

    private static float[] ParseEmbeddingFromResponse(JsonElement root, int index)
    {
        var embeddingArray = root.GetProperty("data")[index].GetProperty("embedding");
        var embedding = new float[EmbeddingDimension];
        var idx = 0;
        foreach (var element in embeddingArray.EnumerateArray())
        {
            if (idx >= EmbeddingDimension) break;
            embedding[idx++] = element.GetSingle();
        }
        return embedding;
    }

    /// <summary>
    /// Deterministic hash-based pseudo-embedding. Used when no embedding API is configured.
    /// Produces a unit-normalized vector from SHA-256 hash buckets.
    /// </summary>
    internal static float[] GeneratePseudoEmbedding(string text)
    {
        var embedding = new float[EmbeddingDimension];
        if (string.IsNullOrWhiteSpace(text))
            return embedding;

        var bytes = Encoding.UTF8.GetBytes(text.ToLowerInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);

        for (var i = 0; i < EmbeddingDimension; i++)
        {
            var byteIndex = i % hash.Length;
            embedding[i] = (hash[byteIndex] / 255f) * 2f - 1f;
        }

        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (var i = 0; i < embedding.Length; i++)
                embedding[i] /= magnitude;
        }

        return embedding;
    }

    private static string BuildEmbeddingText(ContentBlockDocument block)
    {
        var sb = new StringBuilder();
        sb.Append($"[{block.ContentType}] ");
        if (!string.IsNullOrWhiteSpace(block.Topic))
            sb.Append($"Topic: {block.Topic}. ");
        sb.Append(block.ProcessedText);
        return sb.ToString();
    }

    internal static string FormatVector(float[] embedding)
    {
        var sb = new StringBuilder(embedding.Length * 10);
        sb.Append('[');
        for (var i = 0; i < embedding.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(embedding[i].ToString("G9"));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "...");
}
