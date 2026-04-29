# TASK-PRR-275: Dev-fixture UI labeling discipline for `BagrutCorpusItemDocument` (G5)

**Priority**: P1 — labels-don't-match-data violation per shaker memory; gates PRR-245 reference-page UI
**Effort**: S (2-3 days; data-model flag + Vue banner component + i18n + arch test)
**Lens consensus**: claude-5 self-audit 2026-04-29 G5; claude-1 ACK on m_0485821c116e (no objection)
**Source docs**: claude-5 self-audit 2026-04-29 G5; [PRR-251 (done)](done/TASK-PRR-251-past-bagrut-corpus-dev-ingest-verification.md); [src/shared/Cena.Infrastructure/Seed/BagrutCorpusSeedData.cs](../../src/shared/Cena.Infrastructure/Seed/BagrutCorpusSeedData.cs) (claude-1's seed; ships `[DEV-FIXTURE]` items)
**Assignee hint**: frontend (claude-3 — pattern matches her PRR-256 work) + backend (PRR-272 owner — adds the field at the same surface)
**Tags**: source=claude-5-audit-2026-04-29,epic=epic-prr-n,priority=p1,frontend,backend,a11y,arch-test
**Status**: Ready
**Tier**: launch-adjacent (gates ADR-0059 reference-page UI ship)
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

claude-1's PRR-251 closure shipped `BagrutCorpusSeedData.cs` with 10 `[DEV-FIXTURE]` items. Today the reference-page UI (PRR-245) renders all `BagrutCorpusItemDocument` rows identically: a student would see synthetic test fixtures labeled with real Ministry-style citation `Bagrut Math 5U שאלון 035582 q3 (קיץ תשפ״ד)`. **Labels-don't-match-data** violation per the shaker memory `feedback_labels_match_data`.

This task introduces a `is_dev_fixture` flag on `BagrutCorpusItemDocument`, a banner component on the reference page that surfaces "DEV FIXTURE — not a real Ministry past paper" when the flag is true, and an architecture test that bans rendering fixtures without the banner.

## Scope

### Data model

1. **Add `IsDevFixture: bool`** field on `BagrutCorpusItemDocument` (default `false`).
2. **Set `IsDevFixture = true`** on every `BagrutCorpusSeedData.cs` row (claude-1's 10 synthetic items). The seed already self-flags via `RawText` containing `[DEV-FIXTURE]` — make this explicit at the document level.
3. **Marten projection**: rebuild backfill. Existing rows ingested via the production OCR pipeline get `IsDevFixture = false` (or null + null-treated-as-false). Existing rows from `BagrutCorpusSeedData` get `IsDevFixture = true`.

### UI

4. **Vue banner component** at `src/student/full-version/src/components/reference/DevFixtureBanner.vue` (new). Renders above the question body when `is_dev_fixture` is true. Copy in en/he/ar with `<bdi dir="ltr">` around any שאלון codes per `feedback_math_always_ltr` memory.
5. **Reference-page integration**: PRR-245's reference-page Vue component (when implemented) MUST render the banner before the question body if `is_dev_fixture` is true. Document the rule in PRR-245's task body.
6. **Color + a11y**: banner uses Vuetify warning-tier color (NOT amber/red per shipgate scanner; check `scripts/shipgate/banned-mechanics.yml` for allowed colors). `aria-live="polite"`, `role="status"` so screen readers announce it on first render.

### Architecture test

7. **Arch test** `DevFixtureBannerEnforcedTest.cs` — scans Vue components under `src/student/full-version/src/components/reference/**` and `src/student/full-version/src/pages/reference/**` for any rendering of `BagrutCorpusItemDocument` content. Fails if a render path:
   - Does NOT include the `DevFixtureBanner` component.
   - OR does not respect the `is_dev_fixture` flag.
8. Coordinator: file as Vue arch-test under `src/student/full-version/tests/arch/` if Vue arch-test infra exists, OR as a script under `scripts/shipgate/` that lints the Vue files.

### ADR amendment

9. **ADR-0059 §15.10 amendment** (the Accessibility section) — add a normative paragraph: *"`BagrutCorpusItemDocument.is_dev_fixture` rows MUST surface a `DevFixtureBanner` component before the question body. Banner copy localized en/he/ar; aria-live polite; role status. Arch test enforces."*

## Files

### Modified
- `src/shared/Cena.Infrastructure/Documents/BagrutCorpusItemDocument.cs` — add `IsDevFixture` field.
- `src/shared/Cena.Infrastructure/Seed/BagrutCorpusSeedData.cs` — set `IsDevFixture = true` on all seeded rows.
- `docs/adr/0059-bagrut-reference-browse-and-variant-generation.md` — §15.10 amendment.

### New
- `src/student/full-version/src/components/reference/DevFixtureBanner.vue`
- `src/student/full-version/src/plugins/i18n/locales/{en,he,ar}.json` — new copy keys.
- `src/actors/Cena.Actors.Tests/Architecture/DevFixtureBannerEnforcedTest.cs` (or shipgate scanner extension).
- `docs/ops/migrations/2026-04-29-is-dev-fixture-backfill.md`

## Definition of Done

- `IsDevFixture` field on `BagrutCorpusItemDocument`; backfill-rebuilt.
- `BagrutCorpusSeedData` rows all flagged true.
- `DevFixtureBanner.vue` ships in 3 locales with correct a11y semantics.
- Arch test catches future violations.
- Full `Cena.Actors.sln` build green; full Vitest run green.
- ADR-0059 §15.10 amendment merged.

## Blocking

- Coordinate with PRR-272 on `BagrutCorpusItemDocument` extensions (both touch the same surface).
- Coordinate with PRR-245 on the reference-page UI integration (when PRR-245 lands).

## Non-negotiable references

- Memory `feedback_labels_match_data`
- Memory `feedback_math_always_ltr`
- ADR-0059 §15.10
- Shipgate scanner banned-mechanics list

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + ADR-0059 §15.10 sha + arch test sha>"`
