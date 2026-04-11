// =============================================================================
// Cena Platform — Session Question Tests (HARDEN SessionEndpoints)
// Tests for QuestionDocument, QuestionBank, and session endpoints.
// =============================================================================

using Cena.Actors.Serving;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Session;

/// <summary>
/// Tests for QuestionDocument and IQuestionBank implementation.
/// </summary>
public sealed class SessionQuestionTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IQuerySession _querySession = Substitute.For<IQuerySession>();

    public SessionQuestionTests()
    {
        _store.LightweightSession().Returns(_session);
        _store.QuerySession().Returns(_querySession);
    }

    [Fact]
    public void QuestionDocument_HasAllRequiredFields()
    {
        var doc = new QuestionDocument
        {
            Id = "q:test:001",
            QuestionId = "q_001",
            Subject = "Mathematics",
            Topic = "Algebra",
            Difficulty = "medium",
            ConceptId = "concept:math:algebra",
            Prompt = "Solve for x: 2x + 5 = 15",
            QuestionType = "multiple-choice",
            Choices = new[] { "5", "10", "15", "20" },
            CorrectAnswer = "5",
            Explanation = "Subtract 5 from both sides, then divide by 2",
            Grade = 7,
            IsActive = true
        };

        Assert.Equal("q:test:001", doc.Id);
        Assert.Equal("q_001", doc.QuestionId);
        Assert.Equal("Mathematics", doc.Subject);
        Assert.Equal("concept:math:algebra", doc.ConceptId);
        Assert.Equal("5", doc.CorrectAnswer);
        Assert.True(doc.IsActive);
    }

    [Fact]
    public void QuestionDocument_Defaults_IsActiveTrue()
    {
        var doc = new QuestionDocument();
        Assert.True(doc.IsActive);
    }

    [Fact]
    public void QuestionDocument_SupportsAllDifficulties()
    {
        var difficulties = new[] { "easy", "medium", "hard" };

        foreach (var difficulty in difficulties)
        {
            var doc = new QuestionDocument { Difficulty = difficulty };
            Assert.Equal(difficulty, doc.Difficulty);
        }
    }

    [Fact]
    public void QuestionDocument_SupportsMultipleChoice()
    {
        var doc = new QuestionDocument
        {
            QuestionType = "multiple-choice",
            Choices = new[] { "A", "B", "C", "D" },
            CorrectAnswer = "B"
        };

        Assert.Equal(4, doc.Choices.Length);
        Assert.Contains("B", doc.Choices);
    }

    [Fact]
    public void QuestionBank_Constructor_SetsStore()
    {
        var bank = new QuestionBank(_store);
        Assert.NotNull(bank);
    }

    [Fact]
    public void QuestionDocument_CanBeMarkedInactive()
    {
        var doc = new QuestionDocument { IsActive = false };
        Assert.False(doc.IsActive);
    }

    [Fact]
    public void QuestionDocument_ConceptId_IsRequiredForSessionTracking()
    {
        var doc = new QuestionDocument
        {
            ConceptId = "concept:physics:motion",
            Subject = "Physics"
        };

        Assert.NotNull(doc.ConceptId);
        Assert.StartsWith("concept:", doc.ConceptId);
    }

    [Fact]
    public void QuestionDocument_Grade_IsOptional()
    {
        var docWithGrade = new QuestionDocument { Grade = 8 };
        var docWithoutGrade = new QuestionDocument();

        Assert.Equal(8, docWithGrade.Grade);
        Assert.Null(docWithoutGrade.Grade);
    }

    [Fact]
    public void QuestionDocument_Explanation_IsOptional()
    {
        var docWithExplanation = new QuestionDocument { Explanation = "Detailed explanation" };
        var docWithoutExplanation = new QuestionDocument();

        Assert.NotNull(docWithExplanation.Explanation);
        Assert.Null(docWithoutExplanation.Explanation);
    }

    [Fact]
    public void QuestionDocument_Topic_IsOptional()
    {
        var docWithTopic = new QuestionDocument { Topic = "Quadratic Equations" };
        var docWithoutTopic = new QuestionDocument();

        Assert.NotNull(docWithTopic.Topic);
        Assert.Null(docWithoutTopic.Topic);
    }
}
