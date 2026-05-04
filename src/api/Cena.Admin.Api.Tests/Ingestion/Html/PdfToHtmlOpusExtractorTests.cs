// =============================================================================
// Cena Platform — PdfToHtmlOpusExtractor tests (2026-05-04, t_1c57e7389cb4)
//
// Pins the request shape the user-validated Opus 4.7 recipe demands:
//   - model resolves to claude-opus-4-7 by default
//   - MaxTokens=32_000 (verbatim from the recipe)
//   - SystemPrompt contains the load-bearing rubric tokens (rtl, sup, sub,
//     fraction divs) AND the figure-anchor EXAMPLE blocks added by the
//     2026-05-04 coordinator upgrade
//   - PDF flows through to the invoker as-is (the invoker base64-encodes;
//     here we only check the byte stream landed)
//   - the default per-call instruction is supplied when the request leaves
//     it null
// Plus the failure-path contract:
//   - markdown-fence-wrapped responses are stripped
//   - missing API key returns Success=false without invoking the LLM
//   - circuit-open returns Success=false without invoking the LLM
//   - Opus 4.7 model id triggers the temperature-null branch (verified via
//     the model-id-string check the production class makes)
//
// We do NOT exercise the real Anthropic SDK here. The IAnthropicPdfHtmlInvoker
// IS the SDK seam — the production DefaultAnthropicPdfHtmlInvoker covers the
// streaming + StopReason translation, and an integration test elsewhere
// (deferred) would cover wire-format. This unit-level surface is for
// "did the extractor build the right invoker call".
// =============================================================================

using Cena.Admin.Api.AiSettings;
using Cena.Admin.Api.Ingestion.Html;
using Cena.Infrastructure.Llm;
using Cena.Infrastructure.Security;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion.Html;

public sealed class PdfToHtmlOpusExtractorTests
{
    private const string DefaultModelId = "claude-opus-4-7";

    // Minimal "PDF-like" placeholder bytes — the extractor doesn't decode
    // them, only forwards to the invoker. "%PDF-1.4\n" is enough so a real
    // PDF parser would see a valid magic number; the invoker fake just
    // round-trips them.
    private static readonly byte[] FakePdfBytes =
    {
        0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A,
        0x42, 0x4F, 0x47, 0x55, 0x53,
    };

    // ── Test doubles ────────────────────────────────────────────────────

    private sealed class StubModelResolver : IModelResolver
    {
        public string ModelId { get; init; } = DefaultModelId;
        public Task<string> ResolveModelForTaskAsync(string taskName, CancellationToken ct = default)
            => Task.FromResult(ModelId);
        public void Invalidate() { }
        public Task<IReadOnlyList<TaskModelResolution>> SnapshotAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TaskModelResolution>>(Array.Empty<TaskModelResolution>());
    }

    private sealed class StubApiKeyCipher : IApiKeyCipher
    {
        public string EncryptToWire(string plaintext) => plaintext;
        public bool TryDecryptFromWire(string wire, out string plaintext)
        { plaintext = wire ?? ""; return true; }
    }

    /// <summary>
    /// In-memory invoker that captures every call's argument shape so the
    /// tests can assert what landed at the SDK boundary. ResponseText is
    /// the canned text the extractor receives — set per-test to exercise
    /// the fence-stripping and empty-text paths.
    /// </summary>
    private sealed class CapturingPdfHtmlInvoker : IAnthropicPdfHtmlInvoker
    {
        public sealed record Call(
            string ApiKey,
            string ModelId,
            string SystemPrompt,
            byte[] PdfBytes,
            string Instruction,
            int MaxTokens);

        public List<Call> Calls { get; } = new();
        public string? ResponseText { get; set; } = "<html><body>OK</body></html>";
        public Exception? ThrowOnInvoke { get; set; }

