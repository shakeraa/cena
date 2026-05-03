// =============================================================================
// Cena Platform — LlmBagrutQuestionSegmenter tests (ADR-0062, ADR-0026)
//
// Pins the contract that the brief lays out:
//   - LLM returns valid segments  → segments materialised verbatim;
//   - LLM returns empty/null/malformed → fall back to one-draft-per-page
//     with a WARN log line carrying trace_id + pdfId + duration;
//   - LLM throws  → fall back, breaker recorded as failure, WARN logged;
//   - LLM references a non-existent page → fall back (faulty segmenter);
//   - Multi-page segment → constituent pages collected in order;
//   - Flag OFF → invoker is never called;
//   - Cost metric emitted on the success path;
//   - trace_id stamped on log lines + activity tags.
//
// The fake invoker mirrors HybridConceptExtractorTests.FakeInvoker — a
// hand-rolled IAnthropicSegmenterInvoker that owns its JsonDocument
// lifetime so the JsonElement values stay valid for the assertion phase.
//
// The fake cost metric is a simple in-memory counter; we don't depend on
// the real Meter infrastructure here because the metric contract is "called
// once with the right labels", not "exported in a particular OTLP format".
// =============================================================================

using System.Text.Json;
using Cena.Admin.Api.AiSettings;
using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Ingestion.Segmenter;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion.Segmenter;

public sealed class LlmBagrutQuestionSegmenterTests
{
    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private const string TestPdfId = "pdf-test-abc123";
    private const string TestExamCode = "math-5u-2024-summer";

    private static IConfiguration BuildConfig(bool flagEnabled, string? apiKey = "fake-test-key")
    {
        var settings = new Dictionary<string, string?>
        {
            [LlmBagrutQuestionSegmenter.EnabledFlagKey] = flagEnabled ? "true" : "false",
        };
        if (apiKey is not null) settings["Anthropic:ApiKey"] = apiKey;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static IReadOnlyList<ExtractedPage> SamplePages(int count = 6)
    {
        var pages = new List<ExtractedPage>(count);
        for (var p = 1; p <= count; p++)
        {
            pages.Add(new ExtractedPage(
                PageNumber: p,
                RawText: $"Page {p} OCR text — שאלה {p}",
                ExtractedLatex: null,
                Figures: Array.Empty<ExtractedFigure>(),
                OcrConfidence: 0.9));
        }
        return pages;
    }

    private static LlmBagrutQuestionSegmenter BuildSegmenter(
        FakeInvoker invoker,
        IConfiguration config,
        FakeCostMetric? cost = null,
        FakeActivityPropagator? propagator = null)
    {
        return new LlmBagrutQuestionSegmenter(
            fallback:           new OneDraftPerPageSegmenter(),
            invoker:            invoker,
            configuration:      config,
            logger:             NullLogger<LlmBagrutQuestionSegmenter>.Instance,
            runtime:            null,
            documentStore:      null,
            cipher:             null,
            featureCost:        cost,
            activityPropagator: propagator);
    }

    // --------------------------------------------------------------------
    // Tests
    // --------------------------------------------------------------------

    [Fact]
    public async Task LlmReturnsValidSegments_MaterializesEmAll()
    {
        // 6-page PDF, LLM identifies pages 3-6 as 4 separate questions
        // (skipping the cover and "answer 5 of 8" pages 1-2). This is the
        // exact pattern from the user-reported defect on 35581-q.pdf.
        var invoker = new FakeInvoker(BuildToolInput(new (int, int, string?, double)[]
        {
            (3, 3, "שאלה 1", 0.95),
            (4, 4, "שאלה 2", 0.95),
            (5, 5, "שאלה 3", 0.95),
            (6, 6, "שאלה 4", 0.92),
        }));

        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true));

        var segments = await seg.SegmentAsync(SamplePages(), TestExamCode, TestPdfId);

        Assert.Equal(1, invoker.CallCount);
        Assert.Equal(4, segments.Count);
        Assert.Equal(3, segments[0].StartPage);
        Assert.Equal(3, segments[0].EndPage);
        Assert.Equal("שאלה 1", segments[0].QuestionLabel);
        Assert.Equal(0.95, segments[0].Confidence, 3);
        Assert.Equal(6, segments[3].StartPage);
        Assert.Equal("שאלה 4", segments[3].QuestionLabel);

