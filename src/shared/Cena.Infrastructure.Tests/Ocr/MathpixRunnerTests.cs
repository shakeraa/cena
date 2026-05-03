// =============================================================================
// Cena Platform — MathpixRunner tests
//
// Exercises the real HTTP client against a captured HttpMessageHandler —
// not a mock of IMathpixRunner itself (that would violate the no-stubs rule
// by hiding the actual HTTP shape). The DelegatingHandler sits inside the
// HttpClient and lets us assert on outgoing request headers/body AND
// control the response, so the real MathpixRunner code path is executed
// end-to-end short of the network hop.
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Runners;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Tests.Ocr;

public class MathpixRunnerTests
{
    private static readonly byte[] FakeCropBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static OcrMathBlock OriginalBlock(double conf = 0.40)
        => new("3x+5=?", new BoundingBox(10, 10, 100, 30, 1), conf,
            SympyParsed: false, CanonicalForm: null);

    private static MathpixRunner BuildRunner(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder,
        MathpixOptions? overrideOptions = null)
    {
        var options = overrideOptions ?? new MathpixOptions
        {
            BaseUrl = "https://api.mathpix.com/v3/",
            AppId = "test-app-id",
            AppKey = "test-app-key",
            RequestTimeout = TimeSpan.FromSeconds(5),
        };
        var handler = new CapturingHandler(responder);
        var http = new HttpClient(handler);
        return new MathpixRunner(
            http,
            Options.Create(options),
            NullLogger<MathpixRunner>.Instance);
    }

    [Fact]
    public void Constructor_Throws_Without_AppId()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new MathpixRunner(
                new HttpClient(),
                Options.Create(new MathpixOptions { AppId = null, AppKey = "k" }),
                NullLogger<MathpixRunner>.Instance));
    }

    [Fact]
    public void Constructor_Throws_Without_AppKey()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new MathpixRunner(
                new HttpClient(),
                Options.Create(new MathpixOptions { AppId = "a", AppKey = null }),
                NullLogger<MathpixRunner>.Instance));
    }

    [Fact]
    public async Task RescueMathAsync_Empty_Crop_Throws()
    {
        var runner = BuildRunner((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RescueMathAsync(ReadOnlyMemory<byte>.Empty, OriginalBlock(), CancellationToken.None));
    }

    [Fact]
    public async Task RescueMathAsync_Crop_Above_MaxBytes_Throws()
    {
        var runner = BuildRunner(
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK),
            new MathpixOptions { AppId = "a", AppKey = "k", MaxImageBytes = 4 });
        var big = new byte[8];
        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RescueMathAsync(big, OriginalBlock(), CancellationToken.None));
    }

    [Fact]
    public async Task RescueMathAsync_Sends_Correct_Headers_And_DataUri()
    {
        HttpRequestMessage? captured = null;
        var runner = BuildRunner((req, _) =>
        {
            captured = req;
            var body = new { latex_styled = "3x+5=14", confidence = 0.95 };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body),
            };
        });

        await runner.RescueMathAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://api.mathpix.com/v3/text", captured.RequestUri!.ToString());

        Assert.True(captured.Headers.Contains("app_id"), "app_id header missing");
        Assert.True(captured.Headers.Contains("app_key"), "app_key header missing");
        Assert.Equal("test-app-id", captured.Headers.GetValues("app_id").Single());
        Assert.Equal("test-app-key", captured.Headers.GetValues("app_key").Single());

        var body = await captured.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var src = doc.RootElement.GetProperty("src").GetString();
        Assert.StartsWith("data:image/png;base64,", src);
        Assert.Contains("iVBORw0KGgo", src);   // PNG magic base64
        Assert.Equal("latex_styled", doc.RootElement.GetProperty("formats")[0].GetString());
    }

    [Fact]
    public async Task RescueMathAsync_Success_Returns_New_Block_With_Latex_And_Confidence()
    {
        var runner = BuildRunner((_, _) =>
        {
            var body = new { latex_styled = "3x+5=14", confidence = 0.97 };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        });

        var original = OriginalBlock(conf: 0.40);
        var rescued = await runner.RescueMathAsync(FakeCropBytes, original, CancellationToken.None);

        Assert.Equal("3x+5=14", rescued.Latex);
        Assert.Equal(0.97, rescued.Confidence);
        // Bbox preserved from original
        Assert.Equal(original.Bbox, rescued.Bbox);
        // CAS state reset — Layer 5 re-runs validation on the new LaTeX
        Assert.False(rescued.SympyParsed);
        Assert.Null(rescued.CanonicalForm);
    }

    [Fact]
    public async Task RescueMathAsync_Api_Error_Returns_Original_Block()
    {
        var runner = BuildRunner((_, _) =>
        {
            var body = new { error = "image_corrupt" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        });
        var original = OriginalBlock();
        var rescued = await runner.RescueMathAsync(FakeCropBytes, original, CancellationToken.None);
        Assert.Equal(original, rescued);
    }

    [Fact]
    public async Task RescueMathAsync_Non_Success_Status_Returns_Original_Block()
    {
        var runner = BuildRunner((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream failure", Encoding.UTF8),
            });
        var original = OriginalBlock();
        var rescued = await runner.RescueMathAsync(FakeCropBytes, original, CancellationToken.None);
        Assert.Equal(original, rescued);
    }

    [Fact]
    public async Task RescueMathAsync_Transport_Failure_Throws_OcrCircuitOpen()
    {
        var runner = BuildRunner((_, _) => throw new HttpRequestException("connection refused"));
        await Assert.ThrowsAsync<OcrCircuitOpenException>(() =>
            runner.RescueMathAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None));
    }

    [Fact]
    public async Task RescueMathAsync_BrokenCircuitException_Translates_To_OcrCircuitOpen()
    {
        var runner = BuildRunner((_, _) =>
            throw new BrokenCircuitException("Polly circuit open"));
        await Assert.ThrowsAsync<OcrCircuitOpenException>(() =>
            runner.RescueMathAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None));
    }

    [Fact]
    public async Task RescueMathAsync_Caller_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var runner = BuildRunner((_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            runner.RescueMathAsync(FakeCropBytes, OriginalBlock(), cts.Token));
    }

    [Fact]
    public void IsBrokenCircuitException_Matches_By_Type_Name_Only()
    {
        Assert.True(MathpixRunner.IsBrokenCircuitException(
            new BrokenCircuitException("open")));
        Assert.True(MathpixRunner.IsBrokenCircuitException(
            new InvalidOperationException("outer",
                new BrokenCircuitException("inner"))));
        Assert.False(MathpixRunner.IsBrokenCircuitException(
            new InvalidOperationException("no match")));
    }

    // -------------------------------------------------------------------------
    // Test infrastructure — NOT production mocks. The DelegatingHandler lets
    // tests control the HTTP boundary without hitting the network.
    // -------------------------------------------------------------------------
    private sealed class CapturingHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request, ct));
    }

    /// <summary>
    /// Stand-in for Polly's BrokenCircuitException — MathpixRunner matches by
    /// type name ("BrokenCircuitException"), so this local class with the
    /// exact name exercises the real check without pulling in Polly as a
    /// test dep.
    /// </summary>
    private sealed class BrokenCircuitException : Exception
    {
        public BrokenCircuitException(string m) : base(m) { }
    }
}
