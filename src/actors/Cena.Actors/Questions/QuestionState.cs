// =============================================================================
// Cena Platform — Question Aggregate State
// Rebuilt from events via Marten's AggregateStreamAsync<QuestionState>()
// Each Apply() method projects a single event type into the state.
// =============================================================================

using Cena.Actors.Events;

namespace Cena.Actors.Questions;

// ── Supporting Records ──

public sealed record QuestionOptionState(
    string Label,
    string Text,
    string TextHtml,
    bool IsCorrect,
    string? DistractorRationale);

public sealed record QuestionProvenanceState(
    string SourceDocId,
    string SourceUrl,
    string SourceFilename,
    string? OriginalText,
    string ImportedBy,
    DateTimeOffset ImportedAt);

public sealed record AiGenerationState(
    string PromptText,
    string ModelId,
    float ModelTemperature,
    string RawModelOutput,
    string RequestedBy,
    DateTimeOffset GeneratedAt,
    string? Explanation);

public sealed record LanguageVersionState(
    string Language,
    string Stem,
    string StemHtml,
    IReadOnlyList<QuestionOptionState> Options,
    string TranslatedBy,
    DateTimeOffset AddedAt);

public sealed record QualityEvaluationState(
    float CompositeScore,
    int FactualAccuracy,
    int LanguageQuality,
    int PedagogicalQuality,
    int DistractorQuality,
    int StemClarity,
    int BloomAlignment,
    int StructuralValidity,
    int CulturalSensitivity,
    string GateDecision,
    int ViolationCount,
    DateTimeOffset EvaluatedAt);

// ── Question Lifecycle Status (domain-owned) ──

public enum QuestionLifecycleStatus
{
    Draft,
    InReview,
    Approved,
    Published,
    Deprecated
}

// ── Aggregate State ──

public sealed class QuestionState
{
    // Identity
    public string Id { get; set; } = "";

    // Content (primary language, latest version)
    public string Stem { get; set; } = "";
    public string StemHtml { get; set; } = "";
    public List<QuestionOptionState> Options { get; set; } = new();

    // Classification
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    /// <summary>
    /// Legacy string tag. Historically held Bagrut track values
    /// ("3 Units", "4 Units", "5 Units") that don't describe a
    /// calendar-year grade at all (RDY-061 lying-label finding).
    /// New code SHOULD use <see cref="BagrutTrack"/>. We keep the field
    /// for upcaster compatibility and for non-Bagrut content that
    /// continues to use free-form tagging.
    /// </summary>
    public string Grade { get; set; } = "";

    /// <summary>
    /// RDY-061 Phase 5: typed replacement for the overloaded
    /// <see cref="Grade"/> string. Parsed at write-time from the
    /// incoming request or from a legacy Grade value by
    /// <see cref="ParseBagrutTrackFromGradeString"/>.
    /// </summary>
    public Cena.Infrastructure.Documents.BagrutTrack BagrutTrack { get; set; }
        = Cena.Infrastructure.Documents.BagrutTrack.None;

    /// <summary>
    /// Translate the legacy Grade string (e.g. <c>"5 Units"</c> or
    /// <c>"5U"</c>) into the typed enum. Returns
    /// <see cref="BagrutTrack.None"/> for unrecognised values so non-
    /// Bagrut content keeps flowing through.
    /// </summary>
    public static Cena.Infrastructure.Documents.BagrutTrack ParseBagrutTrackFromGradeString(string? grade)
    {
        if (string.IsNullOrWhiteSpace(grade))
            return Cena.Infrastructure.Documents.BagrutTrack.None;
        var normalised = grade.Trim().ToLowerInvariant();
        return normalised switch
        {
            "3 units" or "3u" or "3-unit" or "3unit" => Cena.Infrastructure.Documents.BagrutTrack.ThreeUnit,
            "4 units" or "4u" or "4-unit" or "4unit" => Cena.Infrastructure.Documents.BagrutTrack.FourUnit,
            "5 units" or "5u" or "5-unit" or "5unit" => Cena.Infrastructure.Documents.BagrutTrack.FiveUnit,
            _ => Cena.Infrastructure.Documents.BagrutTrack.None,
        };
    }
    public int BloomsLevel { get; set; }
    public float Difficulty { get; set; }
    public List<string> ConceptIds { get; set; } = new();

