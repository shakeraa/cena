# OCR Spike — RDY-019-OCR-SPIKE (t_a617c472e9f6)

Offline-first OCR evaluation for two Cena surfaces:

1. **Surface A** — student photo / PDF upload (`PhotoCaptureEndpoints`, `PhotoUploadEndpoints`)
2. **Surface B** — admin ingestion (batch Bagrut scrape + interactive upload + cloud-directory drop-zone)

Both surfaces share a single confidence-gated OCR cascade. This spike picks the per-layer tool.

See the [parent task](../../tasks/readiness/RDY-019-ocr-spike.md) for full scope.

## Layout

```
scripts/ocr-spike/
├── README.md               ← you are here
├── requirements.txt        ← pip deps for all runners (install via venv)
├── requirements-core.txt   ← minimal deps for pdf_triage + scraper + harness only
├── setup.sh                ← one-shot venv bootstrap
├── bagrut_scrape.py        ← fetch ~10 public Bagrut pages from Ministry archive
├── synthesize_student_photos.py
│                           ← render LaTeX problems + camera noise → 10 fixtures
├── build_mixed_pdfs.py     ← generate 5 mixed text/image/encrypted PDFs
├── pdf_triage.py           ← text vs image vs mixed classifier (Layer 0/A1)
├── benchmark.py            ← runs every registered runner on every fixture
├── pipeline_prototype.py   ← end-to-end Layer 0..5 cascade with OcrContextHints
├── metrics.py              ← WER, SymPy-equivalence, layout score, figure recall
├── runners/
│   ├── __init__.py
│   ├── base.py             ← Runner protocol + shared dataclasses
│   ├── tesseract_runner.py
│   ├── surya_runner.py
│   ├── marker_runner.py
│   ├── olmocr_runner.py
│   ├── mineru_runner.py
│   ├── pix2tex_runner.py
│   ├── paddleocr_runner.py
│   ├── nougat_runner.py
│   └── doctr_runner.py
├── fixtures/               ← git-ignored
│   ├── bagrut/             ← 10 pages from meyda.education.gov.il
│   ├── student_photos/     ← 10 synthesized photos
│   ├── pdfs_mixed/         ← 5 mixed PDFs
│   └── ground_truth/       ← per-fixture JSON with hebrew text + LaTeX math + layout ref
├── cache/                  ← git-ignored (HF models, downloaded PDFs)
├── results/                ← git-ignored (per-run output)
│   ├── results.json        ← full per-tool per-fixture results
│   └── results.md          ← scored comparison (committed manually after final run)
```

## Quickstart

```bash
cd scripts/ocr-spike
./setup.sh                    # creates .venv, installs requirements
source .venv/bin/activate

# Step 1 — acquire fixtures (offline thereafter)
python bagrut_scrape.py                 # → fixtures/bagrut/ (10 PDFs)
python synthesize_student_photos.py     # → fixtures/student_photos/ (10 PNGs)
python build_mixed_pdfs.py              # → fixtures/pdfs_mixed/ (5 PDFs)

# Step 2 — triage PDFs
python pdf_triage.py fixtures/pdfs_mixed/*.pdf

# Step 3 — run the full benchmark matrix
python benchmark.py --all

# Step 4 — try the cascade end-to-end on one fixture
python pipeline_prototype.py fixtures/student_photos/algebra_01.png --hints subject=math,language=he,track=5u
python pipeline_prototype.py fixtures/bagrut/p01.pdf
```

## Why this directory matters beyond the spike

The runners and metrics package are deliberately small-footprint so they can be
called from a future CI job (tier-2 regression against a frozen fixture set)
once the production C# cascade service stabilizes. The Python code here is
**not** shipped — it's the spike rig. The production cascade lives in
`src/infrastructure/ocr/` (to be built in the follow-up feature task).

## Two surfaces, one cascade

