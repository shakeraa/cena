---
persona: ethics
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: yellow
---

## Summary

Multi-target is the correct model — a single-target plan was the dark-pattern gateway, because "you're behind on your one goal" is the loss-aversion copy that writes itself. Pluralising targets dilutes that pressure per-target, which is a structural win for intrinsic motivation.

That said, the brief as written opens four new dark-pattern surfaces: (a) per-target WeeklyHours is a declared contract the system can weaponise into "you missed your plan", (b) the total-hours >40h warning is benign if one-shot, paternalistic if persistent, (c) deadline fields + any "sort by deadline" UI slides straight into a PRR-019 countdown violation, and (d) target-archive post-exam is a completion-celebration surface with a non-trivial addiction profile. None are blockers in isolation. Together, plus the live streak leak already in `progress/time.vue` that the scanner is supposed to catch, they warrant a yellow with conditions.

## Section 9.3 answers

**Q1 — WeeklyHours: benign input or loss-aversion surface?**

Benign as a *scheduler input*. Malignant the moment any UI reads back "you declared 10h/week, you logged 6h this week." That copy is mechanically a streak (it measures a compliance gap on a student-declared contract), just wearing a planning-tool costume. Decision: WeeklyHours is written-only to the scheduler. No student-facing surface may render `declared − actual`, percent-of-plan, "behind/ahead", trend arrow, amber/red delta colour, or weekly-compliance rollup. The actual-hours number can be shown (fact). The declared-hours number can be shown in `/settings/study-plan` (their own setting, echoed back). The delta between them — forbidden.

Note: `src/student/full-version/src/pages/progress/time.vue:40-54` already computes a `dayStreakCount` and renders it at L135 as `{{ dayStreakCount }}days` with i18n key `progress.time.kpiDayStreak`. This is a live GD-004 violation and the scanner should already be flagging it (`scripts/shipgate/scan.mjs:26` has `\bstreak\b/i`). Multi-target will make this worse if not fixed — the temptation will be "streak per target." Fix before EPIC-PRR-F lands, not after.

**Q2 — Total-hours >40h warning: paternalism or honest?**

Honest if one-shot, advisory, dismissable, and absent from every other surface. The slider-adjacent line ("This is a lot — most students find 15–25h sustainable") meets that bar. Red line: no modal, no block-to-proceed, no re-surfacing in daily/weekly dashboards, no copy like "you're overcommitting." The line is drawn at *frequency + dismissability*. One whisper at plan creation is accommodation; a persistent nag is paternalism. Also: "most students find 15–25h sustainable" must be true and citable (R-28 honest-numbers posture) or removed — do not invent a population statistic to land the warning.

**Q3 — Deadline fields + countdown: ship-safe urgency surface?**

Yes, but narrowly. The deadline is a *date*, not a *duration*. The ship-safe contract:

- Store `Deadline` as UTC date; render in `/settings/study-plan` and confirm-step as `"Bagrut Math 5U — 2026-06-14"`. Date-as-fact. Passes ADR-0048 axis 1 (static, not ticking) and axis 2 (internal reference: the field the student filled in).
- Never compute `daysUntil(Deadline)` for a student-facing surface. Not in the settings page, not in the target-card badge, not in the home widget, not in a tooltip. The scheduler may compute it internally for the 14-day proximity rule; the number may never be rendered. Add a unit test asserting `daysUntil` is not reachable from any Vue component.
- The 14-day exam-week lock (section 6) is silent to the student. The UI may say "this session is focused on Bagrut Math 5U" — no "because exam is in 9 days." The cogsci lens owns whether the mechanic is sound; the ethics lens owns that the *reason text* stays invisible.
- Sorting targets in `/settings/study-plan` by deadline is allowed if the list is a plain date column with no "X days" derived display.
- Banned-mechanics scanner must grow Hebrew/Arabic siblings for "ימים עד" / "الأيام المتبقية" if not already present, and a new rule for `daysUntil`/`daysRemaining` as identifier patterns inside `src/` (see recommended tasks).

**Q4 — Target-archive post-exam: celebration ethics**

Acknowledge without rewarding. Allowed: a one-time toast "Bagrut Math 5U target archived — good luck on your exam" on archive. The archived target moves to a muted section of `/settings/study-plan` labelled "past targets." Banned: confetti, badges, score/XP, "completed N targets" counters, streak-across-archived-targets, retrospective "you studied X hours for this" summaries styled as trophies. The Deci-Koestner-Ryan line holds: decorative reward contingent on task completion crowds out intrinsic motivation for the next target.

One nuance: archiving happens *before* the exam is written, not after. "You completed your plan" is honest. "You passed your Bagrut" would be — the platform doesn't know and must not guess. The scanner should additionally refuse any archived-target copy referencing exam outcome ("well done", "congrats on finishing", "you got this" — all pre-result assumptions).

**Q5 — "Not sure yet, skip": does downstream copy shame it?**

Currently unclear. The brief says "creates a placeholder target that the student is prompted to complete on first home visit." The prompt copy is not specified. Risk vector: the home-visit prompt becomes "⚠ Your plan is incomplete — tap to finish" (loss-framed, urgency styling). Ship-safe variant: a dismissable neutral card, "You can add an exam target any time from Settings → Study plan," no exclamation mark, no amber/red, no repeat if dismissed for the session. The prompt must be closeable with no recurrence inside the same day (the student saw it, they know, leave them alone). If the student never adds a target, the scheduler falls back to diagnostic-driven practice — no "incomplete profile" badge, ever.

