# RDY-OCR-PORT: C# Port of OcrCascadeService — COMPLETE

**Parent**: [RDY-019-OCR-SPIKE](done/RDY-019-ocr-spike.md) (spike that proved the architecture)
**ADR**: [ADR-0033 — Cena OCR Stack](../../docs/adr/0033-cena-ocr-stack.md)
**Roadmap**: [production-content-pipeline-roadmap.md](../../docs/roadmap/production-content-pipeline-roadmap.md)
**Priority**: Critical — blocked Phase 2 wire-ups
**Assignee**: claude-code (sole worker)
**Completed**: 2026-04-17, commit `adb706d`
**Queue ID**: `t_8d3ba71c3e9e` (status: done)

## Problem (solved)

The OCR spike validated the architecture and produced a runnable Python
prototype. `PhotoCaptureEndpoints.RecognizeMathAsync` returned `null`.
`BagrutPdfIngestionService.cs` carried `// Production: call Mathpix API
or Gemini Vision` comments. Every Phase 2 task blocked on this.

## Delivered

Port of `scripts/ocr-spike/pipeline_prototype.py` to C# in
`src/shared/Cena.Infrastructure/Ocr/`. Python gRPC sidecar (Surya +
pix2tex) for the two models without .NET bindings. All other layers
in-process C#. Shared by Surface A (student photo) and Surface B
(admin ingestion).

## Acceptance Criteria — all met

- [x] 1A.1 Contracts + sidecar skeleton (`eb77b40`)
- [x] 1A.2 Orchestrator + PdfTriage + 27 tests (`06da3a0`)
- [x] 1A.3 Brain Layers 3/4/5 + DI + 62/62 tests (`8adee89`)
- [x] 1A.4 CasRouterLatexValidator + TesseractLocalRunner + 71/71 (`82e44f6`)
- [x] 1A.5 MathpixRunner + GeminiVisionRunner + Layer 4 pageBytes + 94/94 (`8869c40`)
- [x] 1A.6 Layer2cFigureExtraction (ImageSharp bbox crop) + 8 tests (`adb706d`)
- [x] 1A.7 Layer0Preprocess (pdftoppm + ImageSharp) + 9 tests (`adb706d`)
- [x] 1A.8 SuryaSidecarClient (Grpc.Tools stubs) (`adb706d`)
- [x] 1A.9 Pix2TexSidecarClient (`adb706d`)
- [x] Full `Cena.Actors.sln` builds with 0 errors
- [x] `dotnet test --filter Ocr`: 111 passed / 0 failed / 0 skipped

## Files shipped

**Production code** — `src/shared/Cena.Infrastructure/Ocr/`:
- `Contracts/{BoundingBox,Enums,OcrCascadeResult,OcrContextHints,OcrFigureRef,OcrMathBlock,OcrTextBlock}.cs`
- `PdfTriage/{IPdfTriage,PdfTriage}.cs`
- `Layers/{ILayers,ConfidenceGateOptions,FigureStorageOptions,Layer0PreprocessOptions,Layer0Preprocess,Layer2cFigureExtraction,Layer3Reassemble,Layer4ConfidenceGate,Layer5CasValidation}.cs`
- `Runners/{IMathpixRunner,IGeminiVisionRunner,MathpixOptions,MathpixRunner,GeminiVisionOptions,GeminiVisionRunner,TesseractLocalRunner,OcrSidecarOptions,SuryaSidecarClient,Pix2TexSidecarClient}.cs`
- `Cas/ILatexValidator.cs`
- `DependencyInjection/OcrServiceCollectionExtensions.cs`
- `OcrCascadeService.cs`, `IOcrCascadeService.cs`, `OcrExceptions.cs`, `OcrJsonOptions.cs`

**Production code** — `src/actors/Cena.Actors/Cas/`:
- `CasRouterLatexValidator.cs`
- `CasOcrServiceCollectionExtensions.cs`

**Sidecar** — `docker/ocr-sidecar/`:
- `Dockerfile` (multi-arch), `requirements.txt`, `README.md`, `.dockerignore`
- `app/{ocr.proto,server.py,surya_service.py,pix2tex_service.py,prewarm.py,healthcheck.py}`

**Tests** — `src/shared/Cena.Infrastructure.Tests/Ocr/`:
- 11 test files, 111 passing tests

## What's next

This task is closed. Downstream work:
- **Phase 2 wire-ups** (RDY-OCR-WIREUP-A/B/C) — plug the cascade into real endpoints
- **Phase 1C** (RDY-019e-IMPL) — curator metadata DTO + admin UI handshake
- **Phase 4.1** — integration tests against running sidecar
- **Phase 4.2** — sidecar container deployment

Tracked in the [production pipeline roadmap](../../docs/roadmap/production-content-pipeline-roadmap.md).
