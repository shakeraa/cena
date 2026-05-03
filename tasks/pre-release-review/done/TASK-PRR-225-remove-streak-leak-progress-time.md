# TASK-PRR-225: Remove pre-existing streak leak in `progress/time.vue`

**Priority**: P0 — ship-gate violation
**Effort**: S (half-day)
**Lens consensus**: persona-ethics
**Source docs**: persona-ethics findings (live GD-004 leak at `src/student/full-version/src/pages/progress/time.vue:40-54,135`)
**Assignee hint**: kimi-coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, ship-gate, bug, pre-existing
**Status**: Ready
**Source**: persona-ethics review (discovered while reviewing multi-target brief)
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Remove the pre-existing `dayStreakCount` KPI from `src/student/full-version/src/pages/progress/time.vue:40-54,135` and the associated i18n key `progress.time.kpiDayStreak`. This is a **pre-existing** GD-004 violation (Design Non-Negotiable #3: no streak counters) that the current shipgate scanner at `scripts/shipgate/scan.mjs:26` should have caught but didn't. EPIC-PRR-F cannot merge while this violation exists because PRR-224 will expand the scanner to catch `streak` identifier bans.

## Scope

1. Remove the `dayStreakCount` computed/data reference from `progress/time.vue:40-54` and the render block at line 135.
2. Remove `progress.time.kpiDayStreak` from all locale files (`en`, `he`, `ar`).
3. Replace with a positively-framed time-on-task metric per [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — suggested: "Time spent learning this week" (non-streaking, non-comparative).
4. Verify shipgate scanner (current version) now catches the banned term in a test fixture.
5. Investigate why the current scanner missed this instance — was it a path exclusion, a typo in the regex, or a pre-existing whitelist? Fix the gap before extending the scanner in PRR-224.

## Files

- `src/student/full-version/src/pages/progress/time.vue` (remove streak KPI)
- `src/student/full-version/src/plugins/i18n/locales/{en,he,ar}.json` (remove streak key)
- `scripts/shipgate/scan.mjs` (identify + fix the evasion gap)
- Test: new fixture that asserts scanner catches `dayStreakCount` going forward.

## Definition of Done

- Streak KPI fully removed; no regression on progress page layout.
- All three locale files cleaned.
- Current scanner now fails on any re-introduction attempt.
- Scanner evasion gap identified in PR description + fixed.
- Replacement metric framed per ADR-0048.

## Non-negotiable references

- Design Non-Negotiable #3 (no streaks).
- ADR-0048 (exam-prep positive framing).
- PRR-019 (banned-mechanics scanner).
- Memory "Ship-gate banned terms".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + scanner fixture output + gap root-cause>"`

## Related

- PRR-224 (scanner v2 extension; must not merge until this is resolved).
