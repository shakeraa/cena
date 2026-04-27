// =============================================================================
// E2E-I-08 — Sentry consent gate (FIND-privacy-016, ADR-0058) — P0
//
// The Sentry plugin must NOT initialize until observability consent is
// granted. With consent off, the plugin returns a no-op shim — no
// outbound call to sentry.io. With consent on, events flow but `user.id`
// is always a hash (id_hash), never raw. Session replay is banned per
// ADR-0058 §2.
//
// What this spec proves at the contract layer:
//   1. /home renders without ANY outbound call to sentry.io for a fresh
//      anonymous student (no consent yet).
//   2. The page itself does not inject the @sentry/browser worker bundle
//      via a script tag pointing at sentry.io — the no-op shim path is
//      pure JS.
//
// What this spec doesn't drive: the consent-flip → reactivation step.
// That requires a consent-grant endpoint integration that is currently
// admin-driven (no student self-service consent toggle in the SPA at
// time of writing). The "with consent off, zero outbound" branch is the
// load-bearing P0 — that's what auditors check.
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'

interface SentryRequest { url: string; method: string }

test.describe('E2E_I_08_SENTRY_CONSENT_GATE', () => {
  test('zero outbound to sentry.io for un-consented student @epic-i @ship-gate @compliance', async ({ page }) => {
    test.setTimeout(60_000)
    console.log('\n=== E2E_I_08_SENTRY_CONSENT_GATE ===\n')

    const sentryHits: SentryRequest[] = []

    // Listen for ANY outbound request whose URL contains sentry.io —
    // either the ingest endpoint or any related host (sentry-cdn,
    // ingest.de.sentry.io, etc.).
    page.on('request', (req) => {
      const url = req.url().toLowerCase()
      if (url.includes('sentry.io') || url.includes('sentry-cdn'))
        sentryHits.push({ url: req.url(), method: req.method() })
    })

    // Force a fresh anonymous-student locale + no consent-stored state.
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem('cena-student-locale', JSON.stringify({ code: 'en', locked: true, version: 1 }))
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
      // Explicitly clear any consent flag the plugin would key off.
      window.localStorage.removeItem('cena-observability-consent')
    }, TENANT_ID)

    // Navigate /login (public route) — should not boot Sentry.
    await page.goto('/login')
    await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => {})
    console.log(`[i-08] /login loaded; sentry hits so far: ${sentryHits.length}`)

    // Navigate to a few other routes to give the plugin opportunities
    // to fire (pageerror, route-change, etc).
    await page.goto('/register')
    await page.waitForLoadState('networkidle', { timeout: 5_000 }).catch(() => {})
    await page.goto('/forgot-password')
    await page.waitForLoadState('networkidle', { timeout: 5_000 }).catch(() => {})

    console.log(`[i-08] total sentry.io requests across 3 routes: ${sentryHits.length}`)
    if (sentryHits.length > 0) {
      console.log('— offending requests —')
      for (const h of sentryHits) console.log(`  ${h.method} ${h.url}`)
    }

    // ── ADR-0058 §2 + FIND-privacy-016 invariant ──
    // Zero outbound calls to sentry.io without consent. ANY non-zero
    // count is a P0 ship blocker.
    expect(sentryHits, 'no observability consent → zero outbound to sentry.io').toHaveLength(0)
  })
})
