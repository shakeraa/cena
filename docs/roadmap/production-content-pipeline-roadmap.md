# Production Content Pipeline — Roadmap

**Scope**: finish Question Engine + OCR + Ingestion to production-grade.
**Owner**: claude-code (sole worker — kimi offline, claude-1 stalled).
**Directive**: NO MOCKS, NO STUBS in production code. Fail-fast DI. Tests may use NSubstitute / DelegatingHandler.
**Entry points**: [ADR-0033 — Cena OCR Stack](../adr/0033-cena-ocr-stack.md), [ADR-0002 — SymPy oracle](../adr/0002-sympy-correctness-oracle.md)

---

## 1. Executive summary

Spike complete, scaffold complete, **every OCR layer has a real implementation.**
RDY-OCR-PORT closed 2026-04-17 (`adb706d`). 111/111 OCR tests pass.
This doc is the single source of truth — update on every merge to main.

**Phase status (commit `3641516`, 2026-04-17)**:
- Phase 1A ✅ — C# OCR cascade complete
- Phase 1B ✅ — CAS production-readiness: all 4 slices landed
- Phase 1C ✅ — Curator metadata handshake live
- Phase 2  ✅ — Photo capture + upload + admin ingestion trio all on real IOcrCascadeService
- Phase 3  ✅ — 3.1 / 3.2 / 3.3 / 3.4 all done
- Phase 4  🚧 — 4.2 / 4.3 / 4.4 done; only 4.1 OCR regression harness remains (environment-gated on tesseract + poppler)

**Readiness-folder shipped the same session**:
  - RDY-017a (`170c00d`) — DLQ replay script CLI-version header,
    NatsDlqHealthCheck unit tests, admin system-health DLQ depth widget
  - RDY-025b (`da47899`) — Proto.Cluster.Kubernetes provider wiring +
    ServiceAccount/Role/RoleBinding Helm template + 12 factory tests
  - RDY-029 sub-task 1 (`fb1b4a4`) — CycloneDX SBOM workflow for 4 hosts
  - RDY-029 sub-task 5 (`1526e41`) — AdminActionAuditMiddleware:
    structured [AUDIT] log + AuditEventDocument for every admin write
  - RDY-030b sub-tasks 1–4 (`5e243d9` + `d5382a7` + `1056cc4`) —
    reduced-motion on 18 components, aria-live on 2 components,
    math-ltr probe fix, a11y CI flipped from advisory to blocking
  - RDY-032 (`3641516`) — pilot data exporter service + SuperAdmin
    endpoint + SHA-256 pseudonymization + ADR-0003 filtering +
    referential-integrity quality checks

**RDY-034 flow-state tri-slice**: ALL LANDED
  - slice 1 (`907ca6c`) — `FlowStateService` + `POST /api/sessions/flow-state/assess`
  - slice 2 (`8f87838`) — `GET /api/sessions/{id}` carries `FlowState` field
  - slice 3 (`f52066f`) — `LearningSessionActor` emits `[FLOW_STATE_TRANSITION]`

**RDY-019b recreation loop landed** (`870f6e0`): SuperAdmin-only
`POST /api/admin/content/recreate-from-reference`. Reads
`corpus/bagrut/reference/analysis.json` (produced by the existing Python
analyzer), plans per-cluster `AiGenerateRequest` bundles weighted by
the ministry reference distribution, drives the existing
`BatchGenerateAsync` (CAS-gated). NO new write paths.

**Next ship (code-reachable)**: none — every readiness task that doesn't
require cluster infra, pilot data, or human content work has landed.
**Environment-gated remainder**:
  - Phase 4.1 OCR regression harness — host-OS dependency **resolved**
    2026-04-22 via `docker-ocr-parity` (eb8b5740); still needs harness
    code + golden fixtures + new CI workflow (see §11)
  - RDY-025c deploy validation (needs kind/staging cluster)
  - RDY-024b BKT Phase B (needs pilot-completion data)
**Non-code remainder**: RDY-004b translation, RDY-005 legal docs,
RDY-019d Bagrut curriculum expert review (Amjad).
**Full open-points checkpoint**: see §11 "Open points — 2026-04-22".

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

#### Phase 1B — CAS production-readiness ✅ COMPLETE

