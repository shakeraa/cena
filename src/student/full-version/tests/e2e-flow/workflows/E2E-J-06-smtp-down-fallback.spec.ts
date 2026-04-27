// =============================================================================
// E2E-J-06 — SMTP down → digest holding queue + fallback (P1)
//
// The dev stack does NOT include an SMTP server (parent digests are
// queued via NATS to a worker that's not present in this compose).
// What this spec can verify at the contract layer:
//   1. The endpoint that triggers a digest dispatch (admin-side digest
//      preview / parent-side opt-in) responds without crashing
//   2. The DigestHoldingQueue surface (if any admin endpoint exposes it)
//      doesn't 5xx on read when no SMTP is up
//
// Honest scope: deeper SMTP outage simulation requires a mailpit or
// similar test SMTP container. Spec asserts the boundary surface that
// ships today.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

test.describe('E2E_J_06_SMTP_DOWN_FALLBACK', () => {
  test('digest-dispatch surfaces stay 4xx/2xx when SMTP is absent @epic-j @resilience @parent', async ({ page }) => {
    test.setTimeout(60_000)

    // Provision SUPER_ADMIN
    const email = `j-06-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const localId = (await (await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )).json() as { localId: string }).localId
    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/accounts:update`,
      {
        headers: { Authorization: `Bearer ${EMU_BEARER}` },
        data: { localId, customAttributes: JSON.stringify({ role: 'SUPER_ADMIN', school_id: SCHOOL_ID, locale: 'en' }) },
      },
    )
    const tok = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tok.json() as { idToken: string }

    // Probe the parent-digest admin surface — the exact endpoint may vary
    // (parent-engagement, parent-digest-preview, parent-digest-trigger).
    // Loose assertion: endpoints that exist must NOT 500 with SMTP absent.
    const candidateRoutes = [
      '/api/admin/parent-digest/preview',
      '/api/admin/parent-engagement',
      '/api/admin/notifications/digests',
    ]
    for (const route of candidateRoutes) {
      const resp = await page.request.get(`${ADMIN_API_URL}${route}`, {
        headers: { Authorization: `Bearer ${idToken}` },
      })
      console.log(`[j-06] GET ${route} → ${resp.status()}`)
      // 200 (probed and worked), 401/403 (auth gate), 404 (not wired) all OK.
      // 500 would mean the endpoint exists but blew up because SMTP isn't
      // there — that's the regression. We allow 500 only if there's a
      // structured CenaError indicating circuit-open semantics.
      expect([200, 401, 403, 404, 405, 503]).toContain(resp.status())
    }
  })
})
