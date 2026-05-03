# Iteration 09 -- Error Handling and Graceful Degradation When Vision Fails

**Series**: Student Screenshot Question Analyzer -- Security & Reliability Research  
**Date**: 2026-04-12  
**Scope**: Failure taxonomy, pre-processing quality gates, fallback hierarchy, circuit breakers, UX recovery, monitoring, and chaos testing for the Cena screenshot-to-question pipeline  
**Security Score Contribution**: 12 / 100  

---

## 1. Executive Summary

The Cena screenshot question analyzer follows a critical path: student photograph of a math problem is sent to Gemini 2.5 Flash for vision-based OCR, the extracted LaTeX is validated by CAS (MathNet / SymPy / Wolfram), and the resulting structured question enters the learning session. Every stage in this chain can fail -- the image may be unusable, the vision model may be unavailable, the LaTeX may be garbled, or the network may drop. This document establishes a defense-in-depth error-handling architecture that ensures no student is ever blocked from learning by a transient failure.

The core principle is **progressive enhancement with graceful degradation**: the system always offers the best available experience, falling back through increasingly manual (but always functional) alternatives. A student should never see a raw error or be left without a path forward.

---

## 2. Failure Taxonomy (17 Failure Modes)

Each failure mode is classified by **detection point** (client, server, or model), **severity** (blocking vs. degraded), and **recovery strategy**.

### 2.1 Image Quality Failures (Client-Side Detection)

| # | Failure Mode | Detection Method | Severity | Recovery |
|---|---|---|---|---|
| 1 | **Image too blurry** | Laplacian variance < 100 | Blocking | Reject with retake guidance; show focus tips |
| 2 | **Image too dark** | Mean brightness < 40 (0-255 scale) | Blocking | Reject; suggest better lighting or flash |
| 3 | **Image too small / low resolution** | Dimensions < 640x480 | Blocking | Reject; prompt to move camera closer |
| 4 | **Image oversized** | File > 10 MB | Blocking | Client-side resize before upload; warn if quality drops |
| 5 | **Invalid format** | Not JPEG/PNG/WebP/HEIC | Blocking | Reject; list accepted formats |

### 2.2 Content Analysis Failures (Server-Side Detection)

| # | Failure Mode | Detection Method | Severity | Recovery |
|---|---|---|---|---|
| 6 | **No math content detected** | Gemini confidence < 0.3, no `math_expressions` | Degraded | Friendly message: "We could not find math in this image. Try photographing just the question." |
| 7 | **Partial math extraction** | Gemini confidence 0.3-0.7, some expressions found | Degraded | Show what was found; let student complete or correct in editor |
| 8 | **Multiple questions detected** | Segmenter returns > 1 question | Degraded | Present list; let student select which question to work on |
| 9 | **Handwriting too messy** | Gemini text_blocks all below 0.5 confidence | Degraded | Suggest typed input or clearer retake |
| 10 | **Wrong language detected** | `detected_language` does not match student profile | Degraded | Language switch prompt with one-tap toggle |
| 11 | **Copyright-protected content** | Watermark / publisher logo detection | Informational | Educational fair-use notice; proceed with analysis |

### 2.3 Processing Pipeline Failures (Server-Side)

| # | Failure Mode | Detection Method | Severity | Recovery |
|---|---|---|---|---|
| 12 | **Vision model timeout** | No response within 8s | Blocking | Retry with exponential backoff; fallback to Mathpix after 2 retries |
| 13 | **Vision model rate-limited** | HTTP 429 from Gemini API | Blocking | Queue request; retry after `Retry-After` header; show spinner with ETA |
| 14 | **Vision model down** | HTTP 5xx or DNS failure | Blocking | Immediate fallback to Mathpix; if Mathpix also down, fall to manual input |
| 15 | **LaTeX parsing fails** | CAS (MathNet/SymPy) cannot parse extracted LaTeX | Degraded | Show raw extracted text in editable field; let student correct LaTeX |
| 16 | **CAS verification fails** | CAS returns error or timeout on equivalence check | Degraded | Flag answer as "needs review"; let student proceed; queue for teacher review |

### 2.4 Network / Client Failures

| # | Failure Mode | Detection Method | Severity | Recovery |
|---|---|---|---|---|
| 17 | **Network failure during upload** | `fetch` throws `TypeError` or timeout | Blocking | Client-side retry with progress preservation; offline queue if sustained |

---

## 3. Pre-Processing Quality Gate

Before any image reaches the Gemini API (where each call costs approximately $0.002), a client-side quality gate rejects obviously unusable images. This saves cost, reduces latency, and provides instant feedback.

### 3.1 Quality Checks (Ordered by Cost)

```
Check Order:
1. File format validation     — <1ms, zero cost
2. File size check            — <1ms, zero cost
3. Image dimensions           — <1ms, requires decode header only
4. Brightness/contrast check  — ~5ms, requires pixel sampling
5. Blur detection (Laplacian) — ~10ms, requires grayscale conversion + convolution
```

### 3.2 JavaScript Client-Side Implementation

