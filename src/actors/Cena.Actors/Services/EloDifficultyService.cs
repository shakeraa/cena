// =============================================================================
// Cena Platform -- Elo Difficulty Service (FIND-pedagogy-009)
// Updates question difficulty ratings using the Elo formula after each answer.
// Implements the 85% rule for optimal learning (Wilson et al., 2019).
//
// Citation:
// Wilson, R.C., Shenhav, A., Straccia, M. & Cohen, J.D. (2019). 
// "The Eighty Five Percent Rule for optimal learning." Nature Communications, 10, 4646.
// DOI: 10.1038/s41467-019-12552-4
//
// Elo, A.E. (1978). "The Rating of Chessplayers, Past and Present." 
// Arco Publishing. ISBN: 978-0668047210
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Service for updating Elo difficulty ratings on questions after student answers.
/// Uses the classic Elo formula: D_new = D_old + K * (actual - expected)
/// </summary>
public interface IEloDifficultyService
{
    /// <summary>
    /// Updates the question's DifficultyElo based on student performance.
    /// </summary>
    /// <param name="questionDoc">The question that was answered.</param>
    /// <param name="studentTheta">The student's current ability estimate.</param>
    /// <param name="isCorrect">Whether the student answered correctly.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated difficulty rating.</returns>
    Task<float> UpdateDifficultyAsync(
        QuestionDocument questionDoc,
        float studentTheta,
        bool isCorrect,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of Elo difficulty updates.
/// K-factor for items is fixed at 20 (moderate adjustment rate).
/// </summary>
public sealed class EloDifficultyService : IEloDifficultyService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<EloDifficultyService> _logger;

    // K-factor for question difficulty updates
    // Lower than student K-factor because questions should be more stable
    private const float ItemK = 20f;

    public EloDifficultyService(IDocumentStore store, ILogger<EloDifficultyService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<float> UpdateDifficultyAsync(
        QuestionDocument questionDoc,
        float studentTheta,
        bool isCorrect,
        CancellationToken ct = default)
    {
        var oldDifficulty = questionDoc.DifficultyElo;
        
        // Compute expected correctness using Elo formula
        float expected = EloScoring.ExpectedCorrectness(studentTheta, oldDifficulty);
        float actual = isCorrect ? 1.0f : 0.0f;
        
        // Elo update: D_new = D_old + K * (actual - expected)
        // If student got it wrong (actual < expected), difficulty decreases
        // If student got it right (actual > expected), difficulty increases
        float adjustment = ItemK * (actual - expected);
        float newDifficulty = oldDifficulty + adjustment;
        
        // Clamp to reasonable bounds (500 - 2500)
        newDifficulty = Math.Clamp(newDifficulty, 500f, 2500f);
        
        // Update the document
        questionDoc.DifficultyElo = newDifficulty;
        
        await using var session = _store.LightweightSession();
        session.Store(questionDoc);
        await session.SaveChangesAsync(ct);
        
        _logger.LogDebug(
            "Updated DifficultyElo for question {QuestionId}: {Old:F0} → {New:F0} " +
            "(studentTheta={Theta:F0}, expected={Expected:F2}, actual={Actual}, adjustment={Adj:F1})",
            questionDoc.QuestionId, oldDifficulty, newDifficulty,
            studentTheta, expected, actual, adjustment);
        
        return newDifficulty;
    }
}
