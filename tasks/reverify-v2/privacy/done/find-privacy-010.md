---
id: FIND-PRIVACY-010
task_id: t_f8ad584a9fb3
severity: P1 — High
lens: privacy
tags: [reverify, privacy, ICO-Children, GDPR, defaults]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-010: Default student preferences are NOT high-privacy (8/9 toggles default ON)

## Summary

Default student preferences are NOT high-privacy (8/9 toggles default ON)

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

framework: ICO-Children (Std 3 + Std 7), GDPR (Art 25 privacy by design)
severity: P1 (high)
lens: privacy
related_prior_finding: none

## Goal

Flip every default in `CreateDefaultPreferences` to its most-private value
so newly-created student accounts conform to ICO Children's Code Standard 3
("privacy by default") and Standard 7 ("default settings — high privacy").

## Background

`src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs:465-499`:

```csharp
return new StudentPreferencesDocument
{
    ProfileVisibility = "class-only",      // visible to peers
    ShowProgressToClass = true,            // peers see your progress
    AllowPeerComparison = true,            // leaderboard opt-in default ON
    EmailNotifications = true,
    PushNotifications = true,
    DailyReminder = true,
    WeeklyProgress = true,
    StreakAlerts = true,
    NewContentAlerts = true,
    ShareAnalytics = false,                // the only safe default
    ...
};
```

8 of 9 visibility/engagement toggles default ON. ICO Children's Code Standard 3
requires all defaults to be the most private option, and Standard 7 requires
explicit, granular age-appropriate consent before each opens. The default-on
push notifications also engage Standard 13 (nudge techniques) by establishing
engagement loops the child did not consent to.

Same `StudentProfileSnapshot.cs:60` defines `Visibility = "class-only"` —
the snapshot itself has a peer-visible default.

## Files

- `src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs` (CreateDefaultPreferences)
- `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` (line 60 default)
- `src/student/full-version/src/pages/onboarding.vue` (add an opt-in step
  for the privacy-relevant features)
- E2E + unit tests

## Definition of Done

1. Every visibility / sharing / engagement field defaults to its most
   private value:
   - ProfileVisibility = "private"
   - ShowProgressToClass = false
   - AllowPeerComparison = false
   - EmailNotifications = false
   - PushNotifications = false
   - DailyReminder = false
   - WeeklyProgress = false
   - StreakAlerts = false
   - NewContentAlerts = false
   - ShareAnalytics = false
2. Onboarding wizard adds an explicit "Privacy & Notifications" step (after
   the existing Welcome / Role / Language / Confirm steps) that lets the
   student (or parent for <13) opt-in to the features they want, with
   child-friendly language for each.
3. Branching on age tier (depends on FIND-privacy-001):
   - <13: opt-in step is NOT shown to the child; parent must enable each
     setting via the parent dashboard / consent challenge
   - 13-15: opt-in step is shown with conservative copy
   - 16+: standard adult flow
4. Audit log entry every time a default is overridden (links to the
   ConsentChangeLog from FIND-privacy-007).
5. Unit test on CreateDefaultPreferences asserting every visibility/notification
   field is false.
6. Integration test that a fresh student account, GET /api/me/settings, returns
   ProfileVisibility="private" and every notification false.
7. E2E test of the new onboarding opt-in step.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-010-defaults-private`. Result must
include:

- the before/after defaults table
- the new onboarding step screenshots in all 3 locales
- unit test path

## Out of scope

- Migration of existing student accounts (decision: respect existing
  preferences; no retroactive change)
- Parent dashboard implementation (deferred; this task only changes the
  defaults and adds the child-side opt-in step)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_f8ad584a9fb3`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
