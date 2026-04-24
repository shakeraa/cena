# ADR-0040 — Accommodation scope and Bagrut parity

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner), persona-ministry (Ministry-of-Education lens), persona-educator, persona-enterprise
- **Task**: prr-044
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md), [ADR-0003](0003-misconception-session-scope.md), [ADR-0038](0038-event-sourced-right-to-be-forgotten.md)

---

## Context

Cena supports runtime accommodations — extended time, text-to-speech, enlarged print, graph-paper overlays, math screen-reader markup. Those flags are currently scattered across the profile, the enrollment, and transient session state, and there is no single audit-trail or parity claim we can show to a Ministry-of-Education inspector or a buying school. That matters in two directions:

- **Student dignity and continuity.** A dyslexic student who moves from institute A to institute B does not lose their diagnosis. Their durable accommodations should follow them without a re-registration ceremony on every enrolment.
- **Buyer-facing parity claim.** Israeli schools procuring a digital learning platform for Bagrut preparation need to verify that the platform's accommodations are at least as expressive as the Ministry-recognised set printed on a student's Bagrut-accommodation letter. Without an auditable parity matrix, that verification does not happen.

Accommodations are emphatically *not* the same category as misconception telemetry. [ADR-0003](0003-misconception-session-scope.md) scopes misconception data to the session aggregate with a 30-day retention cap, because it is evidence of a specific error pattern and legally sensitive to aggregate at the student level. Accommodations are evidence of a **durable diagnosis** (or a durable local preference), they are authorised by a real-world certificate, and they must persist across sessions for the platform to be usable at all. Co-locating accommodation data with misconception data would violate ADR-0003 in one direction and make accommodations unusable in the other.

## Decision

We adopt a **hybrid scope**. Accommodations are split into two storage categories with different scoping rules and different audit semantics.

### Student-profile-scoped accommodations

These correspond to Ministry-recognised diagnostic categories: dyscalculia, dyslexia, ADHD, visual impairment, hearing impairment, physical disability. They follow the student across institutes (per ADR-0001 Phase 2 transfer semantics). They live in a durable aggregate — a successor to the existing `StudentProfile` state, targeted for extraction during the Sprint 2 arm of [ADR-0012](0012-aggregate-decomposition.md).

### Enrollment-scoped accommodations

These are institute-specific or classroom-specific variants. The same dyslexia diagnosis might produce a 25% extended-time accommodation at institute A and a 50% extended-time accommodation at institute B, because each institute has its own letter on file from a Ministry certification authority, and those letters can legitimately differ. These accommodations reset to institute defaults on a fresh enrolment.

| Category | Examples | Storage scope | Follows transfer? |
|---|---|---|---|
| Student-profile-scoped | TTS preference, math spoken output, enlarged print, graph-paper overlay, screen-reader markup | `StudentAccommodations` aggregate | Yes |
| Enrollment-scoped | Extended-time multiplier (e.g. 25% vs 50%), seating preference, language-of-instruction preference | `Enrollment` document | No — resets on new enrolment |

The distinction is enforced by an architecture test `AccommodationStorageDistinctionTest` (forthcoming under `tests/architecture/`) that fails if a profile-scoped field is ever persisted at enrollment scope, or vice versa.

## Bagrut parity matrix

Ministry-recognised Bagrut accommodations map to Cena runtime accommodations as follows. This is the inspector-facing summary; the longer per-row explanation lives in [`docs/compliance/bagrut-accommodations-parity-matrix.md`](../compliance/bagrut-accommodations-parity-matrix.md).

| Ministry-recognised Bagrut accommodation | Cena runtime accommodation | Status |
|---|---|---|
| Extended time (25% / 50%) | `ExtendedTimeMultiplier` per-enrollment | Enrollment-scoped |
| Text-to-speech (TTS) | `MathSpokenPreferred`, `InstructionsSpokenPreferred` | Student-scoped |
| Enlarged print | `PreferredFontSize` (≥ 18pt threshold) | Student-scoped |
| Graph-paper overlay | `GraphPaperOverlayRequired` | Student-scoped |
| MathML screen-reader | (covers visual-impairment diagnosis) | Student-scoped |
| Reader (human reader) | — | Out of scope for a digital platform |
| Scribe | — | Out of scope for a digital platform |

The two "out of scope" rows are not gaps in our parity — they are Ministry-recognised human-delivered accommodations that a digital platform cannot provide. The compliance matrix documents this explicitly, so that a school cannot read the parity matrix as "Cena supports human-reader accommodations" and be surprised.

## Storage distinction (enforced)

The architecture test `AccommodationStorageDistinctionTest` scans:

- Every event type whose name contains `Accommodation` must declare its scope (profile vs enrollment) via a tagging attribute.
- Every read-model record with accommodation fields must be bound to exactly one scope.
- Profile-scoped accommodation data MUST NOT appear on enrollment-scoped read models, and vice versa.

