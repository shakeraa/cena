# ADR-0056 — Grade-passback policy (teacher opt-in + veto window + whitelist)

- **Status**: Accepted
- **Date proposed**: 2026-04-22
- **Deciders**: Shaker (project owner), claude-code (coordinator); persona-educator + persona-ministry + persona-ethics + persona-sre 4-lens consensus
- **Relates to**:
  - [ADR-0001 (multi-institute tenancy)](0001-multi-institute-enrollment.md) — enrollment-scoped grade emission; never cross-tenant
  - [ADR-0002 (SymPy CAS oracle)](0002-sympy-correctness-oracle.md) — the score being passed back is CAS-verified at grading time, but the *decision to emit* is human, not automated
  - [ADR-0040 (accommodation scope + Bagrut parity)](0040-accommodation-scope-and-bagrut-parity.md) — accommodated scores pass back identically; the SIS gets the final grade, not the accommodation metadata
  - [ADR-0043 (Bagrut reference-only)](0043-bagrut-reference-only-enforcement.md) — we pass back grades computed against Cena-recreated items, never against Ministry-reference items
  - [ADR-0048 (exam-prep time-framing)](0048-exam-prep-time-framing.md) — passback notifications never use time-pressure copy
  - [ADR-0052 (Ministry rubric version pinning)](0055-ministry-rubric-version-pinning.md) — `RubricId` is the audit anchor on every passed-back score
  - Sub-processor registry: Mashov + Classroom entries in `contracts/privacy/sub-processors.yml` (prr-035)
- **Source**: [tasks/pre-release-review/TASK-PRR-037-grade-passback-policy-adr-teacher-opt-in-veto-whitelist.md](../../tasks/pre-release-review/TASK-PRR-037-grade-passback-policy-adr-teacher-opt-in-veto-whitelist.md)

---

## Context

"Grade passback" is the outbound SIS integration that writes a Cena-computed grade (mock exam, assignment, diagnostic score) back into the school's system of record — Mashov in Israel, Google Classroom where schools have opted in, other systems as they onboard. Once a grade is in the SIS it shows up on the student's official transcript, factors into GPA, and is visible to parents and school administration. The political and regulatory weight is materially higher than any in-Cena surface.

Four regulatory / ethical constraints converge:

1. **Israeli Ministry of Education** — a score influencing the student's Bagrut trajectory is a material educational decision. The Ministry requires a human teacher "in the loop" for grade assignment; an AI-driven automated pass-back violates the implicit consent structure between students and the school.
2. **GDPR Article 22** — "the data subject shall have the right not to be subject to a decision based solely on automated processing … which produces legal effects concerning him or her or similarly significantly affects him or her." An automated grade that lands on a transcript is the textbook Art 22 case.
3. **FERPA (US partner schools)** — grade records are education records; the school is the data controller, not Cena. The school must authorize each grade emission.
4. **Cena's no-dark-patterns rule** — passback cannot be used to coerce student behavior (e.g. "finish this or your teacher sees your incomplete score"). Passback timing and framing are constrained.

Before this decision the code had no passback code path, but the open-ended "auto-sync grades" idea surfaced in the 2026-04-20 pre-release review. Four persona lenses (educator, ministry, ethics, SRE) independently flagged it as a launch-blocker without an explicit policy. This ADR sets the policy **before** any code is written.

---

## Decision

Grade passback is governed by five hard invariants. No passback code path may violate these; the invariants apply identically to Mashov, Classroom, and any future SIS integration.

### 1. Teacher opt-in is per-(teacher, student, grade-type) and defaults CLOSED

Opt-in is a compound key, not a global switch. A teacher who turns on passback for "mock exam summative grades" has NOT turned on passback for "daily diagnostic scores" or for "practice session scores". The opt-in UI surfaces these as explicitly separate toggles with copy that matches the whitelist vocabulary in §2.

Default posture for every combination: **CLOSED**. No score leaves Cena without an affirmative teacher action recorded in the event stream as a `TeacherGradePassbackEnabled_V1` event carrying `(TeacherId, StudentId, GradeType, EnabledAtUtc, EnrollmentId, RubricId?)`. The teacher can revoke at any time via `TeacherGradePassbackRevoked_V1`.

