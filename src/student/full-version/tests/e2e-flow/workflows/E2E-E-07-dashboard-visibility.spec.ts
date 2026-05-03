// =============================================================================
// E2E-E-07 — Dashboard-visibility age-band filter (P1, prr-052)
//
// /api/me/parent-dashboard returns the parent's view of their bound
// children. The visibility-filter shrinks the field set as a child
// crosses age thresholds (12 → 13 → 14) per ICO Children's Code Std 3+7.
// /api/parent/visibility/* exposes the live filter.
//
// What this spec covers (contract surface):
//   1. PARENT reaches /api/me/parent-dashboard without 5xx
//   2. The response shape is structured (status code < 500), even if
//      the parent has no bound children (returns empty list)
//   3. Per-child fields exposed via the dashboard surface MUST NOT
//      include age-restricted fields like masteryBreakdown,
//      misconceptionPattern at the surface level — those are gated
//      by the visibility filter
// =============================================================================

import { test, expect } from '@playwright/test'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

test.describe('E2E_E_07_DASHBOARD_VISIBILITY', () => {
  test('parent dashboard surface bounded; restricted fields not in default response @epic-e @gdpr @parent', async ({ page }) => {
    test.setTimeout(60_000)

    const email = `e-07-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
        data: { localId, customAttributes: JSON.stringify({ role: 'PARENT', school_id: SCHOOL_ID, locale: 'en' }) },
      },
    )
    await page.waitForTimeout(300)
    const tok = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tok.json() as { idToken: string }

    const dashResp = await page.request.get(`${STUDENT_API}/api/me/parent-dashboard`, {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    console.log(`[e-07] GET /api/me/parent-dashboard → ${dashResp.status()}`)
    // 200 (empty children list) or 403 (premium-tier-gated per file
    // banner) — either is acceptable; 5xx is the regression.
    expect(dashResp.status()).toBeLessThan(500)

    if (dashResp.status() === 200) {
      const body = await dashResp.text()
      // ── prr-052 invariant ──
      // The default dashboard surface MUST NOT carry restricted-field
      // names. If a child is 13+ and these fields appeared, that's
      // FIND-privacy-010 violation.
      expect(body.toLowerCase()).not.toContain('misconceptionpattern')
      // masteryBreakdown is restricted for 14+ per the task body —
      // shouldn't surface in the default shape either.
      expect(body.toLowerCase()).not.toContain('"masterybreakdown"')
      console.log('[e-07] no restricted-field names leaked on default shape')
    }
  })
})
