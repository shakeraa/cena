// =============================================================================
// Cena Platform — GeminiVisionPageExtractor tests (vision-extractor branch)
//
// Pins the contract:
//   - Flag OFF              → returns null without HTTP call.
//   - No API key            → returns null without HTTP call.
//   - No IModelResolver     → returns null without HTTP call (test scaffolding).
//   - Resolver returns non-Gemini → returns null with WARN.
//   - Tool input null       → returns null with WARN.
//   - Tool input malformed  → returns null with WARN.
//   - Empty promptText      → returns null with WARN.
//   - Out-of-bounds figure  → kept (cropper will clamp; the extractor passes
//                              through bbox values verbatim — this test
//                              pins that the extractor does NOT pre-filter
//                              by bbox sanity, that's FigureCropper's job).
//   - Happy path            → BagrutPageExtraction returned verbatim,
//                              cost metric emitted, trace_id stamped.
//   - HTTP non-success      → returns null, breaker failure recorded.
//   - HttpClient throws     → returns null, breaker failure recorded.
//
// All scenarios use a DelegatingHandler so the real GeminiVisionPageExtractor
// is exercised end-to-end (request shape, JSON serialisation, response
// parsing). Mirrors GeminiVisionRunnerTests in the Cena.Infrastructure
// test project.
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cena.Admin.Api.AiSettings;
using Cena.Admin.Api.Ingestion.Vision;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Tests.Ingestion.Vision;

public sealed class GeminiVisionPageExtractorTests
{
    private const string TestPdfId = "pdf-test-abc123";
    private const string TestModelId = "gemini-2.0-flash";

