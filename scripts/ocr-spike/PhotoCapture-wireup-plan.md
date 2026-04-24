# PhotoCapture OCR Wire-Up Plan

Concrete plan for replacing the `RecognizeMathAsync` placeholder in
[`PhotoCaptureEndpoints.cs`](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoCaptureEndpoints.cs)
and [`PhotoUploadEndpoints.cs`](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoUploadEndpoints.cs)
with the shared OCR cascade defined in [ADR-0033](../../docs/adr/0033-cena-ocr-stack.md).

This is a follow-up task spec, not part of the spike. Referenced from
RDY-019-OCR-SPIKE acceptance criteria bullet 9 ("Plan documented for replacing
the `PhotoCaptureEndpoints.RecognizeMathAsync` placeholder").

## 1. C# port of the cascade

New namespace: `Cena.Infrastructure.Ocr`.

```
src/shared/Cena.Infrastructure/Ocr/
├── IOcrCascadeService.cs       ← public interface
├── OcrCascadeService.cs        ← C# port of pipeline_prototype.py
├── OcrContextHints.cs          ← mirrors the Python dataclass
├── OcrCascadeResult.cs         ← mirrors CascadeResult
├── Layers/
│   ├── Layer0Preprocess.cs     ← OpenCVSharp
│   ├── Layer1SuryaLayout.cs    ← spawns Python subprocess OR uses Surya via gRPC sidecar
│   ├── Layer2aTextOcr.cs       ← Tesseract via Tesseract.Net wrapper
│   ├── Layer2bMathOcr.cs       ← pix2tex subprocess OR cloud Mathpix (via RDY-012)
│   ├── Layer3Reassemble.cs     ← pure C#
│   ├── Layer4ConfidenceGate.cs ← thresholding + fallback dispatch
│   └── Layer5CasValidation.cs  ← reuses existing `CasRouter` from ADR-0002 stack
└── Runners/
    ├── TesseractRunner.cs
    ├── MathpixRunner.cs        ← existing MathpixClient from RDY-012 wrapped
    ├── GeminiVisionRunner.cs   ← existing GeminiClient wrapped
    └── SuryaSidecarClient.cs   ← gRPC to a Python Surya service
```

**Python sidecar** for Surya / pix2tex: the Python tools don't have .NET
bindings. Deploy them as a small gRPC service (Python + grpcio) colocated
with the API pod. `SuryaSidecarClient` calls `RecognizeImage(bytes, hints)`
and gets back structured regions.

## 2. Interface

```csharp
public interface IOcrCascadeService
{
    Task<OcrCascadeResult> RecognizeAsync(
        ReadOnlyMemory<byte> bytes,
        string contentType,
        OcrContextHints? hints,
        CancellationToken ct);
}
```

`OcrContextHints` mirrors the Python record:

```csharp
public sealed record OcrContextHints(
    string? Subject,
    Language? Language,
    Track? Track,
    SourceType? SourceType,
    string? TaxonomyNode,
    bool? ExpectedFigures);
```

`OcrCascadeResult`:

```csharp
public sealed record OcrCascadeResult(
    IReadOnlyList<OcrTextBlock> TextBlocks,
    IReadOnlyList<OcrMathBlock> MathBlocks,
    IReadOnlyList<OcrFigureRef> Figures,
    double OverallConfidence,
    IReadOnlyList<string> FallbacksFired,
    int CasValidatedMath,
    int CasFailedMath,
    bool HumanReviewRequired,
    IReadOnlyList<string> ReasonsForReview,
    TimeSpan TotalLatency);
```

## 3. Wire-up — PhotoCaptureEndpoints.cs

Current (placeholder):

```csharp
var (recognizedLatex, confidence) = await RecognizeMathAsync(imageBytes, ct);
// RecognizeMathAsync returns (null, 0.0) today
```

After:

```csharp
var hints = new OcrContextHints(
    Subject: "math",
    Language: null,                          // auto-detect
    Track: null,                             // unknown for student uploads
    SourceType: SourceType.StudentPhoto,
    TaxonomyNode: null,
    ExpectedFigures: null);

OcrCascadeResult ocr;
try
{
    ocr = await cascade.RecognizeAsync(imageBytes, file.ContentType, hints, ct);
}
catch (OcrCircuitOpenException)
{
    // RDY-012 circuit breaker tripped on both Mathpix and Gemini
    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}

if (ocr.HumanReviewRequired && ocr.ReasonsForReview.Contains("low_overall_confidence"))
{
    // Surface A: tell the student to re-photograph
    return Results.UnprocessableEntity(new {
        error = "low_confidence",
        message = "Could not read the math clearly. Try a new photo.",
    });
}

var recognizedLatex = ocr.MathBlocks
    .Where(m => m.SympyParsed)              // ADR-0002 — only CAS-validated math returned
    .Select(m => m.Latex)
    .ToList();

var response = new PhotoCaptureResponse(
    RecognizedLatex: recognizedLatex,
    RecognizedText: string.Join("\n", ocr.TextBlocks.Select(b => b.Text)),
    OverallConfidence: ocr.OverallConfidence,
    FallbacksFired: ocr.FallbacksFired,
    BoundingBoxes: ocr.TextBlocks.Select(b => b.BoundingBox).ToList());
return Results.Ok(response);
```

### Moderation ordering (unchanged)

PhotoDNA / `IContentModerationPipeline.ModerateAsync` continues to run
**before** the cascade — same ordering as today. CSAM-flagged content never
enters the OCR cascade. This is Layer A0 in the spike taxonomy and is owned
by RDY-001 / PP-001, not this work.

## 4. Wire-up — PhotoUploadEndpoints.cs

Identical call shape. The only difference:

- Upload can be a PDF. Before calling the cascade, invoke the PDF triage
  classifier. If `triage == PdfType.Text`, extract via `pypdf` (new C# call
  site wrapping iText / PdfPig) and skip the OCR layer entirely.
- If `triage == PdfType.Encrypted`, return 422 "encrypted PDF — please upload
  an unlocked copy".

## 5. DI registration

`Program.cs` in `Cena.Student.Api.Host`:

```csharp
builder.Services.AddSingleton<IOcrCascadeService, OcrCascadeService>();
builder.Services.AddSingleton<SuryaSidecarClient>();
builder.Services.AddHttpClient<MathpixRunner>(c => c.BaseAddress = new Uri(mathpixUri))
    .AddPolicyHandler(HttpPolicies.StandardRetryPolicy)
    .AddPolicyHandler(HttpPolicies.CircuitBreakerPolicy);  // RDY-012
// (same for GeminiVisionRunner)
```

## 6. Test plan

### Unit

- `Layer0Preprocess` — deskew, binarize, resize shortcuts on golden images.
- `Layer3Reassemble` — RTL stitching on mixed Hebrew+math input.
- `Layer5CasValidation` — rejects ungrammatical LaTeX, accepts valid forms.

### Integration

- Feed `fixtures/student_photos/*.jpg` (synthesized in the spike) through the
  full cascade; assert overall confidence > 0.7 for 8/10, math recall > 0.8.
- Feed the 5 mixed PDFs through `PhotoUploadEndpoints`; assert routing:
  `text` → pypdf shortcut, `image_only` → full cascade, `encrypted` → 422.
- Feed a page from `fixtures/bagrut/*.pdf` as a PDF upload; assert full-page
  text extraction ≥ 80 % Hebrew.

### Regression fixture

Check in a small frozen set (4 student photos + 4 Bagrut pages) under
`tests/fixtures/ocr-regression/`. CI runs the cascade on every push and
fails if average WER or math equivalence drops more than 5 pp.

## 7. Sequencing

1. Port Python cascade → C# `OcrCascadeService` (most of the work).
2. Stand up the Python sidecar (Surya + pix2tex + gRPC).
3. Wire `PhotoCaptureEndpoints.RecognizeMathAsync` — replace placeholder.
4. Wire `PhotoUploadEndpoints` — add PDF triage shortcut.
5. Wire `BagrutPdfIngestionService.cs` + `IngestionPipelineService.cs` to the
   same service (Surface B). Saves duplicating the whole cascade in admin.
6. Wire `IngestionPipelineCloudDir.cs` — B3 reads cloud-directory PDFs through
   the same cascade, with batch-throughput scoring (surface="B").
7. Remove placeholder return paths and any direct Gemini/Mathpix calls from
   endpoint code — all cloud calls flow through the cascade's Layer 4 gates.

Estimated effort (post-spike): 4–6 days for the port + sidecar, 1 day each
for endpoints + admin-side wiring. Sequenced so Surface A ships first (it's
the more visible win).
