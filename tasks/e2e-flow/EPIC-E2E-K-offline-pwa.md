# EPIC-E2E-K — Offline / PWA behavior

**Status**: Proposed
**Priority**: P2 (degraded-mode nice-to-have; offline use is rare on the adult-supervised education flow)
**Related**: [RDY-075](../readiness/done/RDY-075-f4-offline-pwa-sync.md), PWA-001..007, [Memory: PWA over Flutter](../../.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_flutter_mobile.md)

---

## Why this exists

We replaced Flutter with PWA (2026-04-13). Offline support is the piece that made Flutter attractive; the PWA replacement must land that functionality or we ship worse than before.

## Workflows

### E2E-K-01 — Install the PWA (manifest + service worker)

**Journey**: student visits SPA in Chrome → Install prompt appears (manifest valid) → install → launches standalone → routes work offline after first load.

**Boundaries**: manifest.json present + valid, service worker registered, standalone mode detected.

**Regression caught**: manifest invalid (no install prompt); service worker not registering; standalone routes break.

### E2E-K-02 — Offline session answer queue

**Journey**: student signed in → goes offline (network blocker on) → continues session → answers queued locally (localStorage) → goes online → queue flushes to backend → mastery catches up.

**Boundaries**: DOM (offline indicator, answers visually accepted), localStorage queue size grows while offline, backend receives queue on reconnect, no duplicate answers on re-submission (idempotency key client-supplied).

**Regression caught**: queue silently drops on reload; duplicate answers on flush; offline indicator wrong.

### E2E-K-03 — Offline question cache

**Journey**: student's current plan's questions are pre-cached by service worker → offline navigation to next question works from cache (first-pass) → fresh questions NOT cached → those routes show offline-unavailable UI.

**Boundaries**: service worker cache names (asset vs data), cache-freshness invalidation on plan update.

**Regression caught**: cache stale after plan regeneration (student sees old questions); cache grows unbounded; cached-sensitive routes (admin) pollute offline cache.

### E2E-K-04 — Update prompt when new SPA build ships

**Journey**: user on version N → version N+1 deployed → SPA detects via service worker → shows "Update available" prompt → user accepts → reload → new version active.

**Boundaries**: update prompt UI, reload preserves route + scroll, next visit runs new build.

**Regression caught**: update never surfaces (users stuck on old version); update loses session state; update loops (prompts every visit).

### E2E-K-05 — Offline sync idempotency (RDY-075)

**Journey**: offline queue has 10 answers → reconnect flushes → first batch partially fails (network mid-flush) → retry → end state: 10 answers recorded, not 20.

**Boundaries**: client idempotency key per answer, server dedup by key, no double-count in mastery.

**Regression caught**: double-count on retry; ghost answers (not surfaced on next visit because server has them but client thinks they failed).

## Out of scope

- Background sync API (experimental; not shipped)
- Offline admin console — admins are always online

## Definition of Done

- [ ] 5 workflows green
- [ ] K-02 (offline queue) + K-05 (idempotency) tagged `@offline @p1` — the two that actually protect mastery data
- [ ] Network-blocker helper in `probes/chaos.ts` (reused from EPIC-E2E-J)
- [ ] Service-worker registration verified in Playwright context (not just happy-path browser launch)
