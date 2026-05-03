# GD-005: Compliance artifacts — umbrella task (legal/DPO-facing)

## Goal
Draft the 10 compliance artifacts identified in Track 8 of the research synthesis as a single ticketed umbrella with sub-deliverables. Each artifact is a text file or template, NOT code, and lives under `docs/compliance/`.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — Track 8 (Dark Patterns and Child-Safety Enforcement).

## Sub-deliverables
1. **DPIA (Data Protection Impact Assessment)** — GDPR-K Art. 35 template filled for Cena's processing
2. **Record of Processing Activities** — GDPR Art. 30
3. **Age-verification method statement** — how Cena determines a user is ≥ 13 (or parental consent flow under 13)
4. **Parental consent flow spec** — for COPPA + GDPR-K < 13 use
5. **Minor-data retention schedule** — explicit per-event-type retention periods
6. **Data-subject-request runbook** — access / delete / export for minors (and parents on behalf of minors)
7. **Third-party processor register** — every vendor touching minor data + their DPA status
8. **Dark-pattern audit report** — initial baseline scan against FTC 2022 Dark Patterns Staff Report taxonomy
9. **Child-friendly privacy notice (EN/AR/HE)** — ICO Children's Code Standard 4
10. **Breach-notification template** — 72-hour clock under GDPR Art. 33 + state-level US obligations

## Work to do
1. Create `docs/compliance/` directory
2. Create one `.md` stub per artifact with: scope, regulatory basis, owner (DPO if we have one, else "founder"), draft date, review cadence
3. Populate stubs with first-draft content where the research synthesis already provides it
4. Mark items that require legal review with `> STATUS: AWAITING LEGAL REVIEW`
5. Cross-reference each artifact to the regulation that demands it

## Non-negotiables
- Hebrew and Arabic versions of the privacy notice are MVP-blocking for an Israeli market launch (Privacy Protection Law Amendment 13)
- Breach-notification template is MVP-blocking; zero value to Cena but legally required day one

## DoD
- 10 files exist under `docs/compliance/` with first-draft content
- Umbrella task closed; any items needing live legal counsel get spun out as their own queue task

## Reporting
Complete with branch + list of the 10 artifacts + which ones are draft-ready vs awaiting legal review.
