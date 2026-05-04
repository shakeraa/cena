// Cena Platform — OcrTextEnhancer vision-aware path tests
// (ADR-0062 Phase 1.5 vision-aware enhance — t_3401e9e79877)
//
// Pin tests for the new content-block-with-image enhance path. The
// production wiring threads the invoker through DI; here we substitute a
// hand-rolled CapturingEnhanceInvoker that records every call's arguments
// so we can assert:
//
//   1. WithSourcePagePng_AttachesImageBlock — when the request carries
//      SourcePdfId + SourcePage AND the on-disk PNG cache has the page,
//      the invoker is called with sourcePagePng != null carrying the
//      cached bytes verbatim.
//
//   2. WithoutSourcePagePng_FallsBackToTextOnly — when SourcePdfId/Page
//      are absent (older drafts), the invoker is called with
//      sourcePagePng == null. Backwards-compat guarantee.
//
//   3. PngCacheMiss_OndemandRasterize — when the cache is empty but a
//      rasterizer + pdfStore are wired, the enhancer asks the rasterizer
//      to render the PDF on-demand, then re-reads the page bytes. Pins
//      the cache-first / rasterize-fallback ordering.
//
// We do NOT bring in the real Anthropic SDK. The invoker IS the SDK
// boundary — testing the request-shape decision is enough; the SDK
// behaviour is Anthropic's responsibility, not ours.

