// =============================================================================
// E2E-E-03 — One-click unsubscribe (P1, prr-051)
//
// GET /unsubscribe/{token} — anonymous, token-authed. Single-use. Token
// validation gate enforces:
//   - missing/empty token → 400
//   - garbage/expired token → 410 or similar (single-use semantics)
//   - well-formed but never-issued token → 404 / 410
//
// We can't easily mint a real token in dev (requires the digest worker
// to issue one + persist it). Contract: the route is reachable, returns
// a structured outcome (no 5xx), and rejects bogus inputs cleanly.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'

test.describe('E2E_E_03_UNSUBSCRIBE', () => {
  test('unsubscribe token route rejects bogus tokens cleanly @epic-e @parent @compliance', async ({ page }) => {
    test.setTimeout(30_000)

    // Garbage token — must NOT 5xx.
    const garbage = await page.request.get(`${ADMIN_API_URL}/unsubscribe/this-is-not-a-real-token`)
    console.log(`[e-03] GET /unsubscribe/<garbage> → ${garbage.status()}`)
    expect(garbage.status()).toBeLessThan(500)
    expect(garbage.status()).toBeGreaterThanOrEqual(400)

    // Empty-segment token — route may not match; anything < 500 is OK.
    const empty = await page.request.get(`${ADMIN_API_URL}/unsubscribe/`)
    console.log(`[e-03] GET /unsubscribe/ → ${empty.status()}`)
    expect(empty.status()).toBeLessThan(500)
  })
})
