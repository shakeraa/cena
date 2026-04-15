# RDY-019-OCR-SPIKE: Offline OCR Evaluation for Bagrut Reference Scrape

**Parent**: [RDY-019b](tasks/readiness/RDY-019-bagrut-corpus-ingestion.md) (unblocker spike)
**Priority**: Medium — blocks RDY-019b cost/privacy posture
**Complexity**: Mid engineer
**Effort**: 2-3 days
**Blocker status**: None

## Problem

RDY-019b currently depends on Gemini (LLM) + Mathpix (math OCR) cloud APIs. User wants to know whether offline tooling can replace one or both. Offline wins:

- No per-page API costs on a 640-page scrape
- No exam content leaving local machine (even though it's public reference material, offline is cleaner)
- No API-key provisioning gate on RDY-019b

## Scope

### 1. Evaluate candidate offline tools

Run each against a **fixture set of 10 real Bagrut pages** (Hebrew + math notation):

| Tool | What it does | Hebrew? | Math notation? | Runs offline? |
|------|--------------|---------|----------------|---------------|
| Tesseract 5 | General OCR | Yes (heb trained data) | Weak | Yes |
| Nougat (Meta) | Academic-paper-to-LaTeX | Unknown for Hebrew | Strong (LaTeX output) | Yes (HF model) |
| pix2tex / LaTeX-OCR | Math-region → LaTeX | No text, math only | Strong | Yes |
| Surya OCR | Multilingual OCR + layout | Yes | Partial | Yes |
| Marker (VikParuchuri) | PDF → Markdown, math-aware | Yes | Strong | Yes |
| PaddleOCR + math plugin | OCR + formula plugin | Hebrew patchy | Strong | Yes |

Score each on: Hebrew accuracy, math accuracy, layout preservation (two-column, figures), setup friction, inference time per page on M-series Mac.

### 2. Recommend pipeline

Pick one of:
- **(a) Pure offline**: e.g. Marker for layout + text + math, fallback to pix2tex for hard equations.
- **(b) Hybrid**: Marker/Tesseract offline for text, Mathpix (cloud) only for math regions that fail local extraction.
- **(c) Status quo**: cloud-only — justify with quality/cost numbers.

Produce a decision doc with: extraction accuracy, cost projection for 640 pages, runtime, and the recommended architecture.

### 3. Deliverables

- `scripts/ocr-spike/` — smoke harness that runs each tool on the fixture set
- `scripts/ocr-spike/fixtures/` — 10 Bagrut pages (local-only, git-ignored per Bagrut reference-only memory)
- `scripts/ocr-spike/results.md` — scored comparison
- `docs/adr/00XX-bagrut-ocr-stack.md` — ADR recording the choice
- If (a) or (b) wins: update RDY-019b body to swap Mathpix/Gemini out for the chosen tool; drop the API-key env var dependency accordingly.

## Acceptance Criteria

- [ ] All 6 tools tested on the same 10-page fixture
- [ ] Quantitative scores recorded (CER for Hebrew text, LaTeX exact-match % for equations, layout preservation rating)
- [ ] Cost projection table for 640 pages: cloud vs offline
- [ ] ADR committed with the recommendation
- [ ] RDY-019b body updated to match the chosen stack

## Coordination notes

- Fixture PDFs are reference-only — stored locally under `corpus/bagrut/reference/fixtures/`, git-ignored, never committed.
- Do NOT run the full 640-page scrape during this spike — 10 pages is enough to decide.
- This spike is independent of RDY-019a (taxonomy) — can run in parallel.

---- events ----
2026-04-15T16:37:13.375Z  enqueued   -
