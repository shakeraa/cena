# TASK-PRR-254: Ministry takedown-response runbook

**Priority**: P0 — gates ADR-0059 feature-flag flip-on (R8 launch gate)
**Effort**: S (2-3 days; runbook authoring + kill-switch wiring + dry-run)
**Lens consensus**: persona-ministry (§14.2 item 2 + Q-D resolution)
**Source docs**: [ADR-0059 §15.5 + §14.6 Q-D](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md), [PRR-016 exam-day SLO change-freeze](TASK-PRR-016-publish-exam-day-slo-change-freeze-window-in-cd.md)
**Assignee hint**: SRE lane (extends PRR-016 pattern)
**Tags**: source=adr-0059-q-d,epic=epic-prr-n,priority=p0,sre,runbook,launch-gate
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

Ministry of Education or a third-party rights-holder requests takedown of past-Bagrut content in Cena's reference library. SRE must be able to disable the feature globally within 30 minutes, preserve the audit log for legal review, and surgically purge variants derived from a specific source paper code without losing other students' practice history. This runbook documents the procedure + the kill-switch wiring.

## Scope

1. **30-minute global kill-switch** — `reference_library_enabled` feature flag (to be wired into the existing tenant-flag system per PRR-250 §4 follow-up). The kill-switch:
   - Disables `GET /api/v1/reference/papers*` endpoints (returns 404 + `Retry-After` header).
   - Disables `POST /api/v1/reference/{paperCode}/q/{questionNumber}/variant` (returns 503 + structured error).
   - **Bypasses the §3 90-day consent cache** — even students who accepted in the last 89 days see the disabled state on next request.
   - Documented latency target: ≤30 minutes from incident-call to global disable.
2. **Audit-log preservation procedure** — on takedown event, BEFORE any per-source variant purge:
   - Snapshot `BagrutReferenceItemRendered_V1` events for the affected source paper code(s) to a legal-hold S3 bucket (separate retention policy: 7 years per IL litigation-hold standards).
   - Record the takedown event in `IncidentResponseLog` with takedown source (Ministry vs third-party), affected paper codes, kill-switch timestamp, snapshot SHA.
3. **Per-source-paper-code variant purge tool** — script + arch-test:
   - Selects all persisted variants where `Provenance.Source` matches the structured slash-delimited form for the affected paper code (`ministry-bagrut/035582/*`).
   - Invalidates them in `QuestionDocument` (`Status: "Withdrawn"` + reason).
   - Cascades to `LearningSessionQueueProjection` to remove them from any active session queue.
   - Does NOT delete student practice attempts on the variants (audit retention).
4. **Re-enable procedure** — after takedown is resolved (e.g. counsel concludes posture is defensible, or licensing terms negotiated):
   - Verify §15.5 + §15.6 + §15.7 invariants hold.
   - Verify legal-hold snapshot integrity.
   - Flip `reference_library_enabled` back on; communicate to affected tenants via the standard incident-resolved channel.
5. **Tabletop exercise** — quarterly drill: simulated takedown notice, run the runbook end-to-end, measure actual time-to-kill-switch. Logged in `IncidentResponseLog`.

## Files

### New
- `docs/ops/runbooks/ministry-takedown.md` — main runbook
- `scripts/ops/purge-variants-by-source.{sh,py}` — per-source purge tool
- `src/actors/Cena.Actors.Tests/Operations/MinistryTakedownRunbookTest.cs` — arch test verifying:
  - The kill-switch flag exists and is observable from runtime telemetry.
  - The purge script's dry-run output matches a known fixture.
  - The legal-hold S3 bucket name is configured + arch-test enforces the bucket-policy retention is 7 years.

### Modified
- `src/api/Cena.Student.Api.Host/Endpoints/ReferenceLibraryEndpoints.cs` (created in PRR-245) — must respect the kill-switch.
- `docker-compose.yml` and/or operations IaC — provision the legal-hold S3 bucket + retention policy.

## Definition of Done

- Runbook reviewed + approved by SRE lead + Shaker.
- Kill-switch verified to disable both endpoints in <30 min via tabletop drill.
- Per-source variant purge tool dry-run tested on a fixture corpus.
- Legal-hold S3 bucket provisioned with 7-year retention.
- Quarterly tabletop drill scheduled in the SRE on-call calendar.

## Blocking

- PRR-245 endpoints must exist for the runbook to reference them. Coordinate with PRR-245 owner on integration test that exercises the kill-switch.

## Non-negotiable references

- ADR-0059 §15.5 + §14.6 Q-D
- Memory "No stubs — production grade"
- PRR-016 exam-day SLO change-freeze (sibling SRE pattern)

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<runbook sha + tabletop drill log + legal-hold bucket ARN>"`
