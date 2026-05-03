// =============================================================================
// Cena Platform — Bagrut reference-corpus item document (prr-242, ADR-0043)
//
// Stores a single past-Bagrut question as INTERNAL REFERENCE MATERIAL for the
// item-authoring pipeline. Per ADR-0043 and the 2026-04-15 "Bagrut reference
// only" memory, raw corpus text NEVER reaches a student — it only ever feeds:
//
//   * The MinistrySimilarityChecker (n-gram cosine reject-threshold guard)
//   * The isomorph-generation LLM as a "style guide / difficulty anchor"
//   * The admin review surface (curator-authored recreations)
//
// Storage posture:
//   * Marten document (standard-DB schema, same cluster as question bank).
//   * `NormalisedStem` is the deterministic similarity-check form (lower,
//     punctuation-stripped, whitespace-collapsed). The similarity checker
//     treats this as load-bearing; changing the normalisation requires a
//     coordinated re-ingest.
//   * `RawText` is the OCR'd Ministry text. Delivery-time code that tries
//     to put this on a student-facing DTO is caught by two layers:
//       1. `IItemDeliveryGate.AssertDeliverable` (runtime SIEM-logged throw)
//       2. `NoRawBagrutTextInStudentResponseTest` architecture scan of the
//          student-facing HTTP surfaces (this file PR).
//
// Id shape: `bagrut-corpus:{ministry_subject_code}:{question_paper_code}:{question_number}`
// — deterministic so re-ingest is idempotent.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Stream code: Hebrew-instruction mainstream ("Hebrew") vs Arabic-instruction
/// ("Arab") stream per ADR-0050 Q1. Also covers the niche cases ("Druze",
/// "Ultraorthodox") that share the Hebrew pool but are flagged for author
/// review.
/// </summary>
public enum BagrutCorpusStream
{
    Unknown = 0,
    Hebrew = 1,
    Arab = 2,
    Druze = 3,
    Ultraorthodox = 4,
}

/// <summary>
/// Exam season — Ministry runs three main windows and an accommodated
/// "Moed Special" (Miuhad) off-cycle.
/// </summary>
public enum BagrutCorpusSeason
{
    Unknown = 0,
    Winter = 1,
    Spring = 2,
    Summer = 3,
    Special = 4,
}

/// <summary>
/// A single reference item from the past-Bagrut corpus. Document rows are
/// internal — never directly serialized to a student surface.
/// </summary>
public sealed class BagrutCorpusItemDocument
{
    /// <summary>
    /// Deterministic id: bagrut-corpus:{subject}:{paper_code}:{question_number}.
    /// Equal inputs yield equal ids so re-ingest updates in place.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Ministry numeric subject code (e.g. "035" for math).</summary>
    public string MinistrySubjectCode { get; set; } = string.Empty;

    /// <summary>Ministry שאלון code (question-paper code), e.g. "035581".</summary>
    public string MinistryQuestionPaperCode { get; set; } = string.Empty;

    /// <summary>Unit level extracted from the question paper (3, 4, 5).</summary>
    public int Units { get; set; }

    /// <summary>Track code display, e.g. "3U" / "4U" / "5U".</summary>
    public string TrackKey { get; set; } = string.Empty;

    /// <summary>Year sat (calendar year of the canonical sitting).</summary>
    public int Year { get; set; }

    /// <summary>Season taxonomy.</summary>
    public BagrutCorpusSeason Season { get; set; }

    /// <summary>Moed letter (A | B | C | Special).</summary>
    public string Moed { get; set; } = string.Empty;

    /// <summary>Question number inside the paper (1-based).</summary>
    public int QuestionNumber { get; set; }

    /// <summary>Topic id from the curated taxonomy (e.g. "algebra.quadratics").</summary>
    public string TopicId { get; set; } = string.Empty;

    /// <summary>Stream (Hebrew, Arab, etc).</summary>
    public BagrutCorpusStream Stream { get; set; }

    /// <summary>
    /// Raw OCR'd question text. INTERNAL ONLY — never put on a student DTO
    /// (ADR-0043). Included here because the recreation-pipeline LLM sees
    /// it as a style anchor and similarity-check normalisation feeds from it.
    /// </summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>
    /// Similarity-check-normalised form: lowercase, punctuation stripped,
    /// whitespace collapsed. Load-bearing for MinistrySimilarityChecker;
    /// changing normalisation requires a coordinated re-ingest.
    /// </summary>
    public string NormalisedStem { get; set; } = string.Empty;

    /// <summary>Optional LaTeX payload extracted by OCR Layer 2b.</summary>
    public string? LatexContent { get; set; }

    /// <summary>Original source PDF id (sha fragment from the ingestor).</summary>
    public string SourcePdfId { get; set; } = string.Empty;

    /// <summary>OCR confidence at ingest time (0..1).</summary>
    public double IngestConfidence { get; set; }

    /// <summary>When this row landed.</summary>
    public DateTimeOffset IngestedAt { get; set; }

    /// <summary>
    /// Uploader user id — for the admin audit trail. Never propagates past
    /// the admin Read surface.
    /// </summary>
    public string? IngestedBy { get; set; }

    /// <summary>
    /// Deterministic id helper — callers that have the triple can compute
    /// the id without constructing the document.
    /// </summary>
    public static string ComposeId(
        string ministrySubjectCode,
        string ministryQuestionPaperCode,
        int questionNumber)
        => $"bagrut-corpus:{ministrySubjectCode}:{ministryQuestionPaperCode}:{questionNumber}";
}
