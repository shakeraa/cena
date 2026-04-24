// =============================================================================
// probe smoke — chaos primitive
//
// Exercises the probes/chaos.ts helpers without asserting any workflow.
// Run before relying on the chaos probe in a real EPIC-E2E-J spec.
//
//   npm run test:e2e:flow -- --grep "probe-smoke chaos"
//
// Pick cena-sympy-sidecar for the target: it's the lowest-impact service
// to cycle (unit tests in other suites don't depend on it, no state loss
// on restart, has a healthcheck). Each assertion is independent so a
// partial failure still leaves the stack in a known state.
// =============================================================================

import { test, expect } from '@playwright/test'
import {
  stopService,
  startService,
  waitForHealthy,
  withServiceDown,
  blockNetwork,
  restoreNetwork,
  blockRoute,
} from '../probes/chaos'

test.describe('@probe-smoke chaos primitives', () => {
  // Force serial to avoid two smoke tests racing on the same service.
  test.describe.configure({ mode: 'serial' })

  test('stopService + startService + waitForHealthy cycle cena-sympy-sidecar', async () => {
    // Baseline assumption: service is already up from the dev stack.
    await waitForHealthy('cena-sympy-sidecar', 15_000)
    await stopService('cena-sympy-sidecar')
    await startService('cena-sympy-sidecar', { healthyTimeoutMs: 30_000 })
    // If we got here the health check re-established after restart.
  })

  test('withServiceDown runs the block and always restarts', async () => {
    let ran = false
    await withServiceDown('cena-sympy-sidecar', async () => {
      ran = true
    })
    expect(ran).toBe(true)
    await waitForHealthy('cena-sympy-sidecar', 30_000)
  })

  test('withServiceDown restarts even when the block throws', async () => {
    let threw = false
    try {
      await withServiceDown('cena-sympy-sidecar', async () => {
        throw new Error('simulated spec failure inside the chaos block')
      })
    }
    catch {
      threw = true
    }
    expect(threw).toBe(true)
    await waitForHealthy('cena-sympy-sidecar', 30_000)
  })

  test('blockNetwork / restoreNetwork toggles browser offline state', async ({ page, context }) => {
    await page.goto('about:blank')
    await blockNetwork(context)
    // Playwright's offline mode surfaces navigation failures as rejections.
    await expect(page.goto('http://localhost:5175', { timeout: 2000 }))
      .rejects.toThrow()
    await restoreNetwork(context)
    // Sanity: we can navigate again now.
    const resp = await page.goto('http://localhost:5175', { timeout: 10_000 })
    expect(resp?.ok() ?? false).toBe(true)
  })

  test('blockRoute aborts specific URL while letting others through', async ({ page }) => {
    await page.goto('http://localhost:5175')
    // Block a specific asset; the rest of the page still loads.
    const unblock = await blockRoute(page, /\/__vite_ping$/)
    // Navigate again — the page load itself MUST still succeed.
    const resp = await page.goto('http://localhost:5175', { timeout: 10_000 })
    expect(resp?.ok() ?? false).toBe(true)
    await unblock()
  })
})
