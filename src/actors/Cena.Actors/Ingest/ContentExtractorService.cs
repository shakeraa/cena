// =============================================================================
// Cena Platform -- Content Extractor Service
// SAI-05/SAI-07: Chunks OCR output into semantic content blocks, classifies
// type, and links to curriculum concepts via IConceptGraphCache.
// Runs as a parallel pipeline stage alongside QuestionSegmenter.
// SAI-07 enhancements: confidence threshold (0.7), max 5000 chars per block,
// optional LLM-based extraction via Gemini, fault-tolerant execution.
// =============================================================================

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cena.Actors.Mastery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Ingest;

/// <summary>
/// A single semantic content block extracted from OCR text.
/// </summary>
public sealed record ExtractedContentBlock(
    string RawText,
    string ProcessedText,
    string ContentType,
    IReadOnlyList<string> ConceptIds,
    string? PageRange,
    string Topic,
    float Confidence = 0.8f);

public interface IContentExtractorService
{
    /// <summary>
    /// Extracts semantic content blocks from OCR pages, classifies their type,
    /// and links them to curriculum concepts.
    /// </summary>
    Task<IReadOnlyList<ExtractedContentBlock>> ExtractAsync(
        IReadOnlyList<OcrPageOutput> pages,
        string subject,
        string language,
        CancellationToken ct = default);
}

public sealed partial class ContentExtractorService : IContentExtractorService
{
    private readonly IConceptGraphCache _conceptGraph;
    private readonly HttpClient? _http;
    private readonly GeminiOcrOptions? _geminiOptions;
    private readonly ILogger<ContentExtractorService> _logger;

    /// <summary>Minimum confidence to keep a content block.</summary>
    internal const float MinConfidenceThreshold = 0.7f;

    /// <summary>Maximum characters per content block.</summary>
    internal const int MaxBlockChars = 5000;

    /// <summary>Maximum approximate tokens per content block (SAI-05 spec).</summary>
    internal const int MaxBlockTokens = 500;

    /// <summary>Minimum approximate tokens per content block (SAI-05 spec).</summary>
    internal const int MinBlockTokens = 50;

    private const string ContentExtractionPrompt = """
        You are a content extraction specialist for Israeli math education materials.
        Extract explanatory content, definitions, theorems, worked examples, and proofs.
        Do NOT extract questions or exercises.

        Rules:
        - Preserve Hebrew/Arabic text exactly as written (RTL)
        - Preserve ALL mathematical expressions as LaTeX
        - Classify each block as: definition, theorem, example, explanation, exercise_solution, proof
        - Assign a confidence score (0.0-1.0) to each block
        - Skip blocks shorter than 20 characters
        - Skip anything that looks like a question, exercise prompt, or instruction to solve

        Return valid JSON array:
        [
          {
            "text": "the content text with LaTeX",
            "content_type": "definition",
            "confidence": 0.92,
            "topic_hint": "derivatives"
          }
        ]
        """;

    /// <summary>
    /// Primary constructor: rule-based extraction (no LLM dependency).
    /// </summary>
    public ContentExtractorService(
        IConceptGraphCache conceptGraph,
        ILogger<ContentExtractorService> logger)
    {
        _conceptGraph = conceptGraph;
        _logger = logger;
    }

    /// <summary>
    /// Enhanced constructor: LLM-assisted extraction via Gemini for higher accuracy.
    /// </summary>
    public ContentExtractorService(
        IConceptGraphCache conceptGraph,
        HttpClient http,
        IOptions<GeminiOcrOptions> geminiOptions,
        ILogger<ContentExtractorService> logger)
        : this(conceptGraph, logger)
    {
        _http = http;
        _geminiOptions = geminiOptions.Value;
    }

    public async Task<IReadOnlyList<ExtractedContentBlock>> ExtractAsync(
        IReadOnlyList<OcrPageOutput> pages,
        string subject,
        string language,
        CancellationToken ct = default)
    {
        var blocks = new List<ExtractedContentBlock>();

        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();

            // Try LLM-based extraction first if available, fall back to rule-based
            IReadOnlyList<ExtractedContentBlock>? llmBlocks = null;
            if (_http is not null && _geminiOptions is not null)
            {
                llmBlocks = await TryLlmExtractAsync(page, subject, language, ct);
            }

            if (llmBlocks is not null && llmBlocks.Count > 0)
            {
                blocks.AddRange(llmBlocks);
            }
            else
            {
                // Fallback: rule-based extraction
                blocks.AddRange(RuleBasedExtract(page, subject));
            }
        }