Per-student scope (not per-classroom) is deliberate: a teacher may trust passback for most of the class but want to withhold a specific struggling student whose score needs context the SIS cannot carry.

### 2. Whitelist of grade types is exhaustive and short

Only the following grade types are ever eligible for passback. The list is exhaustive — a grade type not listed here is NOT passback-eligible, regardless of teacher configuration.

| Grade type | What it is | Passback eligible when |
|---|---|---|
| `MOCK_EXAM_SUMMATIVE` | A full-length exam-simulation score graded under a pinned rubric (ADR-0052) | Teacher explicitly opts in AND exam-simulation has completed AND CAS gate passed |
| `ASSIGNMENT_COMPLETION` | Pass/fail completion of an assignment the teacher created | Teacher explicitly opts in |
| `DIAGNOSTIC_BASELINE` | Start-of-cohort baseline score, once per (student, cohort) | Teacher explicitly opts in; subsequent diagnostics NOT eligible |

The following grade types are NEVER passback-eligible, even with teacher opt-in. This list is also exhaustive — extensions require a superseding ADR.

- `DAILY_DIAGNOSTIC` — session-scoped signal, not a summative grade. Passback would create a rolling grade ticker students and parents would over-index on.
- `PRACTICE_SESSION_SCORE` — practice is exploration. Shipping practice scores to the transcript is the textbook "desirable difficulty" anti-pattern (ADR-0051).
- `MASTERY_PROBABILITY` — BKT parameter output. Shipping probability estimates as grades mis-communicates the signal.
- `TUTOR_DIALOGUE_RATING` — conversational helpfulness is not a grade.
- Any score computed against a Ministry-reference item (ADR-0043) — delivery gate enforces; passback gate double-enforces.

### 3. Veto window (14 days from grading)

Every eligible, opted-in grade enters a 14-day **veto window** before it is emitted to the SIS. During the window:

- The grade appears in the teacher's pending-passback queue.
- The teacher can explicitly **approve** (emits immediately) or **veto** (discards permanently, emits a `GradePassbackVetoed_V1` audit event with optional reason code).
- The student sees the grade in their Cena surface with a neutral "Shared with your teacher" chip. The student does NOT see an "imminent transcript write" countdown — that's time-pressure framing, banned by the ship-gate scanner (GD-004, ADR-0048).
- The grade does NOT emit to the SIS.

At `grading_time + 14 days`, if the teacher has neither approved nor vetoed, the default is **auto-veto** (fail-closed). This is the opposite of the typical "auto-approve on timeout" design for a reason: an auto-approved grade landing on a transcript without a teacher's explicit action is exactly the GDPR Art 22 "solely automated processing" case we are ruling out.

Rationale for 14 days (not shorter, not longer): one school-week + one follow-up school-week covers teacher absence, half-term breaks, and typical grade-review cycles. Ministry and school-admin personas confirmed 14d as operational.

### 4. Idempotency keyed on (enrollmentId, studentId, gradeType, sourceEventId)

Every passback emission is idempotent on the tuple `(EnrollmentId, StudentId, GradeType, SourceEventId)` where `SourceEventId` is the id of the upstream grading event (e.g. the `ExamSimulationCompleted_V1` event id for a mock exam score). Retries under network failure re-emit with the same tuple; the SIS integration adapter de-duplicates server-side OR the Cena adapter checks its own emission log.

Concretely:

- Successful emission → `GradePassbackEmitted_V1` event with `EmissionId` (deterministic hash of the tuple), `SisProvider`, `SisReceiptId`, `EmittedAtUtc`.
- Retried emission with the same tuple → idempotent no-op; structured log `GradePassbackAlreadyEmitted` at `EventId(8011)`.
- Grade revision (rubric v1.0.0 → v1.0.1 re-grade of the same mock exam) is NOT the same event — it has a new `SourceEventId` and enters a fresh 14-day veto window.

### 5. Observability + audit invariants