| # | Task | Landed | State |
|---|---|---|---|
| 1B.1 | CAS-GATE-TESTS — P5 unit+integration suite (144 Actors + 24 Admin tests) | `fd4a2a0` | ✅ done |
| 1B.2 | CAS-CONFORMANCE-RUNNER — baseline.md parser + runner + artifact + CI | `378a493` | ✅ done |
| 1B.3 | CAS-GATE-SEED-REFACTOR — single gated write site via CasGatedQuestionPersister | (RDY-037) | ✅ done |
| 1B.4 | CAS-DEFERRED-OPS — k8s ConfigMap+Secret + compose env for CAS gate envs | `7b5b8d8` | ✅ done |

#### Phase 1C — Curator metadata ✅ COMPLETE

| # | Slice | Landed | State |
|---|---|---|---|
| 1C.1 | RDY-019e-IMPL — OcrContextHints DTO + admin UI two-phase handshake | `524ff28` + `d22af1c` | ✅ done |

### Phase 2 — Wire OCR into real endpoints ✅ COMPLETE

| # | Slice | Target | State |
|---|---|---|---|
| 2.1 | PhotoCaptureEndpoints.RecognizeMathAsync → IOcrCascadeService | `src/api/Cena.Student.Api.Host/Endpoints/PhotoCaptureEndpoints.cs` | ✅ done |
| 2.2 | PhotoUploadEndpoints + pdf_triage shortcut (Triage=Encrypted → 422) | `PhotoUploadEndpoints.cs` | ✅ done |
| 2.3 | Kill Gemini/Mathpix TODOs in admin ingestion trio — real IOcrCascadeService | `BagrutPdfIngestionService.cs` + `IngestionPipelineService.cs` + `IngestionPipelineCloudDir.cs` | ✅ done (S3 stub in CloudDir tracked separately as cloud-ops) |

### Phase 3 — Content flow

| # | Task | Landed / Queue | State |
|---|---|---|---|
| 3.1 | RDY-019a — Bagrut topic taxonomy + remap existing questions | taxonomy.json shipped | ✅ done |
| 3.2 | RDY-019b — Ministry scrape + AI recreation (unblocked: cost now <$1) | scraper scaffold `ba0fca6`; recreation loop + admin endpoint `870f6e0` | ✅ done |
| 3.3 | RDY-019c — 3u/4u seed + coverage report | `f442b0e` | ✅ done |
| 3.4 | Admin UI `pipeline.vue` + CuratorMetadata review screen | CuratorMetadataPanel `524ff28` + filter bar / stuck-item indicators / inline error previews `d65a802` | ✅ done |

### Phase 4 — Production hardening

| # | Slice | Landed | State |
|---|---|---|---|
| 4.1 | Integration tests + frozen fixture regression (fail CI on > 5 pp WER/math drop) | Host-OS deps landed `eb8b5740` (docker-ocr-parity) | 🟡 partial — harness code still TODO; see §11 item 1 |
| 4.2 | OCR sidecar container + K8s manifest + HF pre-warm init-container | `docker/ocr-sidecar/` + `k8s/ocr-sidecar/` + `docker-compose.ocr-sidecar.yml` | ✅ done |
| 4.3 | Observability — `[OCR_CASCADE]` metrics, Prometheus alerts, Grafana dashboard | `370276e` (OcrMetrics + alerts) + dashboard JSON on main | ✅ done |
| 4.4 | End-to-end load test (subsumes 1B.4) | `tests/load/e2e-student-journey.js` + `tests/load/e2e-admin-corpus.js` + `.github/workflows/e2e-load-nightly.yml` shipped `dc85875` | ✅ done |

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

## 11. Open points — 2026-04-22 checkpoint

Status at the end of the 2026-04-22 session:

**Shipped today**: `docker-ocr-parity` (commit `eb8b5740`). All 3 .NET
host images (Student / Admin / Actors) now have identical OCR toolchain
across `dev` + `runtime` stages — `tesseract 5.3.0` + `heb` + `eng` +
`poppler 22.12.0`. Dev stack runs on `:dev` tags. Prod runtime images
188–192 MB (under RDY-025c 300 MB budget). Phase 4.1 host-OS blocker
retired.

Five items remain. Ordered by decision-gate-nearness, not by size:

### 11.1 Phase 4.1 — OCR regression harness (CODE work, queued)

**Blocker retired**: OS binaries are present in all .NET hosts and will
be present in CI as soon as a new workflow file drops that uses the
existing Dockerfiles (or installs via apt in the runner).