    /// <summary>
    /// FIND-pedagogy-008 — learning-objective id this question assesses.
    /// Nullable: old V1 streams upcast with null; authored questions should
    /// set a value at creation time and the authoring service logs a warning
    /// when missing. See Wiggins &amp; McTighe (2005) for the backward-design
    /// rationale and Anderson &amp; Krathwohl (2001) for the 2-axis Bloom's
    /// matrix that the LO carries.
    /// </summary>
    public string? LearningObjectiveId { get; set; }

    // Lifecycle
    public QuestionLifecycleStatus Status { get; set; } = QuestionLifecycleStatus.Draft;
    public int QualityScore { get; set; }
    public string SourceType { get; set; } = "authored";

    // Provenance
    public QuestionProvenanceState? Provenance { get; set; }
    public AiGenerationState? AiProvenance { get; set; }

    // Multi-language
    public string PrimaryLanguage { get; set; } = "he";
    public Dictionary<string, LanguageVersionState> LanguageVersions { get; set; } = new();

    // Explanation (L1 — static, per-question)
    public string? Explanation { get; set; }

    // Quality gate
    public QualityEvaluationState? LastQualityEvaluation { get; set; }

    // Audit
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int EventVersion { get; set; }

    // ── Apply Methods ──

    // ── V1 Apply methods are retained for back-compat with existing unit
    //    tests. In the live system, Marten upcasts V1 → V2 on stream read
    //    before dispatching to Apply, so production code only ever sees V2.
    //    The V1 overloads delegate to the V2 form with a null LO id.

    public void Apply(QuestionAuthored_V1 e) =>
        Apply(new QuestionAuthored_V2(
            e.QuestionId, e.Stem, e.StemHtml, e.Options,
            e.Subject, e.Topic, e.Grade, e.BloomsLevel, e.Difficulty,
            e.ConceptIds, e.Language, e.AuthorId, e.Timestamp,
            e.Explanation, LearningObjectiveId: null));

    public void Apply(QuestionAuthored_V2 e)
    {
        Id = e.QuestionId;
        Stem = e.Stem;
        StemHtml = e.StemHtml;
        Options = e.Options.Select(MapOption).ToList();
        Subject = e.Subject;
        Topic = e.Topic;
        Grade = e.Grade;
        BloomsLevel = e.BloomsLevel;
        Difficulty = e.Difficulty;
        ConceptIds = e.ConceptIds.ToList();
        PrimaryLanguage = e.Language;
        SourceType = "authored";
        CreatedBy = e.AuthorId;
        CreatedAt = e.Timestamp;
        Explanation = e.Explanation;
        Status = QuestionLifecycleStatus.Draft;
        LearningObjectiveId = e.LearningObjectiveId;
        EventVersion++;
    }

    public void Apply(QuestionIngested_V1 e) =>
        Apply(new QuestionIngested_V2(
            e.QuestionId, e.Stem, e.StemHtml, e.Options,
            e.Subject, e.Topic, e.Grade, e.BloomsLevel, e.Difficulty,
            e.ConceptIds, e.Language, e.SourceDocId, e.SourceUrl,
            e.SourceFilename, e.OriginalText, e.ImportedBy, e.Timestamp,
            e.Explanation, LearningObjectiveId: null));

    public void Apply(QuestionIngested_V2 e)
    {
        Id = e.QuestionId;
        Stem = e.Stem;
        StemHtml = e.StemHtml;
        Options = e.Options.Select(MapOption).ToList();
        Subject = e.Subject;
        Topic = e.Topic;
        Grade = e.Grade;
        BloomsLevel = e.BloomsLevel;
        Difficulty = e.Difficulty;
        ConceptIds = e.ConceptIds.ToList();
        PrimaryLanguage = e.Language;
        SourceType = "ingested";
        CreatedBy = e.ImportedBy;
        CreatedAt = e.Timestamp;
        Explanation = e.Explanation;
        Status = QuestionLifecycleStatus.Draft;
        LearningObjectiveId = e.LearningObjectiveId;
        Provenance = new QuestionProvenanceState(
            e.SourceDocId, e.SourceUrl, e.SourceFilename,
            e.OriginalText, e.ImportedBy, e.Timestamp);
        EventVersion++;
    }

    public void Apply(QuestionAiGenerated_V1 e) =>
        Apply(new QuestionAiGenerated_V2(
            e.QuestionId, e.Stem, e.StemHtml, e.Options,
            e.Subject, e.Topic, e.Grade, e.BloomsLevel, e.Difficulty,
            e.ConceptIds, e.Language, e.PromptText, e.ModelId,
            e.ModelTemperature, e.RawModelOutput, e.RequestedBy,
            e.Explanation, e.Timestamp, LearningObjectiveId: null));