        // Apply confidence threshold and max-chars filter
        var filtered = blocks
            .Where(b => b.Confidence >= MinConfidenceThreshold)
            .Select(TruncateBlock)
            .ToList();

        _logger.LogInformation(
            "Extracted {BlockCount} content blocks from {PageCount} pages (filtered from {RawCount})",
            filtered.Count, pages.Count, blocks.Count);

        return filtered;
    }

    /// <summary>
    /// Truncate blocks exceeding the max character limit, preserving LaTeX integrity.
    /// </summary>
    internal static ExtractedContentBlock TruncateBlock(ExtractedContentBlock block)
    {
        if (block.RawText.Length <= MaxBlockChars && block.ProcessedText.Length <= MaxBlockChars)
            return block;

        return block with
        {
            RawText = TruncatePreservingLatex(block.RawText, MaxBlockChars),
            ProcessedText = TruncatePreservingLatex(block.ProcessedText, MaxBlockChars)
        };
    }

    private static string TruncatePreservingLatex(string text, int maxLen)
    {
        if (text.Length <= maxLen) return text;

        // Try to cut at a paragraph boundary before maxLen
        var cutPoint = text.LastIndexOf("\n\n", maxLen, StringComparison.Ordinal);
        if (cutPoint < maxLen / 2) cutPoint = maxLen;

        var truncated = text[..cutPoint].TrimEnd();

        // Ensure we don't leave unclosed LaTeX delimiters
        var dollarCount = truncated.Count(c => c == '$');
        if (dollarCount % 2 != 0) truncated += "$";

        return truncated;
    }

    // ── Token-based size enforcement (SAI-05 spec: max 500, min 50 tokens) ──

    /// <summary>
    /// Approximate token count using whitespace splitting (~1 token per word).
    /// This is a fast heuristic; exact tokenization is model-dependent.
    /// </summary>
    internal static int EstimateTokenCount(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Enforces token size limits: splits blocks exceeding MaxBlockTokens at sentence
    /// boundaries, and merges consecutive blocks under MinBlockTokens with the same type.
    /// </summary>
    internal static List<ExtractedContentBlock> EnforceTokenLimits(List<ExtractedContentBlock> blocks)
    {
        // Phase 1: Split oversized blocks at sentence boundaries
        var split = new List<ExtractedContentBlock>();
        foreach (var block in blocks)
        {
            var tokenCount = EstimateTokenCount(block.RawText);
            if (tokenCount <= MaxBlockTokens)
            {
                split.Add(block);
                continue;
            }

            // Split at sentence boundaries (. ! ? followed by space or newline)
            var sentences = SentenceBoundaryRegex().Split(block.RawText);
            var currentChunk = new StringBuilder();

            foreach (var sentence in sentences)
            {
                var sentenceTokens = EstimateTokenCount(sentence);

                // Adding this sentence would exceed the limit -- flush current chunk
                if (EstimateTokenCount(currentChunk.ToString()) + sentenceTokens > MaxBlockTokens
                    && currentChunk.Length > 0)
                {
                    var chunkText = currentChunk.ToString().Trim();
                    if (EstimateTokenCount(chunkText) > 0)
                    {
                        split.Add(block with
                        {
                            RawText = chunkText,
                            ProcessedText = ToMarkdown(chunkText, block.ContentType)
                        });
                    }
                    currentChunk.Clear();
                }

                currentChunk.Append(sentence);
            }

            // Flush remainder
            var remainder = currentChunk.ToString().Trim();
            if (EstimateTokenCount(remainder) > 0)
            {
                split.Add(block with
                {
                    RawText = remainder,
                    ProcessedText = ToMarkdown(remainder, block.ContentType)
                });
            }
        }

        // Phase 2: Merge undersized blocks with adjacent blocks of the same type
        if (split.Count <= 1)
            return split;

        var merged = new List<ExtractedContentBlock>();
        var accumulator = split[0];

        for (var i = 1; i < split.Count; i++)
        {
            var current = split[i];
            var accTokens = EstimateTokenCount(accumulator.RawText);

            // Merge if accumulator is undersized and same content type
            if (accTokens < MinBlockTokens && accumulator.ContentType == current.ContentType)
            {
                var mergedText = accumulator.RawText + "\n\n" + current.RawText;
                // Combine concept IDs (deduplicate)
                var combinedConcepts = accumulator.ConceptIds
                    .Concat(current.ConceptIds)
                    .Distinct()
                    .ToList();

                accumulator = accumulator with
                {
                    RawText = mergedText,
                    ProcessedText = ToMarkdown(mergedText, accumulator.ContentType),
                    ConceptIds = combinedConcepts,
                    Confidence = Math.Max(accumulator.Confidence, current.Confidence)
                };
            }
            else
            {
                merged.Add(accumulator);
                accumulator = current;
            }
        }
        merged.Add(accumulator);

        // Final pass: drop any blocks still under MinBlockTokens
        return merged.Where(b => EstimateTokenCount(b.RawText) >= MinBlockTokens).ToList();
    }

    [GeneratedRegex(@"(?<=[.!?。]\s)")]
    private static partial Regex SentenceBoundaryRegex();

    // ── LLM-based extraction via Gemini ──

    private async Task<IReadOnlyList<ExtractedContentBlock>?> TryLlmExtractAsync(
        OcrPageOutput page, string subject, string language, CancellationToken ct)
    {
        try
        {
            var prompt = $"{ContentExtractionPrompt}\n\nLanguage: {language}\nSubject: {subject}\n\nPage text:\n{page.RawText}";

            var request = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new { temperature = 0.1f, responseMimeType = "application/json" }
            };

            var url = $"{_geminiOptions!.BaseUrl}/models/{_geminiOptions.Model}:generateContent?key={_geminiOptions.ApiKey}";
            var response = await _http!.PostAsJsonAsync(url, request, ct);
            response.EnsureSuccessStatusCode();

            var geminiResponse = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var text = geminiResponse
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrEmpty(text))
                return null;

            return ParseLlmContentBlocks(text, page, subject);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LLM content extraction failed for page {Page}, falling back to rule-based",
                page.PageNumber);
            return null;
        }
    }

    private IReadOnlyList<ExtractedContentBlock> ParseLlmContentBlocks(
        string json, OcrPageOutput page, string subject)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<LlmContentBlockJson>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items is null || items.Count == 0)
                return [];

            var blocks = new List<ExtractedContentBlock>();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Text))
                    continue;

                var contentType = item.ContentType ?? "explanation";
                var processedText = ToMarkdown(item.Text, contentType);
                var conceptIds = LinkConcepts(item.Text, subject);
                var topic = !string.IsNullOrWhiteSpace(item.TopicHint) ? item.TopicHint : InferTopic(conceptIds);
                var pageRange = FormatPageRange(page.PageNumber, page.PageNumber);

                blocks.Add(new ExtractedContentBlock(
                    RawText: item.Text,
                    ProcessedText: processedText,
                    ContentType: contentType,
                    ConceptIds: conceptIds,
                    PageRange: pageRange,
                    Topic: topic,
                    Confidence: item.Confidence));
            }

            return blocks;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM content extraction JSON");
            return [];
        }
    }

    private sealed class LlmContentBlockJson
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("topic_hint")]
        public string? TopicHint { get; set; }
    }

    // ── Rule-based extraction (fallback) ──

    internal IReadOnlyList<ExtractedContentBlock> RuleBasedExtract(OcrPageOutput page, string subject)
    {
        var blocks = new List<ExtractedContentBlock>();
        var rawChunks = ChunkBySemanticBoundaries(page.RawText);

        foreach (var chunk in rawChunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.Text))
                continue;

            var contentType = ClassifyContentType(chunk.Text);
            if (contentType is null)
                continue;

            var processedText = ToMarkdown(chunk.Text, contentType);
            var conceptIds = LinkConcepts(chunk.Text, subject);
            var topic = InferTopic(conceptIds);
            var pageRange = FormatPageRange(page.PageNumber, page.PageNumber);

            // Rule-based confidence: higher for explicitly-marked content types,
            // lower for generic explanations
            var confidence = contentType switch
            {
                "definition" => 0.9f,
                "theorem" => 0.9f,
                "proof" => 0.9f,
                "example" => 0.85f,
                "exercise_solution" => 0.85f,
                "exercise" => 0.85f,
                "summary" => 0.85f,
                "explanation" => 0.75f,
                _ => 0.7f
            };

            blocks.Add(new ExtractedContentBlock(
                RawText: chunk.Text,
                ProcessedText: processedText,
                ContentType: contentType,
                ConceptIds: conceptIds,
                PageRange: pageRange,
                Topic: topic,
                Confidence: confidence));
        }

        return blocks;
    }

    // ── Chunking: split OCR text at semantic boundaries ──

    internal sealed record TextChunk(string Text, int StartLine);

    internal IReadOnlyList<TextChunk> ChunkBySemanticBoundaries(string ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return [];

        var lines = ocrText.Split('\n');
        var chunks = new List<TextChunk>();
        var currentChunk = new StringBuilder();
        var startLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Check for semantic boundary triggers
            var isBoundary = IsHeadingBoundary(trimmed)
                          || IsContentTypeMarker(trimmed)
                          || IsDoubleNewlineBoundary(line, i > 0 ? lines[i - 1] : null);

            if (isBoundary && currentChunk.Length > 0)
            {
                chunks.Add(new TextChunk(currentChunk.ToString().Trim(), startLine));
                currentChunk.Clear();
                startLine = i;
            }

            // Never split inside a LaTeX block
            if (IsInsideLatexBlock(currentChunk.ToString(), line))
            {
                currentChunk.AppendLine(line);
                continue;
            }

            currentChunk.AppendLine(line);
        }

        // Flush the last chunk
        if (currentChunk.Length > 0)
        {
            var text = currentChunk.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
                chunks.Add(new TextChunk(text, startLine));
        }

        return chunks;
    }

    private static bool IsHeadingBoundary(string trimmedLine)
    {
        // Numbered section headings: "1.2", "1.2.1", "2.3 ..." at start
        return SectionHeadingRegex().IsMatch(trimmedLine);
    }

    private static bool IsContentTypeMarker(string trimmedLine)
    {
        // Hebrew content markers
        if (trimmedLine.StartsWith("הגדרה:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("הגדרה ", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("משפט:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("משפט ", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("דוגמה:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("דוגמה ", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("פתרון:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("פתרון ", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("הוכחה:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("הוכחה ", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("סיכום:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("סיכום ", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("תרגיל:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("תרגיל ", StringComparison.Ordinal)) return true;

        // Arabic content markers
        if (trimmedLine.StartsWith("تعريف:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("نظرية:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("مثال:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("حل:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("برهان:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("ملخص:", StringComparison.Ordinal)) return true;
        if (trimmedLine.StartsWith("تمرين:", StringComparison.Ordinal)) return true;

        // English content markers
        if (trimmedLine.StartsWith("Definition:", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmedLine.StartsWith("Theorem:", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmedLine.StartsWith("Example:", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmedLine.StartsWith("Solution:", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmedLine.StartsWith("Proof:", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmedLine.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmedLine.StartsWith("Exercise:", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static bool IsDoubleNewlineBoundary(string currentLine, string? previousLine)
    {
        return string.IsNullOrWhiteSpace(currentLine) && string.IsNullOrWhiteSpace(previousLine);
    }

    private static bool IsInsideLatexBlock(string currentChunkText, string currentLine)
    {
        // Count unmatched $$ or \[ delimiters in the accumulated chunk
        var displayMathOpens = DisplayMathOpenRegex().Matches(currentChunkText).Count;
        var displayMathCloses = DisplayMathCloseRegex().Matches(currentChunkText).Count;

        if (displayMathOpens > displayMathCloses)
            return true;

        // Also check for \begin{...} without matching \end{...}
        var beginCount = BeginEnvRegex().Matches(currentChunkText).Count;
        var endCount = EndEnvRegex().Matches(currentChunkText).Count;

        return beginCount > endCount;
    }

    // ── Classification: determine content type from text markers ──

    internal static string? ClassifyContentType(string text)
    {
        var trimmed = text.TrimStart();

        // Definition markers
        if (trimmed.StartsWith("הגדרה", StringComparison.Ordinal)
            || trimmed.StartsWith("تعريف", StringComparison.Ordinal)
            || trimmed.StartsWith("Definition", StringComparison.OrdinalIgnoreCase))
            return "definition";

        // Theorem markers
        if (trimmed.StartsWith("משפט", StringComparison.Ordinal)
            || trimmed.StartsWith("נוסחה", StringComparison.Ordinal)
            || trimmed.StartsWith("נוסחא", StringComparison.Ordinal)
            || trimmed.StartsWith("نظرية", StringComparison.Ordinal)
            || trimmed.StartsWith("Theorem", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Lemma", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Corollary", StringComparison.OrdinalIgnoreCase))
            return "theorem";

        // Proof markers
        if (trimmed.StartsWith("הוכחה", StringComparison.Ordinal)
            || trimmed.StartsWith("برهان", StringComparison.Ordinal)
            || trimmed.StartsWith("Proof", StringComparison.OrdinalIgnoreCase))
            return "proof";

        // Summary markers
        if (trimmed.StartsWith("סיכום", StringComparison.Ordinal)
            || trimmed.StartsWith("ملخص", StringComparison.Ordinal)
            || trimmed.StartsWith("Summary", StringComparison.OrdinalIgnoreCase))
            return "summary";

        // Exercise markers (practice problems, distinct from bank questions)
        if (trimmed.StartsWith("תרגיל", StringComparison.Ordinal)
            || trimmed.StartsWith("تمرين", StringComparison.Ordinal)
            || trimmed.StartsWith("Exercise", StringComparison.OrdinalIgnoreCase))
            return "exercise";

        // Example markers
        if (trimmed.StartsWith("דוגמה", StringComparison.Ordinal)
            || trimmed.StartsWith("דוגמא", StringComparison.Ordinal)
            || trimmed.StartsWith("مثال", StringComparison.Ordinal)
            || trimmed.StartsWith("Example", StringComparison.OrdinalIgnoreCase))
            return "example";

        // Exercise solution markers
        if (trimmed.StartsWith("פתרון", StringComparison.Ordinal)
            || trimmed.StartsWith("תשובה", StringComparison.Ordinal)
            || trimmed.StartsWith("حل", StringComparison.Ordinal)
            || trimmed.StartsWith("Solution", StringComparison.OrdinalIgnoreCase))
            return "exercise_solution";

        // If it has enough prose, classify as explanation (minimum ~30 chars)
        if (text.Length >= 30)
            return "explanation";

        // Too short to be meaningful content
        return null;
    }

    // ── Markdown conversion ──

    internal static string ToMarkdown(string rawText, string contentType)
    {
        var sb = new StringBuilder();

        // Add a markdown heading based on content type
        var heading = contentType switch
        {
            "definition" => "Definition",
            "theorem" => "Theorem",
            "example" => "Example",
            "exercise_solution" => "Solution",
            "exercise" => "Exercise",
            "proof" => "Proof",
            "summary" => "Summary",
            _ => null
        };

        if (heading is not null)
        {
            sb.AppendLine($"### {heading}");
            sb.AppendLine();
        }

        // Preserve the raw text, normalizing line endings
        var normalized = rawText
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        sb.Append(normalized);

        return sb.ToString().TrimEnd();
    }

    // ── Concept linking via IConceptGraphCache ──

    internal IReadOnlyList<string> LinkConcepts(string text, string subject)
    {
        var concepts = _conceptGraph.Concepts;
        if (concepts.Count == 0)
            return ["unlinked"];

        var matched = new List<string>();
        var lowerText = text.ToLowerInvariant();

        foreach (var (conceptId, node) in concepts)
        {
            // Filter by subject if available
            if (!string.IsNullOrEmpty(subject)
                && !string.Equals(node.Subject, subject, StringComparison.OrdinalIgnoreCase))
                continue;

            var conceptName = node.Name;
            if (string.IsNullOrWhiteSpace(conceptName))
                continue;

            // Check for exact concept name match in the text (case-insensitive for Latin,
            // exact for Hebrew/Arabic since those are already case-insensitive)
            if (text.Contains(conceptName, StringComparison.Ordinal)
                || lowerText.Contains(conceptName.ToLowerInvariant(), StringComparison.Ordinal))
            {
                matched.Add(conceptId);
            }
        }

        // Tag as unlinked when no concept matches -- still useful for full-text search
        return matched.Count > 0 ? matched : ["unlinked"];
    }

    private string InferTopic(IReadOnlyList<string> conceptIds)
    {
        if (conceptIds.Count == 0)
            return "";

        // Use the TopicCluster of the first matched concept
        var firstConceptId = conceptIds[0];
        return _conceptGraph.Concepts.TryGetValue(firstConceptId, out var node)
            ? node.TopicCluster
            : "";
    }

    private static string FormatPageRange(int startPage, int endPage) =>
        startPage == endPage ? startPage.ToString() : $"{startPage}-{endPage}";

    // ── Compiled Regexes ──

    [GeneratedRegex(@"^\d+\.\d+")]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"\$\$|\\\\?\[")]
    private static partial Regex DisplayMathOpenRegex();

    [GeneratedRegex(@"\$\$|\\\\?\]")]
    private static partial Regex DisplayMathCloseRegex();

    [GeneratedRegex(@"\\begin\{")]
    private static partial Regex BeginEnvRegex();

    [GeneratedRegex(@"\\end\{")]
    private static partial Regex EndEnvRegex();
}
