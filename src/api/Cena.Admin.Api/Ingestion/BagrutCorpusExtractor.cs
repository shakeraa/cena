// =============================================================================
// Cena Platform — Bagrut corpus extractor (prr-242, ADR-0043)
//
// Transforms a (PDF ingestion draft list + curator metadata) tuple into
// BagrutCorpusItemDocument rows ready for IBagrutCorpusService.UpsertMany.
//
// Deterministic, pure function — no Marten, no LLM, no HTTP. The PDF → OCR
// cascade already ran upstream (Phase 1A, 13/13 layers); this takes the
// per-page draft output and encodes the Ministry tagging we need for the
// isomorph prompt + similarity check.
//
// Heuristics used to fill tags when curator metadata is incomplete:
//   * Year: first 4-digit number in [2000, current_year+1] seen in filename
//     or OCR text.
//   * Season: "קיץ" / "summer" → Summer; "חורף" / "winter" → Winter;
//     "אביב" / "spring" → Spring; "מיוחד" / "special" → Special.
//   * Moed: first "א/ב/ג" after "מועד" in the OCR stream; "A"/"B"/"C" in Latin.
//   * Units/TrackKey: derived from the question-paper code via the
//     prefix convention (035371 → 3U, 035471 → 4U, 035581/82/83 → 5U).
//   * Stream: "מגזר ערבי" / explicit curator tag → Arab; default Hebrew.
//
// The extractor NEVER throws on partial metadata — missing fields fall back
// to Unknown enum values / zero ints and the row still lands (the corpus
// coverage dashboard then surfaces the gap for curator fill-in).
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Ingestion;

/// <summary>
/// Metadata that the ingestion endpoint injects on top of OCR output.
/// Every field is optional; the extractor heuristics fill blanks.
/// </summary>
public sealed record BagrutCorpusIngestContext(
    string ExamCode,
    string MinistrySubjectCode,
    string MinistryQuestionPaperCode,
    int? Units,
    int? Year,
    BagrutCorpusSeason? Season,
    string? Moed,
    BagrutCorpusStream? Stream,
    string? DefaultTopicId,
    string? SourceFilename,
    string SourcePdfId,
    string? UploadedBy,
    DateTimeOffset IngestedAt);

public static class BagrutCorpusExtractor
{
    /// <summary>
    /// Project draft questions into corpus items. Pure, deterministic.
    /// </summary>
    public static IReadOnlyList<BagrutCorpusItemDocument> Extract(
        IReadOnlyList<IngestionDraftQuestion> drafts,
        BagrutCorpusIngestContext ctx)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(ctx);

        if (drafts.Count == 0) return Array.Empty<BagrutCorpusItemDocument>();

        var year = ctx.Year ?? InferYear(ctx.SourceFilename, drafts);
        var season = ctx.Season ?? InferSeason(ctx.SourceFilename, drafts);
        var moed = !string.IsNullOrWhiteSpace(ctx.Moed)
            ? ctx.Moed!.Trim().ToUpperInvariant()
            : InferMoed(ctx.SourceFilename, drafts);
        var stream = ctx.Stream ?? InferStream(ctx.SourceFilename, drafts);
        var units = ctx.Units ?? InferUnitsFromPaperCode(ctx.MinistryQuestionPaperCode);
        var trackKey = units > 0 ? $"{units}U" : string.Empty;

        var items = new List<BagrutCorpusItemDocument>(drafts.Count);
        var questionNumber = 0;
        foreach (var d in drafts)
        {
            questionNumber++;
            var raw = string.IsNullOrWhiteSpace(d.Prompt) ? (d.LatexContent ?? string.Empty) : d.Prompt;
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var normalised = Normalise(raw);
            // Min-length guard: the similarity n-gram checker uses 5-grams,
            // so stems shorter than 5 chars carry no signal. Keep the bar
            // low so short test fixtures and genuinely terse Ministry
            // items (e.g. "Evaluate 2+2.") still land in the corpus.
            if (normalised.Length < 5) continue;

            items.Add(new BagrutCorpusItemDocument
            {
                Id = BagrutCorpusItemDocument.ComposeId(
                    ctx.MinistrySubjectCode,
                    ctx.MinistryQuestionPaperCode,
                    questionNumber),
                MinistrySubjectCode = ctx.MinistrySubjectCode,
                MinistryQuestionPaperCode = ctx.MinistryQuestionPaperCode,
                Units = units,
                TrackKey = trackKey,
                Year = year,
                Season = season,
                Moed = moed,
                QuestionNumber = questionNumber,
                TopicId = ctx.DefaultTopicId ?? string.Empty,
                Stream = stream,
                RawText = raw,
                NormalisedStem = normalised,
                LatexContent = d.LatexContent,
                SourcePdfId = ctx.SourcePdfId,
                IngestConfidence = d.ExtractionConfidence,
                IngestedAt = ctx.IngestedAt,
                IngestedBy = ctx.UploadedBy,
            });
        }

