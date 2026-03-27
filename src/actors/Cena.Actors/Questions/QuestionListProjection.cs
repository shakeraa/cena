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

    public QuestionReadModel Create(QuestionAuthored_V1 e) => FromCreation(
        e.QuestionId, e.Stem, e.Subject, e.Topic, e.Grade,
        e.BloomsLevel, e.Difficulty, e.ConceptIds, e.Language,
        "authored", e.AuthorId, e.Timestamp);

    public QuestionReadModel Create(QuestionIngested_V1 e) => FromCreation(
        e.QuestionId, e.Stem, e.Subject, e.Topic, e.Grade,
        e.BloomsLevel, e.Difficulty, e.ConceptIds, e.Language,
        "ingested", e.ImportedBy, e.Timestamp);

    public QuestionReadModel Create(QuestionAiGenerated_V1 e) => FromCreation(
        e.QuestionId, e.Stem, e.Subject, e.Topic, e.Grade,
        e.BloomsLevel, e.Difficulty, e.ConceptIds, e.Language,
        "ai-generated", e.RequestedBy, e.Timestamp);

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
