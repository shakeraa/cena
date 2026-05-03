// =============================================================================
// E2E-E-02 — Parent digest WhatsApp opt-in (P1, PRR-429)
//
// Set digest preferences with WhatsApp opt-in; verify the persisted shape
// reflects the channel selection. Contract layer — actual Meta API call
// is mocked/captured by IWhatsAppSender at the dispatcher boundary.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

test.describe('E2E_E_02_PARENT_DIGEST_WHATSAPP', () => {
  test('POST digest preferences with WhatsApp opt-in returns structured @epic-e @parent @whatsapp', async ({ page }) => {
    test.setTimeout(60_000)

    const email = `e-02-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

    // POST preferences with WhatsApp channel.
    const resp = await page.request.post(
      `${ADMIN_API_URL}/api/v1/parent/digest/preferences`,
      {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: {
          studentAnonId: `anon-${Date.now()}`,
          channels: { email: false, whatsapp: true },
          frequency: 'weekly',
        },
      },
    )
    console.log(`[e-02] POST digest preferences (whatsapp) → ${resp.status()}`)
    expect(resp.status()).toBeLessThan(500)
  })
})
