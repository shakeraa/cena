// =============================================================================
// Cena Platform -- Embedding Service Tests
// SAI-06: Tests pseudo-embedding generation, vector formatting, and text
// building for content blocks. Integration tests for pgvector are in a
// separate integration test project.
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

public class EmbeddingServiceTests
{
    [Fact]
    public void GeneratePseudoEmbedding_EmptyText_ReturnsZeroVector()
    {
        var embedding = EmbeddingService.GeneratePseudoEmbedding("");

        Assert.Equal(1536, embedding.Length);
        Assert.All(embedding, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void GeneratePseudoEmbedding_WhitespaceText_ReturnsZeroVector()
    {
        var embedding = EmbeddingService.GeneratePseudoEmbedding("   ");

        Assert.Equal(1536, embedding.Length);
        Assert.All(embedding, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void GeneratePseudoEmbedding_ValidText_Returns1536Dimensions()
    {
        var embedding = EmbeddingService.GeneratePseudoEmbedding("quadratic equation");

        Assert.Equal(1536, embedding.Length);
    }

    [Fact]
    public void GeneratePseudoEmbedding_ValidText_IsUnitNormalized()
    {
        var embedding = EmbeddingService.GeneratePseudoEmbedding("quadratic equation");

        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        Assert.InRange(magnitude, 0.99f, 1.01f);
    }

    [Fact]
    public void GeneratePseudoEmbedding_Deterministic_SameInputSameOutput()
    {
        var e1 = EmbeddingService.GeneratePseudoEmbedding("חשב: 2x + 3 = 7");
        var e2 = EmbeddingService.GeneratePseudoEmbedding("חשב: 2x + 3 = 7");

        Assert.Equal(e1, e2);
    }

    [Fact]
    public void GeneratePseudoEmbedding_DifferentText_DifferentVectors()
    {
        var e1 = EmbeddingService.GeneratePseudoEmbedding("quadratic equation");
        var e2 = EmbeddingService.GeneratePseudoEmbedding("linear algebra");

        // They should not be identical
        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void GeneratePseudoEmbedding_CaseInsensitive()
    {
        var e1 = EmbeddingService.GeneratePseudoEmbedding("Quadratic Equation");
        var e2 = EmbeddingService.GeneratePseudoEmbedding("quadratic equation");

        Assert.Equal(e1, e2);
    }

    [Fact]
    public void FormatVector_EmptyArray_ReturnsEmptyBrackets()
    {
        var formatted = EmbeddingService.FormatVector([]);

        Assert.Equal("[]", formatted);
    }

    [Fact]
    public void FormatVector_SingleElement_FormattedCorrectly()
    {
        var formatted = EmbeddingService.FormatVector([0.5f]);

        Assert.StartsWith("[", formatted);
        Assert.EndsWith("]", formatted);
        Assert.Contains("0.5", formatted);
    }

    [Fact]
    public void FormatVector_MultipleElements_CommaSeparated()
    {
        var formatted = EmbeddingService.FormatVector([0.1f, 0.2f, 0.3f]);

        Assert.StartsWith("[", formatted);
        Assert.EndsWith("]", formatted);
        // Should contain commas separating values
        var inner = formatted[1..^1];
        var parts = inner.Split(',');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void FormatVector_NegativeValues_HandledCorrectly()
    {
        var formatted = EmbeddingService.FormatVector([-0.5f, 0.5f]);

        Assert.Contains("-0.5", formatted);
        Assert.Contains("0.5", formatted);
    }

    [Fact]
    public void FormatVector_LargeArray_NoStackOverflow()
    {
        var large = new float[1536];
        for (var i = 0; i < large.Length; i++)
            large[i] = (i / 1536f) * 2f - 1f;

        var formatted = EmbeddingService.FormatVector(large);

        Assert.StartsWith("[", formatted);
        Assert.EndsWith("]", formatted);
        var parts = formatted[1..^1].Split(',');
        Assert.Equal(1536, parts.Length);
    }

    // ── EmbedBatchAsync tests (pseudo-embedding fallback path) ──

    [Fact]
    public void GeneratePseudoEmbedding_MultipleTexts_AllCorrectDimension()
    {
        var texts = new[] { "algebra", "geometry", "calculus" };
        var embeddings = texts.Select(EmbeddingService.GeneratePseudoEmbedding).ToList();

        Assert.Equal(3, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(1536, e.Length));
    }

    [Fact]
    public void GeneratePseudoEmbedding_MultipleTexts_AllUnitNormalized()
    {
        var texts = new[] { "algebra", "geometry", "calculus" };
        var embeddings = texts.Select(EmbeddingService.GeneratePseudoEmbedding).ToList();

        foreach (var embedding in embeddings)
        {
            var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
            Assert.InRange(magnitude, 0.99f, 1.01f);
        }
    }

    [Fact]
    public void GeneratePseudoEmbedding_MultilanguageSupport()
    {
        // Hebrew, Arabic, and English should all produce valid embeddings
        var hebrewEmbedding = EmbeddingService.GeneratePseudoEmbedding("משוואה ריבועית");
        var arabicEmbedding = EmbeddingService.GeneratePseudoEmbedding("المعادلة التربيعية");
        var englishEmbedding = EmbeddingService.GeneratePseudoEmbedding("quadratic equation");

        Assert.Equal(1536, hebrewEmbedding.Length);
        Assert.Equal(1536, arabicEmbedding.Length);
        Assert.Equal(1536, englishEmbedding.Length);

        // All three should be different
        Assert.NotEqual(hebrewEmbedding, arabicEmbedding);
        Assert.NotEqual(hebrewEmbedding, englishEmbedding);
        Assert.NotEqual(arabicEmbedding, englishEmbedding);
    }

    // ── SearchFilter record tests ──

    [Fact]
    public void SearchFilter_DefaultValues_AllNull()
    {
        var filter = new SearchFilter();

        Assert.Null(filter.ConceptIds);
        Assert.Null(filter.ContentType);
        Assert.Null(filter.Language);
    }

    [Fact]
    public void SearchFilter_WithValues_PropertiesSet()
    {
        var filter = new SearchFilter(
            ConceptIds: new[] { "concept-1", "concept-2" },
            ContentType: "definition",
            Language: "he");

        Assert.Equal(2, filter.ConceptIds!.Count);
        Assert.Equal("definition", filter.ContentType);
        Assert.Equal("he", filter.Language);
    }

    // ── ContentSearchResult record tests ──

    [Fact]
    public void ContentSearchResult_HasAllRequiredFields()
    {
        var result = new ContentSearchResult(
            ContentBlockId: "cb-123",
            PipelineItemId: "pi-456",
            ContentType: "theorem",
            ConceptIds: new[] { "concept-1" },
            TextPreview: "For all x...",
            Similarity: 0.85f);

        Assert.Equal("cb-123", result.ContentBlockId);
        Assert.Equal("pi-456", result.PipelineItemId);
        Assert.Equal("theorem", result.ContentType);
        Assert.Single(result.ConceptIds);
        Assert.Equal("For all x...", result.TextPreview);
        Assert.Equal(0.85f, result.Similarity);
    }
}
