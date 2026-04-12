---
id: FIND-PEDAGOGY-014
task_id: t_9e338d79571a
severity: P1 — High
lens: pedagogy
tags: [reverify, pedagogy, i18n]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-pedagogy-014: Zero plural forms in student web i18n — Arabic/Hebrew show ungrammatical singular

## Summary

Zero plural forms in student web i18n — Arabic/Hebrew show ungrammatical singular

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

FIND-pedagogy-014: Add plural forms to all numeric i18n keys (en/ar/he)
classification: new

The student web ships 22 keys with numeric placeholders ({count},
{days}, {minutes}, {hours}, etc.) but ZERO use vue-i18n's pluralization
syntax. Arabic needs 6 plural forms per CLDR plural rules; Hebrew
needs 4; English needs 2. Result: ungrammatical strings on every
learning surface. A learner reading "1 sessions" or "5 question"
pauses to mentally correct the grammar — extraneous cognitive load
(Sweller 1988, DOI: 10.1207/s15516709cog1202_4).

Affected keys (22 in en.json with numeric placeholders):
  challenges.daily.timeLeft        gamification.leaderboard.xpLabel
  gamification.leaderboard.xpValue gamification.xp.currentXp
  gamification.xp.totalEarned      gamification.xp.xpToGo
  home.kpi.sessionsValue           home.resume.minutesAgo
  home.resume.progressAria         knowledgeGraph.detail.estimatedMinutes
  leaderboard.xpValue              profile.streakLabel
  progress.mastery.questionsAttempted progress.mastery.rowAria
  progress.time.chartAria          session.runner.progressAria
  session.runner.questionProgress  session.runner.xpAwarded
  session.setup.durationMinutes    session.summary.durationMinsSecs
  ... (full list in the report)

Reference data:
  Unicode CLDR Plural Rules: https://cldr.unicode.org/index/cldr-spec/plural-rules
  Arabic: zero | one | two | few | many | other (6 forms)
  Hebrew: one | two | many | other (4 forms)
  English: one | other (2 forms)

Files to read first:
  - src/student/full-version/src/plugins/i18n/locales/en.json
  - src/student/full-version/src/plugins/i18n/locales/ar.json
  - src/student/full-version/src/plugins/i18n/locales/he.json
  - vue-i18n pluralization docs (https://vue-i18n.intlify.dev/guide/essentials/pluralization)

Definition of done:
  - All 22 numeric template keys converted to vue-i18n plural syntax
    in en/ar/he.
  - en has ≥2 forms per pluralizable key; he ≥4; ar ≥6 (where the
    rule applies — some keys may not need every form).
  - All call sites updated to t(key, count, { count }) — note that
    vue-i18n requires count as the second argument for pluralization.
  - CI lint added that fails on plural-eligible keys (any key with
    {count}/{days}/{n}/{minutes}/{hours} placeholder) without enough
    forms in each locale.
  - Vitest covering edge counts (0, 1, 2, 5, 10, 100) for at least
    progress.mastery.questionsAttempted, profile.streakLabel,
    home.kpi.sessionsValue.

Reporting:
  - Branch <worker>/<task-id>-find-pedagogy-014-plural-forms
  - Push, then complete with summary + branch + test names


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_9e338d79571a`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
