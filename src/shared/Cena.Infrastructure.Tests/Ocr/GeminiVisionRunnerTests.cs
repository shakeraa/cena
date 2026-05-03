// =============================================================================
// Cena Platform — GeminiVisionRunner tests
//
// Real HTTP client driven by a DelegatingHandler; no IGeminiVisionRunner
// mock. Covers constructor validation, request shape (URL, body, model,
// base64 image), happy-path response parsing, non-success HTTP fallback,
// transport failure translation, cancellation, and RTL detection.
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Runners;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Tests.Ocr;

public class GeminiVisionRunnerTests
{
    private static readonly byte[] FakeCropBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static OcrTextBlock OriginalBlock(double conf = 0.40)
        => new("garbled", new BoundingBox(5, 5, 120, 22, 1),
            Language.English, conf, IsRtl: false);

    private static GeminiVisionRunner BuildRunner(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder,
        GeminiVisionOptions? overrideOptions = null)
    {
        var options = overrideOptions ?? new GeminiVisionOptions
        {
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/",
            Model = "gemini-1.5-flash",
            ApiKey = "test-api-key",
            RequestTimeout = TimeSpan.FromSeconds(5),
        };
        var handler = new CapturingHandler(responder);
        var http = new HttpClient(handler);
        return new GeminiVisionRunner(
            http,
            Options.Create(options),
            NullLogger<GeminiVisionRunner>.Instance);
    }

    [Fact]
    public void Constructor_Throws_Without_ApiKey()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new GeminiVisionRunner(
                new HttpClient(),
                Options.Create(new GeminiVisionOptions { ApiKey = null }),
                NullLogger<GeminiVisionRunner>.Instance));
    }

    [Fact]
    public async Task RescueTextAsync_Empty_Crop_Throws()
    {
        var runner = BuildRunner((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RescueTextAsync(ReadOnlyMemory<byte>.Empty, OriginalBlock(), CancellationToken.None));
    }

    [Fact]
    public async Task RescueTextAsync_Sends_Correct_Url_With_Key_Query_Parameter()
    {
        HttpRequestMessage? captured = null;
        var runner = BuildRunner((req, _) =>
        {
            captured = req;
            return Ok(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[] { new { text = "hello world" } },
                        },
                        finishReason = "STOP",
                    },
                },
            });
        });

        await runner.RescueTextAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None);

        Assert.NotNull(captured);
        var uri = captured!.RequestUri!.ToString();
        Assert.Contains("models/gemini-1.5-flash:generateContent", uri);
        Assert.Contains("key=test-api-key", uri);
    }

    [Fact]
    public async Task RescueTextAsync_Sends_Inline_Base64_Image_And_System_Prompt()
    {
        HttpRequestMessage? captured = null;
        var runner = BuildRunner((req, _) =>
        {
            captured = req;
            return Ok(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new { parts = new[] { new { text = "okay" } } },
                        finishReason = "STOP",
                    },
                },
            });
        });

        await runner.RescueTextAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None);

        var body = await captured!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var parts = doc.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal(2, parts.GetArrayLength());
        Assert.Contains("OCR engine", parts[0].GetProperty("text").GetString());
        var inline = parts[1].GetProperty("inlineData");
        Assert.Equal("image/png", inline.GetProperty("mimeType").GetString());
        Assert.False(string.IsNullOrEmpty(inline.GetProperty("data").GetString()));
    }

    [Fact]
    public async Task RescueTextAsync_Success_Returns_Concatenated_Text_With_Conf_0_90()
    {
        var runner = BuildRunner((_, _) => Ok(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = "Hello " },
                            new { text = "world" },
                        },
                    },
                    finishReason = "STOP",
                },
            },
        }));

        var rescued = await runner.RescueTextAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None);

        Assert.Equal("Hello world", rescued.Text);
        Assert.Equal(0.90, rescued.Confidence);
        Assert.False(rescued.IsRtl);
        Assert.Equal(Language.English, rescued.Language);
    }

    [Fact]
    public async Task RescueTextAsync_Hebrew_Output_Sets_Rtl_And_Language_Hebrew()
    {
        var runner = BuildRunner((_, _) => Ok(new
        {
            candidates = new[]
            {
                new
                {
                    content = new { parts = new[] { new { text = "מתמטיקה" } } },
                    finishReason = "STOP",
                },
            },
        }));

        var rescued = await runner.RescueTextAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None);

        Assert.Equal("מתמטיקה", rescued.Text);
        Assert.True(rescued.IsRtl);
        Assert.Equal(Language.Hebrew, rescued.Language);
    }

    [Fact]
    public async Task RescueTextAsync_Empty_Candidates_Returns_Original_Block()
    {
        var runner = BuildRunner((_, _) => Ok(new { candidates = Array.Empty<object>() }));
        var original = OriginalBlock();
        var rescued = await runner.RescueTextAsync(FakeCropBytes, original, CancellationToken.None);
        Assert.Equal(original, rescued);
    }

    [Fact]
    public async Task RescueTextAsync_Non_Success_Returns_Original_Block()
    {
        var runner = BuildRunner((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest));
        var original = OriginalBlock();
        var rescued = await runner.RescueTextAsync(FakeCropBytes, original, CancellationToken.None);
        Assert.Equal(original, rescued);
    }

    [Fact]
    public async Task RescueTextAsync_Transport_Failure_Throws_OcrCircuitOpen()
    {
        var runner = BuildRunner((_, _) => throw new HttpRequestException("connection refused"));
        await Assert.ThrowsAsync<OcrCircuitOpenException>(() =>
            runner.RescueTextAsync(FakeCropBytes, OriginalBlock(), CancellationToken.None));
    }

    [Fact]
    public async Task RescueTextAsync_Caller_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var runner = BuildRunner((_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            runner.RescueTextAsync(FakeCropBytes, OriginalBlock(), cts.Token));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private sealed class CapturingHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => Task.FromResult(_responder(req, ct));
    }
}