Every passback-related event carries:

- `EnrollmentId` (ADR-0001) — never cross-tenant.
- `RubricId` (ADR-0052) when the grade was rubric-graded — the rubric version that produced the score is part of the audit trail.
- `CorrelationId` — same correlation id as the upstream grading event.
- `TeacherId` — who opted in; who approved / vetoed.
- `SisProvider` (`mashov` | `classroom` | …) + `SisReceiptId` when emitted.

Structured log `EventId` assignments (SIEM pipelines key on ids, not strings):

| EventId | Name | Severity |
|---|---|---|
| 8010 | `GradePassbackEnabled` | Info |
| 8011 | `GradePassbackAlreadyEmitted` | Info |
| 8012 | `GradePassbackVetoed` | Info |
| 8013 | `GradePassbackAutoVetoedOnTimeout` | Warning |
| 8014 | `GradePassbackEmitted` | Info |
| 8015 | `GradePassbackEmissionFailed` | Error |
| 8016 | `GradePassbackIneligibleGradeType` | Warning |

---

## Out of scope at Launch

This ADR is **policy-only** at Launch. No passback code ships.

- No `IGradePassbackEmitter` interface.
- No SIS adapter implementations.
- No teacher opt-in UI.
- No pending-passback queue screen.
- The sub-processor registry entries for Mashov and Classroom carry `status: pending` (prr-035) — they cannot be called from production code until their status changes to `active`, which requires both a signed DPA AND the passback surface having shipped under this ADR's invariants.

The decision to ship passback itself is tracked as a separate future task (prr-037-IMPL, to be authored when the first school concretely requests it). That task MUST cite this ADR as its non-negotiable invariant list. Any passback implementation that does not honor §§1–5 is not shippable.

## Consequences

- **Positive**: the policy is set before code, not after — no "Phase 1 passback stub → Phase 1b real" pattern to retrofit. A regulator, parent, or teacher asking "can Cena push my grade to Mashov automatically?" has a single-paragraph answer grounded in the invariants above. GDPR Art 22 is addressed by the §3 fail-closed auto-veto default; FERPA is addressed by the §1 teacher-per-student opt-in.
- **Negative**: a 14-day veto window means teachers must actively work the queue. Mitigation: the queue is sorted by `veto_window_end`, surfaces the 7-day and 3-day soft reminders (plain chips, no countdown), and schools with strong passback cadence can train their teachers accordingly. Batch-approve is allowed (audit event fires per-student).
- **Operational**: the passback event stream is a separate sub-aggregate under the `GradePassback` bounded context. Replay of the event stream can reconstruct the full state of (enabled → pending → approved/vetoed → emitted) at any point in time; this is the regulator-facing audit trail.

## Enforcement summary (once implementation lands)

| Invariant | Artefact | Catches |
|---|---|---|
| Opt-in default closed | `IGradePassbackEmitter` default | Shipping passback without teacher action |
| Whitelist exhaustive | `GradePassbackGradeType` enum + `IGradePassbackEligibilityGate` | New grade type added without ADR review |
| 14-day veto window | `GradePassbackVetoWindow` domain service | Auto-emit bypassing veto |
| Idempotency key | `GradePassbackEmissionLog` Marten doc + unique index | Double-write on retry |
| EnrollmentId on every event | Arch test `NoGradePassbackEventWithoutEnrollmentIdTest` | Cross-tenant emission |

Each will ship with its implementation; at Launch the artifacts above do not yet exist, which is the correct posture for a policy-only ADR.

---

## Revisiting

Revisit when any of the following occurs:

- A partner school concretely requests passback — triggers the prr-037-IMPL implementation task.
- A SIS integration not yet covered (e.g. a non-Mashov / non-Classroom Israeli SIS) onboards — revisit §2 whitelist and sub-processor registry (prr-035).
- GDPR Art 22 guidance evolves — revisit §3 auto-veto default.
- Ministry of Education issues a circular on AI-assisted grading — revisit whether Cena-computed summative grades remain passback-eligible at all.

No passback code ships until this ADR's invariants are satisfied end-to-end.
