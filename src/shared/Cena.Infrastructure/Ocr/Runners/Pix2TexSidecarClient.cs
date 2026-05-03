// =============================================================================
// Cena Platform — Pix2TexSidecarClient (ADR-0033 Layer 2b)
//
// Real ILayer2bMathOcr implementation. For each math region in Layer 1's
// output, crops the bbox from the corresponding page (ImageSharp), sends
// the crop to the sidecar's RecognizeMath RPC, and emits an OcrMathBlock
// with the returned LaTeX + confidence + original bbox preserved.
//
// Pass-through behaviour on sidecar unavailability: when RpcException fires,
// we skip that region (no math block emitted). The downstream Layer 5 CAS
// gate then can't validate it — acceptable for MVP since Layer 4's
// confidence gate will notice the missing rescue. An explicit "raw bytes
// unavailable" ErrorBlock variant is future work if needed.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.Layers;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using GrpcOcrSidecar = Cena.Infrastructure.Ocr.Grpc.OcrSidecar;
using GrpcMathRequest = Cena.Infrastructure.Ocr.Grpc.MathRequest;
using GrpcMathResponse = Cena.Infrastructure.Ocr.Grpc.MathResponse;

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class Pix2TexSidecarClient : ILayer2bMathOcr, IDisposable
{
    private readonly OcrSidecarOptions _options;
    private readonly GrpcChannel _channel;
    private readonly GrpcOcrSidecar.OcrSidecarClient _client;
    private readonly ILogger<Pix2TexSidecarClient>? _log;
    private int _disposed;

    public Pix2TexSidecarClient(
        IOptions<OcrSidecarOptions> options,
        ILogger<Pix2TexSidecarClient>? log = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _log = log;

        if (string.IsNullOrWhiteSpace(_options.Address))
            throw new InvalidOperationException(
                "OcrSidecarOptions.Address is required. Bind from \"Ocr:Sidecar\".");

        _channel = GrpcChannel.ForAddress(_options.Address);
        _client = new GrpcOcrSidecar.OcrSidecarClient(_channel);
    }

    internal Pix2TexSidecarClient(
        OcrSidecarOptions options,
        GrpcOcrSidecar.OcrSidecarClient client,
        ILogger<Pix2TexSidecarClient>? log = null)
    {
        _options = options;
        _client = client;
        _channel = null!;
        _log = log;
    }

    public async Task<Layer2bOutput> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<LayoutRegion> mathRegions,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (mathRegions.Count == 0 || pageBytes.Count == 0)
        {
            sw.Stop();
            return new Layer2bOutput(
                MathBlocks: Array.Empty<OcrMathBlock>(),
                LatencySeconds: sw.Elapsed.TotalSeconds);
        }

        var blocks = new List<OcrMathBlock>();
        foreach (var region in mathRegions)
        {
            ct.ThrowIfCancellationRequested();

            if (!TryCropRegion(pageBytes, region.Bbox, out var cropBytes))
            {
                _log?.LogDebug(
                    "[OCR_CASCADE] Layer2b skipped region — crop unavailable for bbox {Bbox}",
                    region.Bbox);
                continue;
            }

            if (cropBytes.Length > _options.MaxRegionBytes)
            {
                _log?.LogWarning(
                    "[OCR_CASCADE] Layer2b math region exceeds MaxRegionBytes={Max}; skipping",
                    _options.MaxRegionBytes);
                continue;
            }

            GrpcMathResponse? response;
            try
            {
                response = await _client.RecognizeMathAsync(
                        new GrpcMathRequest { ImageBytes = ByteString.CopyFrom(cropBytes) },
                        deadline: DateTime.UtcNow.Add(_options.RequestTimeout),
                        cancellationToken: ct)
                    .ResponseAsync
                    .ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                _log?.LogWarning(ex,
                    "[OCR_CASCADE] Layer2b pix2tex sidecar failed on region; skipping");
                continue;
            }

            var latex = response.Latex?.Trim();
            if (string.IsNullOrEmpty(latex)) continue;

            blocks.Add(new OcrMathBlock(
                Latex: latex,
                Bbox: region.Bbox,
                Confidence: Math.Clamp(response.Confidence, 0.0, 1.0),
                SympyParsed: false,   // Layer 5 owns this
                CanonicalForm: null));
        }

        sw.Stop();
        return new Layer2bOutput(
            MathBlocks: blocks,
            LatencySeconds: sw.Elapsed.TotalSeconds);
    }

    // -------------------------------------------------------------------------
    // Cropping — shared pattern with Layer4ConfidenceGate. Kept separate here
    // so Layer 2b doesn't depend on Layer 4.
    // -------------------------------------------------------------------------
    private static bool TryCropRegion(
        IReadOnlyList<byte[]> pageBytes, BoundingBox bbox, out byte[] crop)
    {
        crop = Array.Empty<byte>();
        int pageIdx = Math.Max(0, bbox.Page - 1);
        if (pageIdx >= pageBytes.Count) return false;
        var bytes = pageBytes[pageIdx];
        if (bytes.Length == 0) return false;

        try
        {
            using var img = Image.Load(bytes);
            int x = Math.Clamp((int)bbox.X, 0, Math.Max(0, img.Width - 1));
            int y = Math.Clamp((int)bbox.Y, 0, Math.Max(0, img.Height - 1));
            int w = Math.Clamp((int)bbox.W, 1, Math.Max(1, img.Width - x));
            int h = Math.Clamp((int)bbox.H, 1, Math.Max(1, img.Height - y));
            img.Mutate(ctx => ctx.Crop(new Rectangle(x, y, w, h)));
            using var ms = new MemoryStream();
            img.SaveAsPng(ms, new PngEncoder());
            crop = ms.ToArray();
            return crop.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel?.Dispose();
    }
}