Misconception data remains session-scoped per [ADR-0003](0003-misconception-session-scope.md). No co-location is allowed: an event type may carry accommodation data or misconception data, but not both.

## Audit log

Every accommodation change (grant, revoke, variant edit) is appended to an audit stream with:

- The change type (grant / revoke / adjust).
- The authorisation source: `diagnosis-certificate`, `teacher`, `parent`, `student-self-declared`, `admin`.
- The certificate reference, if any (opaque token to the on-file certificate; see "Diagnosis-certificate attachment protocol" below).
- The actor who effected the change, timestamp, institute context.

The audit stream is append-only and is not subject to student-level erasure under [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) — it is legally-required record-keeping for the school buyer. However, the **content fields** of the audit event (student subject ID, certificate reference) are subject to the same crypto-shred PII classification as other subject data. When an audit line is displayed post-erasure, the structural columns (change type, authorisation source category) remain visible, and the PII columns display as `[erased]`. The audit trail's *shape* is preserved; the personal data is not.

## Cross-tenant transfer

Under [ADR-0001](0001-multi-institute-enrollment.md) Phase 2 and later, a student can hold enrolments at multiple institutes simultaneously, and can transfer between institutes. Accommodation behaviour on transfer:

- **Profile-scoped accommodations follow.** A dyslexic student moving from school A to tutor B does not have to re-declare their diagnosis. Their TTS preference, their graph-paper overlay, their font-size setting move with them.
- **Enrollment-scoped accommodations reset to the new institute's defaults.** The new institute's record of the diagnosis is what governs, not the previous institute's. If institute B requires a fresh certificate before granting extended time, the transfer does not circumvent that requirement.

This rule is the same one ADR-0001 applied to mastery state (seepage rather than full transfer) — preserving the learning signal but acknowledging that institutes have their own authorisation processes.

## Diagnosis-certificate attachment protocol

**Pending Ministry of Education clarification.** The question is whether Cena must store the actual diagnostic certificate file (e.g. a PDF from a certified diagnostician) against the student's profile, or whether it is sufficient to store a reference to a certificate held off-platform by the institute.

If Ministry review concludes that on-platform storage is required, the certificate file lives under the same crypto-shred PII classification established in [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) — encrypted with the subject's per-subject key, erased when the subject key is destroyed. The certificate field is treated as a free-form authored artefact from the subject's diagnostic provider, falling under PII class 1 ("free-form text the subject or their provider authored").

If Ministry review concludes a reference is sufficient, the reference is an opaque token with no PII value; it is stored in plaintext on the enrolment or the profile depending on scope.

## Consequences

### Positive

- School buyers and Ministry inspectors get a single parity matrix to audit against. No buyer has to discover runtime-accommodation coverage by poking at the product.
- Dyslexic/visually-impaired students moving between institutes do not lose their diagnoses. Their runtime experience is continuous.
- Enrollment-specific variants (the 25%-vs-50% extended-time case) remain correct — each institute's letter governs its own enrolment.
- Clear boundary with misconception data ([ADR-0003](0003-misconception-session-scope.md)): accommodations are durable diagnosis records, misconception data is ephemeral error-pattern telemetry; they will never be co-located.
- Clear boundary with the erasure story ([ADR-0038](0038-event-sourced-right-to-be-forgotten.md)): accommodations are PII-classified and crypto-shredded on subject erasure, while the audit-trail *shape* survives for buyer record-keeping.

### Negative

- The hybrid scope is genuinely complex and needs to be documented in the mentor/admin runbook. Training materials need an "accommodations: what follows a transfer and what doesn't" page.
- Certificate-attachment protocol remains pending on Ministry response. The design accommodates either outcome, but the implementation effort differs.
- Introducing `StudentAccommodations` as its own aggregate depends on the ADR-0012 aggregate-decomposition work for the Profile successor. The dependency is acknowledged and does not block Phase 1 of this ADR (read-only defaults at the enrolment layer).

### Neutral

- The misconception-scope rule ([ADR-0003](0003-misconception-session-scope.md)) is unchanged. Misconception data is still session-scoped with a 30-day retention cap; accommodations are a different category with different rules, and that is now documented rather than implicit.

## References

- [ADR-0001](0001-multi-institute-enrollment.md) — multi-institute enrollment; transfer semantics.
- [ADR-0003](0003-misconception-session-scope.md) — session-scoped misconception data; contrast category.
- [ADR-0012](0012-aggregate-decomposition.md) — aggregate decomposition; `StudentAccommodations` lands in the Profile successor.
- [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) — crypto-shred erasure; accommodations are PII-classified.
- persona-ministry review notes (Ministry-of-Education lens).
- [docs/compliance/bagrut-accommodations-parity-matrix.md](../compliance/bagrut-accommodations-parity-matrix.md) — full row-by-row parity narrative.
- `docs/tasks/pre-release-review/TASK-PRR-044.md` — task body.
