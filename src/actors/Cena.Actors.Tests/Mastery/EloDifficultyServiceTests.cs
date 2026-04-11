// =============================================================================
// Cena Platform -- Elo Difficulty Service Tests (FIND-pedagogy-009)
// Tests the Elo-based difficulty update system for the 85% rule.
//
// Citation:
// Wilson, R.C., Shenhav, A., Straccia, M. & Cohen, J.D. (2019). 
// "The Eighty Five Percent Rule for optimal learning." Nature Communications, 10, 4646.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public class EloDifficultyServiceTests
{
    private readonly IDocumentStore _store;
    private readonly ILogger<EloDifficultyService> _logger;
    private readonly EloDifficultyService _service;

    public EloDifficultyServiceTests()
    {
        _store = Substitute.For<IDocumentStore>();
        _logger = Substitute.For<ILogger<EloDifficultyService>>();
        _service = new EloDifficultyService(_store, _logger);
    }

    [Fact]
    public void UpdateDifficulty_CorrectAnswerOnEasyQuestion_IncreasesDifficulty()
    {
        // Student (theta=1000) answers easy question (difficulty=800) correctly
        var question = new QuestionDocument
        {
            QuestionId = "q_test_001",
            DifficultyElo = 800f
        };

        var session = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(session);

        var newDifficulty = _service.UpdateDifficultyAsync(question, studentTheta: 1000f, isCorrect: true).Result;

        // Should increase (question was too easy for this student)
        Assert.True(newDifficulty > 800f, "Difficulty should increase when strong student gets easy question right");
        session.Received(1).Store(question);
        session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UpdateDifficulty_WrongAnswerOnHardQuestion_DecreasesDifficulty()
    {
        // Student (theta=1000) answers hard question (difficulty=1200) wrong
        var question = new QuestionDocument
        {
            QuestionId = "q_test_002",
            DifficultyElo = 1200f
        };

        var session = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(session);

        var newDifficulty = _service.UpdateDifficultyAsync(question, studentTheta: 1000f, isCorrect: false).Result;

        // Should decrease (question was too hard for this student)
        Assert.True(newDifficulty < 1200f, "Difficulty should decrease when weak student gets hard question wrong");
    }

    [Fact]
    public void UpdateDifficulty_ClampsToMinimum500()
    {
        var question = new QuestionDocument
        {
            QuestionId = "q_test_003",
            DifficultyElo = 510f
        };

        var session = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(session);

        // Student much stronger than question, gets it wrong (unexpected)
        var newDifficulty = _service.UpdateDifficultyAsync(question, studentTheta: 1500f, isCorrect: false).Result;

        Assert.True(newDifficulty >= 500f, "Difficulty should clamp at minimum 500");
    }

    [Fact]
    public void UpdateDifficulty_ClampsToMaximum2500()
    {
        var question = new QuestionDocument
        {
            QuestionId = "q_test_004",
            DifficultyElo = 2490f
        };

        var session = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(session);

        // Student much weaker than question, gets it right (unexpected)
        var newDifficulty = _service.UpdateDifficultyAsync(question, studentTheta: 500f, isCorrect: true).Result;

        Assert.True(newDifficulty <= 2500f, "Difficulty should clamp at maximum 2500");
    }

    [Fact]
    public void UpdateDifficulty_UpdatesDocumentProperty()
    {
        var question = new QuestionDocument
        {
            QuestionId = "q_test_005",
            DifficultyElo = 1000f
        };

        var session = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(session);

        _service.UpdateDifficultyAsync(question, studentTheta: 1000f, isCorrect: true).Wait();

        // The document's DifficultyElo should be updated in-place
        Assert.NotEqual(1000f, question.DifficultyElo);
    }
}
