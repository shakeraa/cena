# OCR Dev Fixtures

Importable JSON fixtures for teams building downstream of the OCR cascade
without having to run the full cascade service themselves. These let you:

- Stand up the admin UI / pipeline review screens with realistic data
- Test the recreation pipeline (RDY-019b §3) against structural analysis
- Mock the `IOcrCascadeService` response shape in integration tests
- Seed local Marten / admin databases with representative pipeline states
- Exercise the student tutor step-solver with a real OCR response

Every fixture here is either **synthetic** or **structural-only**. Raw Ministry
text from Bagrut fixtures is redacted per the `bagrut-reference-only` rule
(2026-04-15) — you get block counts, confidences, triage verdicts, and
aggregate statistics, not Hebrew prose.

## Layout

```
dev-fixtures/
├── README.md                          ← you are here
├── manifest.json                      ← machine-readable index
├── cascade-results/                   ← OcrCascadeResult shape, 6 scenarios
│   ├── student-photo-algebra-3u.json
│   ├── student-photo-calculus-5u.json
│   ├── bagrut-3u-text-shortcut.json   ← 90% path — pypdf shortcut
│   ├── bagrut-5u-full-cascade.json    ← structural-only for the OCR path
│   ├── pdf-encrypted-422.json         ← early rejection
│   └── pdf-scanned-bad-ocr.json       ← catastrophic / human-review
├── bagrut-analysis/
│   └── analysis.json                  ← aggregate structural stats (RDY-019b §2)
├── context-hints/
│   └── examples.json                  ← OcrContextHints combinations
├── pipeline-states/
│   └── pipeline-items.json            ← PipelineItemDocument samples, all states
└── triage-verdicts/
    └── samples.json                   ← one per PdfType category
```

## Schemas

All JSON conforms to the `OcrContextHints`, `OcrCascadeResult`, `TextBlock`,
`MathBlock`, and `BoundingBox` shapes defined in
[`runners/base.py`](../runners/base.py). The C# port in
[`PhotoCapture-wireup-plan.md`](../PhotoCapture-wireup-plan.md) mirrors these
— a fixture that parses cleanly here parses cleanly there.

Field conventions:

| Field | Type | Notes |
|-------|------|-------|
| `runner` | string | Which runner / layer produced this — `tesseract_5`, `surya`, `cascade_prototype`, `structural_only`, `synthesized` |
| `confidence` | float [0,1] | Per-region (inside `text_blocks[].confidence`) and aggregate (`overall_confidence`) |
| `language` | `"he"` / `"en"` / `"ar"` / `"unknown"` | Detected or hint-supplied |
| `bbox` | object or null | `{x, y, w, h, page}` — page is 1-indexed |
| `sympy_parsed` | bool | Whether SymPy accepted the LaTeX |
| `pdf_triage` | enum or null | `"text"`, `"image_only"`, `"mixed"`, `"scanned_bad_ocr"`, `"encrypted"` |
| `human_review_required` | bool | Layer 4c verdict |

Timestamps in fixtures are ISO-8601 UTC; use them as opaque strings.

## Importing

### Python

```python
import json
from pathlib import Path

root = Path("scripts/ocr-spike/dev-fixtures")
for result in (root / "cascade-results").glob("*.json"):
    data = json.loads(result.read_text())
    print(data["runner"], data["overall_confidence"])
```

### TypeScript / Node

```ts
import raw from "./dev-fixtures/cascade-results/student-photo-algebra-3u.json";
import type { OcrCascadeResult } from "./src/ocr/types";
const fixture = raw as OcrCascadeResult;
```

### C# / .NET

```csharp
using System.Text.Json;
using Cena.Infrastructure.Ocr;

var json = File.ReadAllText("dev-fixtures/cascade-results/bagrut-3u-text-shortcut.json");
var result = JsonSerializer.Deserialize<OcrCascadeResult>(json, new JsonSerializerOptions {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
});
```

### Admin UI (Storybook / mock data)

```ts
import bagrutFixture from "../dev-fixtures/cascade-results/bagrut-5u-full-cascade.json";
import pipelineStates from "../dev-fixtures/pipeline-states/pipeline-items.json";

// Hand pipelineStates[*] to the review-queue page for visual QA
```

## What each file is for

| File | Primary consumer | Scenario |
|------|------------------|----------|
| `cascade-results/student-photo-algebra-3u.json` | Student API / tutor | Happy path — 3u photo, 0.89 conf |
| `cascade-results/student-photo-calculus-5u.json` | Student API / tutor | 5u photo with displayed equation |
| `cascade-results/bagrut-3u-text-shortcut.json` | Admin API / RDY-019b | Text-layer shortcut (no OCR) |
| `cascade-results/bagrut-5u-full-cascade.json` | Admin API | Full cascade, structural only |
| `cascade-results/pdf-encrypted-422.json` | Admin API / UI | Error path, encrypted |
| `cascade-results/pdf-scanned-bad-ocr.json` | Admin API / UI | Catastrophic, human-review |
| `bagrut-analysis/analysis.json` | `ReferenceCalibratedGenerationService` | 151-PDF aggregate, drives recreation |
| `context-hints/examples.json` | All cascade callers | Copy-paste hint examples |
| `pipeline-states/pipeline-items.json` | Admin UI / pipeline review | Every `PipelineItemDocument` state |
| `triage-verdicts/samples.json` | PDF ingestion unit tests | One per category |

## Regeneration

If the cascade shape changes, regenerate via:

```bash
cd scripts/ocr-spike
source .venv/bin/activate
python build_dev_fixtures.py    # re-emits every file under dev-fixtures/
```

That script reads the live prototype output plus the scrubbing rules and
rewrites the fixtures idempotently.

## What's NOT here

- **No raw Hebrew text from Bagrut PDFs.** Every Bagrut cascade fixture has
  `text_blocks` redacted to confidence + bbox + length. The full cascade
  output on real PDFs lives (git-ignored) under `scripts/ocr-spike/results/`.
- **No student PII.** Synthetic photos use LaTeX-rendered problems, not real
  homework.
- **No API keys.** Mathpix / Gemini fallback samples show `fallback_fired`
  entries with `provider: "mock"` — the real cloud payload shape is in
  `PhotoCapture-wireup-plan.md` but not reproduced here.
