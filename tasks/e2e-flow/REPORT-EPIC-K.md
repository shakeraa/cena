# REPORT-EPIC-K — Offline / PWA (real-browser journey)

**Status**: ✅ green for the offline-banner + recovery cycle. Real "answer queues offline / flushes on reconnect" (K-02) deferred — needs a session-flow that depends on a question being available in the bank.
**Date**: 2026-04-27
**Worker**: claude-1
**Spec file**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-K-offline-journey.spec.ts`

## What this spec exercises

1. Provision + sign in a fresh student (real `/login` form drive)
2. `context.setOffline(true)` — browser drops to offline
3. Assert `OfflineBanner` mounts (locator `.offline-banner`)
4. Attempt nav while offline (may abort — that's fine)
5. Snapshot console errors + page errors during the offline window
6. `context.setOffline(false)` — back online
7. Assert no uncaught JS exceptions throughout

The negative signal: **uncaught JS exceptions while offline**. The SPA's offline-aware composables (`useOfflineQueue`, `useNetworkStatus`, `useEncryptedOfflineCache`, `useSignalRConnection`) must tolerate the network drop without throwing.

## Documented dev-mode gaps (filtered from the assertion)

Two error patterns are inherent to a non-PWA-precached **dev** build going offline. They're explicitly suppressed in the spec via a regex allowlist, with the gap noted:

1. `Failed to fetch dynamically imported module: ...vue?t=...` — Vite dev mode lazy-loads route components, which obviously fails while offline. A real PWA build pre-caches these via Workbox; the dev `vite` server does not.
2. `Failed to read the 'localStorage' property from 'Window': Access is denied` — Playwright/Chromium quirk when an offline navigation aborts mid-evaluation. Not a SPA bug.

**Real follow-up to queue**: PWA precache config. The student SPA has `useServiceWorker.ts` and `InstallPrompt.vue` — production builds do create a precached app shell — but in dev these gaps surface. Worth confirming the production build genuinely precaches the route components so the K-02 promise (offline queue then flush) actually works for installed PWA users.

## Buttons / fields touched

- `/login`: full form drive
- `.offline-banner`: visibility assertion (no class without a banner mounted)
- `context.setOffline(true|false)`: Playwright fixture

## API endpoints fired

- Firebase emu signUp / signIn (provisioning)
- `POST /api/auth/on-first-sign-in` (bootstrap)

## What's NOT here (queued)

- **K-01 PWA install**: needs `beforeinstallprompt` event mock
- **K-02 offline answer queue → flushes on reconnect**: needs a session-start flow with a question available in the bank for the user's level (the dev seed doesn't guarantee one). The `useOfflineQueue` composable exists; the regression assertion needs better seed data.
- **K-03 offline question cache**: depends on K-02
- **K-04 update prompt**: needs build-time hash juggling
- **K-05 offline sync idempotency**: server-side test

## Diagnostics

Per-test JSON: `console-entries.json`, `page-errors.json`, `failed-requests.json`, `offline-snapshot.json` (counts during the offline window).

## Build gate

Full suite: 39 passed / 1 fixme.

## What's next

The most valuable next step is genuine K-02 coverage — needs the dev seed to plant a guaranteed-available question for the new student. Queue alongside the real PWA precache audit.
