// =============================================================================
// Cena Platform — Question List Projection
// Marten SingleStreamProjection: maintains QuestionReadModel from events.
// Registered as Inline (synchronous with event append) for immediate consistency.
// =============================================================================

using System.Globalization;
using Cena.Actors.Events;
using Marten.Events.Aggregation;

namespace Cena.Actors.Questions;

public class QuestionListProjection : SingleStreamProjection<QuestionReadModel, string>
{
    // ── Creation Events → Create Read Model ──
    //
    // NOTE: In production Marten upcasts V1 → V2 before dispatch, so only
    // the V2 Create methods run against a live store. We keep the V1
    // overloads as a safety net for unit tests and any legacy replay paths
    // that bypass upcasting.

    public QuestionReadModel Create(QuestionAuthored_V1 e) =>
        Create(new QuestionAuthored_V2(
            e.QuestionId, e.Stem, e.StemHtml, e.Options,
            e.Subject, e.Topic, e.Grade, e.BloomsLevel, e.Difficulty,
            e.ConceptIds, e.Language, e.AuthorId, e.Timestamp,
            e.Explanation, LearningObjectiveId: null));

    public QuestionReadModel Create(QuestionAuthored_V2 e)
    {
        var model = FromCreation(
            e.QuestionId, e.Stem, e.Subject, e.Topic, e.Grade,
            e.BloomsLevel, e.Difficulty, e.ConceptIds, e.Language,
            "authored", e.AuthorId, e.Timestamp);
        model.Explanation = e.Explanation;
        model.LearningObjectiveId = e.LearningObjectiveId;
        return model;
    }

    public QuestionReadModel Create(QuestionIngested_V1 e) =>
        Create(new QuestionIngested_V2(
            e.QuestionId, e.Stem, e.StemHtml, e.Options,
            e.Subject, e.Topic, e.Grade, e.BloomsLevel, e.Difficulty,
            e.ConceptIds, e.Language, e.SourceDocId, e.SourceUrl,
            e.SourceFilename, e.OriginalText, e.ImportedBy, e.Timestamp,
            e.Explanation, LearningObjectiveId: null));

    public QuestionReadModel Create(QuestionIngested_V2 e)
    {
        var model = FromCreation(
            e.QuestionId, e.Stem, e.Subject, e.Topic, e.Grade,
            e.BloomsLevel, e.Difficulty, e.ConceptIds, e.Language,
            "ingested", e.ImportedBy, e.Timestamp);
        model.Explanation = e.Explanation;
        model.LearningObjectiveId = e.LearningObjectiveId;
        return model;
    }

    public QuestionReadModel Create(QuestionAiGenerated_V1 e) =>
        Create(new QuestionAiGenerated_V2(
            e.QuestionId, e.Stem, e.StemHtml, e.Options,
            e.Subject, e.Topic, e.Grade, e.BloomsLevel, e.Difficulty,
            e.ConceptIds, e.Language, e.PromptText, e.ModelId,
            e.ModelTemperature, e.RawModelOutput, e.RequestedBy,
            e.Explanation, e.Timestamp, LearningObjectiveId: null));

    public QuestionReadModel Create(QuestionAiGenerated_V2 e)
    {
        var model = FromCreation(
            e.QuestionId, e.Stem, e.Subject, e.Topic, e.Grade,
            e.BloomsLevel, e.Difficulty, e.ConceptIds, e.Language,
            "ai-generated", e.RequestedBy, e.Timestamp);
        model.Explanation = e.Explanation;
        model.LearningObjectiveId = e.LearningObjectiveId;
        return model;
    }

    // ── FIND-pedagogy-008 — LO backfill on an existing question ──
    public void Apply(LearningObjectiveAssigned_V1 e, QuestionReadModel model)
    {
        model.LearningObjectiveId = e.NewObjectiveId;
        model.UpdatedAt = e.Timestamp;
    }

    // ── Explanation Events ──

    public void Apply(ExplanationEdited_V1 e, QuestionReadModel model)
    {
        model.Explanation = e.NewExplanation;
        model.UpdatedAt = e.Timestamp;
    }

    public void Apply(QuestionExplanationUpdated_V1 e, QuestionReadModel model)
    {
        model.Explanation = e.Explanation;
        model.UpdatedAt = e.UpdatedAt;
    }

    // ── Edit Events → Update Read Model ──

    public void Apply(QuestionStemEdited_V1 e, QuestionReadModel model)
    {
        model.StemPreview = Truncate(e.NewStem, 120);
        model.UpdatedAt = e.Timestamp;
    }

