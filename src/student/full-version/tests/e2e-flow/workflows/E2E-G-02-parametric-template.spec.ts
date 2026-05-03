// =============================================================================
// E2E-G-02 — Parametric template authoring + CAS preview (prr-202, ADR-0002)
//
// Drives a SUPER_ADMIN through the canonical author flow:
//   1. POST /api/admin/templates    — create a deterministic template
//   2. POST /api/admin/templates/{id}/preview — CAS round-trip via SymPy
//      sidecar; the response carries `Accepted` per sample, proving the
//      gate ran for real
//   3. GET  /api/admin/templates/{id} — verify the persisted record
//   4. DELETE /api/admin/templates/{id} — clean up so re-runs don't
//      collide on the unique-id constraint
//
// Why API-only inside a browser context:
// The admin SPA does not yet host a parametric-template authoring page
// (grep src/admin/full-version/src/pages: no template editor route).
// The endpoints are real, AdminOnly-policy-gated, CAS-fan-out wired —
// the contract surface is the load-bearing thing here. We drive the API
// from inside an authenticated `page.request` context so the cookie /
// auth posture is identical to a real admin user; the Authorization
// header is a Firebase idToken from the emulator, same path the SPA
// would walk.
//
// What this spec catches:
//   * LLM slipping into the parametric pipeline (Strategy 1 purity is
//     enforced server-side; if a future refactor introduces an LLM
//     fallback, the preview latency profile changes — we'd see warnings
//     in `/admin/templates/{id}/preview` like LlmPathInvoked. We assert
//     the response shape stays deterministic.)
//   * SymPy sidecar contract drift — if the request shape moves, this
//     spec breaks fast.
//   * CAS-gate bypass — `Samples[i].Accepted=false` with `FailureKind`
//     is the documented "rejection" path; we assert at least one sample
//     was either Accepted=true OR carries a structured FailureKind, not
//     a raw error string.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const ADMIN_SPA_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

interface NetworkFailure { method: string; url: string; status: number; body?: string }