```
Layer 0   Preprocess           deskew / denoise / binarize (OpenCV)
                               PDFs: run pdf_triage first

Layer 1   Layout detection     Surya → {text, math, figures, tables}

Layer 2a  Hebrew text OCR      Tesseract 5   ┐
Layer 2b  Math OCR             pix2tex        ├→ parallel (asyncio)
Layer 2c  Figures              crop + stash  ┘

Layer 3   Reassembly           right-to-left column flow, attach figures

Layer 4   Confidence gate      per-region score < τ → cloud fallback
          Layer 4a math:       Mathpix (region-scoped)
          Layer 4b text:       Gemini Vision (block-scoped)
          Layer 4c catastrophic: flag → human-review (Surface B)
                                       or 422 (Surface A)

Layer 5   CAS validation       parsed LaTeX → SymPy → CAS-gate (ADR-0002)
```

PhotoDNA / CSAM moderation sits **upstream** of Layer 0 on Surface A — see
`PhotoCaptureEndpoints.cs`, `IContentModerationPipeline`. Not in spike scope.

## Bagrut fixtures

Public Ministry PDFs from `meyda.education.gov.il/sheeloney_bagrut/`. Per the
`bagrut-reference-only` memory (2026-04-15), Ministry text is never shown to
students — it exists only to train the recreation pipeline and, here, to
benchmark OCR. Fixtures are git-ignored.

## Student-photo fixtures

Synthetic. The script `synthesize_student_photos.py` renders 10 Hebrew/English
math problems via `matplotlib` + KaTeX-style LaTeX, then applies augmentations
that simulate a phone camera (perspective warp, motion blur, JPEG compression,
noise, non-uniform lighting). This avoids any real student data entering the
fixture set.

## Ground truth

`fixtures/ground_truth/{fixture_id}.json`:

```json
{
  "fixture_id": "bagrut_p01",
  "language_primary": "he",
  "language_secondary": null,
  "hebrew_text": "פתור את המשוואה הבאה…",
  "latex_equations": ["3x + 5 = 14", "\\frac{a+b}{2}"],
  "layout": {
    "columns": 2,
    "direction": "rtl",
    "figures_count": 1,
    "question_ids": ["1a", "1b", "2"]
  },
  "notes": "Hand-transcribed by curator."
}
```

Hebrew text transcription for the first pass is assisted by Gemini Vision +
human review. Gold standard is never the output of any tool being benchmarked.

## Metrics (metrics.py)

| Metric | Implementation |
|--------|----------------|
| Hebrew WER | `jiwer.wer(gt, pred)` — both strings Unicode-NFC normalized, bidi stripped |
| Math equivalence | `sympy.simplify(parse_latex(a) - parse_latex(b)) == 0` w/ fallback to canonical-form string |
| Layout score | Manual 0–3, stored in ground-truth JSON alongside auto-measured column-order match |
| Figure recall | detected bbox IoU > 0.5 with gt bbox → TP |
| Latency | `time.perf_counter()` bracket around the single-page entry point |
| Throughput | Pages per minute on a 50-page batch (Bagrut only) |
| Setup friction | Manual 0–3, scored once per tool during first install |
| RTL bidi | Hebrew+math mixed line round-trips through Unicode bidi algorithm identically |

Scoring weights: WER 0.25, math 0.30, layout 0.20, figures 0.10, perf 0.10, setup 0.05.

## Runner contract

Each runner implements:

```python
class Runner(Protocol):
    name: str
    supports_math: bool
    supports_layout: bool
    supports_hebrew: bool
    requires_gpu: bool

    def setup(self, cache_dir: Path) -> None: ...
    def recognize(self, input_path: Path, hints: OcrContextHints | None = None) -> RecognitionResult: ...
```

`RecognitionResult` carries text blocks, math blocks (as LaTeX + SymPy-parsed),
figure bboxes, overall + per-region confidence, and tool-specific raw output
for later debugging.

## Hardware

Apple M1 Max, 64 GB RAM. Benchmarks use `mps` backend where supported; CPU
fallback for olmOCR / Nougat if MPS out-of-memory.