        public Task<(string? Text, long InputTokens, long OutputTokens)> InvokeAsync(
            string apiKey, string modelId, string systemPrompt,
            ReadOnlyMemory<byte> pdfBytes, string instruction,
            int maxTokens, CancellationToken ct)
        {
            Calls.Add(new Call(
                apiKey, modelId, systemPrompt,
                pdfBytes.ToArray(), instruction, maxTokens));
            if (ThrowOnInvoke is not null) throw ThrowOnInvoke;
            return Task.FromResult<(string?, long, long)>((ResponseText, 1234, 567));
        }
    }

    /// <summary>
    /// Test scaffold for IAnthropicLlmRuntime. The unit tests do not need
    /// the real client cache or breaker; we only need RequestCircuitPermission
    /// (throw or no-op), RecordSuccess/Failure (no-op), and EmitMetrics
    /// (no-op). Substitute pattern would force us to set up four method
    /// returns per test; a hand-rolled stub keeps the call sites readable.
    /// </summary>
    private sealed class StubAnthropicLlmRuntime : IAnthropicLlmRuntime
    {
        public bool CircuitOpen { get; set; }
        public int SuccessCount { get; private set; }
        public int FailureCount { get; private set; }
        public List<(string Model, string Task, long Duration, long In, long Out)> Emissions { get; } = new();

        public Anthropic.AnthropicClient GetOrCreateClient(string apiKey)
            => throw new NotSupportedException("StubAnthropicLlmRuntime never builds a real client.");

        public void RequestCircuitPermission(string modelName)
        {
            if (CircuitOpen)
                throw new CircuitOpenException(
                    $"Circuit breaker is open for {modelName}. Cooling for 30s.");
        }

        public void RecordSuccess(string modelName) => SuccessCount++;
        public void RecordFailure(string modelName) => FailureCount++;

        public void EmitMetrics(string model, string taskType, long durationMs,
            long inputTokens, long outputTokens, LlmCallPricing pricing)
            => Emissions.Add((model, taskType, durationMs, inputTokens, outputTokens));

        public System.Text.Json.JsonSerializerOptions JsonOpts { get; } =
            new System.Text.Json.JsonSerializerOptions();
    }

    // ── Builder ─────────────────────────────────────────────────────────

