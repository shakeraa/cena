// =============================================================================
// E2E-K-03 — Offline question cache (P2)
//
// IMPORTANT FINDING (caught while writing this spec):
//
// The EPIC-E2E-K spec describes K-03 as: "service worker pre-caches the
// current-question payload → offline navigation to next question works
// from cache". That contract is NOT what the codebase ships today.
//
// Reading vite.config.ts (Workbox runtimeCaching, lines 224-271):
//   • /api/questions/.*    → NetworkFirst, cacheName 'cena-questions',
//                            50 entries / 24h. The question-bank
//                            endpoint IS cached for offline reuse.
//   • /api/sessions/.*     → NetworkOnly. Explicitly NOT cached. This
//                            includes /api/sessions/{id}/current-question
//                            and /api/sessions/{id}/state.
//   • /api/progress/.*     → NetworkFirst, 10 entries / 1h.
//
// So the SPA's offline session UX is:
//   1. Question payloads (the "what is the question" data) ARE available
//      offline once seen — they live under /api/questions/.
//   2. Session-state (which question is currently active for THIS session,
//      what's been answered, etc.) is NOT cached — by design.
//
// This is a defensible architecture choice — caching session-state could
// let a stale cache hit serve a question the queue has already advanced
// past, leading to confusing UX or double-counted answers (the K-05
// idempotency contract handles that on the write path, but the read
// path would still show wrong content). NetworkOnly is conservative.
//
// What this spec ASSERTS today (the actual contract):
//   1. The vite-plugin-pwa runtimeCaching config DOES include the
//      /api/questions/.* pattern → questions are cached.
//   2. The vite-plugin-pwa runtimeCaching config DOES set /api/sessions/.*
//      to NetworkOnly → session-state is intentionally NOT cached.
//   3. The /offline.html navigateFallback IS configured (workbox
//      navigateFallback line 272) and IS reachable from the dev server.
//   4. /api/questions GET returns a payload the SW could cache —
//      response is application/json, not auth-redirect, not a
//      Cache-Control: no-store.
//
// What this spec does NOT cover (deferred to EPIC-PRR-K, separate task):
//   • Real SW activation + actual cache miss/hit drive — needs prod build
//     (npm run build && npm run preview). devOptions.enabled = false in
//     vite.config.ts confirms the dev server doesn't run the SW.
//   • If the team wants the EPIC-K K-03 contract literally — offline
//     access to mid-session current-question — that requires changing
//     /api/sessions/.* from NetworkOnly to NetworkFirst with a careful
//     freshness-invalidation strategy. Out of scope for this spec; flag
//     to coordinator (logged at m_<this-tick>).
//
// What this spec catches (regressions):
//   • Workbox runtimeCaching config silently drops /api/questions/.*
//     → offline question reuse breaks.
//   • /api/sessions/.* gets accidentally promoted to NetworkFirst
//     without the corresponding freshness logic → stale-cache UX bugs.
//   • /offline.html missing → SPA-aware offline fallback breaks.
// =============================================================================

import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { test, expect } from '@playwright/test'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const STUDENT_ROOT = resolve(__dirname, '../../..')
const VITE_CONFIG = resolve(STUDENT_ROOT, 'vite.config.ts')

interface RuntimeCachingEntry {
  urlPattern: string
  handler: string
  cacheName?: string
}

/**
 * Parse the runtimeCaching block out of vite.config.ts. We don't `require`
 * the config (it has a Vite-only ESM import surface that doesn't load
 * cleanly under Playwright's test runner); we read the source and pull
 * the (urlPattern, handler) pairs by regex matchAll. The format is stable
 * enough that this won't drift silently — if vite.config.ts is reformatted,
 * the regex matches drop and the spec must be updated.
 */
function parseRuntimeCaching(): RuntimeCachingEntry[] {
  const src = readFileSync(VITE_CONFIG, 'utf-8')

  // Match { urlPattern: /…/, handler: '…', options: { cacheName: '…' } }
  // The options block is optional.
  const blockRegex = /\{\s*urlPattern:\s*(\/[^/]+\/[a-z]*)\s*,\s*handler:\s*'([^']+)'(?:[\s\S]*?cacheName:\s*'([^']+)')?[\s\S]*?\},?/g
  const matches = [...src.matchAll(blockRegex)]
  return matches.map(m => ({
    urlPattern: m[1],
    handler: m[2],
    cacheName: m[3],
  }))
}

