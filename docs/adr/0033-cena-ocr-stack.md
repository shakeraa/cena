# ADR-0033 — Cena OCR Stack: Confidence-Gated Offline-First Cascade

**Status**: Proposed (2026-04-16) — awaiting full benchmark pass before moving to Accepted
**Deciders**: claude-code (spike author), @owner (final approval)
**Consulted**: RDY-019 (Bagrut ingestion), RDY-019b (Ministry scrape), RDY-012 (circuit breakers), ADR-0002 (SymPy oracle), ADR-0003 (misconception session scope)
**Supersedes**: (none)

## Context

Cena has two surfaces that need to recognise math text from images or PDFs:

1. **Surface A — Student photo / PDF upload** ([`PhotoCaptureEndpoints.cs`](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoCaptureEndpoints.cs), [`PhotoUploadEndpoints.cs`](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoUploadEndpoints.cs)).
   A student photographs a homework problem (or uploads a PDF) and the app must
   return the recognised question so the tutor can step-solve it. Today,
   `RecognizeMathAsync` returns `null` — a placeholder.

2. **Surface B — Admin ingestion** ([`BagrutPdfIngestionService.cs`](../../src/api/Cena.Admin.Api/Ingestion/BagrutPdfIngestionService.cs), [`IngestionPipelineService.UploadFromRequestAsync`](../../src/api/Cena.Admin.Api/IngestionPipelineService.cs), [`IngestionPipelineCloudDir.cs`](../../src/api/Cena.Admin.Api/IngestionPipelineCloudDir.cs)).
   Batch Bagrut scrape (RDY-019b — 640 public Ministry PDFs), interactive admin
   uploads, and cloud-directory drop-zone. Output feeds `QuestionDocument`
   drafts for curator review, not a live student response.

Prior design (draft RDY-019b) assumed a cloud-first path: Gemini Vision for
text + Mathpix for math, with offline as a stretch. That required always-on
API keys and a cost model of ~$4 per 1k Bagrut pages.

The spike ([RDY-019-OCR-SPIKE](../../tasks/readiness/RDY-019-ocr-spike.md)) was
asked to answer two questions:

1. What offline-first stack can replace cloud dependencies without sacrificing
   accuracy?
2. What confidence threshold τ cleanly separates "offline is enough" from
   "need cloud fallback", and how often will we hit τ?

## Decision

### Architecture: confidence-gated multi-layer cascade, shared across surfaces

```
Layer 0   Preprocess              deskew / adaptive threshold / selective denoise (OpenCV)
                                  PDFs: run pdf_triage first
                                  If triage == "text" → extract layer, skip Layers 1–3
                                  Else rasterize → continue

Layer 1   Layout detection        Surya → {text_blocks, math_regions, figures, tables}
                                  (degraded mode: whole page → text region)

Layer 2a  Hebrew text OCR         Surya (primary; MPS on Apple Silicon)
                                  Tesseract 5 + heb.traineddata (fallback)
Layer 2b  Math OCR                pix2tex on math_region crops
Layer 2c  Figure extraction       crop bbox from Layer 1, stash as image refs

Layer 3   Reassembly              RTL-aware reading-order stitch
                                  (Hebrew row → right-to-left, math inline)

Layer 4   Confidence gate         per-region conf < τ=0.65 →
          4a math:                Mathpix (region-scoped, circuit-breaker via RDY-012)
          4b text:                Gemini Vision (block-scoped, circuit-breaker)
          4c catastrophic:        page conf < 0.3 →
                                    Surface A: return 422 "could not recognize"
                                    Surface B: flag whole page to human-review queue

Layer 5   CAS validation          parsed LaTeX → SymPy → CAS-gate (ADR-0002)
                                  math that doesn't round-trip is rejected
```

### Per-layer tool choice

| Layer | Primary | Fallback | Rationale |
|-------|---------|----------|-----------|
| 0 | OpenCV preprocess + `pdf_triage.py` | — | Free, <500 ms per page. Triage routes 90 % of Bagrut PDFs away from OCR. |
| 1 | Surya layout | Single-region degraded mode | Apache-2.0, MPS-capable, emits formula/figure/text separation. |
| 2a | Surya OCR (heb) | Tesseract 5 (heb.traineddata) | Surya > Tesseract on skewed photos; Tesseract is the rock-solid baseline. |
| 2b | pix2tex | Mathpix via Layer 4a | Purpose-built math→LaTeX; small model, fast on CPU. |
| 2c | Crop + OpenCV filter | — | No ML — just bbox extraction. |
| 3 | Custom RTL reassembler | — | Tool-agnostic; lives in the cascade service. |
| 4a | Mathpix | Gemini/Claude Vision | Region-scoped, cheap because low-conf regions are <10 % of corpus. |
| 4b | Gemini 2.x Vision | Claude 3.5 Sonnet Vision | Block-scoped. Already wired through RDY-012 circuit breakers. |
| 5 | SymPy parse_latex | — | Non-negotiable oracle per ADR-0002. |

### Confidence threshold τ

**Initial τ = 0.65** — preliminary, derived from Tesseract-only data on one
real Bagrut page. Before production rollout, τ will be re-derived from the
full benchmark fixture distribution (10 Bagrut pages × 9 runners × per-region
confidence histograms).

### Cost + latency projection

**Surface B — 640-page Bagrut scrape:**

| Path | % of pages | Cost per page | Cost total |
|------|------------|---------------|------------|
| Text-layer extraction (Layer 0 shortcut) | ~90 % (576 pages) | $0 | $0 |
| Full cascade, offline-only clears gate | ~8 % (50 pages) | $0 | $0 |
| Full cascade, Layer 4 fallback fires | ~2 % (14 pages) | ~$0.004 | ~$0.06 |
| **Total projected Mathpix+Gemini spend** | | | **< $1** |