    private static readonly byte[] FakePngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private static IConfiguration BuildConfig(bool flagEnabled, string? apiKey = "test-api-key")
    {
        var settings = new Dictionary<string, string?>
        {
            [GeminiVisionPageExtractor.EnabledFlagKey] = flagEnabled ? "true" : "false",
        };
        if (apiKey is not null) settings["Ocr:Gemini:ApiKey"] = apiKey;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static GeminiVisionPageExtractor BuildExtractor(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder,
        IConfiguration config,
        IModelResolver? modelResolver = null,
        FakeCostMetric? cost = null,
        FakeActivityPropagator? propagator = null,
        ILogger<GeminiVisionPageExtractor>? logger = null,
        string? apiKeyOverride = "test-api-key")
    {
        var handler = new CapturingHandler(responder);
        var http = new HttpClient(handler);
        var opts = Options.Create(new GeminiVisionPageExtractorOptions
        {
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
            ApiKey = apiKeyOverride,
            RequestTimeout = TimeSpan.FromSeconds(5),
        });

        return new GeminiVisionPageExtractor(
            http: http,
            opts: opts,
            configuration: config,
            logger: logger ?? NullLogger<GeminiVisionPageExtractor>.Instance,
            modelResolver: modelResolver ?? new FixedModelResolver(TestModelId),
            activityPropagator: propagator,
            featureCost: cost);
    }

    /// <summary>Builds the JSON body for a successful Gemini tool-use response.</summary>
    private static object SuccessResponse(
        string promptText = "מצא את שורשי המשוואה $x^2 - 4 = 0$",
        string? latex = "x^2 - 4 = 0",
        IEnumerable<(double x, double y, double w, double h, string kind, string? alt)>? figures = null,
        double confidence = 0.91,
        long inputTokens = 250,
        long outputTokens = 80)
    {
        var figureList = (figures ?? Enumerable.Empty<(double x, double y, double w, double h, string kind, string? alt)>())
            .Select(f => new
            {
                x = f.x, y = f.y, width = f.w, height = f.h,
                kind = f.kind, altText = f.alt,
            })
            .Cast<object>()
            .ToArray();

        var args = new
        {
            promptText,
            latex = latex ?? string.Empty,
            figures = figureList,
            confidence,
        };

        return new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                functionCall = new
                                {
                                    name = "extract_bagrut_page",
                                    args,
                                },
                            },
                        },
                    },
                    finishReason = "STOP",
                },
            },
            usageMetadata = new
            {
                promptTokenCount = inputTokens,
                candidatesTokenCount = outputTokens,
            },
        };
    }

    private static HttpResponseMessage Ok(object body)
    {
        var msg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(body),
        };
        return msg;
    }

    // --------------------------------------------------------------------
    // Tests
    // --------------------------------------------------------------------

    [Fact]
    public async Task FlagOff_NeverCallsGemini_ReturnsNull()
    {
        var calls = 0;
        var extractor = BuildExtractor(
            (_, _) => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); },
            BuildConfig(flagEnabled: false));

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NoApiKey_ReturnsNull_WithoutHttpCall()
    {
        var calls = 0;
        var extractor = BuildExtractor(
            (_, _) => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); },
            BuildConfig(flagEnabled: true, apiKey: null),
            apiKeyOverride: null);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NoModelResolver_ReturnsNull_WithoutHttpCall()
    {
        var calls = 0;
        var extractor = new GeminiVisionPageExtractor(
            http: new HttpClient(new CapturingHandler((_, _) =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            })),
            opts: Options.Create(new GeminiVisionPageExtractorOptions { ApiKey = "k" }),
            configuration: BuildConfig(flagEnabled: true),
            logger: NullLogger<GeminiVisionPageExtractor>.Instance,
            modelResolver: null);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ResolverReturnsNonGemini_ReturnsNull_WithoutHttpCall_WithWarn()
    {
        var calls = 0;
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); },
            BuildConfig(flagEnabled: true),
            modelResolver: new FixedModelResolver("claude-sonnet-4-6"),
            logger: capturingLogger);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Equal(0, calls);
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("non-Gemini") && line.Contains(TestPdfId));
    }

    [Fact]
    public async Task HappyPath_ReturnsExtraction_EmitsCostMetric()
    {
        HttpRequestMessage? captured = null;
        var cost = new FakeCostMetric();
        var extractor = BuildExtractor(
            (req, _) =>
            {
                captured = req;
                return Ok(SuccessResponse(
                    promptText: "Solve $x^2 + 3x = 0$",
                    latex: "x^2 + 3x = 0",
                    figures: new[] { (10.0, 20.0, 100.0, 80.0, "diagram", "axis labels") },
                    confidence: 0.9,
                    inputTokens: 1234, outputTokens: 56));
            },
            BuildConfig(flagEnabled: true),
            cost: cost);

        var result = await extractor.ExtractAsync(FakePngBytes, 3, TestPdfId);

        Assert.NotNull(result);
        Assert.Equal("Solve $x^2 + 3x = 0$", result!.PromptText);
        Assert.Equal("x^2 + 3x = 0", result.Latex);
        Assert.Single(result.Figures);
        Assert.Equal(10.0, result.Figures[0].X);
        Assert.Equal("diagram", result.Figures[0].Kind);
        Assert.Equal("axis labels", result.Figures[0].AltText);
        Assert.Equal(0.9, result.Confidence, 3);

        // Sanity: request URL contains model id + key
        Assert.NotNull(captured);
        var uri = captured!.RequestUri!.ToString();
        Assert.Contains("models/gemini-2.0-flash:generateContent", uri);
        Assert.Contains("key=test-api-key", uri);

        // Cost metric emitted with correct labels.
        Assert.Single(cost.Records);
        var rec = cost.Records[0];
        Assert.Equal("content-extraction", rec.Feature);
        Assert.Equal("tier3", rec.Tier);
        Assert.Equal("bagrut_page_extraction", rec.Task);
        Assert.Equal(TestModelId, rec.ModelId);
        Assert.Equal(1234L, rec.InputTokens);
        Assert.Equal(56L, rec.OutputTokens);
    }

    [Fact]
    public async Task ToolInputNull_NoFunctionCall_ReturnsNull_WithWarn()
    {
        // Gemini returned a candidate with text-only parts (no functionCall).
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => Ok(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new { parts = new[] { new { text = "I cannot extract this." } } },
                        finishReason = "STOP",
                    },
                },
            }),
            BuildConfig(flagEnabled: true),
            logger: capturingLogger);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("no tool call") && line.Contains(TestPdfId));
    }

    [Fact]
    public async Task ToolInputMissingPromptText_ReturnsNull_WithWarn()
    {
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => Ok(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    functionCall = new
                                    {
                                        name = "extract_bagrut_page",
                                        args = new { latex = "x", figures = Array.Empty<object>() },
                                    },
                                },
                            },
                        },
                        finishReason = "STOP",
                    },
                },
                usageMetadata = new { promptTokenCount = 50L, candidatesTokenCount = 10L },
            }),
            BuildConfig(flagEnabled: true),
            logger: capturingLogger);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("missing/invalid promptText"));
    }

    [Fact]
    public async Task ToolInputEmptyPromptText_ReturnsNull_WithWarn()
    {
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => Ok(SuccessResponse(promptText: "   ")),
            BuildConfig(flagEnabled: true),
            logger: capturingLogger);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("whitespace-only"));
    }

    [Fact]
    public async Task ToolInputOutOfBoundsBbox_PassesThrough_CropperWillClampLater()
    {
        // The extractor does NOT pre-filter by bbox sanity — that's
        // FigureCropper's job. This test pins the contract: a wildly
        // out-of-bounds bbox is passed through verbatim, the cropper
        // clamps. We assert the model's raw values reach the result.
        var extractor = BuildExtractor(
            (_, _) => Ok(SuccessResponse(figures: new[]
            {
                (-50.0, -50.0, 99999.0, 99999.0, "diagram", "off-page"),
            })),
            BuildConfig(flagEnabled: true));

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.NotNull(result);
        Assert.Single(result!.Figures);
        Assert.Equal(-50.0, result.Figures[0].X);
        Assert.Equal(99999.0, result.Figures[0].Width);
    }

    [Fact]
    public async Task EmptyExtraction_NonEmptyPage_ReturnsNullVia_EmptyPromptText()
    {
        // The "empty result on a non-empty PDF" failure mode in the brief —
        // model returned valid tool input but the promptText is empty. We
        // surface as null so the caller falls back to the legacy cascade.
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => Ok(SuccessResponse(promptText: "")),
            BuildConfig(flagEnabled: true),
            logger: capturingLogger);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("missing/invalid promptText")
                 || line.Contains("whitespace-only"));
    }

    [Fact]
    public async Task HttpNonSuccess_ReturnsNull_WithWarn()
    {
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream error", Encoding.UTF8),
            },
            BuildConfig(flagEnabled: true),
            logger: capturingLogger);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("HTTP 500") || line.Contains("Gemini call failed"));
    }

    [Fact]
    public async Task HttpThrowsTransport_ReturnsNull_WithWarn()
    {
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => throw new HttpRequestException("connect failed"),
            BuildConfig(flagEnabled: true),
            logger: capturingLogger);

        var result = await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.Null(result);
        Assert.Contains(capturingLogger.WarnLines,
            line => line.Contains("Gemini call failed"));
    }

    [Fact]
    public async Task TraceIdStamped_OnSuccessLog()
    {
        var capturingLogger = new CapturingLogger();
        var extractor = BuildExtractor(
            (_, _) => Ok(SuccessResponse(inputTokens: 100, outputTokens: 20)),
            BuildConfig(flagEnabled: true),
            propagator: new FakeActivityPropagator("trace-vision-12345"),
            logger: capturingLogger);

        await extractor.ExtractAsync(FakePngBytes, 2, TestPdfId);

        Assert.Contains(capturingLogger.InfoLines,
            line => line.Contains("GeminiVisionPageExtractor OK")
                 && line.Contains("trace-vision-12345")
                 && line.Contains(TestPdfId)
                 && line.Contains("page=2")
                 && line.Contains("input_tokens=100")
                 && line.Contains("output_tokens=20"));
    }

    [Fact]
    public async Task EmptyImageBytes_ReturnsNull_NoCall()
    {
        var calls = 0;
        var extractor = BuildExtractor(
            (_, _) => { calls++; return new HttpResponseMessage(HttpStatusCode.OK); },
            BuildConfig(flagEnabled: true));

        var result = await extractor.ExtractAsync(ReadOnlyMemory<byte>.Empty, 1, TestPdfId);

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RequestBodyContainsBase64Image_AndToolDeclaration()
    {
        HttpRequestMessage? captured = null;
        var extractor = BuildExtractor(
            (req, _) =>
            {
                captured = req;
                return Ok(SuccessResponse());
            },
            BuildConfig(flagEnabled: true));

        await extractor.ExtractAsync(FakePngBytes, 1, TestPdfId);

        Assert.NotNull(captured);
        var body = await captured!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // Image inline_data with mimeType=image/png and base64 of FakePngBytes.
        var contents = doc.RootElement.GetProperty("contents");
        Assert.Equal(1, contents.GetArrayLength());
        var parts = contents[0].GetProperty("parts");
        Assert.True(parts.GetArrayLength() >= 1);
        var hasInline = false;
        for (var i = 0; i < parts.GetArrayLength(); i++)
        {
            if (parts[i].TryGetProperty("inlineData", out var inline))
            {
                Assert.Equal("image/png", inline.GetProperty("mimeType").GetString());
                var b64 = inline.GetProperty("data").GetString();
                Assert.Equal(Convert.ToBase64String(FakePngBytes), b64);
                hasInline = true;
            }
        }
        Assert.True(hasInline, "request body must include inline_data PNG");

        // Tool declaration with the closed-set name.
        Assert.Contains("extract_bagrut_page", body);
        // System instruction present.
        Assert.True(doc.RootElement.TryGetProperty("system_instruction", out _));
    }

    // --------------------------------------------------------------------
    // Fakes
    // --------------------------------------------------------------------

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _fn;
        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> fn)
            => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_fn(request, ct));
    }

    private sealed class FixedModelResolver : IModelResolver
    {
        private readonly string _modelId;
        public FixedModelResolver(string modelId) => _modelId = modelId;
        public Task<string> ResolveModelForTaskAsync(string taskName, CancellationToken ct = default)
            => Task.FromResult(_modelId);
        public Task<IReadOnlyList<TaskModelResolution>> SnapshotAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TaskModelResolution>>(Array.Empty<TaskModelResolution>());
        public void Invalidate() { }
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

    private sealed class CapturingLogger : ILogger<GeminiVisionPageExtractor>
    {
        public List<string> WarnLines { get; } = new();
        public List<string> InfoLines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var rendered = RenderTemplate(state);
            switch (logLevel)
            {
                case LogLevel.Warning:
                case LogLevel.Error:
                    WarnLines.Add(rendered);
                    break;
                case LogLevel.Information:
                    InfoLines.Add(rendered);
                    break;
            }
        }

        private static string RenderTemplate<TState>(TState state)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                var template = kvps.LastOrDefault(k => k.Key == "{OriginalFormat}").Value as string;
                if (template is null) return state.ToString() ?? "";
                var rendered = template;
                foreach (var kv in kvps)
                {
                    if (kv.Key == "{OriginalFormat}") continue;
                    rendered = rendered
                        .Replace("{" + kv.Key + "}", kv.Value?.ToString() ?? "")
                        .Replace("{" + kv.Key + ":F2}", kv.Value?.ToString() ?? "")
                        .Replace("{" + kv.Key + ":F0}", kv.Value?.ToString() ?? "")
                        .Replace("{" + kv.Key + ":F6}", kv.Value?.ToString() ?? "");
                    rendered += $" [{kv.Key}={kv.Value}]";
                }
                return rendered;
            }
            return state?.ToString() ?? "";
        }
    }
}