        // Sanity: prompt content reached the invoker correctly.
        var captured = invoker.LastCall;
        Assert.NotNull(captured);
        Assert.Equal(LlmBagrutQuestionSegmenter.FallbackHaikuModelId, captured!.ModelId);
        Assert.Contains("Bagrut", captured.SystemPrompt);
        Assert.Contains("--- PAGE 1 ---", captured.UserPrompt);
        Assert.Contains("--- PAGE 6 ---", captured.UserPrompt);
        Assert.Contains(TestPdfId, captured.UserPrompt);
        Assert.Contains(TestExamCode, captured.UserPrompt);
    }

    [Fact]
    public async Task LlmReturnsEmptyOrNullToolInput_FallsBackToOneDraftPerPage_WithWarnLog()
    {
        // null tool input — Anthropic responded with text instead of a tool_use block.
        var invoker = new FakeInvoker(toolInput: null, inputTokens: 50, outputTokens: 0);
        var capturingLogger = new CapturingLogger();
        var seg = new LlmBagrutQuestionSegmenter(
            fallback:           new OneDraftPerPageSegmenter(),
            invoker:            invoker,
            configuration:      BuildConfig(flagEnabled: true),
            logger:             capturingLogger,
            activityPropagator: new FakeActivityPropagator("trace-empty"));

        var pages = SamplePages(count: 4);
        var segments = await seg.SegmentAsync(pages, TestExamCode, TestPdfId);

        Assert.Equal(1, invoker.CallCount);
        // Fallback returns one segment per populated page (4).
        Assert.Equal(4, segments.Count);
        Assert.All(segments, s => Assert.Equal(s.StartPage, s.EndPage));
        Assert.All(segments, s => Assert.Null(s.QuestionLabel));

        // Log line includes trace_id + pdf id + a duration tag.
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("no tool_use block")
                 && line.Contains(TestPdfId)
                 && line.Contains("trace-empty")
                 && line.Contains("duration_ms"));
    }

    [Fact]
    public async Task LlmReturnsEmptySegmentsOnNonEmptyPdf_FallsBackToOneDraftPerPage()
    {
        // Tool returned a valid response with an empty segments array. The
        // PDF has populated pages → empty is suspicious → fall back so the
        // curator at least sees drafts. Matches the brief's "empty result"
        // failure mode.
        var invoker = new FakeInvoker(BuildToolInput(Array.Empty<(int, int, string?, double)>()));

        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true));
        var segments = await seg.SegmentAsync(SamplePages(count: 3), TestExamCode, TestPdfId);

        Assert.Equal(1, invoker.CallCount);
        Assert.Equal(3, segments.Count); // fallback emitted one-per-page
    }

    [Fact]
    public async Task LlmThrows_FallsBackToOneDraftPerPage_WithWarnLog()
    {
        var invoker = new FakeInvoker(throwOnInvoke: new TimeoutException("anthropic deadline exceeded"));
        var capturingLogger = new CapturingLogger();
        var seg = new LlmBagrutQuestionSegmenter(
            fallback:           new OneDraftPerPageSegmenter(),
            invoker:            invoker,
            configuration:      BuildConfig(flagEnabled: true),
            logger:             capturingLogger,
            activityPropagator: new FakeActivityPropagator("trace-throw"));

        var segments = await seg.SegmentAsync(SamplePages(count: 5), TestExamCode, TestPdfId);

        Assert.Equal(1, invoker.CallCount);
        Assert.Equal(5, segments.Count); // fallback

        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("Anthropic call failed")
                 && line.Contains(TestPdfId)
                 && line.Contains("trace-throw")
                 && line.Contains("duration_ms"));
    }

    [Fact]
    public async Task LlmReturnsSegmentReferencingNonexistentPage_FallsBackToOneDraftPerPage()
    {
        // PDF has pages 1..3, but LLM claims a question on page 7. That's a
        // contract violation — fall back rather than emit a phantom draft.
        var invoker = new FakeInvoker(BuildToolInput(new (int, int, string?, double)[]
        {
            (1, 1, "שאלה 1", 0.9),
            (7, 7, "שאלה 2", 0.9),  // <-- bogus page
        }));

        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true));
        var pages = SamplePages(count: 3);
        var segments = await seg.SegmentAsync(pages, TestExamCode, TestPdfId);

        Assert.Equal(1, invoker.CallCount);
        // Fallback yields 3 segments (one per populated page).
        Assert.Equal(3, segments.Count);
        Assert.Equal(1, segments[0].StartPage);
        Assert.Equal(2, segments[1].StartPage);
        Assert.Equal(3, segments[2].StartPage);
    }

    [Fact]
    public async Task SegmentSpanningMultiplePages_CollectsConstituentPages_ReturnsSingleSegment()
    {
        // Question 1 spans pages 3-4 (problem statement + figure on p3,
        // sub-parts on p4). Segmenter returns one segment with start=3,
        // end=4; the materialiser is responsible for concatenating text and
        // collecting figures across both pages.
        var invoker = new FakeInvoker(BuildToolInput(new (int, int, string?, double)[]
        {
            (3, 4, "שאלה 1", 0.85),
            (5, 5, "שאלה 2", 0.95),
        }));

        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true));
        var segments = await seg.SegmentAsync(SamplePages(count: 6), TestExamCode, TestPdfId);

        Assert.Equal(2, segments.Count);
        Assert.Equal(3, segments[0].StartPage);
        Assert.Equal(4, segments[0].EndPage);
        Assert.Equal("שאלה 1", segments[0].QuestionLabel);
        Assert.Equal(5, segments[1].StartPage);
        Assert.Equal(5, segments[1].EndPage);
    }

    [Fact]
    public async Task FlagOff_LlmInvokerNeverCalled()
    {
        var invoker = new FakeInvoker(); // would explode if asked to return null tool input
        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: false));

        var segments = await seg.SegmentAsync(SamplePages(count: 3), TestExamCode, TestPdfId);

        Assert.Equal(0, invoker.CallCount);
        Assert.Equal(3, segments.Count); // OneDraftPerPageSegmenter directly
    }

    [Fact]
    public async Task ApiKeyMissing_LlmInvokerNeverCalled_FallsBackSilently()
    {
        var invoker = new FakeInvoker();
        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true, apiKey: null));

        var segments = await seg.SegmentAsync(SamplePages(count: 3), TestExamCode, TestPdfId);

        Assert.Equal(0, invoker.CallCount);
        Assert.Equal(3, segments.Count);
    }

    [Fact]
    public async Task CostMetricEmitted_WhenLlmRuns()
    {
        var invoker = new FakeInvoker(
            BuildToolInput(new (int, int, string?, double)[] { (1, 1, "שאלה 1", 0.9) }),
            inputTokens: 1234, outputTokens: 56);

        var costMetric = new FakeCostMetric();
        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true), cost: costMetric);

        await seg.SegmentAsync(SamplePages(count: 3), TestExamCode, TestPdfId);

        Assert.Single(costMetric.Records);
        var rec = costMetric.Records[0];
        Assert.Equal("content-segmentation", rec.Feature);
        Assert.Equal("tier2", rec.Tier);
        Assert.Equal("bagrut_segmentation", rec.Task);
        Assert.Equal(LlmBagrutQuestionSegmenter.FallbackHaikuModelId, rec.ModelId);
        Assert.Equal(1234L, rec.InputTokens);
        Assert.Equal(56L, rec.OutputTokens);
    }

    [Fact]
    public async Task TraceIdStamped_OnLlmCallSuccessLog()
    {
        var invoker = new FakeInvoker(
            BuildToolInput(new (int, int, string?, double)[] { (1, 1, "שאלה 1", 0.9) }),
            inputTokens: 100, outputTokens: 20);

        var capturingLogger = new CapturingLogger();
        var seg = new LlmBagrutQuestionSegmenter(
            fallback:           new OneDraftPerPageSegmenter(),
            invoker:            invoker,
            configuration:      BuildConfig(flagEnabled: true),
            logger:             capturingLogger,
            activityPropagator: new FakeActivityPropagator("trace-ok-12345"));

        await seg.SegmentAsync(SamplePages(count: 3), TestExamCode, TestPdfId);

        Assert.Contains(capturingLogger.InfoLines,
            line => line.Contains("LlmBagrutQuestionSegmenter OK")
                 && line.Contains("trace-ok-12345")
                 && line.Contains(TestPdfId)
                 && line.Contains("input_tokens=100")
                 && line.Contains("output_tokens=20"));
    }

    [Fact]
    public async Task PromptCarriesEveryPage_AndExamMetadata()
    {
        // Sanity-check the prompt-builder side of the segmenter: every OCR
        // page must appear in the user prompt, and the system prompt must
        // contain the Bagrut-specific cues we expect Haiku to recognise.
        var invoker = new FakeInvoker(BuildToolInput(new (int, int, string?, double)[] { (1, 1, "שאלה 1", 0.9) }));
        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true));

        await seg.SegmentAsync(SamplePages(count: 5), TestExamCode, TestPdfId);

        var captured = invoker.LastCall!;
        Assert.Contains("Hebrew Bagrut mathematics exam", captured.SystemPrompt);
        Assert.Contains("שאלה", captured.SystemPrompt);
        Assert.Contains("answer N of M", captured.SystemPrompt);
        Assert.Contains("Page 1 OCR text", captured.UserPrompt);
        Assert.Contains("Page 2 OCR text", captured.UserPrompt);
        Assert.Contains("Page 3 OCR text", captured.UserPrompt);
        Assert.Contains("Page 4 OCR text", captured.UserPrompt);
        Assert.Contains("Page 5 OCR text", captured.UserPrompt);
    }

    [Fact]
    public async Task EmptyPagesList_ReturnsEmptySegments_DoesNotCallLlm()
    {
        var invoker = new FakeInvoker();
        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true));

        var segments = await seg.SegmentAsync(Array.Empty<ExtractedPage>(), TestExamCode, TestPdfId);

        Assert.Equal(0, invoker.CallCount);
        Assert.Empty(segments);
    }

    [Fact]
    public async Task OverlappingSegmentsFromLlm_FallsBackToOneDraftPerPage()
    {
        // Two segments claim the same page → contract violation → fallback.
        var invoker = new FakeInvoker(BuildToolInput(new (int, int, string?, double)[]
        {
            (3, 4, "שאלה 1", 0.9),
            (4, 5, "שאלה 2", 0.9),  // overlaps page 4
        }));

        var seg = BuildSegmenter(invoker, BuildConfig(flagEnabled: true));
        var segments = await seg.SegmentAsync(SamplePages(count: 6), TestExamCode, TestPdfId);

        Assert.Equal(1, invoker.CallCount);
        Assert.Equal(6, segments.Count); // fallback
    }

    // --------------------------------------------------------------------
    // Fakes
    // --------------------------------------------------------------------

    private sealed class CapturedCall
    {
        public required string ApiKey { get; init; }
        public required string ModelId { get; init; }
        public required string SystemPrompt { get; init; }
        public required string UserPrompt { get; init; }
    }

    private sealed class FakeInvoker : IAnthropicSegmenterInvoker
    {
        private readonly IReadOnlyDictionary<string, JsonElement>? _toolInput;
        private readonly long _inputTokens;
        private readonly long _outputTokens;
        private readonly Exception? _throwOnInvoke;
        // Hold a reference to the JsonDocument so the JsonElement values
        // stay valid across the await boundary in the test method.
        private readonly JsonDocument? _ownedDoc;

        public int CallCount { get; private set; }
        public CapturedCall? LastCall { get; private set; }

        public FakeInvoker(
            JsonDocumentToolInput? toolInput = null,
            long inputTokens = 100,
            long outputTokens = 50,
            Exception? throwOnInvoke = null)
        {
            _ownedDoc = toolInput?.Document;
            _toolInput = toolInput?.AsDictionary();
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
            _throwOnInvoke = throwOnInvoke;
        }

        // Overload for the "null tool input but call succeeded" case.
        public FakeInvoker(
            IReadOnlyDictionary<string, JsonElement>? toolInput,
            long inputTokens,
            long outputTokens)
        {
            _toolInput = toolInput;
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
        }

        public Task<(IReadOnlyDictionary<string, JsonElement>? ToolInput, long InputTokens, long OutputTokens)>
            InvokeAsync(string apiKey, string modelId, string systemPrompt, string userPrompt, CancellationToken ct)
        {
            CallCount++;
            LastCall = new CapturedCall
            {
                ApiKey = apiKey,
                ModelId = modelId,
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
            };
            if (_throwOnInvoke is not null) throw _throwOnInvoke;
            return Task.FromResult<(IReadOnlyDictionary<string, JsonElement>?, long, long)>(
                (_toolInput, _inputTokens, _outputTokens));
        }
    }

    private sealed class JsonDocumentToolInput
    {
        public JsonDocument Document { get; init; } = null!;

        public IReadOnlyDictionary<string, JsonElement> AsDictionary()
        {
            var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in Document.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value;
            return dict;
        }
    }

    private static JsonDocumentToolInput BuildToolInput(
        IEnumerable<(int Start, int End, string? Label, double Confidence)> segments)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"segments\":[");
        var first = true;
        foreach (var (start, end, label, confidence) in segments)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append("\"start_page\":").Append(start).Append(',');
            sb.Append("\"end_page\":").Append(end).Append(',');
            sb.Append("\"question_label_or_null\":");
            if (label is null) sb.Append("null");
            else sb.Append(JsonSerializer.Serialize(label));
            sb.Append(',');
            sb.Append("\"confidence\":").Append(confidence.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture));
            sb.Append('}');
        }
        sb.Append("]}");

        return new JsonDocumentToolInput { Document = JsonDocument.Parse(sb.ToString()) };
    }

    private sealed record CostRecord(
        string Feature,
        string Tier,
        string Task,
        string ModelId,
        long InputTokens,
        long OutputTokens);

    private sealed class FakeCostMetric : ILlmCostMetric
    {
        public List<CostRecord> Records { get; } = new();

        public void Record(
            string feature,
            string tier,
            string task,
            string modelId,
            long inputTokens,
            long outputTokens,
            string? instituteId = null,
            string? examTargetCode = null)
        {
            Records.Add(new CostRecord(feature, tier, task, modelId, inputTokens, outputTokens));
        }
    }

    private sealed class FakeActivityPropagator : IActivityPropagator
    {
        private readonly string _trace;
        public FakeActivityPropagator(string trace) { _trace = trace; }
        public string GetTraceId() => _trace;
        public System.Diagnostics.Activity? StartLlmActivity(string taskName) => null;
    }

    /// <summary>
    /// Captures structured-log lines (with the format string + values flattened
    /// to a stable, assert-friendly text representation). Unlike a raw
    /// FormattedLogValues we render the message using the format string so
    /// the test can grep for "trace_id=..." / "duration_ms=..." literals
    /// rather than chasing the underlying KeyValuePair list.
    /// </summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<LlmBagrutQuestionSegmenter>
    {
        public List<string> WarnLines { get; } = new();
        public List<string> InfoLines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Render with the structured-log values flattened into the
            // template so "trace_id={TraceId}" becomes "trace_id=trace-…".
            var rendered = RenderTemplate(state);
            switch (logLevel)
            {
                case Microsoft.Extensions.Logging.LogLevel.Warning:
                case Microsoft.Extensions.Logging.LogLevel.Error:
                    WarnLines.Add(rendered);
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Information:
                    InfoLines.Add(rendered);
                    break;
            }
        }

        private static string RenderTemplate<TState>(TState state)
        {
            // FormattedLogValues exposes the raw template + values — chase
            // it via reflection because the type is internal.
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                var template = kvps.LastOrDefault(k => k.Key == "{OriginalFormat}").Value as string;
                if (template is null) return state.ToString() ?? "";
                var rendered = template;
                foreach (var kv in kvps)
                {
                    if (kv.Key == "{OriginalFormat}") continue;
                    rendered = rendered.Replace("{" + kv.Key + "}", kv.Value?.ToString() ?? "")
                                       .Replace("{" + kv.Key + ":F0}", kv.Value?.ToString() ?? "")
                                       .Replace("{" + kv.Key + ":F6}", kv.Value?.ToString() ?? "");
                    // Render structured-name=value pairs explicitly so tests
                    // can assert on "trace_id=..." / "duration_ms=..." even
                    // when the template uses curly-brace placeholders.
                    rendered += $" [{kv.Key}={kv.Value}]";
                }
                return rendered;
            }
            return state?.ToString() ?? "";
        }
    }
}
