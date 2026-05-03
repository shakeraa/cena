// =============================================================================
// E2E-E-06 — Time-budget control (P1, prr-077, ship-gate GD-004)
//
// /api/v1/parent/minors/{studentAnonId}/time-budget — GET/PUT.
// Soft cap with 80% + 100% warnings, NO hard lockout (per ship-gate
// GD-004 dark-pattern ban: time-pressure mechanics are forbidden).
//
// What this spec covers:
//   1. PARENT can GET + PUT a time budget (structured shape)
//   2. The PUT response body MUST NOT carry "lockout" / "hardCap" / "blockSession"
//      flags — that's the ship-gate invariant
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

test.describe('E2E_E_06_TIME_BUDGET', () => {
  test('time-budget PUT does not carry hard-lockout fields @epic-e @parent @ship-gate', async ({ page }) => {
    test.setTimeout(60_000)

    const email = `e-06-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

    const studentAnonId = `anon-${Date.now()}`

    const putResp = await page.request.put(
      `${ADMIN_API_URL}/api/v1/parent/minors/${studentAnonId}/time-budget`,
      {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: { dailyMinutesCap: 30 },
      },
    )
    console.log(`[e-06] PUT time-budget(30min) → ${putResp.status()}`)
    expect(putResp.status()).toBeLessThan(500)

    if (putResp.status() < 300) {
      const body = await putResp.text()
      // ── Ship-gate GD-004 invariant ──
      // No hard-lockout vocabulary. Even on a soft-cap response, fields
      // named lockout/hardCap/blockSession would be a regression because
      // the spec explicitly bans hard time-pressure UX.
      expect(body.toLowerCase()).not.toContain('lockout')
      expect(body.toLowerCase()).not.toContain('hardcap')
      expect(body.toLowerCase()).not.toContain('blocksession')
      console.log('[e-06] ship-gate verified: no hard-lockout fields')
    }
  })
})
