# Production Content Pipeline — Roadmap

**Scope**: finish Question Engine + OCR + Ingestion to production-grade.
**Owner**: claude-code (sole worker — kimi offline, claude-1 stalled).
**Directive**: NO MOCKS, NO STUBS in production code. Fail-fast DI. Tests may use NSubstitute / DelegatingHandler.
**Entry points**: [ADR-0033 — Cena OCR Stack](../adr/0033-cena-ocr-stack.md), [ADR-0002 — SymPy oracle](../adr/0002-sympy-correctness-oracle.md)

---

## 1. Executive summary

Spike complete, scaffold complete, **every OCR layer has a real implementation.**
RDY-OCR-PORT closed as of 2026-04-17 with commit `adb706d`. 111/111 OCR tests pass.
This doc is the single source of truth — update on every merge to main.

Phase 1A (C# OCR cascade) done. 10 slices remain across Phases 1B–4.

## 2. OCR layer scoreboard (as of commit `adb706d`)

| Layer | Implementation | Tests |
|---|---|---|
| `IPdfTriage` | ✅ PdfTriage (PdfPig) | ✅ 7 |
| `ILayer0Preprocess` | ✅ Layer0Preprocess (pdftoppm + ImageSharp) | ✅ 9 |
| `ILayer1Layout` | ✅ SuryaSidecarClient (gRPC) | — |
| `ILayer2aTextOcr` | ✅ TesseractLocalRunner (Process) | ✅ 9 |
| `ILayer2bMathOcr` | ✅ Pix2TexSidecarClient (gRPC) | — |
| `ILayer2cFigureExtraction` | ✅ Layer2cFigureExtraction (ImageSharp) | ✅ 8 |
| `ILayer3Reassemble` | ✅ Layer3Reassemble | ✅ 9 |
| `ILayer4ConfidenceGate` | ✅ Layer4ConfidenceGate (ImageSharp crop) | ✅ 13 |
| `ILayer5CasValidation` | ✅ Layer5CasValidation | ✅ 10 |
| `IOcrCascadeService` | ✅ OcrCascadeService orchestrator | ✅ 12 |
| `ILatexValidator` | ✅ CasRouterLatexValidator (wraps `ICasRouterService`) | (indirect) |
| `IMathpixRunner` | ✅ MathpixRunner (HTTP) | ✅ 11 |
| `IGeminiVisionRunner` | ✅ GeminiVisionRunner (HTTP) | ✅ 11 |

**Totals**: 13/13 real. 111/111 OCR tests. Zero production stubs.

gRPC client integration tests (Surya + pix2tex) require a running sidecar —
covered by Phase 4.1 end-to-end tests.

## 3. Shipped commits (origin/main)

Spike (RDY-019-OCR-SPIKE):
- `49eebe3` — spike scaffold (9 runners + prototype + 13 fixture tests)
- `c22f2e0` — dev-fixtures v1 (bagrut-only)
- `08ad219` — dev-fixtures v2 (full 4-category corpus, 182 PDFs triaged)
- `5f11002` — moved RDY-019-ocr-spike to done/

Port (RDY-OCR-PORT):
- `eb77b40` — scaffold contracts + sidecar skeleton + 13 tests
- `06da3a0` — orchestrator + PdfTriage + 14 tests (27 total)
- `8adee89` — brain Layers 3/4/5 + DI + 35 tests (62 total)
- `82e44f6` — CasRouterLatexValidator + TesseractLocalRunner + 9 tests (71 total)
- `8869c40` — MathpixRunner + GeminiVisionRunner + Layer 4 pageBytes + 22 tests (94 total)
- `adb706d` — Layer0 + Layer2c + SuryaSidecarClient + Pix2TexSidecarClient + 17 tests (111 total) **— RDY-OCR-PORT complete**

## 4. Four-phase plan

### Phase 1 — Foundation

#### Phase 1A — ✅ COMPLETE (RDY-OCR-PORT)

All 9 slices shipped. See §3 for commit list.

#### Phase 1B — CAS production-readiness (originally Kimi's; I take)

| # | Task | Queue ID | State |
|---|---|---|---|
| 1B.1 | CAS-GATE-TESTS — P5 unit+integration suite | `t_c8a44ae4f0b2` | ⬜ pending |
| 1B.2 | CAS-CONFORMANCE-RUNNER — xUnit runner for cas-conformance-baseline | `t_b952e9b9a7cb` | ⬜ pending |
| 1B.3 | CAS-GATE-SEED-REFACTOR — route seed+ingestion writers through QuestionBankService | `t_d995fe1da366` | ⬜ pending |
| 1B.4 | CAS-DEFERRED-OPS — k6 load + SIGKILL chaos + Grafana + k8s/compose | `t_8c82e49e06bc` | ⬜ pending |

#### Phase 1C — Curator metadata

| # | Slice | Queue ID | State |
|---|---|---|---|
| 1C.1 | RDY-019e-IMPL — OcrContextHints DTO + admin UI two-phase handshake | `t_f0e2f038a8a2` | ⬜ queued |

### Phase 2 — Wire OCR into real endpoints

| # | Slice | Target | State |
|---|---|---|---|
| 2.1 | PhotoCaptureEndpoints.RecognizeMathAsync → IOcrCascadeService | `src/api/Cena.Student.Api.Host/Endpoints/PhotoCaptureEndpoints.cs` | ⬜ next |
| 2.2 | PhotoUploadEndpoints + pdf_triage shortcut | `PhotoUploadEndpoints.cs` | ⬜ pending |
| 2.3 | Kill Gemini/Mathpix TODOs in admin ingestion trio | `BagrutPdfIngestionService.cs` + `IngestionPipelineService.cs` + `IngestionPipelineCloudDir.cs` | ⬜ pending |

### Phase 3 — Content flow

| # | Task | Queue ID | State |
|---|---|---|---|
| 3.1 | RDY-019a — Bagrut topic taxonomy + remap existing questions | `t_4cd083b88087` | ⬜ pending |
| 3.2 | RDY-019b — Ministry scrape + AI recreation (unblocked: cost now <$1) | `t_712e553197c9` | ⬜ pending |
| 3.3 | RDY-019c — 3u/4u seed + coverage report | `t_cf4918bfdff6` | ⬜ pending |
| 3.4 | Admin UI `pipeline.vue` + CuratorMetadata review screen | — | ⬜ not queued |

### Phase 4 — Production hardening

| # | Slice | State |
|---|---|---|
| 4.1 | Integration tests + frozen fixture regression (fail CI on > 5 pp WER/math drop) | ⬜ pending |
| 4.2 | OCR sidecar container + K8s manifest + HF pre-warm init-container | ⬜ pending |
| 4.3 | Observability — `[OCR_CASCADE]` metrics, log parsing, alerts (p99, fallback rate, CAS-fail, breaker state) | ⬜ pending |
| 4.4 | End-to-end load test (subsumes 1B.4) | ⬜ pending |

## 5. Architectural decisions

- **ADR-0033** — 8-layer cascade, τ=0.65, student catastrophic 0.30, admin catastrophic 0.40
- **ADR-0002** — SymPy CAS oracle; no math reaches students unverified
- **ADR-0003** — misconception data session-scoped, 30-day retention
- **bagrut-reference-only** — Ministry text never shipped; only AI-authored CAS-gated recreations
- **Process-based external tools** (tesseract + pdftoppm) — avoids P/Invoke platform matrix
- **gRPC sidecars** for Surya + pix2tex — HF models isolated from .NET host
- **SixLabors.ImageSharp 3.1.10+** for crop/preprocess (pure managed, cross-platform, past GHSA-2cmq-823j-5qj8)
- **No stub defaults in DI** — fail-fast on missing real impl

## 6. NO STUBS enforcement

1. `AddOcrCascadeCore()` registers only real impls. No NullLatexValidator, no placeholder layers.
2. `ILatexValidator` must be explicitly wired via `AddOcrCascadeWithCasValidation()` (Actors extension).
3. Runners throw `InvalidOperationException` at construction if creds/paths missing (MathpixRunner, GeminiVisionRunner, TesseractLocalRunner).
4. `OcrCircuitOpenException` is real degraded-mode behavior, not a stub.
5. Test mocks (NSubstitute, DelegatingHandler) are test infrastructure only — never in production DI container.

## 7. Composition-root wiring template

```csharp
// Host Program.cs (Student.Api.Host or Admin.Api)

services.AddOcrCascadeCore(builder.Configuration);     // Infrastructure side
services.AddOcrCascadeWithCasValidation();             // Actors side — wires ICasRouterService

// Wrapper layers (Infrastructure-side, host decides when to register)
services.AddSingleton<ILayer1Layout, SuryaSidecarClient>();
services.AddSingleton<ILayer2aTextOcr, TesseractLocalRunner>();
services.AddSingleton<ILayer2bMathOcr, Pix2TexSidecarClient>();

// Sidecar options (shared by SuryaSidecarClient + Pix2TexSidecarClient)
services.Configure<OcrSidecarOptions>(builder.Configuration.GetSection("Ocr:Sidecar"));

// Tesseract options
services.Configure<TesseractOptions>(builder.Configuration.GetSection("Ocr:Tesseract"));

// Cloud fallbacks — opt-in per credentials
if (!string.IsNullOrEmpty(cfg["Ocr:Mathpix:AppId"]))
{
    services.Configure<MathpixOptions>(cfg.GetSection("Ocr:Mathpix"));
    services.AddHttpClient<IMathpixRunner, MathpixRunner>()
        .AddPolicyHandler(HttpPolicies.StandardRetryPolicy)
        .AddPolicyHandler(HttpPolicies.CircuitBreakerPolicy);   // RDY-012
}
if (!string.IsNullOrEmpty(cfg["Ocr:Gemini:ApiKey"]))
{
    services.Configure<GeminiVisionOptions>(cfg.GetSection("Ocr:Gemini"));
    services.AddHttpClient<IGeminiVisionRunner, GeminiVisionRunner>()
        .AddPolicyHandler(HttpPolicies.StandardRetryPolicy)
        .AddPolicyHandler(HttpPolicies.CircuitBreakerPolicy);
}
```

## 8. Configuration schema (appsettings / K8s secret)

```json
{
  "Ocr": {
    "ConfidenceGate": {
      "ConfidenceThreshold": 0.65,
      "StudentCatastrophicThreshold": 0.30,
      "AdminCatastrophicThreshold": 0.40,
      "FallbackLabelTruncation": 20
    },
    "Layer0": {
      "PdftoppmBinaryPath": "pdftoppm",
      "DpiForRasterization": 300,
      "MaxLongEdgePixels": 2200,
      "ConvertToGrayscale": true,
      "PerPageTimeout": "00:00:30",
      "PdfRenderTimeout": "00:00:60",
      "MaxPdfBytes": 50000000
    },
    "FigureStorage": {
      "OutputDirectory": "/var/cena/ocr-figures",
      "MaxFigureBytes": 2000000
    },
    "Tesseract": {
      "TesseractBinaryPath": "tesseract",
      "DefaultLanguageCode": "heb+eng",
      "RequiredLanguagePacks": ["heb", "eng"],
      "PageSegMode": 3,
      "PerPageTimeout": "00:00:30"
    },
    "Sidecar": {
      "Address": "http://ocr-sidecar:50051",
      "RequestTimeout": "00:00:20",
      "IncludeFigures": true,
      "MaxRegionBytes": 8000000
    },
    "Mathpix": {
      "BaseUrl": "https://api.mathpix.com/v3/",
      "AppId": "<secret>",
      "AppKey": "<secret>",
      "RequestTimeout": "00:00:08",
      "MaxImageBytes": 8000000
    },
    "Gemini": {
      "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/",
      "Model": "gemini-1.5-flash",
      "ApiKey": "<secret>",
      "RequestTimeout": "00:00:10",
      "MaxImageBytes": 8000000
    }
  }
}
```

## 9. Resume checklist (read this first at session start)

1. `git log --oneline origin/main..HEAD` — anything unpushed?
2. `node .agentdb/kimi-queue.js list --status in_progress --worker claude-code` — anything mid-claim?
3. Check §4 phase tables — first unchecked row is where to resume.
4. `dotnet test src/shared/Cena.Infrastructure.Tests --filter Ocr` — must be ≥ 111 passing. Regression = previous commit broke something.
5. `git status` — any uncommitted WIP?

## 10. Slice template (reuse per slice)

1. Read interface + any existing tests.
2. Write the real implementation. No mocks in production paths.
3. Add unit tests with `DelegatingHandler` / Process harness / fixture files. Never `Substitute.For<I<the-interface-being-tested>>` — that would be testing nothing.
4. Build Infrastructure → Tests → full sln.
5. `dotnet test --filter Ocr`. Expect count to go UP only.
6. Commit with scoreboard update in the message. Push to origin/main.
7. Update this doc's phase table row state before the next slice.
