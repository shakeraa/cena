# RDY-019-OCR-SPIKE: Offline OCR Evaluation for Student Photo/PDF + Bagrut Scrape

**Parent**: [RDY-019b](RDY-019b-ministry-reference-scrape-recreation.md) (admin-side) and [PhotoCaptureEndpoints / PhotoUploadEndpoints](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoCaptureEndpoints.cs) (student-side)
**Priority**: High — blocks both RDY-019b cost/privacy posture AND the student photo/PDF-upload feature's OCR layer (currently a placeholder)
**Complexity**: Mid engineer
**Effort**: 5–7 days (expanded — two surfaces, PDF variants, multi-layer benchmark)
**Blocker status**: None

## Why this spike exists — two OCR surfaces, one stack

Cena has two pipelines that need to extract math text from images/PDFs. They guard different risks but benefit from sharing the OCR layer.

### Surface A — Student photo / PDF upload (user-generated content)

Current code: [`PhotoCaptureEndpoints.cs`](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoCaptureEndpoints.cs) and [`PhotoUploadEndpoints.cs`](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoUploadEndpoints.cs).

A student photographs a homework question (or uploads a PDF containing either selectable text, embedded images, or a mix) and the app returns the parsed question so the tutor can solve it. Pipeline as implemented / planned:

```
upload (image or PDF)
  ↓
Layer A0  IContentModerationPipeline.ModerateAsync   (CSAM via PhotoDNA + safety — RDY-001/PP-001)
  ↓ Verdict == CsamDetected → 403, SIEM log, NCMEC incident
  ↓ Verdict == Clean
Layer A1  PDF triage (new — see §3)
           - text-PDF  → extract text directly via pdfplumber/pypdf, skip OCR
           - image-PDF → rasterize pages, feed to OCR cascade
           - photo     → feed to OCR cascade
  ↓
Layer A2..A5   OCR CASCADE (see §2) — shared with Surface B
  ↓
Layer A6  LaTeXSanitizer.Sanitize   (LATEX-001)
  ↓
Layer A7  CAS round-trip            (ADR-0002 — SymPy validates the recognized math)
  ↓
return { RecognizedLatex, Confidence, BoundingBoxes }
```

PhotoDNA sits at Layer A0 — **upstream** of OCR, never bypassed. CSAM never reaches the OCR layer.

Today `RecognizeMathAsync` in `PhotoCaptureEndpoints.cs` returns `null` (placeholder). The spike picks the real OCR stack to wire behind it.

### Surface B — Admin ingestion (public / curator-sourced material)

Admin has three ingestion modes that share the OCR cascade:

- **B1 — Batch Bagrut scrape** (covered by [RDY-019b](RDY-019b-ministry-reference-scrape-recreation.md)). Batch-processes 640 public Ministry PDFs from `meyda.education.gov.il/sheeloney_bagrut/` to extract structural metadata. No CSAM gate — public government publication. Throughput > latency.
- **B2 — Interactive admin upload** (existing [`BagrutPdfIngestionService.cs`](../../src/api/Cena.Admin.Api/Ingestion/BagrutPdfIngestionService.cs) + [`IngestionPipelineService.UploadFromRequestAsync`](../../src/api/Cena.Admin.Api/IngestionPipelineService.cs)). A curator uploads a PDF or image in the admin UI and expects per-file feedback. Latency matters. No CSAM gate required (admin-authenticated) but safety moderation still runs.
- **B3 — Cloud-directory drop-zone** (existing [`IngestionPipelineCloudDir.cs`](../../src/api/Cena.Admin.Api/IngestionPipelineCloudDir.cs)). Admin points the ingester at an S3/GCS/Azure/local path, lists files, and batch-queues them. Today `CloudDirIngestRequest` has no per-file metadata fields — the curator metadata gap is tracked separately in [RDY-019e](RDY-019e-curator-ingestion-metadata.md). The OCR cascade itself treats B3 the same as B1 (batch throughput), and consumes whatever hints RDY-019e eventually surfaces.

OCR cascade for B1, B2, and B3 is identical to Surface A Layers A2–A5 — what differs is throughput tuning and that the output feeds `QuestionDocument` drafts for curator review, not a live student response.

### Why unified

Same Hebrew+math+figures input; same extraction needs. Picking one cascade saves maintenance and means fixes benefit both surfaces.

## Scope

### 1. Evaluate candidate tools (2026 landscape)

Run each against a **fixture set of 10 real Bagrut pages + 10 real student-homework photos + 5 mixed-content PDFs** (stored locally under `corpus/ocr-fixtures/`, git-ignored per the `bagrut-reference-only` memory; student-photo fixtures sourced from internal test accounts, never real user data).

Offline / self-hosted (primary candidates):