```typescript
// src/student/full-version/src/composables/useImageQualityGate.ts

export interface QualityCheckResult {
  passed: boolean;
  checks: {
    format: boolean;
    size: boolean;
    dimensions: boolean;
    brightness: boolean;
    blur: boolean;
  };
  failureReason?: string;
  failureMessageKey?: string; // i18n key
  suggestion?: string;
}

const ACCEPTED_FORMATS = ['image/jpeg', 'image/png', 'image/webp', 'image/heic'];
const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB
const MIN_WIDTH = 640;
const MIN_HEIGHT = 480;
const MIN_BRIGHTNESS = 40;   // 0-255 scale
const MAX_BRIGHTNESS = 235;  // Overexposed threshold
const MIN_LAPLACIAN_VARIANCE = 100; // Sharpness threshold

export async function checkImageQuality(file: File): Promise<QualityCheckResult> {
  const result: QualityCheckResult = {
    passed: true,
    checks: { format: true, size: true, dimensions: true, brightness: true, blur: true },
  };

  // 1. Format check
  if (!ACCEPTED_FORMATS.includes(file.type)) {
    return fail(result, 'format',
      'screenshot.error.invalidFormat',
      'Please use JPEG, PNG, or WebP format.');
  }

  // 2. Size check
  if (file.size > MAX_FILE_SIZE_BYTES) {
    return fail(result, 'size',
      'screenshot.error.fileTooLarge',
      'Image must be under 10 MB. Try reducing resolution in camera settings.');
  }

  // 3-5. Require image decode
  const bitmap = await createImageBitmap(file);
  const { width, height } = bitmap;

  // 3. Dimension check
  if (width < MIN_WIDTH || height < MIN_HEIGHT) {
    bitmap.close();
    return fail(result, 'dimensions',
      'screenshot.error.tooSmall',
      `Minimum resolution is ${MIN_WIDTH}x${MIN_HEIGHT}. Move the camera closer.`);
  }

  // 4-5. Pixel analysis via OffscreenCanvas
  const canvas = new OffscreenCanvas(width, height);
  const ctx = canvas.getContext('2d')!;
  ctx.drawImage(bitmap, 0, 0);
  bitmap.close();

  const imageData = ctx.getImageData(0, 0, width, height);

  // 4. Brightness check (sample every 16th pixel for speed)
  const brightness = computeMeanBrightness(imageData, 16);
  if (brightness < MIN_BRIGHTNESS) {
    return fail(result, 'brightness',
      'screenshot.error.tooDark',
      'The image is too dark. Try using better lighting or your camera flash.');
  }
  if (brightness > MAX_BRIGHTNESS) {
    return fail(result, 'brightness',
      'screenshot.error.tooLight',
      'The image is overexposed. Try reducing lighting or moving away from direct light.');
  }

  // 5. Blur detection (Laplacian variance on grayscale)
  const sharpness = computeLaplacianVariance(imageData, width, height);
  if (sharpness < MIN_LAPLACIAN_VARIANCE) {
    return fail(result, 'blur',
      'screenshot.error.tooBlurry',
      'The image appears blurry. Hold your device steady and make sure the text is in focus.');
  }

  return result;
}

function fail(
  result: QualityCheckResult,
  check: keyof QualityCheckResult['checks'],
  messageKey: string,
  suggestion: string,
): QualityCheckResult {
  result.passed = false;
  result.checks[check] = false;
  result.failureMessageKey = messageKey;
  result.suggestion = suggestion;
  return result;
}

function computeMeanBrightness(data: ImageData, sampleInterval: number): number {
  const pixels = data.data;
  let sum = 0;
  let count = 0;
  for (let i = 0; i < pixels.length; i += 4 * sampleInterval) {
    // Luminance formula: 0.299R + 0.587G + 0.114B
    sum += 0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2];
    count++;
  }
  return count > 0 ? sum / count : 0;
}

function computeLaplacianVariance(
  data: ImageData, width: number, height: number,
): number {
  // Convert to grayscale
  const gray = new Float32Array(width * height);
  for (let i = 0; i < gray.length; i++) {
    const p = i * 4;
    gray[i] = 0.299 * data.data[p] + 0.587 * data.data[p + 1] + 0.114 * data.data[p + 2];
  }

  // Apply 3x3 Laplacian kernel: [0, 1, 0; 1, -4, 1; 0, 1, 0]
  let sum = 0;
  let sumSq = 0;
  let count = 0;

  for (let y = 1; y < height - 1; y++) {
    for (let x = 1; x < width - 1; x++) {
      const idx = y * width + x;
      const laplacian =
        gray[idx - width] +       // top
        gray[idx - 1] +           // left
        -4 * gray[idx] +          // center
        gray[idx + 1] +           // right
        gray[idx + width];        // bottom

      sum += laplacian;
      sumSq += laplacian * laplacian;
      count++;
    }
  }

  const mean = sum / count;
  return (sumSq / count) - (mean * mean); // Variance
}
```

### 3.3 Python Server-Side Quality Preprocessor (Backup Validation)

```python
# scripts/image_quality_gate.py
# Server-side backup validation for images that bypass client checks
# (e.g., API-uploaded images, mobile app submissions)

import cv2
import numpy as np
from dataclasses import dataclass
from enum import Enum
from typing import Optional


class QualityFailure(Enum):
    FORMAT_INVALID = "format_invalid"
    SIZE_TOO_LARGE = "size_too_large"
    RESOLUTION_TOO_LOW = "resolution_too_low"
    TOO_DARK = "too_dark"
    TOO_BRIGHT = "too_bright"
    TOO_BLURRY = "too_blurry"
    LOW_CONTRAST = "low_contrast"


@dataclass
class QualityReport:
    passed: bool
    failures: list[QualityFailure]
    brightness: float      # 0-255
    contrast: float         # standard deviation of pixel values
    sharpness: float        # Laplacian variance
    width: int
    height: int


MIN_WIDTH = 640
MIN_HEIGHT = 480
MAX_FILE_BYTES = 10 * 1024 * 1024
MIN_BRIGHTNESS = 40.0
MAX_BRIGHTNESS = 235.0
MIN_CONTRAST = 25.0           # StdDev of grayscale pixels
MIN_LAPLACIAN_VARIANCE = 100.0


def check_image_quality(image_bytes: bytes) -> QualityReport:
    """
    Run all quality checks on raw image bytes.
    Returns a QualityReport with pass/fail and diagnostics.
    """
    failures: list[QualityFailure] = []

    # Decode image
    np_arr = np.frombuffer(image_bytes, np.uint8)
    img = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)
    if img is None:
        return QualityReport(
            passed=False,
            failures=[QualityFailure.FORMAT_INVALID],
            brightness=0, contrast=0, sharpness=0,
            width=0, height=0,
        )

    height, width = img.shape[:2]

    # Size check (redundant with API layer, but defense-in-depth)
    if len(image_bytes) > MAX_FILE_BYTES:
        failures.append(QualityFailure.SIZE_TOO_LARGE)

    # Resolution check
    if width < MIN_WIDTH or height < MIN_HEIGHT:
        failures.append(QualityFailure.RESOLUTION_TOO_LOW)

    # Convert to grayscale for analysis
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    # Brightness (mean pixel intensity)
    brightness = float(np.mean(gray))
    if brightness < MIN_BRIGHTNESS:
        failures.append(QualityFailure.TOO_DARK)
    elif brightness > MAX_BRIGHTNESS:
        failures.append(QualityFailure.TOO_BRIGHT)

    # Contrast (standard deviation of pixel values)
    contrast = float(np.std(gray))
    if contrast < MIN_CONTRAST:
        failures.append(QualityFailure.LOW_CONTRAST)

    # Blur detection: Laplacian variance
    # cv2.Laplacian computes the 2nd derivative. Sharp images have high variance.
    # Reference: Pech-Pacheco et al. (2000), "Diatom autofocusing in brightfield
    # microscopy: a comparative study" -- validated Laplacian as best single-metric
    # focus measure.
    laplacian = cv2.Laplacian(gray, cv2.CV_64F)
    sharpness = float(laplacian.var())
    if sharpness < MIN_LAPLACIAN_VARIANCE:
        failures.append(QualityFailure.TOO_BLURRY)

    return QualityReport(
        passed=len(failures) == 0,
        failures=failures,
        brightness=brightness,
        contrast=contrast,
        sharpness=sharpness,
        width=width,
        height=height,
    )
```

