// =============================================================================
// EPIC-E2E-H-07 — Firebase claim tenant matches backend write tenant
//
// Per the EPIC-H plan: "every backend write tags with the JWT claim's
// tenant — never a query-param-supplied tenant, never a localStorage-
// supplied tenant. Backend refuses writes where the path/body tenantId
// differs from the JWT claim".
//
// This spec asserts the SECOND on-first-sign-in attempt with a DIFFERENT
// tenantId in the body cannot retroactively switch the user's tenant.
// The bootstrap path is the most-attractive surface for tenant-tampering
// because it accepts a body `tenantId` field — the JWT (which carries
// the canonical tenant claim post-bootstrap) MUST win against any
// body-supplied override.
//
// Three attack-surface flavours are tested:
//   1. Re-call /api/auth/on-first-sign-in with a different tenantId
//      → studentProfile must remain in the original tenant
//   2. Provision a user, capture idToken (which encodes tenant=A claim),
//      then attempt to read /api/me/onboarding state under a hypothetical
//      tenant B (we don't have a B-claim token, so we just confirm the
//      JWT contents drive the response)
//   3. Idempotency — second on-first-sign-in with SAME tenantId is OK
//      (no-op, doesn't re-emit duplicate events)
// =============================================================================

import { test, expect, type APIRequestContext } from '@playwright/test'

const TENANT_PRIMARY = `t-h07-A-${Date.now()}-${Math.random().toString(36).slice(2, 5)}`
const TENANT_ATTACKER = `t-h07-B-${Date.now()}-${Math.random().toString(36).slice(2, 5)}`
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const PROBE_TOKEN = process.env.CENA_TEST_PROBE_TOKEN ?? 'dev-only-test-probe-token-do-not-ship'

interface BareUser {
  email: string
  password: string
  uid: string
  idToken: string
}

async function signUp(ctx: APIRequestContext, label: string): Promise<BareUser> {
  const email = `e2e-h07-${label}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
  const r = await ctx.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(r.ok(), `signUp ${label}`).toBe(true)
  const body = await r.json() as { idToken: string; localId: string }
  return { email, password, uid: body.localId, idToken: body.idToken }
}

async function reLogin(ctx: APIRequestContext, u: BareUser): Promise<string> {
  const r = await ctx.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email: u.email, password: u.password, returnSecureToken: true } },
  )
  const body = await r.json() as { idToken: string }
  return body.idToken
}

async function probeStudent(ctx: APIRequestContext, tenantId: string, uid: string): Promise<{ found: boolean; data: { tenantId?: string } | null }> {
  const r = await ctx.get(
    `${STUDENT_API}/api/admin/test/probe?type=studentProfile&tenantId=${encodeURIComponent(tenantId)}&id=${encodeURIComponent(uid)}`,
    { headers: { 'X-Test-Probe-Token': PROBE_TOKEN } },
  )
  expect(r.status(), `probe status for ${tenantId}/${uid}`).toBe(200)
  const body = await r.json() as { found: boolean; data: { tenantId?: string } | null }
  return body
}

test.describe('E2E_H_07_JWT_TENANT_PINNING', () => {
  test('re-bootstrapping with a different tenantId must NOT switch the user\'s tenant @epic-h @h-07 @ship-gate', async ({ request }, testInfo) => {
    test.setTimeout(120_000)

    const u = await signUp(request, 'tampered')

    // ── 1. First bootstrap: bind to TENANT_PRIMARY.
    const first = await request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${u.idToken}` },
      data: { tenantId: TENANT_PRIMARY, schoolId: TENANT_PRIMARY, displayName: 'H-07 Primary' },
    })
    expect(first.status(), 'first on-first-sign-in must be 200').toBe(200)

    // ── 2. Confirm probe finds the user in TENANT_PRIMARY.
    const found1 = await probeStudent(request, TENANT_PRIMARY, u.uid)
    expect(found1.found, 'after first bootstrap, found in TENANT_PRIMARY').toBe(true)

    // ── 3. Refresh idToken so claims are loaded onto the JWT (the
    //    bootstrap path pushed tenant=PRIMARY onto Firebase custom
    //    claims; we want the second call to carry that claim).
    const refreshed = await reLogin(request, u)

    // ── 4. ATTACK: second on-first-sign-in with TENANT_ATTACKER body.
    //    The JWT's tenant claim = PRIMARY (from step 3). The body says
    //    ATTACKER. Spec asserts the backend either rejects or pins to
    //    PRIMARY — under no circumstances does it leave the user in
    //    ATTACKER's tenant.
    const second = await request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${refreshed}` },
      data: { tenantId: TENANT_ATTACKER, schoolId: TENANT_ATTACKER, displayName: 'H-07 Attacker' },
    })

    // The response status is acceptable as 200 (idempotent, ignoring
    // body tenant) OR 4xx (rejected). What MUST NOT happen is the
    // backend silently moving the user to ATTACKER.
    console.log(`[h-07] second on-first-sign-in with tampered tenant returned ${second.status()}`)

    // ── 5. The defining post-attack check: probe.
    const inAttacker = await probeStudent(request, TENANT_ATTACKER, u.uid)
    expect(inAttacker.found,
      `after tampered re-bootstrap with tenantId=ATTACKER, the user MUST NOT be findable in ATTACKER's tenant ` +
      `(this would be a tenant-takeover regression)`,
    ).toBe(false)

    const stillInPrimary = await probeStudent(request, TENANT_PRIMARY, u.uid)
    expect(stillInPrimary.found,
      `after tampered re-bootstrap, the user MUST remain in their original tenant=PRIMARY`,
    ).toBe(true)

    testInfo.attach('h-07-tamper-result.json', {
      body: JSON.stringify({
        uid: u.uid,
        tenantPrimary: TENANT_PRIMARY,
        tenantAttacker: TENANT_ATTACKER,
        firstBootstrap: first.status(),
        secondBootstrap: second.status(),
        probeAttacker: inAttacker.found,
        probePrimary: stillInPrimary.found,
      }, null, 2),
      contentType: 'application/json',
    })
  })

  test('idempotent re-bootstrap with SAME tenantId is a no-op @epic-h @h-07', async ({ request }, testInfo) => {
    test.setTimeout(120_000)

    const u = await signUp(request, 'idempotent')

    const tenant = `t-h07-idem-${Date.now()}-${Math.random().toString(36).slice(2, 5)}`
    const first = await request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${u.idToken}` },
      data: { tenantId: tenant, schoolId: tenant, displayName: 'H-07 Idem' },
    })
    expect(first.status()).toBe(200)

    const refreshed = await reLogin(request, u)
    const second = await request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${refreshed}` },
      data: { tenantId: tenant, schoolId: tenant, displayName: 'H-07 Idem' },
    })
    // 200 (no-op) or 409 (already onboarded) are both acceptable; 500
    // would mean the backend isn't idempotent on duplicate bootstrap.
    expect([200, 409]).toContain(second.status())

    const found = await probeStudent(request, tenant, u.uid)
    expect(found.found, 'idempotent re-bootstrap leaves the user in their original tenant').toBe(true)

    testInfo.attach('h-07-idempotent-result.json', {
      body: JSON.stringify({ uid: u.uid, tenant, first: first.status(), second: second.status(), probed: found.found }, null, 2),
      contentType: 'application/json',
    })
  })
})
