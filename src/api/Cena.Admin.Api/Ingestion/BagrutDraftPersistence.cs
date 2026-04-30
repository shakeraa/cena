// =============================================================================
// Cena Platform — BagrutDraftPersistence
//
// Bridges the gap that BagrutPdfIngestionService leaves: it returns
// IngestionDraftQuestion[] in the response and otherwise discards them.
// This service projects each draft into a PipelineItemDocument so curators
// can find them on the existing Ingestion Pipeline kanban (In Review
// column, Source type = "bagrut").
//
// Rationale: reuse the curator review UI that already exists for
// cloud-dir items rather than building a parallel "Bagrut drafts" page.
// Drafts land in InReview because OCR + segmentation already happened
// during the cascade — there's nothing left to do upstream of curator
// review.
//
// Curator metadata auto-fill (2026-04-29):
//   The Bagrut PDF flow knows the Subject/Language/SourceType/Track
//   deterministically — math is the only Bagrut subject we accept today,
//   the corpus is Hebrew, the source is by definition a Bagrut reference
//   PDF, and the track is encoded in the examCode prefix
//   ("math-5u-2026-35581" → 5u). We populate AutoExtractedMetadata at
//   persist time so the curator UI opens with the five required fields
//   pre-filled instead of forcing manual data-entry on every upload.
//   Strategy = "bagrut_path_inference_v1". TaxonomyNode is set to the
//   track-level placeholder (e.g. "math_5u" matching scripts/
//   bagrut-taxonomy.json) — the curator MUST drill down to a subtopic
//   before confirming, so its confidence is intentionally low (0.40).
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.Ingest;
using Cena.Api.Contracts.Admin.Ingestion;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion;

public interface IBagrutDraftPersistence
{
    Task<IReadOnlyList<string>> PersistAsync(
        string examCode,
        string sourcePdfId,
        string? sourceFilename,
        string submittedBy,
        IReadOnlyList<IngestionDraftQuestion> drafts,
        bool isMinistryReference = false,
        CancellationToken ct = default);
}

public sealed class BagrutDraftPersistence : IBagrutDraftPersistence
{
    private readonly IDocumentStore _store;
    private readonly ILogger<BagrutDraftPersistence> _logger;

