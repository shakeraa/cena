# TASK-PRR-251: Past-Bagrut corpus dev-ingest verification + bootstrap

**Priority**: P0 — BLOCKER for PRR-245 (reference library) and ADR-0059 §3+§4 filter scope
**Effort**: M (3-5 days; depends on whether the OCR pipeline can run in dev or needs production output)
**Lens consensus**: claude-1 PRR-250 finding §2
**Source docs**: [PRR-250 findings](reviews/PRR-250-verification-sweep-findings.md), [PRR-242 (done)](done/TASK-PRR-242-past-bagrut-corpus-ingestion.md), [ADR-0059](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md)
**Assignee hint**: original PRR-242 owner (content-engineering lead + OCR pipeline owner) — claude-code holds coordinator review
**Tags**: source=prr-250-finding,epic=epic-prr-g,priority=p0,blocker,corpus,dev-environment
**Status**: Ready
**Tier**: launch-adjacent (gates PRR-245)
**Epic**: [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md) — corpus-engineering follow-up

---

## Goal

PRR-242 was marked Done with the past-Bagrut corpus ingested. PRR-250 verification (claude-1, 2026-04-28) found `mt_doc_bagrutcorpusitemdocument` does not exist in dev Postgres — the corpus was never actually ingested into a runnable environment. ADR-0059 §3 + §4 reference filter scope is moot until corpus is populated. This is a PRR-148-style false-done; close the gap honestly per the "no stubs — production grade" memory.

## Scope

1. **Verify corpus state in production-equivalent environment**: confirm whether the corpus exists *somewhere* (S3, archived seed, content-team laptop) or only in PRR-242 author's local dev.
2. **Bootstrap dev environment**: pick one of:
   - (a) replay the PRR-242 ingestion pipeline against a small known-good fixture (e.g. 5 שאלונים from 2024 קיץ); document the runbook so any dev box can produce the corpus.
   - (b) ship a seed-data file (gzipped JSONL) checked into the repo or hosted on S3 + restored on dev stand-up.
3. **Add a `docker-compose.yml` post-up hook or `make seed-corpus` target** so new contributors get a populated corpus on first stand-up — not a silent empty table.
4. **Verify Marten document type matches PRR-250's documented field shape**: `paperCode → MinistryQuestionPaperCode`, `year`, `moed`, `questionNumber`, `subject`, `stream`, `body`, `metadata`. Update PRR-242 task with the actual schema.
5. **Add an architecture test** that fails the build if `BagrutCorpusItemDocument` Marten registration is missing or if the corpus-ingest health check returns zero rows in non-test environments. Catches the next false-done attempt.
6. **Update PRR-242 status**: re-open if the gap can't be closed within this task's effort window, OR document corpus location as "production-only, gated on …" with explicit dev-bootstrap procedure.

## Files

### New
- `scripts/dev/seed-bagrut-corpus.{sh,py}` — dev-bootstrap script
- `docs/ops/dev-environment/bagrut-corpus-bootstrap.md` — runbook
- `src/actors/Cena.Actors.Tests/Architecture/BagrutCorpusIngestedHealthTest.cs` — arch test

### Modified
- `tasks/pre-release-review/done/TASK-PRR-242-past-bagrut-corpus-ingestion.md` — add "verification gap closed by PRR-251" history entry
- `docker-compose.yml` and/or `Makefile` — wire bootstrap on dev stand-up

## Definition of Done

- `mt_doc_bagrutcorpusitemdocument` exists and is non-empty on a fresh dev stand-up.
- Dev-bootstrap runbook reproducible without prior content-team context.
- Architecture test catches future regression.
- ADR-0059 §3 + §4 filter scope assumptions hold against the actual corpus shape.

## Blocking

- None at start. Coordination with PRR-242 author needed to recover the actual ingest path.

## Non-negotiable references

- Memory "No stubs — production grade" (the load-bearing constraint that makes this a P0)
- Memory "Verify data E2E"
- ADR-0059 §3+§4
- PRR-250 findings §2

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<runbook sha + arch test sha + dev verification screenshot/log>"`
