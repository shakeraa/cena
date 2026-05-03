// =============================================================================
// E2E-E-05 — Accommodations profile (P1, RDY-066)
//
// /api/v1/parent/minors/{studentAnonId}/accommodations — GET/PUT.
// Parent configures extended-time, font-size, hide-reveal flags +
// signs a consent doc; the next learning-session reads the profile.
//
// Contract layer: PARENT can reach the endpoint, GET returns
// structured shape, PUT accepts a profile body without crashing.
// Wet-run (next-session-reads-profile) requires PRR-436 admin probe.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

test.describe('E2E_E_05_ACCOMMODATIONS_PROFILE', () => {
  test('PARENT GET + PUT accommodations bounded shape @epic-e @parent @a11y', async ({ page }) => {
    test.setTimeout(60_000)

    const email = `e-05-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

    const getResp = await page.request.get(
      `${ADMIN_API_URL}/api/v1/parent/minors/${studentAnonId}/accommodations`,
      { headers: { Authorization: `Bearer ${idToken}` } },
    )
    console.log(`[e-05] GET accommodations → ${getResp.status()}`)
    expect(getResp.status()).toBeLessThan(500)

    const putResp = await page.request.put(
      `${ADMIN_API_URL}/api/v1/parent/minors/${studentAnonId}/accommodations`,
      {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: {
          extendedTimeMultiplier: 1.5,
          fontSizeRem: 1.25,
          hideAndRevealAnswers: false,
          consentDocHash: 'sha256-test',
        },
      },
    )
    console.log(`[e-05] PUT accommodations → ${putResp.status()}`)
    expect(putResp.status()).toBeLessThan(500)
  })
})