    // examCode shape we recognise: "math-5u-..." / "math-4u-..." / "math-3u-..."
    // Anchored at start, case-insensitive. The "5u" fragment is what
    // CuratorMetadata.Track expects (matches the dropdown wire values
    // in CuratorMetadataPanel.vue: 3u/4u/5u). We don't use \b after the
    // track group because in .NET regex \b doesn't fire between word chars,
    // so "math_5u_2026" would fail (the underscore after "5u" is a word
    // char). Instead we require a separator (-/_/.) or end-of-string.
    private static readonly Regex BagrutExamCodeRx = new(
        @"^math[-_](?<track>3u|4u|5u)(?:[-_.]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Strategy identifier persisted onto the document so the UI / future
    // analytics can see how a metadata set was inferred. Bumped when the
    // inference rules change (e.g. moving from path-based to OCR-driven).
    public const string ExtractionStrategy = "bagrut_path_inference_v1";

    public BagrutDraftPersistence(
        IDocumentStore store,
        ILogger<BagrutDraftPersistence> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> PersistAsync(
        string examCode,
        string sourcePdfId,
        string? sourceFilename,
        string submittedBy,
        IReadOnlyList<IngestionDraftQuestion> drafts,
        bool isMinistryReference = false,
        CancellationToken ct = default)
    {
        if (drafts.Count == 0) return Array.Empty<string>();

        var now = DateTimeOffset.UtcNow;
        var ids = new List<string>(drafts.Count);

        // Pre-compute Track from the examCode (same for every draft in
        // this upload, so we parse once). TaxonomyNode is computed PER
        // draft from its prompt content — see ClassifyTaxonomy below.
        var (track, trackConfidence) = ParseTrack(examCode);

        // Capture the classified taxonomy leaves for an end-of-call
        // summary log — one row per draft, distinct values joined.
        // Lets ops see at a glance "this 5-page upload classified into
        // 4 distinct topics + 1 null" without grepping per-row writes.
        var classifiedTaxonomies = new List<string?>(drafts.Count);

        await using var session = _store.LightweightSession();
        foreach (var d in drafts)
        {
            // Synthesise a stable per-draft id derived from the draftId
            // emitted by BagrutPdfIngestionService.ExtractQuestions. Using
            // the draftId verbatim keeps re-uploads idempotent: same draft
            // id → upserts the same row instead of duplicating.
            var id = d.DraftId;
            ids.Add(id);

            // Per-draft taxonomy inference from prompt+LaTeX. Returns
            // (null, 0.0) when no keyword matched — curator picks.
            var (taxonomyNode, taxonomyConfidence) =
                ClassifyTaxonomy(d.Prompt, d.LatexContent);
            classifiedTaxonomies.Add(taxonomyNode);

            // Per-draft language detection by Hebrew/Arabic/Latin
            // character ratio in prompt+LaTeX. Bagrut papers exist in
            // all three (Israeli Arab schools take Arabic-language
            // Bagrut), so hardcoding "he" was wrong — surfaced by user
            // 2026-04-29 ("שאלון בערבית استمارة").
            var (language, languageConfidence) =
                DetectLanguage(d.Prompt, d.LatexContent);

            var stageRecord = new StageRecord
            {
                Stage = Cena.Actors.Ingest.PipelineStage.InReview,
                StartedAt = now,
                Status = "processing",
            };

            var doc = new PipelineItemDocument
            {
                Id = id,
                SourceFilename = sourceFilename ?? $"{examCode}-page{d.SourcePage}.pdf",
                SourceType = "bagrut",
                SourceUrl = sourcePdfId, // pdfId for cross-link
                ContentType = "application/pdf",
                ContentHash = id, // already SHA-derived in ExtractQuestions
                CurrentStage = Cena.Actors.Ingest.PipelineStage.InReview,
                Status = "processing",
                StageHistory = new List<StageRecord> { stageRecord },
                // Variants count starts at 0 — a Bagrut draft is the
                // *source* for AI-generated variants, not a variant
                // itself. GenerateVariantsJobStrategy increments this
                // (and ExtractedQuestionIds) when each candidate
                // persists, so the kanban "N variants" label tracks
                // the actual count in the question bank.
                ExtractedQuestionCount = 0,
                AvgQualityScore = (float)d.ExtractionConfidence,
                SubmittedBy = submittedBy,
                SubmittedAt = now,
                UpdatedAt = now,
                MetadataState = "auto_extracted",
                AutoExtractedMetadata = new PipelineCuratorMetadata
                {
                    Subject         = "math",                // Bagrut PDF flow is math-only today
                    Language        = language,              // detected from prompt+LaTeX char ratio
                    Track           = track,                 // parsed from examCode prefix
                    SourceType      = "bagrut_reference",    // by definition for this code path
                    TaxonomyNode    = taxonomyNode,          // keyword-classified from prompt; null when no match
                    ExpectedFigures = true,                  // Bagrut math nearly always carries figures
                },
                MetadataFieldConfidences = new Dictionary<string, double>
                {
                    [nameof(CuratorMetadata.Subject)]         = 0.95,
                    [nameof(CuratorMetadata.Language)]        = languageConfidence,
                    [nameof(CuratorMetadata.Track)]           = trackConfidence,
                    [nameof(CuratorMetadata.SourceType)]      = 0.99,
                    [nameof(CuratorMetadata.TaxonomyNode)]    = taxonomyConfidence,
                    [nameof(CuratorMetadata.ExpectedFigures)] = 0.70,
                },
                MetadataExtractionStrategy = ExtractionStrategy,
            };

            session.Store(doc);

            // Sibling payload row — actual prompt+LaTeX content keyed by
            // the same id, read by GenerateVariantsJobStrategy when the
            // curator clicks "Generate variants" on the kanban card.
            session.Store(new BagrutDraftPayloadDocument
            {
                Id = id,
                ExamCode = examCode,
                SourcePdfId = sourcePdfId,
                SourcePage = d.SourcePage,
                Prompt = d.Prompt,
                LatexContent = d.LatexContent,
                FigureSpecJson = d.FigureSpecJson,
                ExtractionConfidence = d.ExtractionConfidence,
                IsMinistryReference = isMinistryReference,
                ReviewNotes = d.ReviewNotes.ToList(),
                CreatedAt = now,
            });
        }

        await session.SaveChangesAsync(ct);

        var taxonomySummary = string.Join(", ",
            classifiedTaxonomies
                .Select(t => t ?? "(unmatched)")
                .GroupBy(t => t)
                .Select(g => g.Count() == 1 ? g.Key : $"{g.Key}×{g.Count()}"));

        _logger.LogInformation(
            "Persisted {Count} Bagrut drafts to PipelineItemDocument + BagrutDraftPayloadDocument (examCode={ExamCode}, pdfId={PdfId}, track={Track}, taxonomies={Taxonomies})",
            drafts.Count, examCode, sourcePdfId, track ?? "(unknown)", taxonomySummary);

        return ids;
    }

    // --------------------------------------------------------------------
    // Curator metadata inference
    // --------------------------------------------------------------------

    /// <summary>
    /// Parses the track ("3u" | "4u" | "5u") out of an examCode of the form
    /// "math-{track}-..." (e.g. "math-5u-2026-35581" → "5u"). Returns a
    /// (null, 0.5) fallback when the prefix doesn't match — the curator
    /// must then pick the track manually.
    /// </summary>
    internal static (string? Track, double Confidence) ParseTrack(string examCode)
    {
        if (string.IsNullOrWhiteSpace(examCode)) return (null, 0.5);
        var m = BagrutExamCodeRx.Match(examCode);
        if (!m.Success) return (null, 0.5);
        return (m.Groups["track"].Value.ToLowerInvariant(), 0.95);
    }

    /// <summary>
    /// Per-draft taxonomy classifier. Scans the prompt + LaTeX for
    /// math-domain keywords (Hebrew + Arabic + English) and returns the
    /// best-match taxonomy leaf RELATIVE to a track (per ADR-0019a /
    /// TaxonomyEvents.cs: the path is "topic.subtopic" without the
    /// track prefix). Returns (null, 0.0) when no keyword fires —
    /// curator picks.
    ///
    /// Confidence levels:
    ///   0.65 — strong, content-distinctive keyword (e.g. "אינטגרל" / "integral" / "تكامل")
    ///   0.55 — broader topic-level match (e.g. "פונקציה" / "function" / "دالة")
    ///   0.40 — fallback to topic root when subtopic ambiguous
    ///
    /// Arabic support added 2026-04-29 — Israeli Arab schools take the
    /// Arabic-language Bagrut, and "language" was hardcoded "he" before.
    ///
    /// This is a heuristic seed. Curator confirms / overrides during
    /// review. A future LLM-based classifier can replace this without
    /// changing the call site (signature stays stable).
    /// </summary>
    internal static (string? TaxonomyNode, double Confidence) ClassifyTaxonomy(
        string? prompt, string? latex)
    {
        if (string.IsNullOrWhiteSpace(prompt) && string.IsNullOrWhiteSpace(latex))
            return (null, 0.0);

        // Lower-case ASCII view for English keywords; Hebrew + Arabic
        // tokens matched as-is (no case in either script).
        var combined = ((prompt ?? "") + " " + (latex ?? ""));
        var lower = combined.ToLowerInvariant();

        // Order matters: more-specific subtopics first, broader topics
        // second. First match wins.
        // ── Calculus ────────────────────────────────────────────────────
        if (HasAny(combined, lower, "אינטגרל", "אינטגרלים", "תקאמל", "تكامل", "integral", "antiderivative")
            && HasAny(combined, lower, "מסוים", "מוגדר", "محدد", "معين", "definite"))
            return ("calculus.definite_integrals", 0.65);
        if (HasAny(combined, lower, "אינטגרל", "אינטגרלים", "تكامل", "integral", "antiderivative", "primitive"))
            return ("calculus.integrals_intro", 0.65);
        if (HasAny(combined, lower, "נגזרת", "נגזרות", "مشتقة", "اشتقاق", "derivative")
            && HasAny(combined, lower, "כלל", "כללי", "قاعدة", "قواعد", "rule", "rules", "chain", "product", "quotient"))
            return ("calculus.derivative_rules", 0.65);
        if (HasAny(combined, lower, "נגזרת", "נגזרות", "مشتقة", "اشتقاق", "derivative")
            && HasAny(combined, lower, "מקסימום", "מינימום", "מקסם", "מינימ",
                      "maximum", "minimum", "max", "min", "extremum", "optimum", "tangent",
                      "حد أقصى", "حد أدنى", "أقصى", "أدنى", "نهاية عظمى", "نهاية صغرى"))
            return ("calculus.applications_of_derivatives", 0.60);
        if (HasAny(combined, lower, "נגזרת", "נגזרות", "مشتقة", "اشتقاق", "derivative", "differentiate"))
            return ("calculus.derivative_definition", 0.55);
        if (HasAny(combined, lower, "גבול", "גבולות", "نهاية", "نهايات", "limit", "limits"))
            return ("calculus.limits", 0.65);

        // ── Trigonometry ────────────────────────────────────────────────
        if (HasAny(combined, lower, "סינוס", "קוסינוס", "טנגנס", "جيب", "جتا", "ظل", "sin", "cos", "tan")
            && HasAny(combined, lower, "כלל", "قاعدة", "rule", "law", "rules"))
            return ("trigonometry.sine_cosine_rules", 0.60);
        if (HasAny(combined, lower, "זהות", "זהויות", "متطابقة", "متطابقات", "identity", "identities"))
            return ("trigonometry.trig_identities", 0.55);
        if (HasAny(combined, lower, "רדיאן", "راديان", "radian"))
            return ("trigonometry.radian_measure", 0.65);
        if (HasAny(combined, lower, "סינוס", "קוסינוס", "טנגנס", "טריגונומטר",
                   "جيب", "جتا", "ظل", "مثلثات", "مثلثية",
                   "sin", "cos", "tan", "trig"))
            return ("trigonometry.trig_equations", 0.55);

        // ── Vectors ─────────────────────────────────────────────────────
        if (HasAny(combined, lower, "מכפלה סקלרית", "الجداء النقطي", "الضرب القياسي",
                   "dot product", "scalar product"))
            return ("vectors.dot_product", 0.65);
        if (HasAny(combined, lower, "מכפלה וקטורית", "الجداء الاتجاهي", "الضرب الاتجاهي",
                   "cross product"))
            return ("vectors.cross_product", 0.65);
        if (HasAny(combined, lower, "וקטור", "וקטורים", "متجه", "متجهات", "vector"))
            return ("vectors.vector_basics", 0.55);

        // ── Probability / Statistics ────────────────────────────────────
        if (HasAny(combined, lower, "התפלגות נורמלית", "توزيع طبيعي", "normal distribution"))
            return ("probability.normal_distribution", 0.65);
        if (HasAny(combined, lower, "התפלגות בינומית", "توزيع ثنائي", "binomial"))
            return ("probability.binomial_distribution", 0.65);
        if (HasAny(combined, lower, "מותנה", "احتمال شرطي", "conditional probability"))
            return ("probability.conditional_probability", 0.60);
        if (HasAny(combined, lower, "הסתברות", "احتمال", "احتمالات", "probability"))
            return ("probability.basic_probability", 0.55);
        if (HasAny(combined, lower, "צירוף", "תמורה", "تباديل", "توافيق",
                   "permutation", "combination", "combinatorics"))
            return ("probability.counting_principles", 0.60);

        // ── Geometry ────────────────────────────────────────────────────
        if (HasAny(combined, lower, "משולש", "مثلث", "triangle"))
            return ("geometry.triangles", 0.55);
        if (HasAny(combined, lower, "מעגל", "دائرة", "circle"))
            return ("geometry.circle_properties", 0.55);
        if (HasAny(combined, lower, "ישר", "מערכת צירים", "محاور", "إحداثيات", "coordinate"))
            return ("geometry.coordinate_geometry", 0.55);
        if (HasAny(combined, lower, "שטח", "נפח", "مساحة", "حجم", "area", "volume"))
            return ("geometry.area_volume", 0.55);

        // ── Functions ───────────────────────────────────────────────────
        if (HasAny(combined, lower, "לוגריתם", "لوغاريتم", "logarithm", "log"))
            return ("functions.logarithmic_functions", 0.60);
        if (HasAny(combined, lower, "אקספוננציאל", "أسي", "أسية", "exponential", "exponent"))
            return ("functions.exponential_functions", 0.60);
        if (HasAny(combined, lower, "ריבועי", "ריבועית", "تربيعي", "تربيعية", "quadratic"))
            return ("functions.quadratic_functions", 0.55);
        if (HasAny(combined, lower, "פונקציה", "פונקציות", "دالة", "دوال", "اقتران", "function"))
            return ("functions.function_basics", 0.45);

        // ── Algebra ─────────────────────────────────────────────────────
        if (HasAny(combined, lower, "פולינום", "كثير حدود", "كثيرة الحدود", "polynomial"))
            return ("algebra.polynomials", 0.65);
        if (HasAny(combined, lower, "סדרה", "סדרות", "متتالية", "متتاليات",
                   "sequence", "series"))
            return ("algebra.sequences_series", 0.55);
        if (HasAny(combined, lower, "מערכת משוואות", "نظام معادلات", "جملة معادلات",
                   "system of equations"))
            return ("algebra.systems_of_equations", 0.65);
        if (HasAny(combined, lower, "אי-שוויון", "אי שוויון", "متراجحة", "متباينة", "inequality"))
            return ("algebra.inequalities", 0.55);
        if (HasAny(combined, lower, "ריבועית", "تربيعية", "quadratic equation"))
            return ("algebra.quadratic_equations", 0.55);

        // No match — curator picks.
        return (null, 0.0);
    }

    /// <summary>
    /// Detects the dominant script in prompt+LaTeX and returns one of
    /// "he" / "ar" / "en". Hebrew block: U+0590..U+05FF. Arabic block:
    /// U+0600..U+06FF (plus U+0750-U+077F supplement). Anything else
    /// counted as Latin/symbolic. Confidence 0.95 when one script
    /// dominates (>=70% of letters), 0.70 for a tie/mixed, 0.50 when
    /// no letters at all (fall back to "he").
    /// </summary>
    internal static (string Language, double Confidence) DetectLanguage(string? prompt, string? latex)
    {
        var combined = (prompt ?? "") + " " + (latex ?? "");
        int hebrew = 0, arabic = 0, latin = 0;
        foreach (var c in combined)
        {
            if (c >= 0x0590 && c <= 0x05FF) hebrew++;
            else if ((c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F)) arabic++;
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
        }
        var total = hebrew + arabic + latin;
        if (total == 0) return ("he", 0.50);

        // Pick the largest. Confidence scales with dominance.
        if (hebrew >= arabic && hebrew >= latin)
            return ("he", hebrew >= total * 0.70 ? 0.95 : 0.70);
        if (arabic >= hebrew && arabic >= latin)
            return ("ar", arabic >= total * 0.70 ? 0.95 : 0.70);
        return ("en", latin >= total * 0.70 ? 0.95 : 0.70);
    }

    private static bool HasAny(string original, string lower, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (string.IsNullOrEmpty(n)) continue;
            // ASCII English needles match in lower-cased view to be
            // case-insensitive without allocating per call. Hebrew and
            // Arabic strings have no case — match in the original.
            var isAscii = n.All(c => c < 128);
            if (isAscii)
            {
                if (lower.Contains(n, StringComparison.Ordinal)) return true;
            }
            else
            {
                // Cheap exact-substring path first.
                if (original.Contains(n, StringComparison.Ordinal)) return true;

                // Definite-article + conjunction prefix tolerance:
                //   Hebrew single-letter prefixes: ה ו ב ל מ ש כ
                //   Arabic "ال" (al-, definite article), often combined
                //     with single-letter prefixes ف و ل ب ك (e.g. "بال")
                // Bagrut prompts read "המכפלה הסקלרית" / "الجداء النقطي"
                // rather than bare needles. Split on whitespace and
                // require every token to appear either bare or with a
                // recognized prefix.
                if (RtlTokensMatch(original, n)) return true;
            }
        }
        return false;
    }

    private static readonly char[] HebrewSinglePrefixes = "הובלמשכ".ToCharArray();
    private static readonly string[] ArabicPrefixes =
    {
        "ال", "وال", "بال", "فال", "كال", "لل", "وَال", "و", "ف", "ب", "ل", "ك",
    };

    private static bool RtlTokensMatch(string haystack, string needle)
    {
        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        foreach (var token in tokens)
        {
            if (haystack.Contains(token, StringComparison.Ordinal)) continue;

            var found = false;
            // Hebrew single-letter prefixes
            foreach (var p in HebrewSinglePrefixes)
            {
                if (haystack.Contains(p + token, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }
            // Arabic prefixes (multi-character)
            if (!found)
            {
                foreach (var p in ArabicPrefixes)
                {
                    if (haystack.Contains(p + token, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }
            }
            if (!found) return false;
        }
        return true;
    }
}