Compare to the cloud-first draft (Mathpix + Gemini on every page): ~$4 per 1k
pages × 640 pages = ~$2.56. Offline-first shortcut cuts that **by 40×**, but
the real win is removing the API-key *dependency* — the scrape no longer
blocks on provisioning Mathpix/Gemini credentials.

**Surface A — single student photo:**

| Path | Typical latency (M1 Max) | Cost |
|------|--------------------------|------|
| Offline-only (all layers local) | 2.5–4 s | $0 |
| Fallback fires on one math region | +0.8 s (Mathpix) | ~$0.004 |
| Catastrophic 422 | 1.5 s | $0 |

Target <3 s holds for clean photos; noisy photos hit fallback and land at
~4 s — acceptable tail with a clear UX signal ("rescanning math…").

### Surface A is the hard path, not Surface B

A late finding reshaped the priority: 9 of 10 real Bagrut PDFs already ship
with clean Hebrew text layers (80–87 % Hebrew chars, 0 % gibberish per
`pdf_triage.py`). **The scrape's *default* path is text-layer extraction, not
OCR.** OCR is the exception, fired only for image-only / mixed / scanned-bad
PDFs (~10 %).

Surface A has no such shortcut — every photo must run the full cascade — so
tool choices should be evaluated primarily against photo performance, not
scan performance. The spike's fixture set deliberately includes synthetic
student-photo augmentations (perspective warp, JPEG compression, non-uniform
lighting) for exactly this reason.

## Alternatives considered

### Cloud-only (Gemini + Mathpix on every input)

- **Pro**: simplest implementation, proven multilingual+math quality.
- **Con**: always-on API keys on the critical path. Monthly cost grows
  linearly with students. Student photos leave the machine — avoidable
  privacy exposure even though PhotoDNA is upstream.
- **Verdict**: rejected. Offline is a design principle, not a cost optimisation.

### Single tool — Marker (PDF → Markdown)

- **Pro**: one dep, produces text+math+layout in a single call.
- **Con**: closed-box confidence — no per-region scoring means we can't gate
  fallbacks, and no way to know *which* math blocks to re-run through the
  SymPy oracle. Marker inherits Surya's Hebrew — so we'd be shipping the
  same underlying text OCR anyway, minus the layering and control.
- **Verdict**: rejected for production. Surya (Marker's own dependency) is
  kept in the cascade so we control layer boundaries.

### olmOCR (AllenAI, 2025) as sole tool

- **Pro**: end-to-end VLM, single call.
- **Con**: 7 B weights (14 GB on disk, 16 GB MPS memory); Hebrew is not a
  training language. Memory overhead is a production liability.
- **Verdict**: evaluated in benchmark as fallback candidate at Layer 4b.

### Run Layer 4 always (hybrid)

- **Pro**: simplest composition.
- **Con**: breaks cost model and privacy model — every photo sent to cloud.
- **Verdict**: rejected. Confidence gate at τ=0.65 is what we need.

## Consequences

### Positive

- Cena owns the full math pipeline — no critical-path dependency on external vendor SLAs.
- Unified cascade for Surface A and B halves maintenance cost and means
  improvements benefit both.
- Text-layer shortcut means RDY-019b's 640-page scrape can proceed *without
  Mathpix/Gemini provisioning* — unblocks Wave-3 content ingestion.
- ADR-0002's SymPy CAS oracle remains the final gate. Bad OCR never becomes bad content.

### Negative / risks

- First-run install downloads ~4 GB of HF models. Must be warmed during CI
  bootstrap and cached per-agent.
- Apple Silicon MPS backend is stable but individual model paths have known
  edge cases (PaddleOCR in particular); the cascade must cleanly degrade when
  any single runner fails.
- pix2tex is the weakest math-only link on handwriting. Tracked as
  follow-up: a fine-tune pass on student-handwritten math may be worth it
  once we have telemetry on which regions most often trigger Layer 4a.

### Follow-up work

1. **Implement** — port the Python prototype to C# service
   (`src/infrastructure/ocr/OcrCascadeService.cs`) and wire into
   `PhotoCaptureEndpoints.RecognizeMathAsync` + `PhotoUploadEndpoints`.
2. **Calibrate τ** — run `benchmark.py` with all 9 runners on the full fixture
   set, derive τ from the 95th-percentile confidence separating TP from FP
   extractions; update this ADR from Proposed to Accepted.
3. **Stand up RDY-019e** — the `CuratorMetadata` upstream of the cascade's
   hints. The cascade already consumes hints via `OcrContextHints`; the admin
   UI needs to produce them.
4. **Tag low-confidence math for curation** — any Layer 4a fallback in the
   Bagrut scrape becomes a curator-review item; those items are *not* shown
   to students until reviewed.
5. **CI regression suite** — freeze a fixture subset and fail CI if WER /
   math-equivalence drops more than 5 % between releases.

## References

- [Spike task body](../../tasks/readiness/RDY-019-ocr-spike.md)
- [Spike results](../../scripts/ocr-spike/results.md)
- [Spike code](../../scripts/ocr-spike/)
- [RDY-019b — Ministry scrape](../../tasks/readiness/RDY-019b-ministry-reference-scrape-recreation.md)
- [RDY-012 — HTTP circuit breakers](../../tasks/readiness/RDY-012-http-client-circuit-breakers.md)
- [ADR-0002 — SymPy correctness oracle](0002-sympy-correctness-oracle.md)
- [ADR-0003 — Misconception session scope](0003-misconception-session-scope.md)
- Memory: `project_bagrut_reference_only.md` (2026-04-15)
