# RDY-066 — F3: Accommodations mode (core 4 of 8 dimensions)

- **Wave**: A
- **Priority**: HIGH
- **Effort**: 2-3 engineer-weeks
- **Dependencies**: Tamar accessibility audit for `#7367F0` contrast (flagged in panel review)
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F3
- **Tier**: 1 (equity + compliance)

## Problem

Yael's persona (dyscalculia, formal התאמות) is representative of ~20% of Israeli students. No Israeli EdTech product ships this well today. Dalia (LD-advocate parent) has been failed by three products already — she will uninstall same-day if pressure UI appears. Accommodations mode is a binary retention gate for this cohort.

## Scope

Ship 4 of 8 accommodation dimensions in first pass:

1. **Extended time** — no timers visible at all (not "25% more timer" — *no timer*)
2. **TTS for problem statements** — L1 voice, Levantine Arabic / Modern Hebrew / English
3. **Distraction-reduced layout** — one problem at a time, no sidebar, no mastery widgets visible
4. **No comparative stats** — rankings, peer averages, percentile indicators all hidden

Deferred to v2 (separate task):
- TTS for hints (requires voice-prompt authoring)
- High-contrast theme override (blocked on Vuexy primary `#7367F0` contrast audit)
- Reduced animation mode
- Visible progress indicator toggle

Implementation:
- Profile-based (per-student JSON blob), not single toggle
- Parent-linked accommodations profile for under-18 students → default-on when parent has set it
- Ministry התאמות code → Cena profile mapping table (Prof. Amjad sign-off)
- Session-scoped per ADR-0003; not in long-term analytics

## Files to Create / Modify

- `src/shared/Cena.Domain/Accommodations/AccommodationProfile.cs` — 8-dimension enum
- `src/shared/Cena.Domain/Accommodations/MinistryAccommodationMapping.cs` — code table
- `src/student/full-version/src/composables/useAccommodations.ts` — runtime flags
- `src/student/full-version/src/layouts/AccommodationsLayout.vue` — distraction-reduced
- `src/api/Cena.Admin.Api/Features/ParentConsole/AccommodationsEndpoints.cs` — parent sets profile
- `docs/design/accommodations-design.md` — dimension catalogue + Ministry code mapping

## Acceptance Criteria

- [ ] 4 dimensions toggleable per student, stored as an `AccommodationProfileAssignedV1` event
- [ ] Parent console sets accommodations for minor; parent consent audit trail (per ADR-0003 + parental-consent.md)
- [ ] Internal label "Accommodations mode" never appears in student UI (Dr. Lior critique: no "slow mode" variants)
- [ ] Data is session-scoped; no accommodations field in long-term analytics export
- [ ] 10-student usability walkthrough with Dalia-like persona runner (dyscalculia-experienced)

## Success Metrics

- **Retention @ 30 days for accommodations-enabled cohort**: target ≥ baseline general cohort
- **Session-completion rate**: target ≥ 70% (vs general ~60%)
- **Parent NPS (LD cohort)**: target ≥ 50

## ADR Alignment

- ADR-0003: profile session-scoped; disability status = GDPR Art 9 sensitive category → separate consent bucket
- Parental consent per docs/compliance/parental-consent.md
- ICO Children's Code Std 11 (parental controls)

## Out of Scope

- Full 8 dimensions (4 deferred to v2)
- High-contrast theme override (blocked on Tamar audit, separate task)
- Teacher-set accommodations (Wave B, tied to F6 teacher console)

## Assignee

Unassigned; needs Tamar (accessibility), Dr. Nadia (pedagogy framing), Ran (compliance sign-off).