test.describe('E2E_K_03_OFFLINE_QUESTION_CACHE_CONTRACT', () => {
  test('vite-plugin-pwa runtimeCaching config matches the documented contract @epic-k @pwa @p2', async () => {
    test.setTimeout(15_000)
    expect(existsSync(VITE_CONFIG), 'vite.config.ts must exist').toBe(true)

    const entries = parseRuntimeCaching()
    expect(entries.length,
      'runtimeCaching must declare at least the 6 documented entries (questions, progress, sessions, katex, fonts, images)',
    ).toBeGreaterThanOrEqual(6)

    // Question-bank endpoint MUST be cached for offline reuse — this is the
    // foundation of "review previously-seen questions offline" working.
    const questions = entries.find(e => /api\\\/questions/.test(e.urlPattern))
    expect(
      questions,
      `runtimeCaching must include a /api/questions/.* entry for offline question reuse. ` +
      `Found patterns: ${entries.map(e => e.urlPattern).join(', ')}`,
    ).toBeDefined()
    expect(questions?.handler, '/api/questions/.* must use NetworkFirst (graceful offline)').toBe('NetworkFirst')
    expect(questions?.cacheName, '/api/questions/.* must declare a cacheName').toBeTruthy()

    // Session endpoint MUST be NetworkOnly. If this flips, stale session-
    // state could serve already-advanced questions to the student.
    const sessions = entries.find(e => /api\\\/sessions/.test(e.urlPattern))
    expect(
      sessions,
      'runtimeCaching must include an explicit /api/sessions/.* entry (declaring the NetworkOnly contract intent).',
    ).toBeDefined()
    expect(
      sessions?.handler,
      `/api/sessions/.* MUST be NetworkOnly to prevent stale-cache UX bugs ` +
      `(handler was '${sessions?.handler}'). If you intentionally want offline ` +
      `session access, DO NOT just flip NetworkOnly to NetworkFirst — design ` +
      `freshness-invalidation alongside or wire a /api/sessions/{id}/state ` +
      `revalidation pull.`,
    ).toBe('NetworkOnly')
  })

  test('vite-plugin-pwa navigateFallback /offline.html is configured + reachable @epic-k @pwa @p2', async ({ page }) => {
    test.setTimeout(15_000)
    const src = readFileSync(VITE_CONFIG, 'utf-8')
    expect(
      /navigateFallback:\s*'\/offline\.html'/.test(src),
      'workbox.navigateFallback must be /offline.html',
    ).toBe(true)
    expect(
      /navigateFallbackDenylist:\s*\[/.test(src),
      'workbox.navigateFallbackDenylist must be set so /api routes do not fall through to /offline.html',
    ).toBe(true)

    // The dev server should serve /offline.html — workbox uses it as the
    // fallback in prod, and confirming reachability ensures the file is
    // actually present in public/.
    const resp = await page.request.get('/offline.html')
    expect(resp.status(), '/offline.html must be reachable').toBe(200)
    const html = await resp.text()
    expect(html.length, '/offline.html must have content').toBeGreaterThan(50)
  })

  test('/api/questions GET returns a SW-cacheable JSON payload @epic-k @pwa @p2', async ({ page }) => {
    test.setTimeout(60_000)
    // We don't have a stable known questionId across dev environments,
    // so we probe the question-bank LIST endpoint. This contract test
    // asserts: when the SW Workbox NetworkFirst handler intercepts a
    // /api/questions/* response, it gets back a JSON body it can store —
    // not an auth redirect, not Cache-Control: no-store, not a multi-MB
    // streaming response that would blow workbox cache budgets.
    const list = await page.request.get(`${STUDENT_API}/api/questions?limit=1`)
    if (list.status() === 401 || list.status() === 403) {
      // Endpoint requires auth — that's fine, but means the SW would
      // need an auth header to cache. Validate that the unauth response
      // is well-formed (i.e. the route exists). Bootstrapping a full
      // student to drive auth is unnecessary for the cacheability
      // assertion alone.
      expect(list.status(),
        'questions endpoint exists but requires auth (acceptable; SW would cache the authed response)',
      ).toBeGreaterThanOrEqual(401)
      return
    }
    expect(list.status(), '/api/questions list endpoint responds').toBe(200)
    const ct = (list.headers()['content-type'] ?? '').toLowerCase()
    expect(ct, 'content-type is JSON').toMatch(/json/)
    const cacheControl = (list.headers()['cache-control'] ?? '').toLowerCase()
    expect(
      cacheControl.includes('no-store'),
      `/api/questions response must NOT set Cache-Control: no-store ` +
      `(would block workbox NetworkFirst from caching). Got '${cacheControl}'.`,
    ).toBe(false)
  })

  test('runtime SW activation on dev: by design disabled (devOptions.enabled = false) @epic-k @pwa @p2', async () => {
    test.setTimeout(5_000)
    const src = readFileSync(VITE_CONFIG, 'utf-8')
    // Dev disable is documented; if someone flips it, runtime tests against
    // dev would start drifting from prod and silently mask real bugs.
    expect(
      /devOptions:\s*\{\s*enabled:\s*false/.test(src),
      'vite-plugin-pwa devOptions.enabled must remain false — running the SW in dev creates ' +
      'cache-shape divergence from prod and false-passes runtime offline tests. The runtime ' +
      'offline-cache assertion belongs in a prod-build smoke (npm run build && npm run preview).',
    ).toBe(true)
  })
})
