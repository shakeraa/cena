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
    public string Difficulty { get; set; } = "medium"; // easy, medium, hard
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

    /// <summary>
    /// FIND-pedagogy-008 — reference to the <see cref="LearningObjectiveDocument"/>
    /// that this question assesses. Nullable so existing seeded questions
    /// replay cleanly; authored questions produced after this change are
    /// expected to carry an LO id and the authoring service logs a warning
    /// when one is missing.
    ///
    /// <para>
    /// Pedagogical rationale (Wiggins &amp; McTighe 2005, "Understanding by
    /// Design"): every assessment item must trace to an explicit learning
    /// goal so coverage analysis, gap detection, and standards alignment are
    /// possible. Bloom's taxonomy alone (Anderson &amp; Krathwohl 2001) is
    /// insufficient — the cognitive-process dimension tells you HOW deeply
    /// the learner must think, but the LO tells you WHAT specific goal is
    /// being demonstrated.
    /// </para>
    /// </summary>
    public string? LearningObjectiveId { get; set; }
}