using System.Diagnostics.Metrics;
using Cena.Admin.Api.AiSettings;
using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Ingestion.Vision;
using Cena.Infrastructure.Llm;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class OcrTextEnhancerVisionTests : IDisposable
{
    private const string TestModelName = "claude-sonnet-4-6";

    // PNG file-magic header — "real-enough" bytes so the file lands on
    // disk and is read back. The invoker doesn't decode the PNG; only
    // the byte-equality assertion matters.
    private static readonly byte[] FakePngBytes =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x42, 0x4F, 0x47, 0x55, 0x53, 0x21, 0x21, 0x21,
    };

    private readonly string _testRoot;

    public OcrTextEnhancerVisionTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"cena-vision-enhance-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true); }
        catch { /* best effort */ }
    }

    // ── Test doubles ────────────────────────────────────────────────────

    private sealed class StubMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private sealed class StubLlmCostMetric : ILlmCostMetric
    {
        public int Calls;
        public void Record(string feature, string tier, string task, string modelId,
            long inputTokens, long outputTokens, string? instituteId = null,
            string? examTargetCode = null) => Calls++;
    }

    private sealed class StubApiKeyCipher : IApiKeyCipher
    {
        public string EncryptToWire(string plaintext) => plaintext;
        public bool TryDecryptFromWire(string wire, out string plaintext)
        { plaintext = wire ?? ""; return true; }
    }

    private sealed class StubModelResolver : IModelResolver
    {
        public Task<string> ResolveModelForTaskAsync(string taskName, CancellationToken ct = default)
            => Task.FromResult(TestModelName);
        public void Invalidate() { }
        public Task<IReadOnlyList<TaskModelResolution>> SnapshotAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TaskModelResolution>>(Array.Empty<TaskModelResolution>());
    }

    /// <summary>
    /// Invoker that records every call, lets the test inspect the
    /// sourcePagePng argument, and returns a canned successful response
    /// so the enhancer's success branch runs end-to-end.
    /// </summary>
    private sealed class CapturingEnhanceInvoker : IAnthropicEnhanceInvoker
    {
        public sealed record Call(
            string ApiKey, string ModelId, string SystemPrompt, string OcrText,
            byte[]? PagePng, int MaxTokens);

        public List<Call> Calls { get; } = new();

        public string ResponseText { get; set; } = "ENHANCED OUTPUT";

        public Task<(string? Text, long InputTokens, long OutputTokens)> InvokeAsync(
            string apiKey, string modelId, string systemPrompt, string ocrText,
            ReadOnlyMemory<byte>? sourcePagePng, int maxTokens, CancellationToken ct)
        {
            Calls.Add(new Call(
                ApiKey: apiKey,
                ModelId: modelId,
                SystemPrompt: systemPrompt,
                OcrText: ocrText,
                PagePng: sourcePagePng?.ToArray(),
                MaxTokens: maxTokens));
            return Task.FromResult<(string?, long, long)>((ResponseText, 100, 50));
        }
    }

    /// <summary>
    /// In-memory enhancement cache (no Marten). Keyed by string, immediate
    /// store/retrieve, no TTL — the vision tests don't exercise expiry.
    /// </summary>
    private sealed class InMemoryEnhancementCache : IOcrEnhancementCache
    {
        private readonly Dictionary<string, EnhancedOcrCacheEntry> _rows = new(StringComparer.Ordinal);
        private static readonly OcrEnhancementCache _real
            = new(Substitute.For<IDocumentStore>());

        public string ComputeKey(string input) => _real.ComputeKey(input);

        public Task<EnhancedOcrCacheEntry?> TryGetAsync(string inputKey, CancellationToken ct = default)
            => Task.FromResult(_rows.GetValueOrDefault(inputKey));

        public Task StoreAsync(string inputKey, string enhancedText, string modelUsed, CancellationToken ct = default)
        {
            _rows[inputKey] = new EnhancedOcrCacheEntry(
                enhancedText, modelUsed,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Rasterizer fake. Records every RasterizeAsync call; on each call,
    /// writes the configured PNG bytes to the canonical D3 path so the
    /// enhancer's post-rasterize re-read finds the cache hit.
    /// </summary>
    private sealed class FakeRasterizer : IPdfPageRasterizer
    {
        public int CallCount;
        public string? LastPdfId;
        public byte[]? LastPdfBytes;

        private readonly string _pageStorageRoot;
        private readonly byte[] _pngToWrite;
        private readonly int _pageCount;

        public FakeRasterizer(string pageStorageRoot, byte[] pngToWrite, int pageCount = 1)
        {
            _pageStorageRoot = pageStorageRoot;
            _pngToWrite = pngToWrite;
            _pageCount = pageCount;
        }

        public Task<IReadOnlyList<string>> RasterizeAsync(
            byte[] pdfBytes, string pdfId, CancellationToken ct = default)
        {
            CallCount++;
            LastPdfId = pdfId;
            LastPdfBytes = pdfBytes;

            var safe = SafeIdSegment(pdfId);
            var dir = Path.Combine(_pageStorageRoot, safe);
            Directory.CreateDirectory(dir);
            var paths = new List<string>(_pageCount);
            for (int i = 1; i <= _pageCount; i++)
            {
                var p = Path.Combine(dir, $"page-{i:D3}.png");
                File.WriteAllBytes(p, _pngToWrite);
                paths.Add(p);
            }
            return Task.FromResult<IReadOnlyList<string>>(paths);
        }

        private static string SafeIdSegment(string id)
        {
            var span = id.AsSpan();
            var buf = new char[span.Length];
            int j = 0;
            foreach (var ch in span)
            {
                var ok = (ch >= '0' && ch <= '9')
                      || (ch >= 'a' && ch <= 'z')
                      || (ch >= 'A' && ch <= 'Z')
                      || ch == '-' || ch == '_';
                if (ok) buf[j++] = ch;
            }
            return j == 0 ? "_" : new string(buf, 0, j).ToLowerInvariant();
        }
    }

    private sealed class FakePdfStore : IBagrutPdfStore
    {
        public byte[]? Bytes;
        public Task PersistAsync(string pdfId, byte[] pdfBytes, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<bool> ExistsAsync(string pdfId, CancellationToken ct = default)
            => Task.FromResult(Bytes is not null);
        public Task<Stream?> OpenReadAsync(string pdfId, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Bytes is null ? null : new MemoryStream(Bytes));
    }

    // ── Builder ─────────────────────────────────────────────────────────

    private (OcrTextEnhancer enhancer,
             CapturingEnhanceInvoker invoker,
             FakeRasterizer rasterizer,
             FakePdfStore pdfStore,
             string pageStorageRoot)
        BuildEnhancer(int rasterizerPageCount = 1)
    {
        // Marten store — cipher fall-through means LoadAsync<AiSettings>
        // returns null (NSubstitute default), then ResolveApiKey reads
        // Anthropic:ApiKey from configuration.
        var docStore = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        var query = Substitute.For<IQuerySession>();
        docStore.LightweightSession().Returns(session);
        docStore.QuerySession().Returns(query);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "sk-ant-test",
            })
            .Build();

        var meterFactory = new StubMeterFactory();
        var runtime = new AnthropicLlmRuntime(
            NullLogger<AnthropicLlmRuntime>.Instance, meterFactory);

        var pageStorageRoot = Path.Combine(_testRoot, $"pages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pageStorageRoot);

        var rasterizer = new FakeRasterizer(pageStorageRoot, FakePngBytes, rasterizerPageCount);
        var pdfStore = new FakePdfStore();
        var invoker = new CapturingEnhanceInvoker();

        var enhancer = new OcrTextEnhancer(
            logger: NullLogger<OcrTextEnhancer>.Instance,
            configuration: configuration,
            meterFactory: meterFactory,
            documentStore: docStore,
            cipher: new StubApiKeyCipher(),
            featureCost: new StubLlmCostMetric(),
            cache: new InMemoryEnhancementCache(),
            runtime: runtime,
            invoker: invoker,
            modelResolver: new StubModelResolver(),
            rasterizer: rasterizer,
            pdfStore: pdfStore,
            pageStorageOptions: Options.Create(new SourcePageStorageOptions
            {
                RootDirectory = pageStorageRoot,
            }));

        return (enhancer, invoker, rasterizer, pdfStore, pageStorageRoot);
    }

    // ── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public async Task WithSourcePagePng_AttachesImageBlock_WhenCacheHasFile()
    {
        var (enhancer, invoker, rasterizer, _, pageStorageRoot) = BuildEnhancer();

        // Pre-seed the on-disk page PNG cache as if the rasterizer had
        // already run. The pdfId here uses lowercase hex, matching the
        // production GeneratePdfId convention.
        const string pdfId = "pdf-aabbccddeeff";
        const int page = 2;
        var pageDir = Path.Combine(pageStorageRoot, pdfId);
        Directory.CreateDirectory(pageDir);
        var pagePath = Path.Combine(pageDir, $"page-{page:D3}.png");
        await File.WriteAllBytesAsync(pagePath, FakePngBytes);

        var resp = await enhancer.EnhanceOcrTextAsync(
            new EnhanceOcrTextRequest(
                OcrText: "1/4 v 2 + 13/6 v + 10/3",
                SourceContext: "math-5u",
                SourcePdfId: pdfId,
                SourcePage: page));

        Assert.True(resp.Success, $"expected success but got: {resp.Error}");
        Assert.Equal("ENHANCED OUTPUT", resp.EnhancedText);
        // The invoker was called once, with sourcePagePng populated from
        // the cache PNG bytes. This is the regression-pin: text-only
        // enhance hallucinated stacked-fraction layout; image-attached
        // gives the model visual ground truth.
        Assert.Single(invoker.Calls);
        var call = invoker.Calls[0];
        Assert.NotNull(call.PagePng);
        Assert.Equal(FakePngBytes, call.PagePng);
        // No on-demand rasterize when the cache was already populated.
        Assert.Equal(0, rasterizer.CallCount);
    }

    [Fact]
    public async Task WithoutSourcePagePng_FallsBackToTextOnly()
    {
        var (enhancer, invoker, rasterizer, pdfStore, _) = BuildEnhancer();

        var resp = await enhancer.EnhanceOcrTextAsync(
            new EnhanceOcrTextRequest(
                OcrText: "older draft, no SourcePdfId",
                SourceContext: "math-5u"));

        Assert.True(resp.Success, $"expected success but got: {resp.Error}");
        // Invoker called with sourcePagePng == null — backwards-compat
        // path. Pre-vision drafts continue to enhance via text-only.
        Assert.Single(invoker.Calls);
        Assert.Null(invoker.Calls[0].PagePng);
        // Rasterizer + store stayed cold.
        Assert.Equal(0, rasterizer.CallCount);
        Assert.Null(pdfStore.Bytes);
    }

    [Fact]
    public async Task PngCacheMiss_OndemandRasterize()
    {
        var (enhancer, invoker, rasterizer, pdfStore, _) = BuildEnhancer(rasterizerPageCount: 3);

        // Cache is empty (no PNG on disk yet). Seed the pdf store so the
        // enhancer can fetch the bytes and ask the rasterizer to render.
        const string pdfId = "pdf-1122334455";
        const int page = 1;
        pdfStore.Bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF" placeholder

        var resp = await enhancer.EnhanceOcrTextAsync(
            new EnhanceOcrTextRequest(
                OcrText: "stacked fraction page",
                SourceContext: "math-5u",
                SourcePdfId: pdfId,
                SourcePage: page));

        Assert.True(resp.Success, $"expected success but got: {resp.Error}");
        // Rasterizer was asked to render the PDF on demand.
        Assert.Equal(1, rasterizer.CallCount);
        Assert.Equal(pdfId, rasterizer.LastPdfId);
        Assert.NotNull(rasterizer.LastPdfBytes);
        Assert.Equal(pdfStore.Bytes, rasterizer.LastPdfBytes);
        // The post-rasterize re-read picks up the page PNG and attaches
        // it to the invoker call.
        Assert.Single(invoker.Calls);
        Assert.NotNull(invoker.Calls[0].PagePng);
        Assert.Equal(FakePngBytes, invoker.Calls[0].PagePng);
    }

    [Fact]
    public async Task RasterizerThrows_FailsOpenToTextOnly()
    {
        // Senior-architect rule: every failure path on the vision attempt
        // must drop to text-only. This pins that contract — a rasterizer
        // I/O error must not turn a successful enhance into a failure.
        var (enhancer, invoker, _, pdfStore, pageStorageRoot)
            = BuildEnhancerWithThrowingRasterizer();
        pdfStore.Bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        var resp = await enhancer.EnhanceOcrTextAsync(
            new EnhanceOcrTextRequest(
                OcrText: "rasterizer-fails-but-enhance-succeeds",
                SourceContext: "math-5u",
                SourcePdfId: "pdf-deadbeef",
                SourcePage: 1));

        Assert.True(resp.Success, $"expected success but got: {resp.Error}");
        Assert.Single(invoker.Calls);
        // sourcePagePng must be null — fail-open to text-only is the
        // contract when the vision attempt fails for any reason.
        Assert.Null(invoker.Calls[0].PagePng);
    }

    [Fact]
    public async Task ImageAttached_CacheKeyDiffersFromTextOnlyKey()
    {
        // Pin: cache rows for "same OCR text, image attached" must NOT
        // collide with "same OCR text, no image" rows. Otherwise a
        // curator who re-uploads a different render of the same exam
        // would get a stale text-only result served from cache.
        var (enhancer, invoker, _, _, pageStorageRoot) = BuildEnhancer();

        const string pdfId = "pdf-cachekey";
        const int page = 1;
        var pageDir = Path.Combine(pageStorageRoot, pdfId);
        Directory.CreateDirectory(pageDir);
        await File.WriteAllBytesAsync(
            Path.Combine(pageDir, $"page-{page:D3}.png"),
            FakePngBytes);

        const string ocrText = "same text both ways";

        var withImage = await enhancer.EnhanceOcrTextAsync(
            new EnhanceOcrTextRequest(ocrText, SourcePdfId: pdfId, SourcePage: page));
        var withoutImage = await enhancer.EnhanceOcrTextAsync(
            new EnhanceOcrTextRequest(ocrText));

        Assert.True(withImage.Success);
        Assert.True(withoutImage.Success);
        Assert.NotNull(withImage.InputHash);
        Assert.NotNull(withoutImage.InputHash);
        // Different key means different cache row → no stale cross-talk.
        Assert.NotEqual(withImage.InputHash, withoutImage.InputHash);
        // Both invoker calls happened — neither hit a cached entry from
        // the other shape.
        Assert.Equal(2, invoker.Calls.Count);
    }

    // ── Helper for the rasterizer-throws scenario ───────────────────────

    private (OcrTextEnhancer enhancer,
             CapturingEnhanceInvoker invoker,
             ThrowingRasterizer rasterizer,
             FakePdfStore pdfStore,
             string pageStorageRoot)
        BuildEnhancerWithThrowingRasterizer()
    {
        var docStore = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        var query = Substitute.For<IQuerySession>();
        docStore.LightweightSession().Returns(session);
        docStore.QuerySession().Returns(query);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "sk-ant-test",
            })
            .Build();

        var meterFactory = new StubMeterFactory();
        var runtime = new AnthropicLlmRuntime(
            NullLogger<AnthropicLlmRuntime>.Instance, meterFactory);

        var pageStorageRoot = Path.Combine(_testRoot, $"pages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pageStorageRoot);

        var rasterizer = new ThrowingRasterizer();
        var pdfStore = new FakePdfStore();
        var invoker = new CapturingEnhanceInvoker();

        var enhancer = new OcrTextEnhancer(
            logger: NullLogger<OcrTextEnhancer>.Instance,
            configuration: configuration,
            meterFactory: meterFactory,
            documentStore: docStore,
            cipher: new StubApiKeyCipher(),
            featureCost: new StubLlmCostMetric(),
            cache: new InMemoryEnhancementCache(),
            runtime: runtime,
            invoker: invoker,
            modelResolver: new StubModelResolver(),
            rasterizer: rasterizer,
            pdfStore: pdfStore,
            pageStorageOptions: Options.Create(new SourcePageStorageOptions
            {
                RootDirectory = pageStorageRoot,
            }));

        return (enhancer, invoker, rasterizer, pdfStore, pageStorageRoot);
    }

    private sealed class ThrowingRasterizer : IPdfPageRasterizer
    {
        public Task<IReadOnlyList<string>> RasterizeAsync(
            byte[] pdfBytes, string pdfId, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated pdftoppm failure");
    }
}
