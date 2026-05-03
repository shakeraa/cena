---
id: FIND-PRIVACY-001
task_id: t_4c77eb4b3436
severity: P0 — Critical
lens: privacy
tags: [reverify, privacy, COPPA, GDPR-K, ICO-Children, age-gate]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-001: No age gate or parental consent on student registration

## Summary

No age gate or parental consent on student registration

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

framework: COPPA, GDPR-K, ICO-Children
severity: P0 (critical)
lens: privacy
related_prior_finding: none (privacy lens new in v2)

## Goal

Implement an effective age gate + parental consent flow on the student web
register and onboarding flow so the platform stops collecting PII from minors
without lawful basis.

## Background

The student web at `src/student/full-version/src/pages/register.vue` collects
display name + email + password and immediately creates an account with zero
age verification, no DOB field, no parental email field, no consent checkbox,
no link to a Privacy Policy or Terms of Service. The mobile-only
`AgeSafetyService` at `src/mobile/lib/core/services/age_safety_service.dart`
exists in scaffolding but the web tier has no equivalent. `StudentProfileSnapshot`
has no DateOfBirth, AgeAtRegistration, ParentEmail, or ParentalConsent fields.
None of the three locale files (en/ar/he) contain the strings "parental",
"guardian", or "consent".

This is a direct violation of:
- COPPA 16 CFR §312.5 (verifiable parental consent before collection from
  children under 13)
- GDPR Article 8 (member-state age of digital consent, default 16)
- ICO Children's Code Standard 7 (age-appropriate application)
- Israel Privacy Protection Law §11

Per user memory, Israel is in scope and Hebrew is a secondary language; US
and EU are potentially in scope. The hardest framework wins (COPPA <13).

## Files

- `src/student/full-version/src/pages/register.vue` (add gate before form)
- `src/student/full-version/src/pages/onboarding.vue` (branch on age tier)
- `src/student/full-version/src/components/onboarding/AgeGateStep.vue` (NEW)
- `src/student/full-version/src/components/onboarding/ParentalConsentStep.vue` (NEW)
- `src/student/full-version/src/plugins/i18n/locales/{en,ar,he}.json` (consent + parent strings)
- `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` (add age + consent fields)
- `src/actors/Cena.Actors/Events/LearnerEvents.cs` (add `AgeAndConsentRecorded_V1`)
- `src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs` (gate onboarding on consent)
- `src/api/Cena.Student.Api.Host/Endpoints/AuthEndpoints.cs` (parent consent challenge endpoint)
- Server upcaster for existing rows: default `consentStatus = unknown_needs_reverification`

## Definition of Done

1. New `/api/auth/age-check` endpoint accepts a DOB and returns the required
   consent path (none, child-self, parent-required).
2. New `/api/auth/parent-consent-challenge` endpoint sends a verifiable
   challenge email to parent (per COPPA §312.5(b)(2)) and creates a
   pending consent token.
3. New `/api/auth/parent-consent-verify/{token}` endpoint accepts the parent's
   verification and marks the consent active.
4. Register form rejects submission with no DOB, with no parent email for <13,
   and with no completed parent challenge for <13.
5. `StudentProfileSnapshot` carries DOB, AgeAtRegistration, ParentEmail,
   ParentalConsentRecord. Upcaster handles existing rows without breaking
   read-side projections.
6. All three locales have full consent / parent strings.
7. Privacy Policy + Terms link visible on register, login, forgot-password
   (delivers FIND-privacy-002 too if you want to bundle, but that finding
   has its own queue row).
8. E2E test covering the three age branches.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-001-age-gate`. In `complete --result`:

- file:line of every new endpoint
- screenshots of register flow at each age branch (Playwright)
- the upcaster strategy used for existing rows
- the parent-challenge email template path
- sql migration / event-store catch-up plan if any

## Out of scope

- The mobile Flutter age service (mobile is planned, not built)
- Adult-only product features (FIND-privacy-009 DPIA covers them)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_4c77eb4b3436`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
