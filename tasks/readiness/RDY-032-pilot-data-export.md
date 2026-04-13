# RDY-032: Pilot Data Export Pipeline

- **Priority**: SHIP-BLOCKER — prerequisite for RDY-007 (DIF), RDY-024 (BKT calibration), RDY-028 (Bagrut baseline)
- **Complexity**: Mid engineer + data privacy review
- **Source**: Adversarial review — Rami
- **Tier**: 0 (blocks multiple post-pilot tasks)
- **Effort**: 1 week

## Problem

Multiple tasks require pilot data for calibration and analysis (DIF, BKT parameters, Bagrut baseline), but no data export pipeline exists. Without a defined export format, storage location, and privacy controls, post-pilot analysis cannot begin.

## Scope

### 1. Pilot data schema

Define the analysis-ready export format:
- **Per-attempt record**: `student_id_hash, concept_id, subject, question_id, correct, hints_used, response_time_ms, scaffolding_level, session_id, session_number, timestamp`
- **Per-session record**: `student_id_hash, session_id, duration_ms, questions_attempted, questions_correct, fatigue_state_final, flow_state_final`
- Student IDs must be pseudonymized (hashed, not raw)

### 2. Export script/endpoint

- Script that queries Marten event store and materializes analysis-ready CSV/Parquet
- GDPR-compliant: no PII in export (pseudonymized student IDs, no names, no emails)
- ADR-0003 compliant: no misconception events in export (respect `[MlExcluded]`)
- Run as scheduled job (weekly during pilot) or on-demand

### 3. Data quality validation

- Row count checks (expected vs. actual)
- NULL checks on required fields
- Referential integrity (every attempt references valid concept_id, question_id)
- Export metadata: date range, school count, student count, total attempts

### 4. Storage

- Version-controlled export config in `config/pilot-export.json`
- Exports stored in `data/pilot/` (git-ignored, not committed)
- Retention: pilot data retained for 12 months post-pilot, then deleted

## Files to Create

- New: `scripts/pilot-data-export.py` — export script
- New: `config/pilot-export.json` — schema + config
- New: `docs/data/pilot-export-schema.md` — documentation

## Acceptance Criteria

- [ ] Export produces analysis-ready CSV with per-attempt and per-session records
- [ ] Student IDs pseudonymized (hashed)
- [ ] No PII in export
- [ ] No `[MlExcluded]` events in export (ADR-0003)
- [ ] Data quality validation passes (NULLs, referential integrity, row counts)
- [ ] Export metadata included (date range, counts)
- [ ] Script runs as scheduled job or on-demand