    public void Apply(QuestionMetadataUpdated_V1 e, QuestionReadModel model)
    {
        switch (e.Field.ToLowerInvariant())
        {
            case "difficulty" when float.TryParse(e.NewValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d):
                model.Difficulty = d;
                break;
            case "bloomslevel" when int.TryParse(e.NewValue, out var b):
                model.BloomsLevel = b;
                break;
            case "grade":
                model.Grade = e.NewValue;
                break;
            case "topic":
                model.Topic = e.NewValue;
                break;
            case "conceptids":
                var ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(e.NewValue);
                if (ids != null)
                {
                    model.Concepts = ids;
                    model.ConceptNames = ids.Select(SlugToName).ToList();
                }
                break;
        }
        model.UpdatedAt = e.Timestamp;
    }

    // ── Quality Gate → Update Score ──

    public void Apply(QuestionQualityEvaluated_V1 e, QuestionReadModel model)
    {
        model.QualityScore = (int)Math.Round(e.CompositeScore);
        if (e.GateDecision == "NeedsReview" && model.Status == "Draft")
            model.Status = "InReview";
        model.UpdatedAt = e.Timestamp;
    }

    // ── Lifecycle Events → Update Status ──

    public void Apply(QuestionApproved_V1 e, QuestionReadModel model)
    {
        model.Status = "Approved";
        model.UpdatedAt = e.Timestamp;
    }

    public void Apply(QuestionPublished_V1 e, QuestionReadModel model)
    {
        model.Status = "Published";
        model.UpdatedAt = e.Timestamp;
    }

    public void Apply(QuestionDeprecated_V1 e, QuestionReadModel model)
    {
        model.Status = "Deprecated";
        model.UpdatedAt = e.Timestamp;
    }

    // ── FIND-data-008: Missing event handlers added ──

    public void Apply(QuestionOptionChanged_V1 e, QuestionReadModel model)
    {
        // Option change affects the question content but not the list view fields
        // Update timestamp so admin list shows "updated" status
        model.UpdatedAt = e.Timestamp;
    }

    public void Apply(LanguageVersionAdded_V1 e, QuestionReadModel model)
    {
        if (model.Languages == null)
            model.Languages = new List<string>();
        if (!model.Languages.Contains(e.Language))
            model.Languages.Add(e.Language);
        
        // FIND-pedagogy-013 — Persist per-locale explanation and distractor rationales
        if (!string.IsNullOrEmpty(e.Explanation))
        {
            model.ExplanationByLocale ??= new Dictionary<string, string>();
            model.ExplanationByLocale[e.Language] = e.Explanation;
        }
        
        if (e.DistractorRationales?.Count > 0)
        {
            model.DistractorRationalesByLocale ??= new Dictionary<string, Dictionary<string, string>>();
            model.DistractorRationalesByLocale[e.Language] = e.DistractorRationales.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value);
        }
        
        model.UpdatedAt = e.Timestamp;
    }
    
    // FIND-pedagogy-013 — Apply explanation to existing language version
    public void Apply(LanguageExplanationAdded_V1 e, QuestionReadModel model)
    {
        if (!string.IsNullOrEmpty(e.Explanation))
        {
            model.ExplanationByLocale ??= new Dictionary<string, string>();
            model.ExplanationByLocale[e.Language] = e.Explanation;
        }
        
        if (e.DistractorRationales?.Count > 0)
        {
            model.DistractorRationalesByLocale ??= new Dictionary<string, Dictionary<string, string>>();
            model.DistractorRationalesByLocale[e.Language] = e.DistractorRationales.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value);
        }
        
        model.UpdatedAt = e.Timestamp;
    }

    /// <summary>
    /// QuestionForked_V1 is intentionally ignored by the read model.
    /// Forking creates a new question stream; the source question is unchanged.
    /// </summary>
    public void Apply(QuestionForked_V1 e, QuestionReadModel model)
    {
        // No-op: source question is unchanged when forked
        // The fork creates a new QuestionReadModel in its own stream
    }

    // ── Helpers ──

    private static QuestionReadModel FromCreation(
        string id, string stem, string subject, string topic, string grade,
        int bloom, float difficulty, IReadOnlyList<string> concepts,
        string language, string sourceType, string createdBy, DateTimeOffset ts)
    {
        return new QuestionReadModel
        {
            Id = id,
            StemPreview = Truncate(stem, 120),
            Subject = subject,
            Topic = topic,
            Grade = grade,
            BloomsLevel = bloom,
            Difficulty = difficulty,
            Concepts = concepts.ToList(),
            ConceptNames = concepts.Select(SlugToName).ToList(),
            Status = "Draft",
            QualityScore = 0,
            UsageCount = 0,
            SuccessRate = null,
            SourceType = sourceType,
            Language = language,
            CreatedBy = createdBy,
            CreatedAt = ts
        };
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private static string SlugToName(string slug) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            slug.Replace("-", " ").Replace("_", " "));
}