**Still to do**:

- New test class `OcrEndToEndRegressionTests` in `Cena.Infrastructure.Tests/Ocr/`
- Real golden inputs under `tests/fixtures/ocr-regression/inputs/` — 10–20 PDFs/PNGs spanning 3u/4u/5u bagrut, Geva solutions, student photos, and edge cases (encrypted PDF, low-res scan)
- Golden expected outputs (`.expected.json`) per input — WER, math parse rate, block counts
- Assertion: fail CI if WER or math-parse-rate regresses > 5 pp on any fixture
- New workflow `.github/workflows/ocr-regression-e2e.yml` (separate from the existing fixture-contract `ocr-regression.yml`) — installs tesseract+poppler, starts OCR sidecar via docker-compose, runs the new test filter

**Effort**: ~1 dev-day. **Queue task**: see "Queued tasks" below.

### 11.2 S3 backend for IngestionPipelineCloudDir ✅ SHIPPED 2026-04-22 (Scope B)

**Shipped via commit `c46f9b79`** (merge `33d672a5`). User selected **Scope B** (code + ops handoff ADR) on 2026-04-22. 20 new unit tests passing; Admin.Api.Tests 1001/1001; OCR 142/142.

Architectural decisions locked in [ADR-0058](../adr/0058-ingestion-s3-provider.md):

- **IAM**: IRSA primary (EKS OIDC), static-key fallback for kind / dev / LocalStack.
- **Allowlist**: `Ingestion:S3:AllowedBuckets` (analog to `Ingestion:CloudWatchDirs`).
- **Dedup**: two-tier — ETag match at list-time, full SHA-256 at ingest-time. `PipelineItemDocument` gained optional `S3Bucket` + `S3ETag` (null on non-S3 rows, Marten-additive).
- **Abstraction**: `ICloudDirectoryProvider` + `LocalDirectoryProvider` + `S3DirectoryProvider` with `ICloudDirectoryProviderRegistry` dispatch.
- **Dev/prod parity**: LocalStack profile-gated docker-compose service; dev exercises the same code path prod does.
- **Batch gate**: 1000 files / 10 GiB via `Ingestion:MaxBatchFiles` + `Ingestion:MaxBatchBytes`.
- **Permissions**: `s3:ListBucket` + `s3:GetObject` only.

**Remaining work is pure-ops** (not engineering):

- Create S3 bucket + IAM role `cena-ingest-reader` per ADR-0058 Terraform snippet.
- Annotate the Admin.Api K8s ServiceAccount with the role ARN.
- Flip `ingestion.s3.enabled: true` + populate `allowedBuckets` in `values-production.yaml`.

**Deferred follow-up** (not blocking): Testcontainers-based LocalStack integration test. Manual dev verification today: `docker compose --profile localstack up -d localstack`, then POST `/api/admin/ingestion/cloud-dir/list` with `provider=s3 bucketOrPath=cena-ingest-dev`.

#### 11.2.x Pre-shipment analysis (audit trail)

**Original placeholder**: `src/api/Cena.Admin.Api/IngestionPipelineCloudDir.cs:35-42` returned empty results when `provider == "s3"`.

**Architectural decisions required before implementation**:

1. **IAM model** — IRSA (K8s-native, EKS OIDC provider + SA annotation, no secrets) vs static access keys in a K8s secret (simpler, rotation burden) vs STS AssumeRole (cross-account).
   *Prod-grade recommendation*: IRSA as primary, static-key as fallback for kind/MicroK8s clusters.
2. **Bucket allowlist** — `Ingestion:S3Buckets` config analog to existing `CloudWatchDirs`, so an admin can't type `prod-secrets-bucket` into the UI.
3. **Dedup identity** — do NOT GetObject-and-hash every file (network-heavy on large buckets). Store `bucket + key + etag` on `PipelineItemDocument.S3Source`; full SHA-256 only computed on actual ingest of selected files. This is a `PipelineItemDocument` **contract change**.
4. **Provider abstraction** — today's hardcoded `"s3"` / `"local"` string checks should become `ICloudDirectoryProvider` + `LocalDirectoryProvider` + `S3DirectoryProvider`, registered by config.
5. **Dev/prod parity** (recurring directive, locked 2026-04-22) — LocalStack as a docker-compose service so dev exercises S3-shaped code paths, not a different provider.
6. **Batch-size gate** — reject batches > 1000 files OR > 10 GB to bound egress cost.
7. **Read-only permissions** — `s3:ListBucket` + `s3:GetObject` only. Never write or delete.

