// =============================================================================
// Cena E2E flow — DB-boundary probe (PRR-436)
//
// Typed wrapper around the backend's GET /api/admin/test/probe. Lets a spec
// assert against the canonical Marten state directly instead of
// agreeing-with-the-API at /api/me/*.
//
// Auth: X-Test-Probe-Token header. The token is sourced from the
// CENA_TEST_PROBE_TOKEN env var the e2e-flow Playwright config injects.
// Production builds don't set this env var; the backend then 404s on every
// probe call so this fixture is harmless if accidentally invoked.
//
// Tenant isolation:
//   * studentProfile / consent — tenant-gated server-side (AdminUser.School
//     mismatch returns found: false). Cross-tenant probes look like a miss.
//   * subscription — token-gated only. Encrypted student-id at rest blocks
//     server-side tenant cross-check; rely on the test token + the
//     `parentSubjectId` argument to scope the read.
// =============================================================================

import { request as playwrightRequest } from '@playwright/test'

const DEFAULT_BASE_URL = process.env.E2E_FLOW_BASE_URL ?? 'http://localhost:5175'
const TOKEN_ENV = 'CENA_TEST_PROBE_TOKEN'

/** Discriminated union of supported probe kinds. */
export type ProbeKind = 'studentProfile' | 'subscription' | 'consent'

/** Server response envelope. Mirrors `TestProbeEndpoint.TestProbeResponse`. */
export interface ProbeEnvelope<TData = unknown> {
  kind: string
  tenantId: string
  found: boolean
  data: TData | null
  /** Stream length (subscription, consent) or null (studentProfile snapshot). */
  version: number | null
}

export interface StudentProfileProbeData {
  uid: string
  email: string
  tenantId: string
  schoolId: string | null
  role: string
  createdAt: string
  onboardedAt: string | null
  consentTier: string
}

export interface SubscriptionProbeData {
  parentSubjectId: string
  status: string
  tier: string
  cycle: string
  activatedAt: string | null
  renewsAt: string | null
  cancelledAt: string | null
  refundedAt: string | null
  consecutivePaymentFailures: number
  linkedStudentCount: number
}

export interface ConsentProbeData {
  subjectId: string
  grants: Record<string, {
    isGranted: boolean
    grantedAt: string | null
    grantedByRole: string | null
    expiresAt: string | null
    revokedAt: string | null
    revokedByRole: string | null
    revocationReason: string | null
  }>
  vetoedParentVisibilityPurposes: string[]
}

interface ProbeOptions {
  /**
   * SPA base URL (the vite proxy forwards /api/* to student-api). Defaults to
   * `E2E_FLOW_BASE_URL` env var or `http://localhost:5175`.
   */
  baseUrl?: string
  /**
   * Override the token. Defaults to `process.env.CENA_TEST_PROBE_TOKEN`.
   * Throws when neither is set so a misconfigured CI run fails loudly.
   */
  token?: string
}

async function probe<TData>(
  kind: ProbeKind,
  tenantId: string,
  id: string,
  opts?: ProbeOptions,
): Promise<ProbeEnvelope<TData>> {
  const token = opts?.token ?? process.env[TOKEN_ENV]
  if (!token) {
    throw new Error(
      `[db-probe] ${TOKEN_ENV} not set. Configure it in the Playwright env so X-Test-Probe-Token can be presented.`,
    )
  }

  const baseUrl = opts?.baseUrl ?? DEFAULT_BASE_URL
  const ctx = await playwrightRequest.newContext({ baseURL: baseUrl })
  try {
    const url = `/api/admin/test/probe?type=${encodeURIComponent(kind)}&tenantId=${encodeURIComponent(tenantId)}&id=${encodeURIComponent(id)}`
    const resp = await ctx.get(url, {
      headers: { 'X-Test-Probe-Token': token },
    })
    if (!resp.ok()) {
      throw new Error(`[db-probe] ${kind} probe returned ${resp.status()} for tenantId=${tenantId} id=${id}: ${await resp.text()}`)
    }
    return await resp.json() as ProbeEnvelope<TData>
  }
  finally {
    await ctx.dispose()
  }
}

/** Probe StudentProfile snapshot keyed by Firebase uid, tenant-gated. */
export function probeStudentProfile(
  opts: { tenantId: string; uid: string } & ProbeOptions,
): Promise<ProbeEnvelope<StudentProfileProbeData>> {
  return probe<StudentProfileProbeData>('studentProfile', opts.tenantId, opts.uid, opts)
}

/** Probe SubscriptionAggregate state by parentSubjectId. */
export function probeSubscription(
  opts: { tenantId: string; parentSubjectId: string } & ProbeOptions,
): Promise<ProbeEnvelope<SubscriptionProbeData>> {
  return probe<SubscriptionProbeData>('subscription', opts.tenantId, opts.parentSubjectId, opts)
}

/** Probe ConsentAggregate state by subjectId, tenant-gated. */
export function probeConsent(
  opts: { tenantId: string; subjectId: string } & ProbeOptions,
): Promise<ProbeEnvelope<ConsentProbeData>> {
  return probe<ConsentProbeData>('consent', opts.tenantId, opts.subjectId, opts)
}
