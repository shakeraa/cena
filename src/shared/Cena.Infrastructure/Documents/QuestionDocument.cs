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
    /// FIND-pedagogy-009 (enriched): Continuous Elo rating for adaptive item
    /// selection. Targets ≈0.85 expected correctness per Wilson et al. 2019.
    /// Default 1500.0 matches standard Elo convention (1500 = average).
    ///
    /// Existing seeded questions should call <see cref="SeedDifficultyEloFromBucket"/>
    /// on first load to bootstrap from the 3-bucket <see cref="Difficulty"/>
    /// string: easy→1300, medium→1500, hard→1700. Default is used only when
    /// the bucket string is also missing or unknown.
    ///
    /// Stored as double (not float) because Elo accumulates many tiny updates
    /// over thousands of attempts and float precision loss is observable.
    /// </summary>
    public double DifficultyElo { get; set; } = 1500.0;

    /// <summary>
    /// FIND-pedagogy-009 (enriched): attempt counter for K-factor decay on the
    /// question side. New items use a larger K (≈32) for fast calibration and
    /// decay to a small K (≈8) once settled (&gt;500 attempts), mirroring the
    /// student-side decay in Cena.Actors.Mastery.EloScoring.StudentKFactor.
    /// </summary>
    public int EloAttemptCount { get; set; }

    /// <summary>
    /// FIND-pedagogy-009 (enriched): migrates the 3-bucket Difficulty string
    /// into an Elo seed on first load. Idempotent — only writes if DifficultyElo
    /// is still at the default. Returns true if a seed was applied so the
    /// caller can persist it.
    /// </summary>
    public bool SeedDifficultyEloFromBucket()
    {
        // Only seed if DifficultyElo is still at the unset default.
        if (Math.Abs(DifficultyElo - 1500.0) > 0.001) return false;
        var seed = Difficulty?.ToLowerInvariant() switch
        {
            "easy"   => 1300.0,
            "medium" => 1500.0,
            "hard"   => 1700.0,
            _        => 1500.0
        };
        if (Math.Abs(seed - 1500.0) < 0.001) return false; // medium == default
        DifficultyElo = seed;
        return true;
    }

    public string ConceptId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string QuestionType { get; set; } = "multiple-choice"; // multiple-choice, free-text, etc.
    public string[]? Choices { get; set; }
    public string CorrectAnswer { get; set; } = "";
    public string? Explanation { get; set; }

    /// <summary>
    /// FIND-pedagogy-013 — Per-locale explanations keyed by language code
    /// (e.g., "en", "ar", "he"). Legacy single-string Explanation is backfilled
    /// into the "en" slot for backward compatibility.
    /// </summary>
    public Dictionary<string, string>? ExplanationByLocale { get; set; }

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
    /// FIND-pedagogy-013 — Per-locale distractor rationales keyed by language code.
    /// Each inner dictionary maps choice text to rationale for that locale.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? DistractorRationalesByLocale { get; set; }

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
    /// BAGRUT-ALIGN-001: Structural alignment to a Bagrut exam section.
    /// Enables "Train Part A Q1" or "Train Part B proofs" strata in the
    /// question selector. Null for non-Bagrut or untagged questions.
    /// </summary>
    public BagrutAlignment? BagrutAlignment { get; set; }

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

    /// <summary>
    /// FIGURE-002 — Structured figure specification for the question.
    /// Stored as JSONB (polymorphic via STJ discriminator).
    /// Null for text-only questions or legacy documents.
    /// </summary>
    public FigureSpec? FigureSpec { get; set; }

    /// <summary>
    /// FIND-pedagogy-006 — list of prerequisite concept IDs that should be
    /// mastered before attempting this question. Used to calculate the
    /// Prerequisite Satisfaction Index (PSI) for scaffolding level determination.
    /// </summary>
    public List<string>? Prerequisites { get; set; }

    /// <summary>
    /// FIND-pedagogy-006 — authored worked example text shown to students
    /// when scaffolding level is "Full". Provides step-by-step guidance
    /// for novice learners.
    /// </summary>
    public string? WorkedExample { get; set; }

    /// <summary>
    /// FIND-pedagogy-013 — Gets the explanation for the specified locale,
    /// falling back to "en" and then to the legacy Explanation property.
    /// </summary>
    /// <param name="locale">The locale code (e.g., "en", "ar", "he")</param>
    /// <returns>The explanation text, or null if none found</returns>
    public string? GetExplanationForLocale(string locale)
    {
        // Try requested locale first
        if (ExplanationByLocale?.TryGetValue(locale, out var explanation) == true)
            return explanation;

        // Fall back to "en" (backfilled legacy content)
        if (ExplanationByLocale?.TryGetValue("en", out var enExplanation) == true)
            return enExplanation;

        // Legacy fallback for old documents
        return Explanation;
    }

    /// <summary>
    /// FIND-pedagogy-013 — Gets the distractor rationale for the specified
    /// choice and locale, falling back to "en" and then to the legacy
    /// DistractorRationales property.
    /// </summary>
    /// <param name="choice">The choice text</param>
    /// <param name="locale">The locale code (e.g., "en", "ar", "he")</param>
    /// <returns>The rationale text, or null if none found</returns>
    public string? GetDistractorRationaleForLocale(string choice, string locale)
    {
        // Try requested locale first
        if (DistractorRationalesByLocale?.TryGetValue(locale, out var rationales) == true)
            if (rationales?.TryGetValue(choice, out var rationale) == true)
                return rationale;

        // Fall back to "en"
        if (DistractorRationalesByLocale?.TryGetValue("en", out var enRationales) == true)
            if (enRationales?.TryGetValue(choice, out var enRationale) == true)
                return enRationale;

        // Legacy fallback
        return DistractorRationales?.GetValueOrDefault(choice);
    }
}

/// <summary>
/// BAGRUT-ALIGN-001: Structural alignment metadata for Bagrut exam questions.
/// Maps a question to a specific position and type within the Bagrut exam structure.
/// Used by the question selector to build structural strata (e.g., "Train Part A Q1").
/// </summary>
public sealed record BagrutAlignment
{
    /// <summary>Bagrut exam code: "806" (Calc+Geometry), "807" (Calc+Probability), "036" (Physics).</summary>
    public string ExamCode { get; init; } = "";

    /// <summary>Exam part: "A" (short problems, ~10 min each) or "B" (long problems, ~20-25 min each).</summary>
    public string Part { get; init; } = "";

    /// <summary>Typical position in Part A: "Q1"-"Q5". Null for Part B (variable ordering).</summary>
    public string? TypicalPosition { get; init; }

    /// <summary>
    /// Topic cluster for CAT stratification. Examples:
    /// "function_investigation", "integral_application", "analytic_geometry",
    /// "sequence_series", "word_problem", "probability", "vectors",
    /// "mechanics", "electricity", "optics".
    /// </summary>
    public string TopicCluster { get; init; } = "";

    /// <summary>Part B proof question flag. Part A questions are never proofs.</summary>
    public bool IsProofQuestion { get; init; }

    /// <summary>Estimated solve time in minutes (10-25). Used for session time budgeting.</summary>
    public int EstimatedMinutes { get; init; } = 15;
}