**Three possible scopes** (historical — user picked B):

| Scope | What | Time | Output |
|---|---|---|---|
| **A** | Contract + LocalStack | ~1 dev-day | Provider abstraction, S3 impl against LocalStack, allowlist, tests, docker-compose LocalStack service. Code ready; awaits ops-side bucket + IAM to go live. |
| **B** (recommended) | A + ops handoff ADR | ~1 dev-day + 30 min | All of A, plus a short ADR spelling out the IRSA/static-key choice and the Helm values changes needed so ops has a one-pager to execute from. |
| **C** | Just the abstraction | 2 hours | Refactor to `ICloudDirectoryProvider`; defer S3 impl. |

**Effort**: 2 h / 1 day / 1 day + 30 min. User chose B; shipped in `c46f9b79`.

---

### 11.3 RDY-025c — Deploy validation (OPS-owned, not queued)

**Current state**: Helm chart passes `helm lint` + `kubeconform -strict` on all 3 overlays (18/18 valid for production overlay). See [deploy/LINT-VALIDATION.md](../../deploy/LINT-VALIDATION.md).

**Still to do** (all on a real cluster):

- `docker build` × 4 images, confirm size ≤ 300 MB each — **our 3 .NET host images are 188–192 MB after docker-ocr-parity, well under budget**
- HEALTHCHECK smoke inside each image
- `helm install cena ./deploy/helm/cena -f values-staging.yaml` on `kind` → verify Pods Running + Ready
- HPA scale-test (k6 load → confirm scale-up)
- PDB drain-test, ingress smoke
- Replace `image.tag: latest` with SHA-based tags in CI (anti-pattern flagged in [deploy/LINT-VALIDATION.md:67-88](../../deploy/LINT-VALIDATION.md#L67-L88))

**Effort**: ~2 dev-days once a cluster is available; ~1 week if ops needs to stand up staging.

### 11.4 RDY-024b — BKT Phase B calibration (DATA-gated, not queued)

**Current state**: Phase A (RDY-024) shipped the BKT framework with default priors. Phase B re-calibrates `p(learn)` / `p(slip)` / `p(guess)` per concept using real student attempts — design requires **≥ 200 attempts per concept** for statistical power.

**Dependency chain**: RDY-024 ✅ + RDY-032 (pilot data exporter, ✅ shipped `3641516`) → RDY-024b blocked on pilot completion.

**Effort**: ~1 dev-week, gated on 4–6 weeks of pilot usage.

### 11.5 Non-code trio (HUMAN / DOMAIN / LEGAL, not queued)

| Task | Who | Artifact | Effort |
|---|---|---|---|
| **RDY-004b** | Amjad + native-speaker translator | Hebrew/Arabic translation of seed question-bank corpus; two-translator peer review + glossary-anchored QA; machine translation explicitly banned ([docs/translations/drafts/rdy-004b-pilot-batch-1.json](../../docs/translations/drafts/rdy-004b-pilot-batch-1.json)) | 2–3 weeks per language |
| **RDY-005** | Legal counsel + DPO + Dr. Lior + Dr. Rami | DPA, Privacy Notice, COPPA, school SDPA, teacher consent, breach notification, subprocessor disclosure, AI transparency notice — 7 documents | **8–16 weeks, $15–30 K counsel cost** |
| **RDY-019d** | Amjad (math curriculum SME) | Review AI-recreated Bagrut items for curriculum alignment, difficulty calibration, dialect/terminology | ~2 weeks per track (3u/4u/5u) |

### Queued tasks (for future claim)

| Task ID | Title | Priority | Assignee |
|---|---|---|---|
| see queue `list --status pending` | Phase 4.1 OCR regression harness (§11.1) | medium | unassigned — any worker |
| see queue `list --status pending` | S3 backend for IngestionPipelineCloudDir (§11.2, scope A/B/C) | medium | unassigned — decision-gated |

### User-revisit reminder

User paused on 2026-04-22 at the end of the `docker-ocr-parity` merge and
said "I will get back to it." Decision gate on re-entry: **S3 scope A / B / C**.
Target revisit date: **2026-04-29** (1 week).
