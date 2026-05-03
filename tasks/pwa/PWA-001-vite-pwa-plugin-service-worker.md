# PWA-001: Vite PWA Plugin + Service Worker Foundation

## Goal
Install and configure `vite-plugin-pwa` (Workbox-based) in the Vue 3 student app at `src/student/full-version/`. Produce a production-grade Service Worker that handles asset caching, API request strategies, and update lifecycle — not a tutorial "hello world" SW.

## Context
- Student app: `src/student/full-version/` — Vue 3 + Vite + TypeScript
- Architecture doc: `docs/research/cena-mobile-pwa-approach.md` §2.1
- The SW must support offline question review (PWA-005), camera upload queuing (PWA-006), and figure asset caching (PWA-007)
- This task builds the foundation; subsequent tasks wire specific features into it

## Scope of Work

### 1. Install & Configure `vite-plugin-pwa`
- `npm install -D vite-plugin-pwa`
- Configure in `vite.config.ts` with `VitePWA()` plugin
- Mode: `generateSW` (Workbox generates the SW; we don't need custom SW logic yet)
- Scope: `/` (entire app)

### 2. Caching Strategies (Workbox `runtimeCaching`)
Define these route-specific strategies — justify each choice in code comments:

| Route Pattern | Strategy | Max Entries | Max Age | Why |
|---------------|----------|-------------|---------|-----|
| `/api/questions/*` | NetworkFirst | 50 | 24h | Fresh questions preferred, cached fallback for offline |
| `/api/progress/*` | NetworkFirst | 10 | 1h | Mastery data must be fresh |
| `/api/sessions/*` | NetworkOnly | — | — | Session state is real-time, no caching |
| `*.woff2`, `*.woff` | CacheFirst | 30 | 30d | Fonts never change |
| `*.js`, `*.css` | CacheFirst (hashed) | — | — | Vite content-hashes these; immutable |
| `/katex/*` | CacheFirst | 50 | 30d | KaTeX fonts/CSS are versioned |
| `*.png`, `*.svg`, `*.jpg` | StaleWhileRevalidate | 100 | 7d | Figure assets; fresh preferred but stale ok |

### 3. Precache Manifest
- Precache the app shell: `index.html`, main JS/CSS bundles, critical fonts
- Do NOT precache lazy-loaded chunks (JSXGraph ~250KB, MathLive ~300KB) — these cache on first use via runtime caching
- Verify precache manifest size stays under 500KB (critical for first install on slow networks)

### 4. Update Lifecycle
- Implement `registerSW` with `onNeedRefresh` callback
- Show a non-intrusive toast: "New version available — tap to update"
- On user confirmation: `updateSW(true)` → `skipWaiting()` → reload
- Do NOT auto-reload during an active session (check `sessionStore.isActive` before triggering reload)
- If session is active, defer update until session ends (queue the update, apply on next session start)

### 5. SW Registration
- Register in `src/student/full-version/src/main.ts` (or dedicated `registerSW.ts`)
- Add `navigator.serviceWorker` support check
- Log SW lifecycle events (`installing`, `waiting`, `active`, `redundant`) to structured console output for debugging
- No registration in dev mode (`import.meta.env.DEV` → skip)

## Files to Create/Modify
- `src/student/full-version/vite.config.ts` — add VitePWA plugin config
- `src/student/full-version/src/registerSW.ts` — SW registration + update lifecycle
- `src/student/full-version/src/composables/useServiceWorker.ts` — reactive SW state (updateAvailable, isOffline, etc.)
- `src/student/full-version/src/components/UpdateToast.vue` — update notification UI

## Non-Negotiables
- **No `skipWaiting()` without user consent** — silent updates mid-session can corrupt state
- **No catch-all CacheFirst** — API routes must be NetworkFirst or NetworkOnly; stale API data = wrong mastery, wrong questions
- **Precache manifest under 500KB** — measure and log the size in build output
- **CSP compatibility** — the SW must work with the existing `Content-Security-Policy` headers (no `unsafe-eval`, no `unsafe-inline` for SW)

## Acceptance Criteria
- [ ] `npm run build` produces `sw.js` in `dist/`
- [ ] `sw.js` registers successfully on Chrome, Safari 16.4+, Firefox
- [ ] Lighthouse PWA audit scores 100/100 (installable, SW registered, offline-capable)
- [ ] Network tab shows correct caching strategies per route type
- [ ] Update toast appears when a new build is deployed
- [ ] Update does NOT trigger during an active learning session
- [ ] Precache manifest logged at build time; size < 500KB
- [ ] No console errors in production build

## Testing Requirements
- **Unit**: `useServiceWorker.ts` composable — mock `navigator.serviceWorker`, test state transitions
- **Integration**: Playwright test — build, serve with `serve -s dist`, verify SW registration, verify offline page loads from cache
- **Manual**: Test on real iOS Safari 16.4+ (iPhone SE or newer) and Android Chrome — SW behavior differs significantly between browsers

## DoD
- PR merged to `main`
- Lighthouse PWA audit screenshot attached to PR
- Precache manifest size documented in PR description
- No regressions in existing student app functionality

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-sw-foundation,lighthouse_score=<n>,precache_kb=<n>`
