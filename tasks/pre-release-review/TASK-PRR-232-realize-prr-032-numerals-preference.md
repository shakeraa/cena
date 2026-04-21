# TASK-PRR-232: Realize PRR-032 — numerals preference task

**Priority**: P1 — persona-a11y
**Effort**: S (3-5 days)
**Lens consensus**: persona-a11y
**Source docs**: persona-a11y findings (PRR-032 is a ghost reference)
**Assignee hint**: kimi-coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, a11y, ghost-reference
**Status**: Ready
**Source**: persona-a11y review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

The brief (and `onboardingStore.ts` comments) cite PRR-032 "Arabic RTL math delta notation profile numerals toggle" but no such task file exists and no `numeralSystem` field exists in the store. Either create the task + implement, or stop citing. Per persona-a11y non-negotiable: create + implement.

## Scope

1. Create the PRR-032 task file at `tasks/pre-release-review/TASK-PRR-032-ship-arabic-rtl-math-delta-notation-profile-numerals-toggle.md` if it doesn't exist — grep first; existing reference was found in brief but needs verification.
2. Add `numeralsPreference: 'western' | 'eastern' | null` to `onboardingStore.ts` `OnboardingState`.
3. Apply preference to:
   - Weekly-hours slider value display (PRR-221).
   - Deadline dates when rendered (PRR-221, PRR-227).
   - KaTeX math rendering (existing `useMathRenderer.ts` — extend).
4. Default inference: `null` = follow locale (ar → eastern, he/en → western).
5. Toggle in settings page.

## Files

- `tasks/pre-release-review/TASK-PRR-032-*.md` (verify/create as appropriate)
- `src/student/full-version/src/stores/onboardingStore.ts`
- `src/student/full-version/src/composables/useMathRenderer.ts`
- `src/student/full-version/src/pages/settings/accessibility.vue` (new toggle)
- Tests: locale → numerals inference, slider formatting, math rendering with each preference.

## Definition of Done

- PRR-032 task file exists and is either Done or cross-linked to this task.
- Numerals preference wired through all surfaces listed in Scope.
- Settings toggle functional, SR-announcable.
- persona-a11y sign-off.

## Non-negotiable references

- Memory "Math always LTR".
- Memory "Language Strategy" (EN primary, AR/HE secondary).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch>"`

## Related

- PRR-221, PRR-227.
