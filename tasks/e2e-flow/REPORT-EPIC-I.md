# REPORT-EPIC-I — GDPR / COPPA parental consent (real-browser journey)

**Status**: ✅ green for the front half (age-gate → consent → credentials). Back half (parent verification email) deferred — gated on consent-email worker.
**Date**: 2026-04-27
**Worker**: claude-1
**Spec file**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-I-consent-journey.spec.ts`

## What this spec exercises

Drives the real `/register` flow for a **14-year-old registrant** (teen tier — requires parental consent):

1. Fill `age-gate-dob` with a DOB making the registrant 14
2. Click `age-gate-next` → SPA advances to `parental-consent-step`
3. Fill `parent-email` field
4. Check `consent-checkbox`
5. Click `consent-next` → SPA advances to credentials form
6. Fill `auth-display-name`, `auth-email`, `auth-password`
7. Click `auth-submit` → SPA lands on `/onboarding`

This proves the multi-step consent UX wires correctly: each step's data persists into the next, the SPA's age-gate evaluator correctly classifies age 14 as teen-tier (not under-13 strict-COPPA, not adult-no-consent), and the backend's `on-first-sign-in` carries the parent-email metadata through the registration.

## Buttons / fields touched

- `age-gate-dob` input
- `age-gate-next` button
- `parental-consent-step` container (asserted visible)
- `parent-email` input
- `consent-checkbox` (Vuetify VCheckbox — fill via inner `<input>`)
- `consent-next` button
- `auth-display-name`, `auth-email`, `auth-password` inputs
- `auth-submit` button
- `onboarding-page` (asserted visible — proves end-state)

## API endpoints fired

- Firebase emu `accounts:signUp` (the SPA's register flow)
- `POST /api/auth/on-first-sign-in` (backend bootstrap)

## Gaps surfaced

These are queued because they need either backend support or back-half flow coverage:

- **I-04 audit-export**: parent receives the verify email (Firebase emu OOB type `VERIFY_EMAIL`), clicks the link, becomes verified — needs the consent-email worker to actually emit. Backend smoke-tested in unit specs.
- **I-01 misconception-retention**: tied to ADR-0003 retention worker; not a UI-driven journey but a TTL-side test.
- **I-03 RTBF-crypto-shred**: parent-initiated RTBF flow has its own test path under `parent-child-binding.spec.ts`; this consent spec doesn't double up.
- **I-06 age-band-consistency**: lives in unit tests for `@cena/age-gate` package.

## Diagnostics

Console errors / page errors / failed requests collected — all empty for the consent → credentials → onboarding path.

## Build gate

Same full-suite regression as the rest of EPIC-F-K: 39 passed / 1 fixme.

## What's next

The next vertical slice is the **consent-email back half**: implement the worker that listens for `ParentConsentRequested_V1` events and posts to the Firebase emu's `sendOobCode`. Once that's in place, an extension to this spec can fetch the OOB code, verify it, and assert `/api/me/consent` flips to `parental_consent_given=true`.