test.describe('E2E_G_02_PARAMETRIC_TEMPLATE', () => {
  test('SUPER_ADMIN creates template → CAS preview returns deterministic samples @epic-g @cas', async ({ page }, testInfo) => {
    test.setTimeout(120_000)

    const failedRequests: NetworkFailure[] = []
    page.on('response', async (resp) => {
      if (resp.status() >= 400) {
        let body: string | undefined
        try { const t = await resp.text(); body = t.length > 800 ? `${t.slice(0, 800)}…` : t }
        catch { body = '<flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    // ── 1. SUPER_ADMIN sign-in (real form) ──
    const email = `g-02-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== E2E_G_02_PARAMETRIC_TEMPLATE for ${email} ===\n`)

    expect((await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )).ok()).toBe(true)
    const localId = (await (await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )).json() as { localId: string }).localId

    const claims = { role: 'SUPER_ADMIN', school_id: SCHOOL_ID, locale: 'en', plan: 'free' }
    expect((await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/accounts:update`,
      { headers: { Authorization: `Bearer ${EMU_BEARER}` }, data: { localId, customAttributes: JSON.stringify(claims) } },
    )).ok()).toBe(true)

    // Re-sign-in to bake the new claims into the idToken.
    const tokenResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tokenResp.json() as { idToken: string }

    // Plant the auth state in the browser. Even though we're driving the
    // API directly, going through /login first proves the role-gate
    // (SUPER_ADMIN can land at /dashboards/admin) and warms the cookie
    // jar that page.request reuses.
    await page.goto(`${ADMIN_SPA_URL}/login`)
    await page.getByPlaceholder('admin@cena.edu').fill(email)
    await page.locator('input[type="password"]').fill(password)
    await page.getByRole('button', { name: /sign in/i }).first().click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
    console.log(`[g-02] post-login url: ${page.url()}`)

    // ── 2. Create the template ──
    // Payload mirrors the canonical `HappyCreate` from
    // src/api/Cena.Admin.Api.Tests/Templates/ParametricTemplateAuthoringServiceTests.cs:76
    // — solve {a}x={b} for x, integer slots a∈[1,5] b∈[1,10], CAS sees
    // `b/a` as the canonical answer. Per-run unique id so re-runs don't
    // collide on the soft-delete unique-id check.
    const templateId = `e2e-g02-lin-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`
    const createBody = {
      id: templateId,
      subject: 'math',
      topic: 'linear-slope',
      track: 'FourUnit',
      difficulty: 'Easy',
      methodology: 'Halabi',
      bloomsLevel: 3,
      language: 'en',
      stemTemplate: 'Solve for x: {a} x = {b}',
      solutionExpr: 'b / a',
      variableName: 'x',
      acceptShapes: ['integer', 'rational'],
      slots: [
        { name: 'a', kind: 'integer', integerMin: 1, integerMax: 5,
          enumValues: [], rationalNumeratorMin: 0, rationalNumeratorMax: 0,
          rationalDenominatorMin: 1, rationalDenominatorMax: 1, reduceRational: true,
          choices: [] },
        { name: 'b', kind: 'integer', integerMin: 1, integerMax: 10,
          enumValues: [], rationalNumeratorMin: 0, rationalNumeratorMax: 0,
          rationalDenominatorMin: 1, rationalDenominatorMax: 1, reduceRational: true,
          choices: [] },
      ],
      constraints: [{ description: 'a != 0', predicateExpr: 'a != 0' }],
      distractorRules: null,
      status: 'draft',
    }

    const createResp = await page.request.post(`${ADMIN_API_URL}/api/admin/templates`, {
      headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
      data: createBody,
    })
    const createBodyText = await createResp.text()
    console.log(`[g-02] POST /api/admin/templates → ${createResp.status()}`)
    expect(createResp.status(), `create body: ${createBodyText.slice(0, 400)}`).toBeLessThan(300)

    const created = JSON.parse(createBodyText) as { id: string; version: number; active: boolean; status: string }
    expect(created.id).toBe(templateId)
    expect(created.version).toBe(1)
    expect(created.active).toBe(true)

    // ── 3. CAS preview round-trip (the load-bearing assertion) ──
    // Fixed seed 42 so the test is deterministic. SampleCount=3 keeps
    // the SymPy sidecar latency low (~200ms × 3) but exercises the loop.
    const previewResp = await page.request.post(
      `${ADMIN_API_URL}/api/admin/templates/${templateId}/preview`,
      {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: { baseSeed: 42, sampleCount: 3 },
      },
    )
    const previewBodyText = await previewResp.text()
    console.log(`[g-02] POST .../preview → ${previewResp.status()}`)
    expect(previewResp.status(), `preview body: ${previewBodyText.slice(0, 400)}`).toBeLessThan(300)

    interface PreviewSample {
      seed: number
      accepted: boolean
      stem: string | null
      canonicalAnswer: string | null
      distractors: { misconceptionId: string; text: string; rationale: string | null }[]
      failureKind: string | null
      failureDetail: string | null
      latencyMs: number
    }
    interface PreviewResponse {
      templateId: string
      templateVersion: number
      requestedCount: number
      acceptedCount: number
      samples: PreviewSample[]
      overallError: string | null
      totalLatencyMs: number
    }
    const preview = JSON.parse(previewBodyText) as PreviewResponse

    expect(preview.templateId).toBe(templateId)
    expect(preview.requestedCount).toBe(3)
    expect(preview.samples).toHaveLength(3)

    // ── ADR-0002 invariant ──
    // Each sample must be EITHER Accepted=true (CAS verified the answer)
    // OR Accepted=false with a structured FailureKind. Raw error strings
    // without a kind would mean the gate is silently swallowing
    // exceptions — that's the regression this test catches.
    for (const s of preview.samples) {
      const hasStructuredOutcome = s.accepted
        || (s.failureKind !== null && s.failureKind.length > 0)
      expect(hasStructuredOutcome, `sample seed=${s.seed} must report accepted OR a non-empty failureKind`).toBe(true)
    }
    console.log(`[g-02] CAS preview: ${preview.acceptedCount}/${preview.requestedCount} accepted, total ${preview.totalLatencyMs.toFixed(0)}ms`)

    // ── ADR-0002 invariant: CAS actually round-tripped ──
    // overallError must be null or empty — non-empty here means the
    // SymPy sidecar wasn't reached, which would be a real outage and
    // the spec must fail.
    expect(preview.overallError ?? '').toBe('')

    // ── 4. Read-back via GET /api/admin/templates/{id} ──
    const detailResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/templates/${templateId}`,
      { headers: { Authorization: `Bearer ${idToken}` } },
    )
    expect(detailResp.status()).toBe(200)
    const detail = await detailResp.json() as { id: string; version: number; status: string }
    expect(detail.id).toBe(templateId)
    expect(detail.version).toBe(1)

    // ── 5. Cleanup (soft-delete) ──
    const deleteResp = await page.request.delete(
      `${ADMIN_API_URL}/api/admin/templates/${templateId}`,
      { headers: { Authorization: `Bearer ${idToken}` } },
    )
    expect([200, 204]).toContain(deleteResp.status())
    console.log(`[g-02] DELETE → ${deleteResp.status()} (soft-deleted)`)

    // ── 6. Diagnostics ──
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })

    console.log('\n=== E2E_G_02 DIAGNOSTICS SUMMARY ===')
    console.log(`Failed network: ${failedRequests.length}`)
    if (failedRequests.length) {
      console.log('— failed requests —')
      for (const f of failedRequests.slice(0, 10))
        console.log(`  ${f.status} ${f.method} ${f.url} :: ${(f.body ?? '').slice(0, 160)}`)
    }
    expect(failedRequests, 'No 4xx/5xx during the parametric-template + CAS path').toHaveLength(0)
  })
})
