# Bagrut Reference Pipeline (RDY-019b)

## Legal posture
Ministry Bagrut exams are downloaded locally only for **structural
analysis** — topic distribution, difficulty curve, item formats. No
question text ever reaches students. Student-facing items are
AI-authored CAS-gated recreations per ADR-0033 + the
`memory:bagrut_reference_only` pointer.

`corpus/bagrut/reference/` is **git-ignored** except for
`analysis.json` (aggregate summary, no raw text).

## Running the scraper
```
pip install -r requirements.txt   # requests + beautifulsoup4 + pdfminer.six
python scripts/bagrut-scraper.py --dry-run                 # verify URL discovery
python scripts/bagrut-scraper.py --track 5u --year 2020 2021
python scripts/bagrut-scraper.py --resume                   # pick up from checkpoint
```

`discover_papers` is a placeholder — the Ministry directory structure
changes per academic year. Before production runs, populate with the
site-specific walker. The scraper respects `robots.txt` and polite-
crawls at 2-second intervals.

## Running the analyzer
```
python scripts/bagrut-reference-analyzer.py \
  --input corpus/bagrut/reference \
  --output corpus/bagrut/reference/analysis.json
```

Output shape:
```
{
  "schema_version": "1.0",
  "by_track": { "5u": { "papers": N, "topic_hits": {...}, "format_hits": {...} }},
  "papers":   [{ "filename": "...", "topic_hits": {...}, "format_hits": {...} }]
}
```

No raw question text — only aggregate counts.

## Recreation pipeline
The coverage-calibrated `ReferenceCalibratedGenerationService` that
consumes `analysis.json` and drives `AiGenerationService` into
`QuestionBankService.CreateQuestionAsync` lands in a follow-up PR
once the analyzer has produced a real `analysis.json` to calibrate
against. Stubbing the C# layer without reference data would violate
the NO-STUBS rule.
