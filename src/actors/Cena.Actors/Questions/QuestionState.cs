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
    public string Grade { get; set; } = "";
    public int BloomsLevel { get; set; }
    public float Difficulty { get; set; }
    public List<string> ConceptIds { get; set; } = new();

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

    public void Apply(QuestionAuthored_V1 e)
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
        EventVersion++;
    }

    public void Apply(QuestionIngested_V1 e)
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
        Provenance = new QuestionProvenanceState(
            e.SourceDocId, e.SourceUrl, e.SourceFilename,
            e.OriginalText, e.ImportedBy, e.Timestamp);
        EventVersion++;
    }

    public void Apply(QuestionAiGenerated_V1 e)
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
        AiProvenance = new AiGenerationState(
            e.PromptText, e.ModelId, e.ModelTemperature,
            e.RawModelOutput, e.RequestedBy, e.Timestamp,
            e.Explanation);
        Explanation = e.Explanation;
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
