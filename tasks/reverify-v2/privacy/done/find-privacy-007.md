---
id: FIND-PRIVACY-007
task_id: t_d2be304a10fe
severity: P0 — Critical
lens: privacy
tags: [reverify, privacy, GDPR, fake-fix, consent]
status: pending
assignee: unassigned
created: 2026-04-11
type: fake-fix
---

# FIND-privacy-007: Consent system is cosmetic — HasConsentAsync defined, never called

## Summary

Consent system is cosmetic — HasConsentAsync defined, never called

## Severity

**P0 — Critical** — FAKE-FIX

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

framework: GDPR (Art 7, Art 6 lawful basis)
severity: P0 (critical) — fake-fix (consent is cosmetic)
lens: privacy
related_prior_finding: none

## Goal

Bind GdprConsentManager into the request pipeline so that data processing
endpoints actually check consent before processing. Today the consent system
is cosmetic — `HasConsentAsync` is defined but never called.

## Background

`src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` defines:
- `RecordConsentAsync` (called by 1 admin endpoint)
- `RevokeConsentAsync` (called by 1 admin endpoint)
- `GetConsentsAsync` (called by 1 admin endpoint)
- `HasConsentAsync` — **zero callers** anywhere in the codebase

`grep -rn 'HasConsentAsync' src/` returns only the interface and impl
declarations. No producer or consumer of student data ever gates on consent.

The 3 ConsentType enum values (Analytics, Marketing, ThirdParty) are not
bound to any specific processing purpose, are not bound to any data flow, and
nothing checks them. The settings/privacy.vue toggles are stored only in
localStorage and never POSTed to the server.

This means the consent system is decorative. A student or parent toggling
consent makes zero functional difference.

## Files

- `src/shared/Cena.Infrastructure/Compliance/ProcessingPurpose.cs` (NEW —
  enumerate the 7-10 actual processing purposes the platform uses)
- `src/shared/Cena.Infrastructure/Compliance/RequiresConsentAttribute.cs`
  (NEW — `[RequiresConsent(ProcessingPurpose.X)]`)
- `src/shared/Cena.Infrastructure/Compliance/ConsentEnforcementMiddleware.cs`
  (NEW — read the attribute on the matched endpoint, short-circuit with 403
  + error="consent_required" if missing)
- `src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs` — decorate the
  social endpoints with `[RequiresConsent(PeerComparison)]`,
  `[RequiresConsent(SocialFeatures)]` etc.
- `src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs` — decorate
  with `[RequiresConsent(ThirdPartyAi)]`
- `src/api/Cena.Admin.Api/FocusAnalyticsService.cs` — decorate analytics
  reads with `[RequiresConsent(BehavioralAnalytics)]`
- `src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs` — add a
  /api/me/consent endpoint that the student app POSTs to
- `src/student/full-version/src/pages/settings/privacy.vue` — POST to
  /api/me/consent on toggle change instead of localStorage
- `src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` — replace
  ConsentType enum with ProcessingPurpose

## Definition of Done

1. ProcessingPurpose enum covers the actual purposes:
   - AccountAuth (always lawful)
   - SessionContinuity (always lawful)
   - AdaptiveRecommendation
   - PeerComparison
   - LeaderboardDisplay
   - SocialFeatures
   - ThirdPartyAi (Anthropic)
   - BehavioralAnalytics
   - CrossTenantBenchmarking
   - MarketingNudges (default OFF for minors)
2. RequiresConsentAttribute applied to every relevant endpoint handler.
3. ConsentEnforcementMiddleware short-circuits requests with missing consent
   before they hit the handler.
4. Default consent for any new student is "denied" for everything except
   AccountAuth + SessionContinuity (high-privacy by default — supports
   FIND-privacy-010).
5. settings/privacy.vue POSTs to server, persists via GdprConsentManager.
6. Auth contract test: with consent.peer_comparison=false, GET /api/social/leaderboard
   returns 403 with body `{"error":"consent_required","purpose":"peer_comparison"}`.
   Set the consent and the same request returns 200.
7. Same gating tested for /api/tutor (third-party AI) and /api/admin/analytics
   (behavioral analytics).
8. ConsentChangeLog dedicated audit document captures every consent change.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-007-consent-enforcement`. Result must
include:

- ProcessingPurpose enum source
- the endpoint × purpose mapping table
- before / after auth-contract test results

## Out of scope

- The privacy policy text disclosing each purpose (FIND-privacy-002)
- DPIA (FIND-privacy-009)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_d2be304a10fe`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
