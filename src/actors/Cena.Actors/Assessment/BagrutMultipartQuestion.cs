// =============================================================================
// Cena Platform — BagrutMultipartQuestion (Phase 2A)
//
// Real Bagrut math questions are multi-part: a single Q1 typically has
// sub-parts (a), (b), (c) with their own prompts, answers, and point
// weights. Phase 1A's single-cell QuestionDocument cannot model this
// shape; the runner served fundamentally wrong question structure.
//
// Architectural decision: a new doc type alongside QuestionDocument
// rather than extending QuestionDocument with optional Subparts. The
// trade-offs:
//   ✅ Zero blast radius into the rest of the codebase. The shared
//      QuestionDocument is consumed by adaptive serving, BKT
//      calibration, item authoring, etc — none of those need to
//      reason about subparts. Any change to QuestionDocument touches
//      6+ teams.
//   ✅ The exam-prep service can prefer multi-part candidates per
//      slot and fall back to single-cell if the multi-part pool is
//      thin (graceful degradation).
//   ✅ Mark sheet honesty: a multi-part Q is naturally point-weighted
//      via its subparts; the structure catalog's slot-points just
//      become the "envelope" total, with subpart points summing to
//      that envelope.
//   ⚠️ Two doc types means two queries on the runner hot path. The
//      cost is small (Marten LINQ is fast for these shapes), and the
//      win in domain clarity outweighs it.
//
// CAS-gating: every subpart's CanonicalAnswer is registered with the
// same delivery-gate posture as QuestionDocument
// (TeacherAuthoredOriginal here; AiRecreated for variant-derived).
// Ministry-derived multi-part Q's flow through the same authoring
// pipeline + ADR-0043 gate.
// =============================================================================

namespace Cena.Actors.Assessment;

/// <summary>
/// One subpart of a multi-part Bagrut question. PartId is locale-free
/// ("a", "b", "c") so the SPA renders it untranslated; display label
/// comes from i18n.
/// </summary>
public sealed record BagrutQuestionSubpart(
    string PartId,
    string Prompt,
    string CorrectAnswer,
    int Points);

/// <summary>
/// Full multi-part Bagrut question. Sum of subparts.Points equals the
/// "envelope" point weight expected from the BagrutPaperStructure slot
/// that picks this Q.
/// </summary>
public sealed class BagrutMultipartQuestion
{
    /// <summary>Document Id — same convention as QuestionDocument
    /// (e.g., "exam-prep-multipart-{topic}-{n}").</summary>
    public string Id { get; set; } = "";

    /// <summary>"math" / "physics" — drives the subject filter.</summary>
    public string Subject { get; set; } = "";

    /// <summary>Topic id — must match BagrutPaperStructure slot TopicId
    /// (e.g., "math.calculus.integral"). Drives slot-aware draw.</summary>
    public string Topic { get; set; } = "";

    /// <summary>Bloom band that gates which slots can pick this Q.</summary>
    public int BloomsLevel { get; set; }

    /// <summary>Stem visible above the subparts (often "Given f(x) = ...").
    /// Empty string is acceptable when each subpart is fully self-contained.</summary>
    public string Stem { get; set; } = "";

    /// <summary>Provenance — must remain non-MinistryBagrut to satisfy
    /// ADR-0043 at the delivery gate. TeacherAuthoredOriginal for the
    /// dev seeder; AiRecreated for variant-pipeline output.</summary>
    public string SourceType { get; set; } = "TeacherAuthoredOriginal";

    /// <summary>Locale of the prompt + canonical answers ("en"/"he"/"ar").</summary>
    public string Language { get; set; } = "en";

    /// <summary>Marten audit metadata — when the row was upserted.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The subparts in order. Empty list is invalid (use
    /// QuestionDocument for single-cell).</summary>
    public List<BagrutQuestionSubpart> Subparts { get; set; } = new();

    /// <summary>Sum of <see cref="BagrutQuestionSubpart.Points"/>.</summary>
    public int TotalPoints => Subparts.Sum(p => p.Points);
}