    private static (PdfToHtmlOpusExtractor extractor,
                    CapturingPdfHtmlInvoker invoker,
                    StubAnthropicLlmRuntime runtime,
                    StubModelResolver modelResolver)
        BuildExtractor(string? apiKey = "sk-ant-test", bool circuitOpen = false,
                       string modelId = DefaultModelId)
    {
        var docStore = Substitute.For<IDocumentStore>();
        var query = Substitute.For<IQuerySession>();
        docStore.QuerySession().Returns(query);

        var configBuilder = new ConfigurationBuilder();
        if (apiKey is not null)
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = apiKey,
            });
        }
        var configuration = configBuilder.Build();

        var runtime = new StubAnthropicLlmRuntime { CircuitOpen = circuitOpen };
        var invoker = new CapturingPdfHtmlInvoker();
        var modelResolver = new StubModelResolver { ModelId = modelId };

        var extractor = new PdfToHtmlOpusExtractor(
            logger: NullLogger<PdfToHtmlOpusExtractor>.Instance,
            configuration: configuration,
            documentStore: docStore,
            cipher: new StubApiKeyCipher(),
            runtime: runtime,
            invoker: invoker,
            modelResolver: modelResolver);

        return (extractor, invoker, runtime, modelResolver);
    }

    // ── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertAsync_BuildsCorrectAnthropicCall()
    {
        var (extractor, invoker, _, _) = BuildExtractor();

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-abcdef"));

        Assert.True(resp.Success, $"expected success but got: {resp.Error}");
        Assert.Single(invoker.Calls);

        var call = invoker.Calls[0];
        // Model id flows from the resolver default — claude-opus-4-7.
        Assert.Equal(DefaultModelId, call.ModelId);
        // MaxTokens matches the user's verbatim recipe.
        Assert.Equal(32_000, call.MaxTokens);
        // The system prompt carries the load-bearing rubric tokens — these
        // are the strings the model relies on to produce the gold-standard
        // output. Asserting on substrings rather than exact equality so
        // future small tweaks (whitespace, comment lines) don't break the
        // test, but the load-bearing semantics stay pinned.
        Assert.Contains("rtl", call.SystemPrompt);
        Assert.Contains("sup, sub, fraction divs", call.SystemPrompt);
        Assert.Contains("inline <svg>", call.SystemPrompt);
        // Figure-anchor EXAMPLE blocks added by the 2026-05-04 upgrade:
        // the model uses these as visual anchors for Q4 (inscribed
        // triangle) and Q5 (kite with parallel) shaped figures.
        Assert.Contains("EXAMPLE — inscribed triangle", call.SystemPrompt);
        Assert.Contains("EXAMPLE — kite ABCD", call.SystemPrompt);
        // The two reference SVGs landed in the prompt — assert on the
        // distinctive coordinates the user-validated recipe pinned. These
        // are load-bearing for figure quality on structurally similar
        // questions and a paraphrase that drops them silently regresses
        // the gold-standard output.
        Assert.Contains("cx=\"140\" cy=\"148\" r=\"93\"", call.SystemPrompt);
        Assert.Contains("polygon points=\"100,25 35,105 100,220 165,105\"", call.SystemPrompt);
        Assert.Contains("stroke-dasharray=\"3,3\"", call.SystemPrompt);
        // The PDF bytes were forwarded as-is.
        Assert.Equal(FakePdfBytes, call.PdfBytes);
        // Default instruction (the user's request omitted the override).
        Assert.Equal(PdfToHtmlRequest.DefaultInstruction, call.Instruction);
    }

    [Fact]
    public async Task ConvertAsync_StripsHtmlFences()
    {
        var (extractor, invoker, _, _) = BuildExtractor();
        // Model emitted ```html ... ``` despite the system-prompt rule. The
        // extractor's StripHtmlFences method handles the wrap-style fences
        // so the persisted HTML is clean.
        invoker.ResponseText = "```html\n<html><body>HELLO</body></html>\n```";

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-fences"));

        Assert.True(resp.Success);
        Assert.Equal("<html><body>HELLO</body></html>", resp.Html);
        // Token counts came through from the canned invoker response.
        Assert.Equal(1234, resp.InputTokens);
        Assert.Equal(567, resp.OutputTokens);
    }

    [Fact]
    public async Task ConvertAsync_NoApiKey_ReturnsFailureResponse()
    {
        var (extractor, invoker, _, _) = BuildExtractor(apiKey: null);

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-nokey"));

        Assert.False(resp.Success);
        Assert.NotNull(resp.Error);
        Assert.Contains("API key", resp.Error!);
        // The invoker was never called — the extractor short-circuits at
        // the API-key gate, so no token cost is incurred.
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public async Task ConvertAsync_CircuitOpen_ReturnsFailureWithoutRetry()
    {
        var (extractor, invoker, runtime, _) = BuildExtractor(circuitOpen: true);

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-breaker"));

        Assert.False(resp.Success);
        Assert.Equal(DefaultModelId, resp.ModelUsed);
        Assert.NotNull(resp.Error);
        // The breaker tripped before any outbound traffic — no invoker call.
        Assert.Empty(invoker.Calls);
        // The circuit-open path does NOT record an additional failure on
        // the breaker (it was already open); RecordFailure stays at zero.
        Assert.Equal(0, runtime.FailureCount);
    }

    [Fact]
    public async Task ConvertAsync_DropsTemperatureOnOpus47()
    {
        // The extractor itself doesn't construct MessageCreateParams — that
        // lives in DefaultAnthropicPdfHtmlInvoker. What the extractor MUST
        // do is route the call through the invoker with the Opus 4.7 model
        // id so the invoker's `Temperature = isOpus47 ? null : 0.0` branch
        // fires the no-temperature path. We pin the model-id contract here;
        // the invoker's wire-shape discipline is its own seam.
        var (extractor, invoker, _, _) = BuildExtractor(modelId: "claude-opus-4-7");

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-opus"));

        Assert.True(resp.Success);
        Assert.Equal("claude-opus-4-7", invoker.Calls[0].ModelId);
        // The model id starts with the Opus 4.7 prefix the invoker uses to
        // detect "drop temperature"; this is the load-bearing string check.
        Assert.StartsWith("claude-opus-4-7", invoker.Calls[0].ModelId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsync_StreamingTruncated_ReturnsFailureFromInvokerException()
    {
        // The DefaultAnthropicPdfHtmlInvoker translates a streaming
        // StopReason=MaxTokens into an InvalidOperationException with the
        // message "Output truncated at {n} tokens." (production behavior
        // pinned by the 2026-05-04 coordinator upgrade). The extractor must
        // catch that exception in its outer try/catch and surface it as
        // Success=false with the message in Error so the /render-html
        // endpoint maps cleanly to a 400.
        var (extractor, invoker, runtime, _) = BuildExtractor();
        invoker.ThrowOnInvoke = new InvalidOperationException(
            "Output truncated at 32000 tokens. Re-render with a higher max_tokens, or split the source PDF.");

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-trunc"));

        Assert.False(resp.Success);
        Assert.NotNull(resp.Error);
        Assert.Contains("truncated at 32000", resp.Error!);
        // Truncation is a real failure — the breaker records it so a
        // sustained truncation pattern (e.g. a 200-page PDF) doesn't burn
        // budget on retries. Same shape as any other Anthropic exception
        // hitting the catch-all.
        Assert.Equal(1, runtime.FailureCount);
    }

    [Fact]
    public async Task ConvertAsync_EmptyResponseFromAnthropic_ReturnsFailureWithoutBreakerTrip()
    {
        var (extractor, invoker, runtime, _) = BuildExtractor();
        // Anthropic-side anomaly: the call succeeded (200 OK, no exception)
        // but produced no text block. Mirrors the OcrTextEnhancer empty-
        // response branch — the call is real, so the breaker records
        // success, but the response is a Failure to the caller.
        invoker.ResponseText = null;

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-empty"));

        Assert.False(resp.Success);
        Assert.Contains("empty response", resp.Error!, StringComparison.OrdinalIgnoreCase);
        // Empty is not a breaker failure — the round-trip itself succeeded.
        Assert.Equal(0, runtime.FailureCount);
        // Token counters still flow (cost is real). EmitMetrics ran.
        Assert.Single(runtime.Emissions);
    }

    [Fact]
    public async Task ConvertAsync_EmitsCostMetricOnSuccess()
    {
        // Pins the per-feature cost-counter contract: every successful
        // round-trip records on ILlmCostMetric so the finops dashboard
        // shows pdf-to-html spend separately from ocr-text-enhance.
        var (extractor, _, runtime, _) = BuildExtractor();

        var resp = await extractor.ConvertAsync(
            new PdfToHtmlRequest(FakePdfBytes, PdfId: "pdf-cost"));

        Assert.True(resp.Success);
        Assert.Single(runtime.Emissions);
        var emission = runtime.Emissions[0];
        Assert.Equal(DefaultModelId, emission.Model);
        Assert.Equal(PdfToHtmlOpusExtractor.TaskName, emission.Task);
        Assert.Equal(1234, emission.In);
        Assert.Equal(567, emission.Out);
    }

    // ── StripHtmlFences direct unit tests ───────────────────────────────

    [Fact]
    public void StripHtmlFences_NoFence_ReturnsInputUnchanged()
    {
        const string input = "<html><body>plain</body></html>";
        Assert.Equal(input, PdfToHtmlOpusExtractor.StripHtmlFences(input));
    }

    [Fact]
    public void StripHtmlFences_WrapFences_AreRemoved()
    {
        const string input = "```html\n<html>X</html>\n```";
        Assert.Equal("<html>X</html>", PdfToHtmlOpusExtractor.StripHtmlFences(input));
    }
}