| Tool | What it does | Hebrew | Math | Apple-Silicon |
|------|--------------|--------|------|---------------|
| Surya (VikParuchuri) | Layout detection + OCR, 90+ langs | `heb` confirmed | partial | MPS |
| Marker (same author) | PDF → Markdown, uses Surya + formula models | yes | strong | MPS |
| olmOCR (AllenAI, 2025) | Qwen2-VL-7B finetune, academic docs | uncertain — test | strong | MPS (7B fits M3 Max) |
| MinerU (OpenDataLab, 2024) | Layout + OCR + formula unified | multilingual | strong | yes |
| Tesseract 5 | Classic OCR, mature Hebrew trained data | `heb.traineddata` | weak | CPU |
| pix2tex / LaTeX-OCR | Math regions → LaTeX | N/A | strong | yes |
| PaddleOCR + PP-FormulaNet-L | OCR + formula plugin | patchy | strong | partial |
| Nougat (Meta, 2023) | Paper → Markdown+LaTeX | English-dominant | strong | yes |
| docTR (Mindee) | Multilingual OCR, text-focused | yes | weak | yes |

Cloud (fallback-only — keep offline-first, already wired via circuit breakers in RDY-012):

| API | Strength | Approx cost per 1k pages |
|-----|----------|---------------------------|
| Mathpix | Best math OCR; Hebrew limited | ~$4 |
| Gemini 2.x Vision | Strong multilingual incl. Hebrew + math | ~$0.30–$2 |
| Claude 3.5 Sonnet Vision | Strong on mixed text+math reasoning | ~$3 |
| Azure Document Intelligence | Layout + tables | ~$1 |

Cost is not the driver at these volumes. The drivers are: (1) latency for Surface A (target <3s single page), (2) PhotoDNA already keeps images local briefly and we don't want to push them to cloud unnecessarily, (3) removing always-on API-key dependency from Surface B.

### 2. Evaluation rig (how we check)

`scripts/ocr-spike/benchmark.py` runs every tool on the same fixtures and emits `results.json`. Per page, per tool, measure:

| Metric | Method |
|--------|--------|
| Hebrew text WER | `jiwer.wer(ground_truth_hebrew, extracted_text)` — ground truth hand-transcribed once |
| Math equivalence | Parse LaTeX → SymPy expr; compare via `sympy.simplify(a - b) == 0` (beats string match on whitespace/ordering) |
| Layout preservation | Manual 0–3: column order, question ↔ figure attachment, answer-option grouping |
| Figure extraction recall | `detected_figures / ground_truth_figures` |
| Latency per page (Surface A) | Wall clock on M-series, single-image path |
| Throughput (Surface B) | Pages/minute, batch of 50 |
| Setup friction | 0–3: `pip install` (0) → HF download (1) → custom CUDA (3) |
| RTL handling | Did bidi Hebrew+math round-trip correctly? |

Scoring weights: WER 0.25, math 0.30, layout 0.20, figures 0.10, latency-or-throughput 0.10, setup 0.05.

Outputs: `scripts/ocr-spike/results.{json,md}` + `scripts/ocr-spike/fixtures/ground_truth/` (hand-transcribed).

### 3. PDF handling (required — current code does not distinguish text vs image PDFs)

Before dispatching to OCR, PDFs must be triaged:

| PDF type | Detection | Path |
|----------|-----------|------|
| Text PDF | `pypdf` / `pdfplumber` extracts non-empty text layer | Use extracted text + `pix2tex` on any math-image spans — no full OCR |
| Image-only PDF | Text layer empty on all pages | Rasterize at 300 DPI → OCR cascade |
| Mixed (text + embedded images) | Text layer present but images contain math | Extract text layer; crop image regions; OCR only those |
| Encrypted / scanned-with-OCR'd-layer | Detect via pypdf flags | Prefer rasterize + fresh OCR — embedded OCR layers are often wrong for Hebrew math |

Deliverable: `scripts/ocr-spike/pdf_triage.py` — a standalone function returning one of the four types above, benchmarked on the 5 mixed-content PDF fixtures.

### 4. Multi-layer cascade (the recommended shape)

Single-tool approaches always fail on *something*. The practical design is a confidence-gated cascade, identical for both surfaces:

```text
Layer 0  Preprocess          deskew / denoise / binarize (OpenCV)
                             for PDFs: run PDF triage (§3) first
Layer 1  Layout detection    Surya → {text_blocks, math_regions, figures, tables}
Layer 2a Hebrew text OCR     Tesseract 5 on text_blocks only    ┐
Layer 2b Math OCR            pix2tex on math_regions             ├→ parallel
Layer 2c Figure extraction   crop diagrams, save as image refs  ┘
Layer 3  Reassembly          reading-order stitch (RTL-aware, right-to-left column flow)
Layer 4  Confidence gate     each region scores [0,1]; if < τ →
         Layer 4a (math):    Mathpix on that region only
         Layer 4b (text):    Gemini Vision on that block only
         Layer 4c (catastrophic): flag whole page to human-review queue (Surface B)
                                   OR return 422 "could not recognize" (Surface A)
Layer 5  CAS validation      parsed LaTeX → SymPy → CAS-gate (ADR-0002)
                             math that can't round-trip through SymPy = rejected
```

Why this layout:

