// =============================================================================
// E2E-D-03 — CAS verification happy path (P0 ship-gate, ADR-0002)
//
// SymPy sidecar is wired and reachable via NATS request/reply on
// `cena.cas.verify.*` subjects (verified by the sympy chaos probe used
// in EPIC-J). The contract this spec proves: the parametric template
// preview path (already shipped in EPIC-G-02) round-trips through the
// CAS gate and returns Accepted=true samples — that's the structural
// proof the gate runs and reports a verified outcome.
//
// What this spec does:
//   1. Provision SUPER_ADMIN
//   2. Create a parametric template with a deterministic CAS-friendly
//      stem (Solve a*x = b for x) — same shape as G-02
//   3. POST /preview with sampleCount=5 → assert acceptedCount > 0 AND
//      every accepted sample carries a non-null canonicalAnswer (the
//      verified-green proof)
//   4. The 'Verified' status maps to the sample's `accepted=true` AND
//      `failureKind=null` shape on the wire
//
// Why this layer: G-02 already covered RBAC + creation; D-03 adds the
// invariant "every Accepted sample MUST have a populated CAS-derived
// canonicalAnswer field — null answer + accepted=true would mean the
// gate skipped verification".
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const ADMIN_SPA_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

test.describe('E2E_D_03_CAS_VERIFICATION_HAPPY', () => {
  test('preview samples carry canonicalAnswer when accepted=true (CAS round-trip) @epic-d @cas @ship-gate', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_D_03_CAS_VERIFICATION_HAPPY ===\n')

    const email = `d-03-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
    await page.waitForTimeout(300)
    const tok = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tok.json() as { idToken: string }

    await page.goto(`${ADMIN_SPA_URL}/login`)
    await page.getByPlaceholder('admin@cena.edu').fill(email)
    await page.locator('input[type="password"]').fill(password)
    await page.getByRole('button', { name: /sign in/i }).first().click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    const templateId = `e2e-d03-cas-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`
    const createResp = await page.request.post(`${ADMIN_API_URL}/api/admin/templates`, {
      headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
      data: {
        id: templateId, subject: 'math', topic: 'linear-slope',
        track: 'FourUnit', difficulty: 'Easy', methodology: 'Halabi', bloomsLevel: 3, language: 'en',
        stemTemplate: 'Solve for x: {a} x = {b}', solutionExpr: 'b / a', variableName: 'x',
        acceptShapes: ['integer', 'rational'],
        slots: [
          { name: 'a', kind: 'integer', integerMin: 1, integerMax: 5, enumValues: [],
            rationalNumeratorMin: 0, rationalNumeratorMax: 0, rationalDenominatorMin: 1,
            rationalDenominatorMax: 1, reduceRational: true, choices: [] },
          { name: 'b', kind: 'integer', integerMin: 1, integerMax: 10, enumValues: [],
            rationalNumeratorMin: 0, rationalNumeratorMax: 0, rationalDenominatorMin: 1,
            rationalDenominatorMax: 1, reduceRational: true, choices: [] },
        ],
        constraints: [{ description: 'a != 0', predicateExpr: 'a != 0' }],
        distractorRules: null, status: 'draft',
      },
    })
    expect(createResp.status()).toBeLessThan(300)

    const previewResp = await page.request.post(
      `${ADMIN_API_URL}/api/admin/templates/${templateId}/preview`,
      {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: { baseSeed: 42, sampleCount: 5 },
      },
    )
    expect(previewResp.status()).toBe(200)

    interface Sample {
      seed: number; accepted: boolean; canonicalAnswer: string | null
      failureKind: string | null; failureDetail: string | null
    }
    interface Resp { acceptedCount: number; samples: Sample[]; overallError: string | null }
    const preview = await previewResp.json() as Resp
    console.log(`[d-03] CAS preview: ${preview.acceptedCount}/${preview.samples.length} accepted`)

    // ── Load-bearing assertion ──
    // For ADR-0002 to hold, every Accepted sample MUST have a CAS-derived
    // canonicalAnswer. accepted=true with canonicalAnswer=null would
    // mean the gate signed off without computing an answer — the
    // ship-blocker class of regression.
    expect(preview.acceptedCount, 'at least one CAS round-trip must succeed for a deterministic linear template').toBeGreaterThan(0)
    for (const s of preview.samples) {
      if (s.accepted) {
        expect(s.canonicalAnswer, `accepted sample seed=${s.seed} must carry a CAS-derived canonicalAnswer`).not.toBeNull()
        expect(s.failureKind, `accepted sample seed=${s.seed} must NOT carry a failureKind`).toBeNull()
      }
    }
    expect(preview.overallError ?? '').toBe('')

    // Cleanup
    await page.request.delete(
      `${ADMIN_API_URL}/api/admin/templates/${templateId}`,
      { headers: { Authorization: `Bearer ${idToken}` } },
    )
  })
})
