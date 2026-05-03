// =============================================================================
// Cena Platform — Learning Objective Aggregate (domain model)
// FIND-pedagogy-008: explicit learning-objective metadata per assessment item
//
// Pedagogical basis:
//   - Wiggins, G. & McTighe, J. (2005). "Understanding by Design" (2nd ed.).
//     ASCD. ISBN: 978-1416600350 — backward design: every assessment item
//     must trace to a learning goal.
//   - Anderson, L.W. & Krathwohl, D.R. (Eds.) (2001). "A Taxonomy for
//     Learning, Teaching, and Assessing." Pearson. ISBN: 978-0321084057 —
//     the revised Bloom's matrix (cognitive-process × knowledge dimension).
//   - Biggs, J. (2003). "Aligning Teaching for Constructing Learning."
//     Higher Education Academy — constructive alignment: learning
//     objectives, teaching activities, and assessment tasks must be aligned.
//
// This is a pure domain model used by the admin API / tests. The persistent
// shape is carried by Cena.Infrastructure.Documents.LearningObjectiveDocument.
// Keeping the domain model here preserves the Actors bounded context; the
// Infrastructure document is merely the Marten-friendly POCO view.
// =============================================================================

using Cena.Infrastructure.Documents;

namespace Cena.Actors.Questions;

/// <summary>
/// Domain-layer view of a single learning objective. Produced by mapping from
/// the persisted <see cref="LearningObjectiveDocument"/>.
/// </summary>
public sealed record LearningObjective(
    string Id,
    string Code,
    string Title,
    string Description,
    string Subject,
    string? Grade,
    CognitiveProcess CognitiveProcess,
    KnowledgeType KnowledgeType,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyDictionary<string, string> StandardsAlignment,
    DateTimeOffset CreatedAt,
    bool IsActive)
{
    /// <summary>
    /// Backward-compatible single integer projection of the 2-axis
    /// classification — equal to <c>(int)CognitiveProcess</c> (1-6), matching
    /// the old <c>QuestionState.BloomsLevel</c> convention so consumers that
    /// still read the legacy int field continue to work.
    /// </summary>
    public int BloomsLevel => (int)CognitiveProcess;

    /// <summary>
    /// Convert a persisted Marten document into the immutable domain record.
    /// </summary>
    public static LearningObjective FromDocument(LearningObjectiveDocument doc) => new(
        Id: doc.Id,
        Code: doc.Code,
        Title: doc.Title,
        Description: doc.Description,
        Subject: doc.Subject,
        Grade: doc.Grade,
        CognitiveProcess: doc.CognitiveProcess,
        KnowledgeType: doc.KnowledgeType,
        ConceptIds: doc.ConceptIds.ToList().AsReadOnly(),
        StandardsAlignment:
            new Dictionary<string, string>(doc.StandardsAlignment),
        CreatedAt: doc.CreatedAt,
        IsActive: doc.IsActive);
}

/// <summary>
/// Classification tuple that represents a single cell in the Anderson &amp;
/// Krathwohl (2001) revised Bloom's matrix. Used by the aggregate when the
/// caller needs both axes of the classification — the legacy single-int
/// <c>BloomsLevel</c> collapses to only the cognitive-process axis and loses
/// the knowledge dimension.
/// </summary>
/// <param name="CognitiveProcess">
/// Vertical axis: remember / understand / apply / analyze / evaluate / create.
/// </param>
/// <param name="KnowledgeType">
/// Horizontal axis: factual / conceptual / procedural / metacognitive.
/// </param>
public readonly record struct BloomsClassification(
    CognitiveProcess CognitiveProcess,
    KnowledgeType KnowledgeType)
{
    /// <summary>Legacy single-int view (1..6).</summary>
    public int Level => (int)CognitiveProcess;

    /// <summary>Decompose a legacy int into a 2-axis classification. Defaults to conceptual knowledge.</summary>
    public static BloomsClassification FromLegacyLevel(int level) =>
        new((CognitiveProcess)Math.Clamp(level, 1, 6), KnowledgeType.Conceptual);
}
