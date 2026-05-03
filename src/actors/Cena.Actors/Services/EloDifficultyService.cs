// =============================================================================
// Cena Platform — Elo Difficulty Service (FIND-pedagogy-009, enriched)
//
// Updates BOTH the student's Elo rating AND the question's Elo rating using
// the classical dual-update rule after each answer. The student side is
// persisted by appending StudentEloRatingUpdated_V1 to the student stream so
// the inline StudentProfileSnapshot projection replays it deterministically.
// The question side is a plain QuestionDocument write on the same caller-
// supplied IDocumentSession — one session, one SaveChangesAsync, no CQRS
// race (see FIND-data-007 for the lesson).
//
// Target: expected correctness ≈ 0.847 per Wilson et al. 2019 ("The Eighty
// Five Percent Rule for optimal learning", Nature Communications 10:4646,
// DOI 10.1038/s41467-019-12552-4). The existing ItemSelector already uses
// MasteryConstants.ProgressionThresholdF = 0.85f as its target; this service
// is the write-side that calibrates both ratings so that target is reachable.
//
// K-factor policy:
//   - Student K decays with attempt count via EloScoring.StudentKFactor
//     (40 at N<20, 25 at N<50, 10 thereafter). Matches Elo chess conventions
//     adapted for educational items (Pelanek 2016,
//     DOI 10.1007/s11257-015-9156-4).
//   - Question K decays similarly: 32 when the item has < 50 attempts,
//     16 up to 500, 8 thereafter. Items need slower decay because there are
//     orders of magnitude more students per item.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Result of a dual Elo update so the caller can log / surface both sides
/// without reloading the documents.
/// </summary>
public readonly record struct EloRatingUpdate(
    double OldStudentElo,
    double NewStudentElo,
    double OldQuestionElo,
    double NewQuestionElo,
    double ExpectedCorrectness,
    float StudentK,
    float QuestionK);

public interface IEloDifficultyService
{
    /// <summary>
    /// Dual Elo update. Mutates <paramref name="profile"/> and
    /// <paramref name="questionDoc"/> in-place, appends a
    /// <see cref="StudentEloRatingUpdated_V1"/> event to the student stream,
    /// and stages the question document write on the caller's session. The
    /// caller is responsible for <c>session.SaveChangesAsync()</c>.
    /// </summary>
    EloRatingUpdate UpdateRatings(
        IDocumentSession session,
        StudentProfileSnapshot profile,
        QuestionDocument questionDoc,
        bool isCorrect,
        DateTimeOffset timestamp);
}

public sealed class EloDifficultyService : IEloDifficultyService
{
    private readonly ILogger<EloDifficultyService> _logger;

    // Rating bounds. Below 500 Elo is meaningless (expected < 1e-3 vs a
    // 1500-rated student); above 2500 the adaptation is noise because the
    // target student population can't reach it in finite attempts.
    private const double MinRating = 500.0;
    private const double MaxRating = 2500.0;

    public EloDifficultyService(ILogger<EloDifficultyService> logger)
    {
        _logger = logger;
    }

    public EloRatingUpdate UpdateRatings(
        IDocumentSession session,
        StudentProfileSnapshot profile,
        QuestionDocument questionDoc,
        bool isCorrect,
        DateTimeOffset timestamp)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));

        // Pure math + in-place mutations — testable without Marten mocks.
        var result = ComputeAndApply(profile, questionDoc, isCorrect);

        // Student side → event stream. Replayed by
        // StudentProfileSnapshot.Apply(StudentEloRatingUpdated_V1).
        session.Events.Append(profile!.StudentId, new StudentEloRatingUpdated_V1(
            StudentId: profile.StudentId,
            QuestionId: questionDoc!.QuestionId,
            OldStudentElo: result.OldStudentElo,
            NewStudentElo: result.NewStudentElo,
            OldQuestionElo: result.OldQuestionElo,
            NewQuestionElo: result.NewQuestionElo,
            IsCorrect: isCorrect,
            ExpectedCorrectness: result.ExpectedCorrectness,
            StudentAttemptCountAfter: profile.EloAttemptCount,
            Timestamp: timestamp));

        // Question side → plain document write on the SAME session. Marten
        // batches it with the event append into one Postgres transaction —
        // no race with the caller's SaveChangesAsync (FIND-data-007 lesson).
        session.Store(questionDoc);

        _logger.LogDebug(
            "Elo dual update: student {StudentId} {OldS:F1}→{NewS:F1} (K={SK}), " +
            "question {QuestionId} {OldQ:F1}→{NewQ:F1} (K={QK}), " +
            "expected={Expected:F3} isCorrect={IsCorrect}",
            profile.StudentId, result.OldStudentElo, result.NewStudentElo, result.StudentK,
            questionDoc.QuestionId, result.OldQuestionElo, result.NewQuestionElo, result.QuestionK,
            result.ExpectedCorrectness, isCorrect);

        return result;
    }

    /// <summary>
    /// Pure dual-update computation that mutates the profile + question in
    /// place and returns the rating deltas. No session access — tests call
    /// this directly to avoid mocking Marten's IDocumentSession.Events.
    /// Internal for test visibility (InternalsVisibleTo already enabled on
    /// Cena.Actors).
    /// </summary>
    internal static EloRatingUpdate ComputeAndApply(
        StudentProfileSnapshot profile,
        QuestionDocument questionDoc,
        bool isCorrect)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (questionDoc is null) throw new ArgumentNullException(nameof(questionDoc));

        questionDoc.SeedDifficultyEloFromBucket();

        var oldStudent = profile.EloRating;
        var oldQuestion = questionDoc.DifficultyElo;

        var expected = ExpectedCorrectness(oldStudent, oldQuestion);
        var actual = isCorrect ? 1.0 : 0.0;

        var studentK = EloScoring.StudentKFactor(profile.EloAttemptCount);
        var questionK = QuestionKFactor(questionDoc.EloAttemptCount);

        // Classical Elo dual update — student and question share the same
        // probability delta and move in opposite directions.
        var delta = actual - expected;
        var newStudent = Clamp(oldStudent + studentK * delta);
        var newQuestion = Clamp(oldQuestion - questionK * delta);

        profile.EloRating = newStudent;
        profile.EloAttemptCount += 1;
        questionDoc.DifficultyElo = newQuestion;
        questionDoc.EloAttemptCount += 1;

        return new EloRatingUpdate(
            OldStudentElo: oldStudent,
            NewStudentElo: newStudent,
            OldQuestionElo: oldQuestion,
            NewQuestionElo: newQuestion,
            ExpectedCorrectness: expected,
            StudentK: studentK,
            QuestionK: questionK);
    }

    /// <summary>
    /// Expected probability of a correct response (double precision — the
    /// existing EloScoring helper is float-based; this service retains
    /// double throughout to avoid precision loss across many updates).
    /// </summary>
    internal static double ExpectedCorrectness(double studentElo, double questionElo)
        => 1.0 / (1.0 + Math.Pow(10.0, (questionElo - studentElo) / 400.0));

    /// <summary>
    /// Item-side K-factor decay. Items settle more slowly than students
    /// because they see many more attempts before stabilising — and because
    /// a wrong-shaped student-Elo update on a popular item has outsized
    /// downstream effect on everyone else who sees it.
    /// </summary>
    internal static float QuestionKFactor(int attemptCount)
    {
        if (attemptCount < 50) return 32f;
        if (attemptCount < 500) return 16f;
        return 8f;
    }

    private static double Clamp(double rating) => Math.Clamp(rating, MinRating, MaxRating);
}
