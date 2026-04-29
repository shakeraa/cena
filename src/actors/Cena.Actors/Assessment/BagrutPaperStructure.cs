// =============================================================================
// Cena Platform — BagrutPaperStructure (Phase 1B for the שאלון playbook)
//
// Describes the section composition of a Bagrut paper so the mock-exam
// runner can draw questions whose topic mix + bloom range matches the
// real-world structure, not just "any published math item".
//
// Three layers:
//   - PaperSlot: one question slot in a section. Topic + bloom band +
//     points + free-text hint.
//   - PaperSection: a group of slots with a shared "answer K of N" rule
//     and per-question point weight (Ministry-style).
//   - BagrutPaperStructure: the full paper — exam code (806/807/036),
//     optional paper code (035582 etc.), time limit, sections.
//
// Storage: Marten doc keyed by Id ("806/035582" or "806/default").
// Catalog seeds a small in-memory set for dev; the persistent store
// gets populated by the same seed at startup so the catalog can fall
// back to DB lookup once admin tooling exists to author/edit them.
//
// Why this matters: without a structure, "Mock-exam for paper 035582"
// served the same bag of questions as "any random math run". With it,
// Section A pulls 5 algebra/trig/calc-flavored items at bloom 2-3,
// Section B pulls 4 calculus/probability/vector items at bloom 3-4,
// and the result page shows Ministry-style 100-pt section breakdown.
// =============================================================================

namespace Cena.Actors.Assessment;

/// <summary>
/// One slot in a section. The question selector matches a published
/// QuestionDocument by <see cref="TopicId"/> + bloom range; if the
/// concrete topic has no items, the selector falls back to the
/// section's coarser topic family (see <see cref="PaperSection.FallbackTopicId"/>).
/// </summary>
public sealed record PaperSlot(
    int SlotNumber,
    string TopicId,
    int MinBloom,
    int MaxBloom,
    int Points,
    string? Notes = null);

/// <summary>
/// One section of a Bagrut paper. <see cref="RequiredAnswers"/> &lt;
/// <c>Slots.Count</c> means the student picks a subset (real Bagrut
/// "choose K of N").
/// </summary>
public sealed record PaperSection(
    string SectionLabel,            // "A" / "B" / numeric for niche papers
    int RequiredAnswers,
    string FallbackTopicId,
    IReadOnlyList<PaperSlot> Slots)
{
    public int TotalSlots => Slots.Count;
    public int TotalPoints => Slots.Take(RequiredAnswers).Sum(s => s.Points); // student-attainable max
}

/// <summary>
/// Persistent doc — the catalog is seeded with hard-coded structures
/// for the canonical Bagrut shapes (806 / 807 / 036) and per-paper
/// overrides where we have the details. Admin tooling for authoring
/// new structures is out of Phase 1 scope.
/// </summary>
public sealed class BagrutPaperStructureDocument
{
    /// <summary>"{examCode}/{paperCode}" or "{examCode}/default".</summary>
    public string Id { get; set; } = "";

    public string ExamCode { get; set; } = "";
    public string? PaperCode { get; set; }
    public int TimeLimitMinutes { get; set; }
    public List<PaperSection> Sections { get; set; } = new();

    /// <summary>
    /// PRR-293 — Ministry calculator policy for this paper. Real Bagrut
    /// math שאלונים differ: 5U typically permits a programmable graphing
    /// calculator with restrictions; 4U is more permissive; some niche
    /// papers prohibit. Display-only on the runner today (banner plus
    /// "Calculator: Allowed / Restricted / Prohibited"); a future
    /// version could enforce by hiding the in-app calculator widget.
    /// Default <see cref="CalculatorPolicy.Allowed"/> mirrors current
    /// 5U/4U convention; explicit Prohibited or Restricted overrides
    /// per-paper.
    /// </summary>
    public CalculatorPolicy CalculatorPolicy { get; set; } = CalculatorPolicy.Allowed;

    /// <summary>
    /// PRR-293 — formula-sheet mode. Real Bagrut day provides a printed
    /// formula sheet for math 5U + physics; some papers don't. The
    /// runner doesn't render a formula sheet today (PRR-292 deferred);
    /// this field is forward-compatible so the structure carries the
    /// signal to future renderers.
    /// </summary>
    public FormulaSheetMode FormulaSheetMode { get; set; } = FormulaSheetMode.None;

    /// <summary>
    /// Total achievable points if the student answers every required
    /// slot. Real Ministry math 5U sums to 100; Phase 1 mirrors that.
    /// </summary>
    public int TotalPoints =>
        Sections.Sum(s => s.Slots.Take(s.RequiredAnswers).Sum(slot => slot.Points));

    public static string ComposeId(string examCode, string? paperCode) =>
        string.IsNullOrWhiteSpace(paperCode) ? $"{examCode}/default" : $"{examCode}/{paperCode}";
}

/// <summary>PRR-293 — calculator policy values for a paper structure.</summary>
public enum CalculatorPolicy
{
    Allowed = 0,
    Restricted = 1,
    Prohibited = 2,
}

/// <summary>PRR-293 — formula-sheet supplied at exam time.</summary>
public enum FormulaSheetMode
{
    None = 0,
    MathBasic = 1,
    MathAdvanced = 2,
    PhysicsStandard = 3,
}

/// <summary>
/// In-memory catalog seeded with canonical structures. Marten-backed
/// lookup is checked first; on miss the in-memory default for the
/// exam code is returned.
/// </summary>
public interface IBagrutPaperStructureCatalog
{
    /// <summary>
    /// Resolve the structure for (examCode, paperCode). If
    /// <paramref name="paperCode"/> is null/empty or not found, returns
    /// the canonical default for the exam code. Throws
    /// <see cref="InvalidOperationException"/> only if no default exists
    /// for the exam code (i.e., misconfigured catalog).
    /// </summary>
    Task<BagrutPaperStructureDocument> GetAsync(
        string examCode, string? paperCode, CancellationToken ct);
}
