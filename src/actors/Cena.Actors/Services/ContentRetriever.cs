// =============================================================================
// Cena Platform — Content Retriever
// SAI-07: Retrieves relevant educational content for tutoring context.
// Combines student question + question stem as query, filters by subject,
// concept, and language, returns top 3 matches.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

public interface IContentRetriever
{
    Task<IReadOnlyList<ContentMatch>> RetrieveAsync(TutoringContext context, CancellationToken ct);
}

public sealed record TutoringContext(
    string StudentQuestion,
    string CurrentQuestionStem,
    string ConceptId,
    string Subject,
    string Language);

public sealed class ContentRetriever : IContentRetriever
{
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<ContentRetriever> _logger;

    private const int TopK = 3;
    private const float MinSimilarity = 0.3f;

    public ContentRetriever(IEmbeddingService embeddings, ILogger<ContentRetriever> logger)
    {
        _embeddings = embeddings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ContentMatch>> RetrieveAsync(TutoringContext context, CancellationToken ct)
    {
        // Combine student question + question stem for richer query signal
        var queryParts = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(context.StudentQuestion))
            queryParts.Add(context.StudentQuestion);

        if (!string.IsNullOrWhiteSpace(context.CurrentQuestionStem))
            queryParts.Add(context.CurrentQuestionStem);

        if (queryParts.Count == 0)
        {
            _logger.LogDebug("No query text for content retrieval, returning empty");
            return [];
        }

        var combinedQuery = string.Join(" ", queryParts);

        var query = new EmbeddingQuery(
            QueryText: combinedQuery,
            SubjectFilter: context.Subject,
            ConceptIdFilter: context.ConceptId,
            LanguageFilter: context.Language,
            TopK: TopK,
            MinSimilarity: MinSimilarity);

        var results = await _embeddings.SearchAsync(query, ct);

        _logger.LogDebug(
            "Content retrieval for concept {ConceptId}: {Count} matches",
            context.ConceptId, results.Count);

        return results;
    }
}