- Cheap offline tools handle the easy 80% — Tesseract and pix2tex run free and fast (<1s per page on M-series).
- Cloud calls only on low-confidence regions — 10×+ cheaper than cloud-only.
- Every layer has a kill-switch — drop Mathpix or Gemini if budget/privacy shifts.
- The SymPy CAS gate at the end catches OCR'd nonsense — a misread `3x+2=5` vs `3x+2=6` never reaches students (ADR-0002 oracle).
- **Surface A's PhotoDNA stays at Layer A0 — upstream of this whole cascade. CSAM never touches OCR.**

### 5. Two concrete questions the spike must answer

1. What offline-confidence threshold τ cleanly separates "Tesseract/pix2tex is enough" from "need cloud fallback"? — derived from the fixture benchmark distribution.
2. At that τ, what percentage of pages (Surface B batch) and single uploads (Surface A live) need cloud fallback, and what's the projected cost + latency?

### 6. Deliverables

- `scripts/ocr-spike/benchmark.py` — runs all tools on the fixture set
- `scripts/ocr-spike/pdf_triage.py` — text vs image PDF classifier
- `scripts/ocr-spike/fixtures/` — 10 Bagrut pages + 10 student-photo simulations + 5 mixed-content PDFs + hand-transcribed ground truth (git-ignored)
- `scripts/ocr-spike/results.md` — scored comparison table
- `scripts/ocr-spike/pipeline-prototype.py` — end-to-end Layer 0–5 runnable on one input
- `docs/adr/00XX-cena-ocr-stack.md` — ADR recording per-layer tool choice + confidence threshold τ + how Surface A and Surface B share it
- If the cascade wins (it will):
  - Update [RDY-019b](RDY-019b-ministry-reference-scrape-recreation.md) body — swap Mathpix/Gemini from "required" to "low-confidence fallback only"
  - Replace the placeholder in `PhotoCaptureEndpoints.RecognizeMathAsync` with a call to the shared cascade service
  - Wire `PhotoUploadEndpoints` to the same service
  - Leave PhotoDNA at Layer A0 untouched — it is not part of this spike

### 7. Curator-metadata hint interface (schema + review loop deferred to RDY-019e)

Admin-sourced ingestion (B1/B2/B3) benefits from curator hints before OCR runs: knowing the document is Math 5u Hebrew-primary lets the cascade pick the right glossary, the right CAS subject, the right difficulty prior, and the right language-model for the fallback layer.

The full feature lives in [RDY-019e](RDY-019e-curator-ingestion-metadata.md): a two-phase handshake where (a) the system auto-extracts metadata from filename / path / embedded PDF metadata / a 1-page OCR preview, (b) the curator reviews in an admin UI, edits / adds / **removes** any auto-extracted field, then confirms — only confirmed items enter the full cascade. That's out of spike scope because it touches contract DTOs, `PipelineItemDocument`, and admin UI.

What the spike *must* do:

- Define an `OcrContextHints` record (in `scripts/ocr-spike/pipeline-prototype.py` and later as the C# interface) with fields: `subject`, `language`, `track`, `sourceType`, `taxonomyNode?`. This record is the *consumer-side* contract that RDY-019e's `CuratorMetadata` will eventually satisfy.
- Show that the cascade works two ways:
  - **Hints absent** (today's state for B1/B2/B3, and always for Surface A student upload) — cascade infers language/subject from content, writes confidence scores, returns.
  - **Hints present** (future state once RDY-019e lands) — cascade uses hints to pick glossary + CAS router + confidence threshold, and validates the extraction against the hint (e.g. if hint says Hebrew but OCR returns Arabic, surface as `InconsistentMetadata` violation).
- Benchmark the delta: how much does accuracy improve when hints are supplied? This quantifies the ROI of RDY-019e and tells us whether to enforce hints as required or keep them optional.

## Acceptance Criteria

- [ ] All 9 offline tools tested on the same fixture set (Bagrut pages + student photos + PDFs)
- [ ] PDF triage classifier built and benchmarked
- [ ] Quantitative scores recorded (WER for text, SymPy-equivalence for math, layout 0–3, figure recall, latency, throughput)
- [ ] Cost/latency projection: cloud-only vs cascade-with-fallback vs pure offline, for both surfaces
- [ ] Multi-layer prototype runs end-to-end on 1 student-photo and 1 Bagrut page, producing structured output
- [ ] Confidence threshold τ derived from fixture distribution, documented in ADR
- [ ] ADR committed with per-layer tool choice
- [ ] RDY-019b body updated to match the chosen stack
- [ ] Plan documented for replacing the `PhotoCaptureEndpoints.RecognizeMathAsync` placeholder (actual wire-up can be a follow-up task, but the service interface must be agreed)

## Coordination notes

- **PhotoDNA / CSAM check is NOT part of this spike.** It remains at Layer A0 and is owned by RDY-001 / PP-001. CSAM-flagged content never enters the OCR cascade.
- Bagrut fixture PDFs are reference-only — stored locally, git-ignored, never committed.
- Student-photo fixtures are synthetic / internal-test-account content — no real user data.
- Do NOT run the full 640-page Bagrut scrape during this spike — 10 pages is enough to decide.
- This spike is independent of [RDY-019a](RDY-019a-bagrut-taxonomy.md) (taxonomy) — can run in parallel.