### 3.4 Quality Gate Thresholds

| Check | Threshold | Rationale | Source |
|---|---|---|---|
| Min resolution | 640 x 480 | Minimum for OCR legibility of printed math at arm's length | Mathpix API docs |
| Min brightness | 40 / 255 | Below this, even adaptive thresholding cannot recover text | Empirical; OpenCV community consensus |
| Max brightness | 235 / 255 | Overexposed images lose ink/pencil contrast | Same |
| Min contrast (stddev) | 25 | Low-contrast images (e.g., pencil on white paper under fluorescent light) | Same |
| Min Laplacian variance | 100 | Below this, text edges are indistinguishable from noise | [PyImageSearch (2015)](https://pyimagesearch.com/2015/09/07/blur-detection-with-opencv/); per-dataset tuning recommended |
| Max file size | 10 MB | Gemini API limit is 20 MB; 10 MB leaves headroom for base64 expansion | Gemini API docs |

---

## 4. Fallback Hierarchy

The system maintains four tiers of question extraction capability. Each tier is progressively less capable but more reliable.

```
Tier 1 (Primary)     Gemini 2.5 Flash Vision
  |                   - Full structured JSON output
  |                   - RTL text + LaTeX extraction
  |                   - Confidence scores per block
  |                   - Cost: ~$0.002/image
  |                   - Latency: 1-3 seconds
  v
Tier 2 (Secondary)   Mathpix OCR API
  |                   - Specialist math-to-LaTeX
  |                   - No full-text extraction (math only)
  |                   - Cost: ~$0.004/image
  |                   - Latency: 1-2 seconds
  v
Tier 3 (Tertiary)    Tesseract.js (Client-Side)
  |                   - Runs in browser via WebAssembly
  |                   - Good for printed text, poor for math/handwriting
  |                   - Cost: $0 (runs on client device)
  |                   - Latency: 2-20 seconds (device-dependent)
  |                   - Language data download: ~2 MB per language
  v
Tier 4 (Ultimate)    Manual Input
                      - Student types the question directly
                      - LaTeX editor with MathLive for math notation
                      - Cost: $0
                      - Latency: student-dependent (typically 30-90 seconds)
                      - Always available, even offline
```

### 4.1 Fallback Trigger Conditions

| Transition | Trigger | Condition |
|---|---|---|
| Tier 1 to Tier 2 | Gemini fails | Circuit breaker open, HTTP 5xx, timeout after 2 retries, or confidence < 0.3 |
| Tier 1 to Tier 2 | Low confidence | Gemini overall confidence < `MinConfidenceForFallback` (0.7, configured in `GeminiOcrOptions`) |
| Tier 2 to Tier 3 | Mathpix fails | HTTP 5xx, timeout, or Mathpix circuit breaker open |
| Tier 2 to Tier 3 | Both APIs down | Gemini and Mathpix circuit breakers both open |
| Tier 3 to Tier 4 | Tesseract insufficient | Tesseract output has no recognizable math, or user rejects result |
| Any to Tier 4 | User choice | Student taps "Type it instead" at any point |

### 4.2 C# Fallback Orchestrator

This integrates with the existing `IngestionOrchestrator` and circuit breaker infrastructure already present in the Cena actor system.

```csharp
// src/actors/Cena.Actors/Ingest/ScreenshotAnalyzerService.cs

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Actors.Gateway;
using Cena.Actors.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Ingest;

public sealed record ScreenshotAnalysisRequest(
    Stream ImageStream,
    string ContentType,
    string StudentId,
    string Language,
    string SessionId);

public sealed record ScreenshotAnalysisResult(
    bool Success,
    string? ExtractedLatex,
    string? ExtractedText,
    string? DetectedLanguage,
    float Confidence,
    string ProviderUsed,          // "gemini", "mathpix", "tesseract", "manual"
    string? FallbackReason,
    TimeSpan Latency);

public sealed class ScreenshotAnalyzerService
{
    private readonly IOcrClient _gemini;
    private readonly IMathOcrClient _mathpix;
    private readonly IRedisCircuitBreaker _geminiCb;
    private readonly ILogger<ScreenshotAnalyzerService> _logger;
    private readonly Counter<long> _fallbackCounter;
    private readonly Histogram<double> _latencyHistogram;

    // Latency budgets
    private static readonly TimeSpan GeminiTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MathpixTimeout = TimeSpan.FromSeconds(5);
    private const int MaxGeminiRetries = 2;
    private const float MinConfidenceForAccept = 0.3f;

    public ScreenshotAnalyzerService(
        IOcrClient gemini,
        IMathOcrClient mathpix,
        IRedisCircuitBreaker geminiCb,
        ILogger<ScreenshotAnalyzerService> logger,
        IMeterFactory meterFactory)
    {
        _gemini = gemini;
        _mathpix = mathpix;
        _geminiCb = geminiCb;
        _logger = logger;

        var meter = meterFactory.Create("Cena.ScreenshotAnalyzer", "1.0.0");
        _fallbackCounter = meter.CreateCounter<long>(
            "cena.screenshot.fallback_total",
            description: "Screenshot analysis fallback events by reason");
        _latencyHistogram = meter.CreateHistogram<double>(
            "cena.screenshot.analysis_latency_ms",
            unit: "ms",
            description: "End-to-end screenshot analysis latency");
    }

    public async Task<ScreenshotAnalysisResult> AnalyzeAsync(
        ScreenshotAnalysisRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // --- Tier 1: Gemini 2.5 Flash ---
        if (_geminiCb.IsAvailable)
        {
            for (int attempt = 0; attempt <= MaxGeminiRetries; attempt++)
            {
                try
                {
                    using var cts = CancellationTokenSource
                        .CreateLinkedTokenSource(ct);
                    cts.CancelAfter(GeminiTimeout);

                    request.ImageStream.Position = 0;
                    var page = await _gemini.ProcessPageAsync(
                        request.ImageStream, request.ContentType, cts.Token);

                    _geminiCb.RecordSuccess();

                    if (page.Confidence >= MinConfidenceForAccept)
                    {
                        sw.Stop();
                        _latencyHistogram.Record(sw.ElapsedMilliseconds,
                            new KeyValuePair<string, object?>("provider", "gemini"));
                        return BuildResult(page, "gemini", null, sw.Elapsed);
                    }

                    // Confidence too low -- fall through to Mathpix
                    _fallbackCounter.Add(1,
                        new KeyValuePair<string, object?>("reason", "low_confidence"));
                    _logger.LogInformation(
                        "Gemini confidence {Confidence:F2} below threshold, "
                        + "falling back to Mathpix",
                        page.Confidence);
                    break;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "Gemini timeout on attempt {Attempt}/{Max}",
                        attempt + 1, MaxGeminiRetries + 1);

                    if (attempt == MaxGeminiRetries)
                    {
                        _geminiCb.RecordFailure();
                        _fallbackCounter.Add(1,
                            new KeyValuePair<string, object?>("reason", "timeout"));
                    }
                    else
                    {
                        // Exponential backoff with jitter before retry
                        var delay = ComputeBackoff(attempt);
                        await Task.Delay(delay, ct);
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _geminiCb.RecordFailure();
                    _fallbackCounter.Add(1,
                        new KeyValuePair<string, object?>("reason", "rate_limited"));
                    _logger.LogWarning("Gemini rate-limited, falling back to Mathpix");
                    break;
                }
                catch (Exception ex)
                {
                    _geminiCb.RecordFailure();
                    _fallbackCounter.Add(1,
                        new KeyValuePair<string, object?>("reason", "error"));
                    _logger.LogWarning(ex, "Gemini failed, falling back to Mathpix");
                    break;
                }
            }
        }
        else
        {
            _fallbackCounter.Add(1,
                new KeyValuePair<string, object?>("reason", "circuit_open"));
            _logger.LogInformation(
                "Gemini circuit breaker open, skipping to Mathpix");
        }

        // --- Tier 2: Mathpix ---
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(MathpixTimeout);

            request.ImageStream.Position = 0;
            var latex = await _mathpix.ExtractLatexAsync(
                request.ImageStream, cts.Token);

            if (!string.IsNullOrWhiteSpace(latex))
            {
                sw.Stop();
                _latencyHistogram.Record(sw.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("provider", "mathpix"));
                return new ScreenshotAnalysisResult(
                    Success: true,
                    ExtractedLatex: latex,
                    ExtractedText: null,
                    DetectedLanguage: null,
                    Confidence: 0.8f,
                    ProviderUsed: "mathpix",
                    FallbackReason: "gemini_unavailable",
                    Latency: sw.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mathpix fallback also failed");
            _fallbackCounter.Add(1,
                new KeyValuePair<string, object?>("reason", "mathpix_failed"));
        }

        // --- Tier 3/4: Signal client to use Tesseract.js or manual input ---
        sw.Stop();
        _latencyHistogram.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("provider", "manual_fallback"));
        return new ScreenshotAnalysisResult(
            Success: false,
            ExtractedLatex: null,
            ExtractedText: null,
            DetectedLanguage: null,
            Confidence: 0f,
            ProviderUsed: "none",
            FallbackReason: "all_providers_failed",
            Latency: sw.Elapsed);
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        // Decorrelated jitter: base * 2^attempt + random(0, base)
        // Prevents thundering herd across concurrent student requests
        var baseMs = 500;
        var maxMs = baseMs * (1 << attempt); // 500, 1000, 2000
        var jitter = Random.Shared.Next(0, baseMs);
        return TimeSpan.FromMilliseconds(maxMs + jitter);
    }

    private static ScreenshotAnalysisResult BuildResult(
        OcrPageOutput page, string provider, string? fallbackReason, TimeSpan latency)
    {
        var latex = page.MathExpressions.Count > 0
            ? string.Join("\n", page.MathExpressions.Values)
            : null;
        return new ScreenshotAnalysisResult(
            Success: true,
            ExtractedLatex: latex,
            ExtractedText: page.RawText,
            DetectedLanguage: page.DetectedLanguage,
            Confidence: page.Confidence,
            ProviderUsed: provider,
            FallbackReason: fallbackReason,
            Latency: latency);
    }
}
```

---

## 5. Circuit Breaker Implementation

Cena already has two production circuit breakers: `RedisCircuitBreaker` (INF-019) for cache protection and `LlmCircuitBreakerActor` (per-model, Proto.Actor-based) for LLM calls. The screenshot analyzer reuses the same architecture.

### 5.1 State Machine

```
         ┌─────────┐
         │  CLOSED  │  (Normal operation -- all requests pass through)
         └────┬─────┘
              │ 3 consecutive failures
              v
         ┌─────────┐
         │   OPEN   │  (All requests rejected -- return fallback immediately)
         └────┬─────┘
              │ After 30 seconds
              v
        ┌───────────┐
        │ HALF-OPEN │  (Allow 1 probe request)
        └─────┬─────┘
              │
      ┌───────┴────────┐
      │ Probe succeeds │ Probe fails
      v                v
   CLOSED            OPEN (reset 30s timer)
```

### 5.2 Configuration per Provider

| Provider | Max Failures | Open Duration | Half-Open Successes | Rationale |
|---|---|---|---|---|
| Gemini 2.5 Flash | 3 | 30 seconds | 1 | Fast recovery; Gemini outages are typically transient (< 60s) |
| Mathpix | 5 | 60 seconds | 2 | Higher threshold because Mathpix is the fallback; tripping too eagerly leaves no options |
| Tesseract.js | N/A | N/A | N/A | Client-side; no circuit breaker needed (always available) |

### 5.3 Integration with HealthAggregatorActor

The existing `HealthAggregatorActor` (RES-005) polls all circuit breakers every 5 seconds and computes a `SystemHealthLevel`. When the screenshot analyzer's Gemini circuit breaker opens:

- `openCircuitBreakers >= 1` triggers `SystemHealthLevel.Degraded`
- `DegradationMode.ShouldUseFallbackQuestions()` returns `true`
- The student session actor auto-switches to pre-built question pools as a safety net
- If both Gemini and Mathpix circuit breakers open (2 open), the system enters `SystemHealthLevel.Critical`

### 5.4 Polly v8 Resilience Pipeline (Alternative to Hand-Rolled CB)

For teams preferring the Microsoft-recommended approach, here is the equivalent using `Microsoft.Extensions.Http.Resilience` with Polly v8:

```csharp
// In Program.cs / service registration
// Requires: Microsoft.Extensions.Http.Resilience (Polly v8)

services.AddHttpClient("GeminiVision")
    .AddResiliencePipeline("screenshot-gemini", builder =>
    {
        // 1. Timeout per attempt
        builder.AddTimeout(TimeSpan.FromSeconds(8));

        // 2. Retry with exponential backoff + jitter
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .HandleResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    || r.StatusCode >= System.Net.HttpStatusCode.InternalServerError)
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>(),
        });

        // 3. Circuit breaker
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .HandleResult(r => (int)r.StatusCode >= 500)
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>(),
        });
    });
```

### 5.5 Monitoring the Circuit Breaker

The existing `LlmCircuitBreakerActor` already emits two metrics via `IMeterFactory`:

- `cena.llm.circuit_opened_total{model}` -- Counter, incremented each time a circuit opens
- `cena.llm.requests_rejected_total{model}` -- Counter of requests rejected by an open circuit

For the screenshot analyzer, the following additional metrics are emitted:

- `cena.screenshot.fallback_total{reason}` -- Counter by fallback reason (timeout, rate_limited, circuit_open, low_confidence, error, mathpix_failed)
- `cena.screenshot.analysis_latency_ms{provider}` -- Histogram of end-to-end latency by provider

---

## 6. Retry Strategy

### 6.1 Exponential Backoff with Decorrelated Jitter

The retry strategy follows the AWS Architecture Blog recommendation for "decorrelated jitter" -- each retry delay is randomized independently, preventing synchronized retries from multiple concurrent student requests.

```
Attempt 0: Immediate
Attempt 1: 500ms + random(0..500ms) = 500-1000ms
Attempt 2: 1000ms + random(0..500ms) = 1000-1500ms
```

**Why decorrelated jitter over full jitter?** Full jitter (`random(0, base * 2^n)`) can produce very short delays (close to 0ms), which defeats the purpose of backoff. Decorrelated jitter guarantees a minimum wait that grows with each attempt.

Source: [AWS Architecture Blog -- Exponential Backoff and Jitter](https://aws.amazon.com/blogs/architecture/exponential-backoff-and-jitter/)

### 6.2 Idempotency

All vision API calls are naturally idempotent -- sending the same image twice produces the same result. No special idempotency keys are needed.

### 6.3 Retry Limits

| Provider | Max Retries | Max Total Wait | Rationale |
|---|---|---|---|
| Gemini | 2 | ~3.5 seconds | 2 retries keep total under 4s latency budget |
| Mathpix | 0 | 0 | Mathpix is itself a fallback; retrying delays manual fallback |

---

## 7. Latency Budget Under Failure

| Scenario | Latency | Path |
|---|---|---|
| **Happy path** | < 2 seconds | Client quality gate (10ms) + Gemini (1-2s) + CAS (200ms) |
| **First retry** | < 4 seconds | Quality gate + Gemini attempt 1 (timeout 8s, but typically fails fast) + backoff (500-1000ms) + Gemini attempt 2 |
| **Fallback to Mathpix** | < 5 seconds | Quality gate + Gemini retries exhausted (~3.5s) + Mathpix (1-2s) |
| **Circuit breaker open** | < 3 seconds | Quality gate + skip Gemini (0ms) + Mathpix (1-2s) + CAS (200ms) |
| **All APIs down** | < 1 second | Quality gate + both CBs open, return "use manual input" immediately |
| **Fallback to Tesseract.js** | 3-20 seconds | Runs on client; highly device-dependent; iPhone X: 2-20s |
| **Manual input** | Immediate | No server round-trip; student types directly |

### 7.1 Latency Budget Enforcement

Each tier enforces a hard timeout via `CancellationTokenSource.CancelAfter()`:

- Gemini: 8 seconds per attempt
- Mathpix: 5 seconds
- Total end-to-end: 15 seconds hard ceiling before falling to manual

---

## 8. UX for Each Failure Mode

### 8.1 Error Message Templates (EN / AR / HE)

Every failure mode maps to a specific i18n key. Messages follow three principles from UX research:
1. **Explain what happened** (not technical details)
2. **Suggest what to do next** (actionable)
3. **Preserve progress** (never lose work)

Source: [Pencil & Paper -- Error Message UX](https://www.pencilandpaper.io/articles/ux-pattern-analysis-error-feedback)

```json
{
  "screenshot": {
    "error": {
      "tooBlurry": {
        "en": "The image appears blurry. Hold your device steady and make sure the text is in focus.",
        "ar": "تبدو الصورة ضبابية. ثبّت جهازك وتأكد من أن النص واضح.",
        "he": "התמונה נראית מטושטשת. החזיקו את המכשיר יציב וודאו שהטקסט בפוקוס."
      },
      "tooDark": {
        "en": "The image is too dark. Try using better lighting or your camera flash.",
        "ar": "الصورة مظلمة جدًا. حاول استخدام إضاءة أفضل أو فلاش الكاميرا.",
        "he": "התמונה חשוכה מדי. נסו להשתמש בתאורה טובה יותר או בפלאש של המצלמה."
      },
      "tooSmall": {
        "en": "The image is too small. Move your camera closer to the question.",
        "ar": "الصورة صغيرة جدًا. قرّب الكاميرا من السؤال.",
        "he": "התמונה קטנה מדי. קרבו את המצלמה לשאלה."
      },
      "fileTooLarge": {
        "en": "The image file is too large (max 10 MB). Try reducing your camera resolution.",
        "ar": "حجم ملف الصورة كبير جدًا (الحد الأقصى 10 ميجابايت). حاول تقليل دقة الكاميرا.",
        "he": "קובץ התמונה גדול מדי (מקסימום 10 MB). נסו להקטין את רזולוציית המצלמה."
      },
      "invalidFormat": {
        "en": "This file format is not supported. Please use JPEG, PNG, or WebP.",
        "ar": "هذا التنسيق غير مدعوم. يرجى استخدام JPEG أو PNG أو WebP.",
        "he": "פורמט קובץ זה אינו נתמך. השתמשו ב-JPEG, PNG או WebP."
      },
      "noMathFound": {
        "en": "We could not find math in this image. Try photographing just the question, or type it below.",
        "ar": "لم نتمكن من إيجاد رياضيات في هذه الصورة. حاول تصوير السؤال فقط، أو اكتبه أدناه.",
        "he": "לא מצאנו מתמטיקה בתמונה. נסו לצלם רק את השאלה, או הקלידו אותה למטה."
      },
      "partialExtraction": {
        "en": "We found some of the math, but not all. Please review and complete the question below.",
        "ar": "وجدنا بعض الرياضيات، لكن ليس كلها. يرجى مراجعة السؤال وإكماله أدناه.",
        "he": "מצאנו חלק מהמתמטיקה, אבל לא הכול. אנא בדקו והשלימו את השאלה למטה."
      },
      "multipleQuestions": {
        "en": "We found more than one question. Please select the one you want to work on.",
        "ar": "وجدنا أكثر من سؤال واحد. يرجى اختيار السؤال الذي تريد العمل عليه.",
        "he": "מצאנו יותר משאלה אחת. אנא בחרו את השאלה שאתם רוצים לעבוד עליה."
      },
      "handwritingUnclear": {
        "en": "The handwriting is hard to read. Try typing the question instead, or take a clearer photo.",
        "ar": "يصعب قراءة الخط. حاول كتابة السؤال بدلاً من ذلك، أو التقط صورة أوضح.",
        "he": "קשה לקרוא את כתב היד. נסו להקליד את השאלה במקום, או צלמו תמונה ברורה יותר."
      },
      "wrongLanguage": {
        "en": "The text appears to be in {detectedLang}. Switch language?",
        "ar": "يبدو أن النص مكتوب بـ{detectedLang}. هل تريد تبديل اللغة؟",
        "he": "נראה שהטקסט כתוב ב{detectedLang}. להחליף שפה?"
      },
      "processingFailed": {
        "en": "We could not process this image right now. You can try again or type the question manually.",
        "ar": "لم نتمكن من معالجة هذه الصورة الآن. يمكنك المحاولة مرة أخرى أو كتابة السؤال يدويًا.",
        "he": "לא הצלחנו לעבד את התמונה כרגע. אפשר לנסות שוב או להקליד את השאלה ידנית."
      },
      "networkError": {
        "en": "Connection lost. Your photo has been saved. We will process it when you are back online.",
        "ar": "فُقد الاتصال. تم حفظ صورتك. سنعالجها عندما تعود متصلاً.",
        "he": "החיבור נותק. התמונה שלכם נשמרה. נעבד אותה כשתחזרו לרשת."
      },
      "latexParseFailed": {
        "en": "We extracted the text but could not parse the math notation. Please review and correct below.",
        "ar": "استخرجنا النص لكن لم نتمكن من تحليل الترميز الرياضي. يرجى المراجعة والتصحيح أدناه.",
        "he": "חילצנו את הטקסט אבל לא הצלחנו לפענח את הסימון המתמטי. אנא בדקו ותקנו למטה."
      },
      "casVerificationFailed": {
        "en": "Your answer has been saved but could not be verified automatically. A teacher will review it.",
        "ar": "تم حفظ إجابتك لكن لم يتم التحقق منها تلقائيًا. سيراجعها المعلم.",
        "he": "התשובה שלכם נשמרה אך לא ניתן היה לאמת אותה אוטומטית. מורה יבדוק אותה."
      },
      "copyrightNotice": {
        "en": "This appears to be from a published textbook. Cena processes it for educational analysis only -- no copies are stored.",
        "ar": "يبدو أن هذا من كتاب مدرسي منشور. يقوم Cena بمعالجته للتحليل التعليمي فقط -- لا يتم تخزين نسخ.",
        "he": "נראה שזה מתוך ספר לימוד שפורסם. Cena מעבד אותו לניתוח לימודי בלבד -- לא נשמרים עותקים."
      }
    },
    "action": {
      "retake": {
        "en": "Retake Photo",
        "ar": "إعادة التقاط الصورة",
        "he": "צלמו שוב"
      },
      "typeInstead": {
        "en": "Type it instead",
        "ar": "اكتبه بدلاً من ذلك",
        "he": "הקלידו במקום"
      },
      "tryAgain": {
        "en": "Try Again",
        "ar": "حاول مرة أخرى",
        "he": "נסו שוב"
      },
      "switchLanguage": {
        "en": "Switch Language",
        "ar": "تبديل اللغة",
        "he": "החלפת שפה"
      },
      "editExtracted": {
        "en": "Edit Extracted Text",
        "ar": "تعديل النص المستخرج",
        "he": "ערכו טקסט שחולץ"
      },
      "selectQuestion": {
        "en": "Select Question",
        "ar": "اختر السؤال",
        "he": "בחרו שאלה"
      }
    },
    "status": {
      "analyzing": {
        "en": "Analyzing your photo...",
        "ar": "جارٍ تحليل صورتك...",
        "he": "מנתחים את התמונה שלכם..."
      },
      "retrying": {
        "en": "Taking a bit longer than usual. Retrying...",
        "ar": "يستغرق وقتًا أطول من المعتاد. جارٍ إعادة المحاولة...",
        "he": "לוקח קצת יותר זמן מהרגיל. מנסים שוב..."
      },
      "switchingProvider": {
        "en": "Using backup processor...",
        "ar": "جارٍ استخدام المعالج الاحتياطي...",
        "he": "משתמשים במעבד גיבוי..."
      }
    }
  }
}
```

### 8.2 UX Flow per Failure Mode

| # | Failure | UI Component | Action Buttons | Progress Preserved? |
|---|---|---|---|---|
| 1 | Blurry | Toast with camera icon | Retake / Type Instead | Yes (image kept in memory) |
| 2 | Too dark | Toast with lightbulb icon | Retake / Type Instead | Yes |
| 3 | Too small | Toast with zoom icon | Retake / Type Instead | Yes |
| 4 | No math found | Bottom sheet with suggestion | Retake / Type Instead | Yes |
| 5 | Partial extraction | Editable LaTeX preview | Edit / Retake / Accept Partial | Yes (partial result shown) |
| 6 | Timeout (retrying) | Spinner with "Retrying..." text | Cancel / Type Instead | Yes |
| 7 | Rate limited | Spinner with ETA | Wait / Type Instead | Yes |
| 8 | All APIs down | Full-page fallback card | Type Instead | Yes (image queued for later) |
| 9 | LaTeX parse error | Editable text field pre-filled | Edit / Retake | Yes (raw text shown) |
| 10 | CAS verification fails | Green checkmark with asterisk | Continue | Yes (answer saved, flagged) |
| 11 | Multiple questions | List of detected questions | Select one | Yes |
| 12 | Handwriting unclear | Bottom sheet | Retake / Type Instead | Yes |
| 13 | Wrong language | Language switch dialog | Switch / Keep Current | Yes |
| 14 | Copyright content | Informational banner (dismissable) | OK / Dismiss | Yes |
| 15 | Network failure | Offline banner | Retry When Online | Yes (IndexedDB persistence) |

### 8.3 Progress Preservation Strategy

Every image capture is immediately persisted to `IndexedDB` before any server call. If the page is closed, the app restores the last image on reload:

```typescript
// Pseudocode for progress preservation
const STORE_KEY = 'cena_screenshot_pending';

async function captureAndAnalyze(file: File) {
  // 1. Persist immediately (survive tab close, network loss, crash)
  await idbSet(STORE_KEY, {
    imageBlob: file,
    capturedAt: Date.now(),
    sessionId: currentSession.id,
  });

  // 2. Run quality gate
  const quality = await checkImageQuality(file);
  if (!quality.passed) {
    showQualityError(quality);
    return; // Image stays in IDB for retake
  }

  // 3. Upload and analyze
  try {
    const result = await api.analyzeScreenshot(file);
    await idbDelete(STORE_KEY); // Success -- clear pending
    showResult(result);
  } catch (err) {
    if (isNetworkError(err)) {
      showOfflineBanner(); // Image safe in IDB
    } else {
      showFallbackOptions(); // Retake or Type Instead
    }
  }
}

// On app load, check for pending screenshot
async function restorePending() {
  const pending = await idbGet(STORE_KEY);
  if (pending && Date.now() - pending.capturedAt < 30 * 60 * 1000) {
    showResumePrompt(pending); // "You had a photo in progress. Resume?"
  }
}
```

---

## 9. Monitoring and Alerting

### 9.1 Metrics Dashboard

| Metric | Type | Dimensions | Purpose |
|---|---|---|---|
| `cena.screenshot.analysis_latency_ms` | Histogram | `provider` | Track P50/P95/P99 latency per provider |
| `cena.screenshot.fallback_total` | Counter | `reason` | Distribution of fallback causes |
| `cena.screenshot.quality_gate_rejected` | Counter | `check` | Which quality checks reject most images |
| `cena.screenshot.success_rate` | Gauge | `provider` | Per-hour success rate per provider |
| `cena.llm.circuit_opened_total` | Counter | `model` | Circuit breaker trip frequency |
| `cena.llm.requests_rejected_total` | Counter | `model` | Requests blocked by open circuit |

### 9.2 SLO Targets

| Indicator | Target | Alert Threshold |
|---|---|---|
| Screenshot analysis success rate | > 95% over 1 hour | < 90% triggers P2 alert |
| P50 latency (happy path) | < 2 seconds | > 3 seconds triggers P3 alert |
| P95 latency (all paths) | < 5 seconds | > 8 seconds triggers P2 alert |
| P99 latency (all paths) | < 10 seconds | > 15 seconds triggers P1 alert |
| Fallback rate (Mathpix) | < 5% of requests | > 15% triggers P3 alert (Gemini degraded) |
| Manual fallback rate | < 1% of requests | > 5% triggers P2 alert (both APIs degraded) |

### 9.3 Alert Routing

| Severity | Condition | Action |
|---|---|---|
| P1 (page) | Success rate < 80% OR P99 > 15s | Page on-call engineer; auto-open circuit breaker for Gemini |
| P2 (urgent) | Success rate < 90% OR fallback rate > 15% | Slack alert to #cena-infra; investigate Gemini status |
| P3 (warning) | P50 > 3s OR fallback rate > 10% | Log for daily review; may indicate Gemini model update |

### 9.4 Grafana Dashboard Panels

1. **Success Rate Over Time** -- Time series, `cena.screenshot.success_rate` by provider
2. **Latency Heatmap** -- Heatmap, `cena.screenshot.analysis_latency_ms` buckets
3. **Fallback Distribution** -- Pie chart, `cena.screenshot.fallback_total` by reason
4. **Circuit Breaker State Timeline** -- State timeline, `cena.llm.circuit_opened_total` events
5. **Quality Gate Rejection Rate** -- Stacked bar, `cena.screenshot.quality_gate_rejected` by check
6. **Cost Tracking** -- Running total, estimated Gemini + Mathpix spend per day

---

## 10. Chaos Testing Strategy

### 10.1 Failure Injection Scenarios

Following the principles from Microsoft's chaos engineering guidance and the emerging practice of chaos engineering for AI pipelines, the following scenarios should be tested before production deployment.

Source: [Microsoft Azure Blog -- Advancing Resilience through Chaos Engineering](https://azure.microsoft.com/en-us/blog/advancing-resilience-through-chaos-engineering-and-fault-injection/)

| Scenario | Injection Method | Expected Behavior | Validation |
|---|---|---|---|
| Gemini API returns 500 | HTTP mock / WireMock | Retry twice, then fallback to Mathpix | Mathpix result returned; fallback counter incremented |
| Gemini API returns 429 | HTTP mock | Skip retries, immediate Mathpix fallback | No retry; rate_limited reason logged |
| Gemini response latency 10s | Network delay injection | Timeout after 8s, retry, fallback | Total latency < 15s |
| Gemini returns empty JSON | Response mock | Low confidence path; fallback to Mathpix | Handled without exception |
| Gemini returns malformed JSON | Response mock | `JsonException` caught; fallback to Mathpix | Error logged, no crash |
| Mathpix API down | DNS blackhole | Both APIs failed; manual fallback signal | Client shows "Type Instead" |
| Network partition during upload | tc netem / Chaos Studio | Client detects network error; image preserved in IDB | Image restored on reconnect |
| Gemini CB open + Mathpix slow | Combined injection | Skip Gemini (0ms), Mathpix within 5s | Total latency < 6s |
| Concurrent 100 students hit rate limit | Load test + mock 429s | Queue and backoff without thundering herd | Jitter prevents synchronized retries |

### 10.2 Integration Test Scaffolding

```csharp
// tests/Cena.Actors.Tests/Ingest/ScreenshotAnalyzerFallbackTests.cs

[Fact]
public async Task FallsBackToMathpix_WhenGeminiTimesOut()
{
    // Arrange
    var gemini = Substitute.For<IOcrClient>();
    gemini.ProcessPageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns<OcrPageOutput>(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30)); // Simulate hang
            throw new OperationCanceledException();
        });

    var mathpix = Substitute.For<IMathOcrClient>();
    mathpix.ExtractLatexAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
        .Returns("f(x) = x^2");

    var sut = new ScreenshotAnalyzerService(
        gemini, mathpix, new AlwaysAvailableCb(),
        NullLogger<ScreenshotAnalyzerService>.Instance,
        new TestMeterFactory());

    // Act
    var result = await sut.AnalyzeAsync(CreateRequest(), CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    result.ProviderUsed.Should().Be("mathpix");
    result.FallbackReason.Should().NotBeNull();
}

[Fact]
public async Task ReturnsManualFallback_WhenAllProvidersFail()
{
    // Arrange
    var gemini = Substitute.For<IOcrClient>();
    gemini.ProcessPageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("Service Unavailable"));

    var mathpix = Substitute.For<IMathOcrClient>();
    mathpix.ExtractLatexAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
        .ThrowsAsync(new HttpRequestException("Service Unavailable"));

    var sut = new ScreenshotAnalyzerService(
        gemini, mathpix, new AlwaysAvailableCb(),
        NullLogger<ScreenshotAnalyzerService>.Instance,
        new TestMeterFactory());

    // Act
    var result = await sut.AnalyzeAsync(CreateRequest(), CancellationToken.None);

    // Assert
    result.Success.Should().BeFalse();
    result.ProviderUsed.Should().Be("none");
    result.FallbackReason.Should().Be("all_providers_failed");
}
```

---

## 11. Security Score Contribution

This iteration contributes **12 points** to the cumulative Security Robustness Score.

| Control | Points | Rationale |
|---|---|---|
| Client-side quality gate prevents junk uploads reaching API | 2 | Reduces attack surface for adversarial images (iteration 01) |
| Circuit breaker prevents cascade failures | 3 | Single provider failure cannot bring down student sessions |
| Graceful degradation preserves student experience | 2 | No student is blocked from learning by transient failure |
| Retry with jitter prevents thundering herd | 2 | Rate limit attacks cannot synchronize retry storms |
| Progress preservation (IndexedDB) | 1 | No data loss on network failure or app crash |
| Monitoring and alerting | 1 | Rapid detection of sustained failures or attacks |
| Chaos testing validates resilience claims | 1 | Defense is proven, not assumed |

**Running total after iteration 09**: Sum of iterations 01-09 (security score is cumulative across the series).

---

## 12. Architecture Integration Points

### 12.1 Existing Cena Components Reused

| Component | Location | Role in Screenshot Analyzer |
|---|---|---|
| `RedisCircuitBreaker` | `src/actors/Cena.Actors/Infrastructure/RedisCircuitBreaker.cs` | Pattern reused for Gemini CB (same state machine) |
| `LlmCircuitBreakerActor` | `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | Manages per-model circuit state; extended with Gemini config |
| `HealthAggregatorActor` | `src/actors/Cena.Actors/Infrastructure/HealthAggregatorActor.cs` | Polls screenshot CB; triggers `SystemHealthLevel` transitions |
| `DegradationMode` | `src/actors/Cena.Actors/Infrastructure/DegradationMode.cs` | `ShouldUseFallbackQuestions()` activates when Gemini CB opens |
| `GeminiOcrClient` | `src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` | Primary Tier 1 provider; already handles JSON parse errors |
| `MathpixClient` | `src/actors/Cena.Actors/Ingest/MathpixClient.cs` | Tier 2 fallback; already integrated as `IMathOcrClient` |
| `IOcrClient` / `IMathOcrClient` | `src/actors/Cena.Actors/Ingest/IOcrClient.cs` | Clean abstractions enabling provider swapping |
| `IngestionOrchestrator` | `src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs` | Existing fault-tolerant pipeline; `SafeExtractContentAsync()` pattern reused |
| `GracefulShutdownCoordinator` | `src/actors/Cena.Actors/Infrastructure/GracefulShutdownCoordinator.cs` | 4-phase shutdown ensures in-flight screenshot analyses complete |
| `ErrorClassificationService` | `src/actors/Cena.Actors/Services/ErrorClassificationService.cs` | Pattern for LLM fallback with safe defaults |

### 12.2 New Components Required

| Component | Purpose |
|---|---|
| `ScreenshotAnalyzerService` | Orchestrates fallback hierarchy (Gemini -> Mathpix -> manual) |
| `useImageQualityGate.ts` | Client-side pre-processing quality checks |
| `screenshot.error.*` i18n keys | Trilingual error messages (EN/AR/HE) |
| `ScreenshotAnalyzerFallbackTests` | Chaos/fault-injection integration tests |

---

## 13. References

### Research and Best Practices

- [AWS Architecture Blog -- Exponential Backoff and Jitter](https://aws.amazon.com/blogs/architecture/exponential-backoff-and-jitter/) -- Retry strategies at scale
- [AWS Builders Library -- Timeouts, Retries, and Backoff with Jitter](https://aws.amazon.com/builders-library/timeouts-retries-and-backoff-with-jitter/) -- Production retry guidance
- [Polly Circuit Breaker Strategy Documentation](https://www.pollydocs.org/strategies/circuit-breaker.html) -- .NET resilience patterns
- [Microsoft .NET Blog -- Building Resilient Cloud Services with .NET 8](https://devblogs.microsoft.com/dotnet/building-resilient-cloud-services-with-dotnet-8/) -- Polly v8 integration
- [Microsoft Azure Blog -- Advancing Resilience through Chaos Engineering](https://azure.microsoft.com/en-us/blog/advancing-resilience-through-chaos-engineering-and-fault-injection/) -- Failure injection methodology
- [PyImageSearch -- Blur Detection with OpenCV (Laplacian variance)](https://pyimagesearch.com/2015/09/07/blur-detection-with-opencv/) -- Image quality assessment
- [Pencil & Paper -- Error Message UX](https://www.pencilandpaper.io/articles/ux-pattern-analysis-error-feedback) -- User-facing error design
- [Gemini API Rate Limits Documentation](https://ai.google.dev/gemini-api/docs/rate-limits) -- Current Gemini quotas and error codes
- [Tesseract.js Performance Documentation](https://github.com/naptha/tesseract.js/blob/master/docs/performance.md) -- Client-side OCR constraints
- [Srinivasa Rao Bittla -- Chaos Engineering for AI Pipelines](https://bittla.medium.com/chaos-engineering-for-ai-testing-pipeline-network-failures-b5f2546edb03) -- AI-specific resilience testing
- [Brandon Lincoln Hendricks -- Graceful Degradation for AI Agents Hitting Rate Limits](https://brandonlincolnhendricks.com/research/graceful-degradation-ai-agent-rate-limits) -- Production degradation patterns
- [ItSoli -- When AI Breaks: Building Degradation Strategies](https://itsoli.ai/when-ai-breaks-building-degradation-strategies-for-mission-critical-systems/) -- Tiered degradation architecture

### Cena Platform Internal References

- `RedisCircuitBreaker` -- INF-019 task; lightweight state machine for Redis protection
- `LlmCircuitBreakerActor` -- Per-model CB with configurable thresholds (Kimi 5/60s, Sonnet 3/90s, Opus 2/120s)
- `HealthAggregatorActor` -- RES-005; polls all CBs every 5s, computes `SystemHealthLevel`
- `DegradationMode` -- RES-006; maps health levels to feature toggles
- `IngestionOrchestrator.SafeExtractContentAsync()` -- Fault-tolerant wrapper pattern
- `GeminiOcrOptions.MinConfidenceForFallback` -- Configured at 0.7; triggers Mathpix fallback

---

*Next iteration (10/10): End-to-end proof -- attack simulation and defense verification across all 9 preceding security layers.*
