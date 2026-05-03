# TASK-PRR-044: ADR: Accommodation profile scope (student vs enrollment)

**Priority**: P1 — strongly recommended before launch (lens consensus: 2)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-educator, persona-enterprise
**Source docs**: `axis3_accessibility_accommodations_findings.md:L~`
**Assignee hint**: human-architect
**Tags**: source=pre-release-review-2026-04-20, lens=educator
**Status**: Done — 2026-04-20
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal

ADR for Accommodations aggregate scope + Ministry-parity claim. Accommodations are **durable profile settings** (NOT misconception data per ADR-0003 — Ministry lens confirmed: mirror Ministry-recognized diagnoses, audit-logged, persist across sessions).

### Ministry lens read (2026-04-20, persona-ministry/axis3_accessibility_accommodations_findings.yaml)

- Accommodations are durable, not session-scoped — ADR-0003 does not apply
- **Parity claim required**: Cena runtime accommodations must be a superset of Ministry-recognized Bagrut exam accommodations (extended time, TTS, MathML, enlarged print, graph-paper overlay)
- Arabic RTL math (F7) is shipping precondition for Arabic cohort (non-negotiable #6)
- MathML pipeline (F6) understated in axis3 doc — Ministry wants ship-tier for blind/low-vision students

### User decision 2026-04-20 — hybrid scope, Ministry-informed mandatory sections

**Hybrid scope**:
- **Student-profile-scoped** (follows institute transfers): core disability-backed accommodations — dyscalculia, dyslexia, ADHD, visual impairment, hearing impairment, physical/motor. Mirror Ministry-recognized diagnoses. Audit-logged with authorization source (diagnosis certificate / teacher / parent / student).
- **Enrollment-scoped**: institute-specific variants — teacher-approved seating, institute's extra-time variant (25% vs 50%), language-of-instruction preference, institute-specific assistive-tech preferences.

**ADR mandatory sections (all 6 required)**:

1. Scope decision + rationale (hybrid per above)
2. **Parity-claim matrix**: row per Ministry-recognized Bagrut accommodation; column mapping to Cena runtime accommodation; any Cena-only enhancement labeled "Cena-only, not Bagrut-certified"
3. **Storage distinction**: Accommodations → durable aggregate (`StudentAccommodations` in student-profile context post Sprint 2); misconception data → session-scoped (ADR-0003); arch test enforces categories not co-located
4. **Audit log requirement**: every accommodation change logged with authorization source; append-only; erasure via prr-003a crypto-shred preserves structure
5. **Cross-tenant transfer semantics**: ADR-0001 Phase 2+ — profile-scoped accommodations follow, enrollment-scoped reset to defaults
6. **Diagnosis-certificate attachment protocol** (if required by MoE) or explicit "pending MoE clarification" marker

## Files

- `docs/adr/NNNN-accommodation-scope-and-bagrut-parity.md`
- `docs/compliance/bagrut-accommodations-parity-matrix.md`
- Cross-ref updates: ADR-0001 (multi-institute tenancy) + ADR-0003 (session-scope)
- `tests/architecture/AccommodationStorageDistinctionTest.cs`

## Definition of Done

1. ADR accepted with all 6 mandatory sections
2. Parity matrix signed off by human-architect + Ministry-lens reviewer
3. Storage-distinction arch test green
4. Cross-references to ADR-0001 and ADR-0003 updated
5. Diagnosis-certificate attachment protocol documented or explicitly deferred
6. Full `Cena.Actors.sln` builds cleanly

## Rolls up into EPIC-PRR-A

Sprint 2+ scope — ADR must land before Accommodations aggregate is extracted from StudentActor (one of the three successor contexts per ADR-0012).

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker human-architect --result "<branch>"

---

## Non-negotiable references
None

## Implementation Protocol — Senior Architect

Implementation of this task must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this task exist?** Read the source-doc lines cited above and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised it. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole task.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What's the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping — see user memory "Verify data E2E" and "Labels match data".
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing
- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No "Phase 1 stub → Phase 1b real" pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).
- If the task as-scoped is wrong in light of what you find, **push back** and propose the correction via a task comment — do not silently expand scope, shrink scope, or ship a stub.

### If blocked
- Fail loudly: `node .agentdb/kimi-queue.js fail <task-id> --worker <you> --reason "<specific blocker, not 'hard'>"`.
- Do not silently reduce scope. Do not skip a non-negotiable. Do not bypass a hook with `--no-verify`.

### Definition of done is higher than the checklist above
- Labels match data (UI label = API key = DB column intent).
- Root cause fixed, not masked.
- Observability added (metrics, structured logs with tenant/session IDs, runbook entry).
- Related personas' cross-lens handoffs addressed or explicitly deferred with a new task ID.

**Reference**: full protocol and its rationale live in [`/tasks/pre-release-review/README.md`](../../tasks/pre-release-review/README.md#implementation-protocol-senior-architect) (this section is duplicated there for skimming convenience).

---

## Related
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-044)
