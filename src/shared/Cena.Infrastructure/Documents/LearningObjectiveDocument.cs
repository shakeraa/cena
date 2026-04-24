// =============================================================================
// Cena Platform — Learning Objective Document
// FIND-pedagogy-008: explicit learning-objective metadata per assessment item
//
// Pedagogical basis:
//   - Wiggins, G. & McTighe, J. (2005). "Understanding by Design" (2nd ed.).
//     ASCD. ISBN: 978-1416600350. Backward design requires every assessment
//     item to trace to an explicit learning goal, so coverage/gap analysis is
//     even possible.
//   - Anderson, L.W. & Krathwohl, D.R. (Eds.) (2001). "A Taxonomy for Learning,
//     Teaching, and Assessing." Pearson. ISBN: 978-0321084057. The revised
//     Bloom's taxonomy separates the cognitive-process dimension (remember,
//     understand, apply, analyze, evaluate, create) from the knowledge
//     dimension (factual, conceptual, procedural, metacognitive).
//   - Biggs, J. (2003). "Aligning Teaching for Constructing Learning."
//     Higher Education Academy. Constructive alignment requires objectives,
//     teaching activities, and assessment tasks to be aligned.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Anderson &amp; Krathwohl (2001) cognitive-process dimension. The vertical
/// axis of the revised Bloom's taxonomy matrix. Represents the mental action
/// the learner is expected to perform when demonstrating the objective.
/// </summary>
public enum CognitiveProcess
{
    /// <summary>Recognize, recall facts (L1).</summary>
    Remember = 1,
    /// <summary>Interpret, exemplify, classify, summarize, infer, compare, explain (L2).</summary>
    Understand = 2,
    /// <summary>Execute or implement a procedure in a familiar or novel situation (L3).</summary>
    Apply = 3,
    /// <summary>Differentiate, organize, attribute — break into parts (L4).</summary>
    Analyze = 4,
    /// <summary>Check, critique — make judgments against criteria (L5).</summary>
    Evaluate = 5,
    /// <summary>Generate, plan, produce — put elements together into a coherent whole (L6).</summary>
    Create = 6,
}

/// <summary>
/// Anderson &amp; Krathwohl (2001) knowledge dimension. The horizontal axis of
/// the revised Bloom's matrix. Represents the type of knowledge that the
/// objective requires the learner to master.
/// </summary>
public enum KnowledgeType
{
    /// <summary>Basic elements the learner must know: vocabulary, specific details.</summary>
    Factual = 1,
    /// <summary>Relationships among basic elements: classifications, principles, models.</summary>
    Conceptual = 2,
    /// <summary>How to do something; methods, techniques, algorithms.</summary>
    Procedural = 3,
    /// <summary>Awareness and knowledge of one's own cognition — strategies, self-knowledge.</summary>
    Metacognitive = 4,
}

/// <summary>
/// Marten document representing a single learning objective.
/// Questions carry a nullable <c>LearningObjectiveId</c> that references this
/// document's <c>Id</c>. The relationship is many-to-one (question → LO) as a
/// v1 constraint; many-to-many is explicitly deferred.
/// </summary>
public class LearningObjectiveDocument
{
    /// <summary>
    /// Stable Marten document identity. Format: <c>lo-{subject}-{code-slug}</c>.
    /// Example: <c>lo-math-alg-linear-001</c>.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable, curriculum-facing code that authors recognise.
    /// Example: <c>MATH-ALG-LINEAR-001</c>.
    /// </summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// Short, student-facing title (≤120 chars).
    /// Example: "Solve single-variable linear equations in one unknown".
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Longer prose description used by the author/reviewer UI. May embed
    /// the full "Students will be able to…" statement used during authoring.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Subject slug that scopes the LO — matches the same Subject strings used
    /// on <see cref="QuestionDocument"/>.
    /// </summary>
    public string Subject { get; set; } = "";

    /// <summary>
    /// Grade band for which the objective is authored (free text to match
    /// existing question-doc convention: "3 Units", "Grade 7", etc.). Nullable
    /// for cross-grade objectives.
    /// </summary>
    public string? Grade { get; set; }

    /// <summary>
    /// Anderson &amp; Krathwohl cognitive-process dimension. Required: every
    /// LO picks exactly one level of cognition.
    /// </summary>
    public CognitiveProcess CognitiveProcess { get; set; } = CognitiveProcess.Understand;

    /// <summary>
    /// Anderson &amp; Krathwohl knowledge dimension. Required: every LO
    /// tags the type of knowledge it operates on.
    /// </summary>
    public KnowledgeType KnowledgeType { get; set; } = KnowledgeType.Conceptual;

    /// <summary>
    /// Backward-compatibility projection of the 2-axis classification onto a
    /// single integer 1-6 (the old "BloomsLevel" value). Computed from
    /// <see cref="CognitiveProcess"/>. Questions that still carry
    /// <c>BloomsLevel</c> as an int should read this derived value.
    /// </summary>
    public int BloomsLevel => (int)CognitiveProcess;

    /// <summary>
    /// Concept ids this objective concerns. Bridges the existing
    /// ConceptId-based adaptive system to the new LO model. A single LO can
    /// cover multiple concepts (e.g. "linear-equations", "equation-solving").
    /// </summary>
    public List<string> ConceptIds { get; set; } = new();

    /// <summary>
    /// External standards alignment (Bagrut, Common Core, NGSS, etc.).
    /// Free-form JSON-ish: key = standard framework, value = code in that
    /// framework. Example: <c>{"bagrut":"MATH-3U-ALG","common-core":"HSA.REI.B.3"}</c>.
    /// </summary>
    public Dictionary<string, string> StandardsAlignment { get; set; } = new();

    /// <summary>Authoring audit.</summary>
    public string CreatedBy { get; set; } = "seed-script";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Soft-delete flag. Disabled LOs are kept for history but cannot be picked.</summary>
    public bool IsActive { get; set; } = true;
}