        return items;
    }

    // ---- heuristics ----

    private static readonly Regex YearRegex = new(@"\b(20\d{2})\b", RegexOptions.Compiled);

    internal static int InferYear(
        string? sourceFilename,
        IReadOnlyList<IngestionDraftQuestion> drafts)
    {
        var corpus = new[] { sourceFilename ?? string.Empty }
            .Concat(drafts.Select(d => d.Prompt ?? string.Empty))
            .ToArray();

        foreach (var text in corpus)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (Match m in YearRegex.Matches(text))
            {
                if (int.TryParse(m.Groups[1].Value, out var y)
                    && y >= 2000 && y <= DateTime.UtcNow.Year + 1)
                    return y;
            }
        }
        return 0;
    }

    internal static BagrutCorpusSeason InferSeason(
        string? sourceFilename,
        IReadOnlyList<IngestionDraftQuestion> drafts)
    {
        var needle = ((sourceFilename ?? string.Empty) + " "
                      + string.Concat(drafts.Select(d => (d.Prompt ?? string.Empty) + " ")))
                     .ToLowerInvariant();
        if (needle.Contains("קיץ") || needle.Contains("summer")) return BagrutCorpusSeason.Summer;
        if (needle.Contains("חורף") || needle.Contains("winter")) return BagrutCorpusSeason.Winter;
        if (needle.Contains("אביב") || needle.Contains("spring")) return BagrutCorpusSeason.Spring;
        if (needle.Contains("מיוחד") || needle.Contains("special")) return BagrutCorpusSeason.Special;
        return BagrutCorpusSeason.Unknown;
    }

    private static readonly Regex HebrewMoedRegex = new(
        @"מועד\s*([אבג])", RegexOptions.Compiled);

    internal static string InferMoed(
        string? sourceFilename,
        IReadOnlyList<IngestionDraftQuestion> drafts)
    {
        foreach (var text in new[] { sourceFilename ?? string.Empty }
            .Concat(drafts.Select(d => d.Prompt ?? string.Empty)))
        {
            if (string.IsNullOrEmpty(text)) continue;
            var m = HebrewMoedRegex.Match(text);
            if (m.Success) return m.Groups[1].Value switch
            {
                "א" => "A",
                "ב" => "B",
                "ג" => "C",
                _ => string.Empty,
            };
            var lower = text.ToLowerInvariant();
            if (lower.Contains("moed a") || lower.Contains("moed-a")) return "A";
            if (lower.Contains("moed b") || lower.Contains("moed-b")) return "B";
            if (lower.Contains("moed c") || lower.Contains("moed-c")) return "C";
        }
        return string.Empty;
    }

    internal static BagrutCorpusStream InferStream(
        string? sourceFilename,
        IReadOnlyList<IngestionDraftQuestion> drafts)
    {
        var needle = ((sourceFilename ?? string.Empty) + " "
                      + string.Concat(drafts.Select(d => (d.Prompt ?? string.Empty) + " ")))
                     .ToLowerInvariant();
        if (needle.Contains("arab") || needle.Contains("מגזר ערבי") || needle.Contains("ערבי"))
            return BagrutCorpusStream.Arab;
        if (needle.Contains("druze") || needle.Contains("דרוזי"))
            return BagrutCorpusStream.Druze;
        return BagrutCorpusStream.Hebrew;
    }

    /// <summary>
    /// Ministry question-paper code → unit count. Derived from the prefix
    /// convention the Ministry uses for math (035xxx). Returns 0 on unknown.
    /// </summary>
    internal static int InferUnitsFromPaperCode(string paperCode)
    {
        if (string.IsNullOrWhiteSpace(paperCode) || paperCode.Length < 4) return 0;
        if (!paperCode.StartsWith("035", StringComparison.Ordinal)) return 0;
        // 035 371 / 035 372 → 3U; 035 471 / 472 → 4U; 035 581 / 582 / 583 → 5U.
        return paperCode[3] switch
        {
            '3' => 3,
            '4' => 4,
            '5' => 5,
            _ => 0,
        };
    }

    /// <summary>
    /// Stem normalisation aligned with Cena.Actors.QuestionBank.Coverage.
    /// MinistrySimilarityChecker.Normalise — we copy the contract so the
    /// checker can compare against what we stored.
    /// </summary>
    internal static string Normalise(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var lowered = s.Trim().ToLowerInvariant();
        var kept = new System.Text.StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) kept.Append(ch);
            else if (char.IsWhiteSpace(ch))
            {
                if (kept.Length > 0 && kept[^1] != ' ') kept.Append(' ');
            }
        }
        return kept.ToString().Trim();
    }
}
