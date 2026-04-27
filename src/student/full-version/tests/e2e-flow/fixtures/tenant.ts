// =============================================================================
// Cena E2E flow — tenant-isolation fixture
//
// Extends Playwright's base `test` with worker-scoped `tenant` + test-scoped
// `authUser` + `stripeScope` fixtures. Every spec in tests/e2e-flow/ imports
// from here instead of '@playwright/test' directly.
//
// Isolation contract:
//   * tenant.id      - stable across a worker's lifetime, unique per worker
//   * authUser       - fresh Firebase emulator user per spec
//   * stripeScope    - Stripe helpers bound to this tenant's customer metadata
//
// See tasks/e2e-flow/README.md for the full isolation model.
// =============================================================================

import { test as base } from '@playwright/test'
import type { Page } from '@playwright/test'
import { createFirebaseUser, signInFirebaseUser, type FirebaseAuthUser } from './auth'
import { createStripeScope, type StripeScope } from './stripe'
import { createBusProbe, type BusProbe } from './bus-probe'
import { createDynamicSeed, type DynamicSeed } from './dynamic-seed'

export interface TenantContext {
  /**
   * Stable tenant id for this worker. Shape: `t_e2e_{workerIndex}_{runId}`.
   * All writes performed via this worker's tests carry this id, so cross-
   * worker bleed is structurally impossible as long as backend honours
   * ADR-0001 tenant scoping.
   */
  readonly id: string
  readonly workerIndex: number
  /**
   * Fork a fresh sub-tenant for a spec that needs blank-slate isolation
   * (e.g. testing institute-level provisioning). Cheap — no setup cost
   * beyond string allocation.
   */
  fork(suffix: string): TenantContext
}

export interface E2EFixtures {
  tenant: TenantContext
  authUser: FirebaseAuthUser
  stripeScope: StripeScope
  busProbe: BusProbe
  dynamicSeed: DynamicSeed
}

function buildTenantId(workerIndex: number, runId: string): string {
  return `t_e2e_${workerIndex}_${runId}`
}

const RUN_ID = process.env.E2E_RUN_ID
  ?? Date.now().toString(36)

export const e2eTest = base.extend<E2EFixtures, { tenant: TenantContext }>({
  // Worker-scoped: instantiated once per worker process, reused across
  // every spec that worker runs. Keeps setup cost O(1) per suite.
  tenant: [async ({}, use, workerInfo) => {
    const id = buildTenantId(workerInfo.workerIndex, RUN_ID)
    const ctx: TenantContext = {
      id,
      workerIndex: workerInfo.workerIndex,
      fork(suffix: string) {
        return {
          ...ctx,
          id: `${id}_${suffix}`,
          fork: ctx.fork,
        }
      },
    }
    await use(ctx)
    // No teardown: tenant rows are cleaned up by a scheduled GC job (cron
    // matches `t_e2e_%`). Keeping rows around helps post-mortem debugging.
  }, { scope: 'worker' }],

  // Test-scoped: fresh Firebase user per spec. No residual session state.
  authUser: async ({ tenant, page }, use, testInfo) => {
    const email = `e2e-w${tenant.workerIndex}-${testInfo.title.replace(/\W+/g, '-').toLowerCase()}-${Date.now()}@cena.test`
    const user = await createFirebaseUser(email, {
      tenantId: tenant.id,
      role: 'student',
    })
    await signInFirebaseUser(page, user)
    await use(user)
    // Teardown: left to emulator reset between full suite runs. Mid-suite
    // user accumulation is O(N) where N = number of specs — acceptable.
  },

  // Test-scoped: Stripe helpers that tag every test-mode interaction with
  // our tenant.id so webhook routing is unambiguous.
  stripeScope: async ({ tenant }, use) => {
    const scope = createStripeScope(tenant.id)
    await use(scope)
  },

  // Test-scoped: NATS bus probe for asserting that expected events land on
  // the JetStream subjects each boundary names. Fresh TCP connection per
  // spec → no subscriber state bleed across specs. Teardown is guaranteed
  // even on assertion failure (Playwright calls the cleanup arm).
  busProbe: async ({}, use) => {
    const probe = await createBusProbe()
    try {
      await use(probe)
    }
    finally {
      await probe.close()
    }
  },

  // Test-scoped: TASK-E2E-INFRA-03 dynamic-route seed fixture. Each
  // spec gets its own DynamicSeed instance bound to the worker's
  // tenant. Tracked Firebase emu users are deleted on teardown
  // (best-effort). Tests for [id] routes (mastery/student/[id],
  // tutor/[threadId], etc.) consume this to plant ids without each
  // spec re-implementing the provisioning boilerplate.
  dynamicSeed: async ({ tenant }, use) => {
    const seed = createDynamicSeed({ tenantId: tenant.id })
    try {
      await use(seed)
    }
    finally {
      await seed.cleanup()
    }
  },
})

export { expect } from '@playwright/test'

/**
 * Utility for spec files that need to quote the tenant id into an API call
 * or assertion message.
 */
export function tenantDebugString(ctx: TenantContext, page: Page): string {
  return `tenant=${ctx.id} worker=${ctx.workerIndex} url=${page.url()}`
}
