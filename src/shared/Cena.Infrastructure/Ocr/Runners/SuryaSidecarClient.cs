// =============================================================================
// Cena Platform — SuryaSidecarClient (ADR-0033 Layer 1)
//
// Real ILayer1Layout implementation. Calls the Python OCR sidecar via gRPC
// (stubs generated from docker/ocr-sidecar/app/ocr.proto by Grpc.Tools at
// build time — see Cena.Infrastructure.csproj).
//
// For each page in pageBytes, calls DetectLayoutAndRecognize and maps the
// response's text_lines + figures + math_regions back to our shared
// LayoutRegion + Kind conventions ("text", "math", "figure").
//
// The sidecar also returns recognised text; this client discards it because
// Layer 2a's TesseractLocalRunner is the authoritative text OCR path in the
// cascade. If you want Surya as the primary text OCR, register a
// SuryaTextClient separately that re-uses the same gRPC channel.
//
// Circuit / availability: when the sidecar is unreachable the gRPC call
// throws RpcException. We translate to "degraded mode" Layer1Output (empty
// regions + IsDegradedMode=true) rather than crashing — a cascade without
// layout detection still works via Tesseract on the whole page.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// Alias so `BoundingBox` in this file always means our contract type; the
// wire DTO is accessed via `GrpcBoundingBox` where needed.
using GrpcBoundingBox = Cena.Infrastructure.Ocr.Grpc.BoundingBox;
using GrpcOcrSidecar = Cena.Infrastructure.Ocr.Grpc.OcrSidecar;
using GrpcRecognizeRequest = Cena.Infrastructure.Ocr.Grpc.RecognizeRequest;
using GrpcRecognizeResponse = Cena.Infrastructure.Ocr.Grpc.RecognizeResponse;

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class SuryaSidecarClient : ILayer1Layout, IDisposable
{
    private readonly OcrSidecarOptions _options;
    private readonly GrpcChannel _channel;
    private readonly GrpcOcrSidecar.OcrSidecarClient _client;
    private readonly ILogger<SuryaSidecarClient>? _log;
    private int _disposed;

    public SuryaSidecarClient(
        IOptions<OcrSidecarOptions> options,
        ILogger<SuryaSidecarClient>? log = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _log = log;

        if (string.IsNullOrWhiteSpace(_options.Address))
            throw new InvalidOperationException(
                "OcrSidecarOptions.Address is required. Bind from \"Ocr:Sidecar\".");

        _channel = GrpcChannel.ForAddress(_options.Address);
        _client = new GrpcOcrSidecar.OcrSidecarClient(_channel);
    }

    // Constructor overload for tests: inject a pre-built client backed by an
    // in-memory channel (Grpc.Net.Client.Web / in-proc server). Keeps the
    // production path free of test-only branches.
    internal SuryaSidecarClient(
        OcrSidecarOptions options,
        GrpcOcrSidecar.OcrSidecarClient client,
        ILogger<SuryaSidecarClient>? log = null)
    {
        _options = options;
        _client = client;
        _channel = null!;   // tests manage channel lifetime
        _log = log;
    }

    public async Task<Layer1Output> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        OcrContextHints? hints,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (pageBytes.Count == 0)
        {
            sw.Stop();
            return new Layer1Output(
                Regions: Array.Empty<LayoutRegion>(),
                IsDegradedMode: false,
                LatencySeconds: sw.Elapsed.TotalSeconds);
        }

        var regions = new List<LayoutRegion>();
        string languageHint = hints?.Language switch
        {
            Language.Hebrew => "he",
            Language.English => "en",
            Language.Arabic => "ar",
            _ => string.Empty,
        };

        for (int i = 0; i < pageBytes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var bytes = pageBytes[i];
            if (bytes.Length == 0) continue;
            if (bytes.Length > _options.MaxRegionBytes)
            {
                _log?.LogWarning(
                    "[OCR_CASCADE] Layer1 page {Page} exceeds MaxRegionBytes={Max}; skipping",
                    i + 1, _options.MaxRegionBytes);
                continue;
            }

            var request = new GrpcRecognizeRequest
            {
                ImageBytes = ByteString.CopyFrom(bytes),
                LanguageHint = languageHint,
                IncludeFigures = _options.IncludeFigures,
            };

            GrpcRecognizeResponse? response;
            try
            {
                response = await _client.DetectLayoutAndRecognizeAsync(
                        request,
                        deadline: DateTime.UtcNow.Add(_options.RequestTimeout),
                        cancellationToken: ct)
                    .ResponseAsync
                    .ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                _log?.LogWarning(ex,
                    "[OCR_CASCADE] Layer1 sidecar unavailable on page {Page}; degrading",
                    i + 1);
                sw.Stop();
                return new Layer1Output(
                    Regions: Array.Empty<LayoutRegion>(),
                    IsDegradedMode: true,
                    LatencySeconds: sw.Elapsed.TotalSeconds);
            }

            foreach (var line in response.TextLines)
            {
                regions.Add(new LayoutRegion("text", ToBoundingBox(line.Bbox, i + 1)));
            }
            foreach (var mathRegion in response.MathRegions)
            {
                regions.Add(new LayoutRegion("math", ToBoundingBox(mathRegion, i + 1)));
            }
            foreach (var figure in response.Figures)
            {
                regions.Add(new LayoutRegion(
                    NormaliseFigureKind(figure.Kind),
                    ToBoundingBox(figure.Bbox, i + 1)));
            }
        }

        sw.Stop();
        return new Layer1Output(
            Regions: regions,
            IsDegradedMode: false,
            LatencySeconds: sw.Elapsed.TotalSeconds);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static BoundingBox ToBoundingBox(
        GrpcBoundingBox? bbox, int pageNumber)
    {
        if (bbox is null)
            return new BoundingBox(0, 0, 0, 0, pageNumber);
        return new BoundingBox(
            X: bbox.X,
            Y: bbox.Y,
            W: bbox.W,
            H: bbox.H,
            Page: bbox.Page > 0 ? bbox.Page : pageNumber);
    }

    private static string NormaliseFigureKind(string kind) =>
        string.IsNullOrEmpty(kind)
            ? "figure"
            : kind.ToLowerInvariant() switch
            {
                "table" => "table",
                "plot" or "chart" => "plot",
                _ => "figure",
            };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel?.Dispose();
    }
}
