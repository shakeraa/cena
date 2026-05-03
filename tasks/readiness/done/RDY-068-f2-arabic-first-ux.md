# RDY-068 — F2: Arabic-first UX with Hebrew-Bagrut exam-simulation toggle

- **Wave**: B (depends on terminology lexicon from Prof. Amjad)
- **Priority**: HIGH (market wedge)
- **Effort**: 4-6 engineer-weeks + 2 expert-review weeks (parallel)
- **Dependencies**: RDY-069a lexicon (blocker); F3 accommodations (TTS shared infra)
- **Source**: [track-7-israeli-bagrut-market.md](../../docs/research/tracks/track-7-israeli-bagrut-market.md); [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F2
- **Tier**: 1 (market wedge)

## Problem

No incumbent serves the Arabic-speaking Israeli market at quality. Ehab Omar + VEVOX are thin. Amir (Nazareth 4-unit) and Tariq (rural Druze) pay a daily cognitive-load tax translating Hebrew exam vocabulary — Sweller/Ayres/Kalyuga (2011) quantified this as measurable extraneous load. Shohamy (2010) documented the dual-language Bagrut processing penalty.

## Scope

**Arabic-first onboarding**: device locale + geolocation hint selects Arabic by default for users in Arab-majority locales. No "pick your flag" friction screen (Dr. Lior critique).

**UI language parity**: every student-facing surface renders in Arabic (Levantine register) with Hebrew and English as alternates. Math content always renders LTR in `<bdi dir="ltr">`.

**Hebrew-Bagrut simulation mode**: a toggle on practice-test mode that renders items in Ministry-standard Hebrew notation — so students train code-switching explicitly. Not a default mode.

**Terminology lexicon** (locked, reviewed):
- 5-unit syllabus terms × {Arabic canonical, Hebrew Ministry-standard, English}
- Prof. Amjad personally reviews and signs off
- Enforced via i18n key validation; no free-text math vocabulary

**Parent/teacher comms parity**: F5 parent digest and F6 teacher heatmap render in parent/teacher's language, not the student's.

## Files to Create / Modify

- `src/shared/Cena.Domain/Localization/TerminologyLexicon.cs` — typed lexicon
- `src/shared/Cena.Domain/Localization/LexiconReview.cs` — expert-review audit trail
- `src/student/full-version/src/i18n/ar/*.json` — full Arabic UI translation (Levantine)
- `src/student/full-version/src/composables/useLocaleInference.ts` — device locale + geo hint
- `docs/content/arabic-math-lexicon.md` — Prof. Amjad-signed lexicon

## Acceptance Criteria

- [ ] Locked Levantine Arabic math lexicon, 5-unit syllabus complete, expert-reviewed
- [ ] Onboarding defaults to Arabic for Arabic-set devices in Israel/Palestine locales
- [ ] Mixed-direction hint text (Arabic prose + inline math) renders correctly — Tamar test suite
- [ ] Hebrew-Bagrut simulation toggle isolated to practice-test mode
- [ ] Ministry notation drift test: 20-item golden set, Arabic → Hebrew exam mode, notation matches Ministry exemplars
- [ ] Parent digest + teacher heatmap honor parent/teacher language when different from student

## Success Metrics

- **Arabic-cohort retention @ 30 days**: target ≥ Hebrew-cohort baseline
- **Lexicon drift rate in generated content**: target 0 unapproved terms
- **Mixed-direction rendering bug reports**: target 0 critical, <3 cosmetic per quarter
- **Market penetration in Arab-sector pilot schools**: target ≥ 60% activation within 4 weeks of school onboarding

## ADR Alignment

- Aligns with Shohamy (2010), Clarkson (2007) findings already in project docs
- No ADR violations
- Reference-only Bagrut content (RDY-072) mirrors Hebrew Ministry notation in simulation mode only

## Out of Scope

- Classical Arabic / Egyptian Arabic registers (Levantine only first)
- Arabic TTS voices for hints (shared infra with F3, separate task)
- 4-unit and 3-unit lexicon expansion (follow-up after 5-unit ships)

## Assignee

Unassigned. Critical path: Prof. Amjad for lexicon (no substitute). Tamar for RTL-math bidi tests. Dr. Lior for onboarding copy.
