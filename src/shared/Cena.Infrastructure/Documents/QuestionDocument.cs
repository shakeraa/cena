// =============================================================================
// Cena Platform — Question Document (HARDEN SessionEndpoints)
// Production-grade question storage for question bank
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Question document for the question bank. Stores question content,
/// metadata, and answer information for adaptive learning sessions.
/// </summary>
public class QuestionDocument
{
    public string Id { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Topic { get; set; }
    /// <summary>
    /// UI display label for difficulty (easy, medium, hard).
    /// FIND-pedagogy-009: This is for display ONLY — adaptive decisions use DifficultyElo.
    /// </summary>
    public string Difficulty { get; set; } = "medium"; // easy, medium, hard

    /// <summary>
    /// FIND-pedagogy-009: Continuous Elo rating for adaptive item selection.
    /// Targets 85% expected correctness (Wilson et al., 2019).
    /// Default: 1000 + (Grade - 7) * 100, or 1000 if grade unknown.
    /// </summary>
    public float DifficultyElo { get; set; } = 1000f;

    public string ConceptId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string QuestionType { get; set; } = "multiple-choice"; // multiple-choice, free-text, etc.
    public string[]? Choices { get; set; }
    public string CorrectAnswer { get; set; } = "";
    public string? Explanation { get; set; }

    /// <summary>
    /// FIND-pedagogy-001 — per-option distractor rationale keyed by the choice
    /// text (must match the value stored in <see cref="Choices"/>). When a
    /// student selects a wrong option, the endpoint looks up the rationale for
    /// THAT specific choice so feedback explains why their mistake happened,
    /// not just that it was wrong.
    ///
    /// Null when no rationales have been authored. Individual keys may be
    /// missing for options that have no rationale yet.
    /// </summary>
    public Dictionary<string, string>? DistractorRationales { get; set; }

    /// <summary>
    /// FIND-pedagogy-003 — BKT slip probability for this concept
    /// (P(wrong | knows)). Used by BktService when computing the posterior
    /// mastery after a student answer. Null means "use default"
    /// (<c>BktParameters.Default.PSlip</c>).
    /// </summary>
    public double? BktSlip { get; set; }

    /// <summary>
    /// FIND-pedagogy-003 — BKT guess probability for this concept
    /// (P(correct | does not know)). Used by BktService when computing the
    /// posterior mastery after a student answer. Null means "use default"
    /// (<c>BktParameters.Default.PGuess</c>).
    /// </summary>
    public double? BktGuess { get; set; }

    /// <summary>
    /// FIND-pedagogy-003 — BKT learning transition probability for this concept
    /// (P(learns between attempts)). Null means "use default"
    /// (<c>BktParameters.Default.PLearning</c>).
    /// </summary>
    public double? BktLearning { get; set; }

    public int? Grade { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