## Additional findings

**Free-text per-target note** — drop it entirely for v1. Section 5 step 4 ("retake, got 85 last time") is a PII minefield (persona-privacy 9.9 will say the same louder) and an LLM prompt-injection surface. The 200-char cap does not save it. Value to the student is low; value to the attack surface is high. If something like this must exist in v2, it is a structured enum ("retake?", "first attempt?"), not free-text.

**Override logging** — section 6's `ExamTargetOverrideApplied` event is fine, but the policy "no penalty, no nudging" must be enforced in code, not docstring. If any future parent-dashboard / teacher-dashboard / student-insights surface reads this event and renders "student overrode their plan 4× this week," the override becomes a compliance ledger. Gate the event behind a no-student-surface-allowed access control now.

**Parent surface inheritance** — the section-10 question on parent visibility is ethically load-bearing. Answered below.

**Live leak** — `progress/time.vue` streak counter (above) is a pre-existing ethics debt that blocks the yellow-to-green transition.

## Section 10 positions

2. **Free-text note**: drop. Low value, high risk. Not a v1 feature.
3. **Maximum targets**: 4 is defensible. A student with 5 genuine concurrent targets is either unusually prepared (retake + next-year Bagrut + Psychometry + SAT + one subject), in which case they can add the fifth via `/settings/study-plan` after onboarding — no reason to lengthen the onboarding loop. Hard cap 5 server-side to accommodate that case without opening the gates to script abuse (redteam 9.8). Not an ethics blocker either way — it's UX economy.
4. **Not-sure-yet skip**: allow, with the downstream-copy guardrails in Q5 above. Forcing a target at onboarding is friction that disproportionately punishes the "I just signed up to look around" user — whose autonomy (risk-map dimension 1) is the more important signal. Skip + unobtrusive prompt is the autonomy-preserving answer.
6. **Parent visibility of exam plan**: default **hidden**. Per-student opt-in (the student chooses to share their plan with the parent), not per-parent opt-in. The exam plan is the student's declared intention; showing it to a parent without student consent turns it into surveillance. For ≥18 students: no parent visibility, full stop, regardless of what the legal-guardian relation implies in the account model. ADR-0048's per-family opt-in applies to the exam *date* only (already decided); the *plan* — WeeklyHours, deadlines, progress — is stricter.

## Recommended new PRR tasks

1. **PRR-ethics-MT1** — scanner rule extension: add lint rules for `daysUntil`, `daysRemaining`, `countdownTo`, `%ofPlan`, `planCompliance`, `behindPlan`, `declaredVsActual` as identifier-name patterns in `src/` (not just copy strings). Hebrew/Arabic equivalents if missing from `scripts/shipgate/banned-mechanics.yml`. Targets the "escapes via variable names" bypass route.
2. **PRR-ethics-MT2** — remove `dayStreakCount` from `src/student/full-version/src/pages/progress/time.vue` and the `progress.time.kpiDayStreak` i18n keys across en/he/ar. Pre-existing GD-004 violation that will be harder to extract after multi-target lands. **P1 — blocks EPIC-PRR-F green sign-off.**
3. **PRR-ethics-MT3** — access-control test asserting `ExamTargetOverrideApplied` events are not readable from any student-facing, parent-facing, or teacher-facing endpoint. Event store-only for analysis.
4. **PRR-ethics-MT4** — positive-framing reference-copy addition: a §7 pair for "archive target" showing the allowed toast vs. the banned confetti/badge copy. Extends `docs/design/exam-prep-positive-framing.md`.
5. **PRR-ethics-MT5** — onboarding "skip target" downstream-copy audit: specify the home-visit prompt copy in en/he/ar, assert it passes `rulepack-scan.mjs --pack=mechanics`, assert it does not re-surface if dismissed.

## Blockers / non-negotiables

- **No `declared − actual` rollup surface, ever.** Plan compliance is not a user-facing metric.
- **No `daysUntil(Deadline)` rendered to any student surface.** Scheduler-internal only.
- **No free-text per-target note in v1.**
- **No completion celebration beyond a neutral archive toast.** No badges, no confetti, no counters.
- **Parent visibility of plan defaults to hidden, opt-in is per-student.**
- **Live streak leak in `progress/time.vue` must be removed before EPIC-PRR-F merges.** Otherwise the multi-target surface inherits the pattern.

## Questions back to decision-holder

1. Is the home-surface widget from `docs/design/exam-prep-positive-framing.md` §1 expected to be **per-target** (one widget per active target, date-as-fact each) or **consolidated** (single widget summing across targets)? The per-target version is safer (each surface stays inside the ADR-0048 axes); the consolidated version tempts a "weekly total" compliance number.
2. For the 14-day exam-week lock (section 6), is the scheduler allowed to tell the student *which* target it picked today, without saying *why* (no deadline reference)? Assumed yes above; confirm.
3. On the section-6 override event — should the student see their own override history in `/settings/study-plan`? Defensible (their own data), but opens the door to "you overrode 4×" self-shaming. Recommend no, hide it; confirm.
4. Is there appetite to retire `progress/time.vue`'s entire `kpiDayStreak` tile as part of EPIC-PRR-F, or does it need its own task outside this epic? Either works — it must not survive into the multi-target release either way.
