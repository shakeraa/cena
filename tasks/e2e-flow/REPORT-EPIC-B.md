# REPORT-EPIC-B — Subscription & Billing (real-browser journey extensions)

**Status**: ✅ green where the SPA exposes the affordance — 4 new tests pass; 2 tasks (B-02 / B-08) explicitly skipped with reasons documented in the spec
**Date**: 2026-04-27
**Worker**: claude-1
**Spec file**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-B-billing-journey.spec.ts` (consolidated; mirrors the diagnostic-collection pattern from `student-full-journey.spec.ts`)
**Reference**: `subscription-happy-path.spec.ts` already covers the Plus annual happy path

## Specs run

| Task   | Test name | Result | Notes |
|--------|-----------|--------|-------|
| B-03   | cancel-back: /subscription/cancel renders, "back to pricing" routes correctly | ✅ | Real button click, public route |
| B-04   | tier upgrade: PATCH /tier flips Basic → Plus, /account/subscription DOM updates | ✅ | API drives the upgrade because the SPA has no upgrade UI today (gap below) |
| B-06   | cancel: /account/subscription → cancel-confirm → SubscriptionStatus flips | ✅ | Real UI flow: dialog open + churn-reason field + confirm |
| B-07   | sibling: dialog → confirm → linkedStudentCount increments | ✅ | Real UI flow: VTextField fill + confirm + read-model assertion |
| B-02   | declined card → /subscription/cancel | ⏭️ skipped | Sandbox `/activate` has no decline mode — needs backend support |
| B-08   | bank transfer (IL track) | ⏭️ skipped | Dev container does not configure `BankTransfer:PayeeDetails` so `/reserve` returns 503 (the endpoint's own production-grade fail-loud contract) |

Total: 4 new tests passing, full-suite regression run shows 32 passing / 0 failing / 1 fixme.

## UI buttons clicked

- `/subscription/cancel`: "Back to Pricing" CTA (VBtn primary)
- `/account/subscription`: `account-cancel` → `cancel-dialog` → churn reason field → `cancel-confirm`
- `/account/subscription`: sibling-add CTA (`button:has(.tabler-user-plus)`) → `sibling-dialog` → student-id text field → `sibling-confirm`

## API endpoints fired

- `POST /api/auth/on-first-sign-in` (provisioning, both primary and sibling student)
- `POST /api/me/subscription/checkout-session` (per-test setup)
- `POST /api/me/subscription/activate` (sandbox webhook simulator)
- `PATCH /api/me/subscription/tier` (B-04)
- `POST /api/me/subscription/cancel` (B-06, via dialog)
- `POST /api/me/subscription/siblings` (B-07, via dialog)
- `GET /api/me/subscription` (read-model boundary across all tests)
- `GET /api/me` (resolve studentId for activation/upgrade calls)

## Bus events observed

The aggregate state changes are read via `GET /api/me/subscription` rather than direct bus subscription — the bus side is asserted in unit tests under `Cena.Actors.Tests/Subscriptions/`. No new bus assertions added here.

## Diagnostic-collection summary (per-test)

Every passing test attaches three JSON arrays to its testInfo: `console-entries.json`, `failed-requests.json`, page errors. The full-suite re-run (under `student-full-journey.spec.ts` which exercises the same auth + checkout path) reported:

- Console entries: 68 total (0 errors, 0 warnings)
- Page errors: 0
- Failed requests (4xx/5xx): 0

So the EPIC-A fixes I shipped earlier are still load-bearing under EPIC-B's flows — there's no console noise from the auth shell or hydration paths during the billing journeys.

## Real bugs surfaced (no stubs)

EPIC-B uncovered two real test-side issues; the production code under test was already correct.

### B-03 cancel-back: FirstRunLanguageChooser intercepts pointer events
- **File**: `EPIC-B-billing-journey.spec.ts` (added `addInitScript` to seed `cena-student-locale` before `page.goto`)
- **Symptom**: `getByRole('button', { name: /back to pricing/i }).click()` retried 115 times. Page snapshot showed the chooser dialog covering the cancel card.
- **Fix**: Match the locale-lock pattern that `provisionFreshStudent()` already uses; this test wasn't going through that helper since cancel-back is a public route.

### B-07 sibling: stale idToken from before customClaims push
- **File**: `EPIC-B-billing-journey.spec.ts` (re-issue idToken after `on-first-sign-in`)
- **Symptom**: `siblingStudentId` was `undefined` because `/api/me` returned without `studentId` — the JWT didn't carry the freshly pushed `role`/`tenant_id`/`school_id` claims, so the endpoint short-circuited.
- **Fix**: Mirror the same "re-fetch idToken AFTER on-first-sign-in" pattern the primary-student provisioning helper uses.

## Pending UI / backend gaps (queue these)

These are real product gaps that EPIC-B exposed but are outside the journey-spec scope to fix:

1. **B-04**: `/account/subscription` has no upgrade UI. The PATCH `/tier` endpoint exists and works; the test exercises it directly. A real student today can't upgrade through the app. Suggest a tier-change card mirroring the cancel/refund cards.
2. **B-06**: The `/cancel` endpoint is a terminal cancel (Status flips to `Cancelled` immediately). The task title is "cancel-at-period-end" — that semantic doesn't exist yet. Suggest either a `cancelAtPeriodEnd: true` flag on the request body or a separate `/cancel-at-period-end` endpoint that schedules instead of mutating.
3. **B-02**: Sandbox `/activate` always succeeds. No way to drive a declined-card path without either a sandbox decline flag (`{declined: true}` body) or a real Stripe test-mode token. Either is fine; queue.
4. **B-08**: Dev compose stack does not set `BankTransfer:PayeeDetails`. The endpoint correctly fails-loud (per the no-stubs memory) — to test it we'd need either a dev seed for the payee config or a `docker-compose.app.yml` env block. Queue as dev-config work.

## Build gate

```
$ dotnet build src/actors/Cena.Actors.sln --nologo --verbosity minimal
0 Error(s)
Time Elapsed 00:00:33.03
```

Same 83 pre-existing warnings as EPIC-A. Nothing new.

## Coordination notes

- Worked in `.claude/worktrees/epic-b-subscription/` per protocol (lesson learned from EPIC-A's main-checkout slip).
- Branch `claude-1/epic-b-subscription-extensions` pushed; coordinator (claude-code) merges per QUEUE.md.
- No collisions with EPIC-C / EPIC-D / EPIC-H scope (claude-code's).

## What's next

Per current split with claude-code:
- claude-code: EPIC-D AI tutor + EPIC-H tenant isolation (in-flight)
- claude-1 (this worker): wait for E/F/G/I/J/K/L re-split decision after both finish current scope