    public void Apply(QuestionAiGenerated_V2 e)
    {
        Id = e.QuestionId;
        Stem = e.Stem;
        StemHtml = e.StemHtml;
        Options = e.Options.Select(MapOption).ToList();
        Subject = e.Subject;
        Topic = e.Topic;
        Grade = e.Grade;
        BloomsLevel = e.BloomsLevel;
        Difficulty = e.Difficulty;
        ConceptIds = e.ConceptIds.ToList();
        PrimaryLanguage = e.Language;
        SourceType = "ai-generated";
        CreatedBy = e.RequestedBy;
        CreatedAt = e.Timestamp;
        Status = QuestionLifecycleStatus.Draft;
        LearningObjectiveId = e.LearningObjectiveId;
        AiProvenance = new AiGenerationState(
            e.PromptText, e.ModelId, e.ModelTemperature,
            e.RawModelOutput, e.RequestedBy, e.Timestamp,
            e.Explanation);
        Explanation = e.Explanation;
        EventVersion++;
    }

    public void Apply(LearningObjectiveAssigned_V1 e)
    {
        LearningObjectiveId = e.NewObjectiveId;
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionStemEdited_V1 e)
    {
        Stem = e.NewStem;
        StemHtml = e.NewStemHtml;
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionOptionChanged_V1 e)
    {
        var idx = Options.FindIndex(o => o.Label == e.OptionLabel);
        if (idx >= 0)
        {
            Options[idx] = new QuestionOptionState(
                e.OptionLabel, e.NewText, e.NewTextHtml ?? $"<p>{e.NewText}</p>",
                e.IsCorrect, e.DistractorRationale);
        }
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionMetadataUpdated_V1 e)
    {
        switch (e.Field.ToLowerInvariant())
        {
            case "difficulty" when float.TryParse(e.NewValue, out var d):
                Difficulty = d;
                break;
            case "bloomslevel" when int.TryParse(e.NewValue, out var b):
                BloomsLevel = b;
                break;
            case "grade":
                Grade = e.NewValue;
                break;
            case "topic":
                Topic = e.NewValue;
                break;
            case "conceptids":
                ConceptIds = System.Text.Json.JsonSerializer
                    .Deserialize<List<string>>(e.NewValue) ?? new();
                break;
        }
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionQualityEvaluated_V1 e)
    {
        QualityScore = (int)Math.Round(e.CompositeScore);
        LastQualityEvaluation = new QualityEvaluationState(
            e.CompositeScore, e.FactualAccuracy, e.LanguageQuality,
            e.PedagogicalQuality, e.DistractorQuality, e.StemClarity,
            e.BloomAlignment, e.StructuralValidity, e.CulturalSensitivity,
            e.GateDecision, e.ViolationCount, e.Timestamp);

        // Gate decision drives status transitions
        if (e.GateDecision == "NeedsReview" && Status == QuestionLifecycleStatus.Draft)
            Status = QuestionLifecycleStatus.InReview;

        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionApproved_V1 e)
    {
        Status = QuestionLifecycleStatus.Approved;
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionPublished_V1 e)
    {
        Status = QuestionLifecycleStatus.Published;
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionDeprecated_V1 e)
    {
        Status = QuestionLifecycleStatus.Deprecated;
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(ExplanationEdited_V1 e)
    {
        Explanation = e.NewExplanation;
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(QuestionExplanationUpdated_V1 e)
    {
        Explanation = e.Explanation;
        UpdatedAt = e.UpdatedAt;
        EventVersion++;
    }

    public void Apply(QuestionForked_V1 e)
    {
        // The fork creates a NEW stream for NewQuestionId.
        // On the source stream, just record that a fork happened.
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    public void Apply(LanguageVersionAdded_V1 e)
    {
        LanguageVersions[e.Language] = new LanguageVersionState(
            e.Language, e.Stem, e.StemHtml,
            e.Options.Select(MapOption).ToList(),
            e.TranslatedBy, e.Timestamp);
        UpdatedAt = e.Timestamp;
        EventVersion++;
    }

    private static QuestionOptionState MapOption(QuestionOptionData d) =>
        new(d.Label, d.Text, d.TextHtml, d.IsCorrect, d.DistractorRationale);
}
