// =============================================================================
// Cena E2E flow — chaos probe (compose-service + network fault injection)
//
// EPIC-E2E-J and EPIC-E2E-K need to test recovery from peripheral outages
// end-to-end:
//
//   * J-01 — SymPy sidecar down → CAS degraded-mode UX
//   * J-03 — NATS down → outbox buffers, drains on restart
//   * J-04 — Firebase emu down → existing sessions keep working
//   * J-06 — SMTP down → digest holding queue, recovery delivers
//   * J-07 — Redis down → rate limiter fail-closed
//   * J-09 — SignalR hub drop mid-session → reconnect without state loss
//   * K-02 — offline answer queue flushes on reconnect
//   * K-03 — offline question cache serves cached pages
//
// This file provides the raw primitives. Per-spec compositions (e.g.
// "start a session, kill NATS, answer 3 questions, restart NATS, assert
// drain") live in the fixtures.
//
// Tradeoff: shelling out to `docker compose` is not pretty, but:
//   1. The dev stack IS docker-compose — every operator already uses it.
//   2. Parsing the docker socket (JSON) is worse than a pinned CLI.
//   3. The `execFile` surface is tight (no shell, no injection) because we
//      only pass known service names from the epic files, not user input.
//
// NOT for production tests — dev stack only. The methods here assume a
// long-running `docker compose up -d` is already running.
// =============================================================================

import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import type { BrowserContext, Page } from '@playwright/test'

const execFileAsync = promisify(execFile)

/**
 * Compose service names the epic files reference. Narrow union so a typo
 * in a spec file is a compile error, not a runtime "no such service".
 */
export type ChaosService =
  | 'cena-nats'
  | 'cena-redis'
  | 'cena-postgres'
  | 'cena-firebase-emulator'
  | 'cena-sympy-sidecar'
  | 'cena-actor-host'
  | 'cena-student-api'
  | 'cena-admin-api'

export interface ChaosOptions {
  /** Default 30_000ms. Raise for services whose health check is slow. */
  healthyTimeoutMs?: number
}

/**
 * Stop a compose service. Equivalent to `docker stop <service>`. Returns
 * when the container is confirmed stopped, or throws. No-op if already
 * stopped — checks inspect first so back-to-back `stop` calls don't
 * surface a non-zero docker exit.
 */
export async function stopService(service: ChaosService): Promise<void> {
  const state = await inspectState(service)
  if (state === 'not-found' || state === 'exited' || state === 'created')
    return
  await docker(['stop', service])
  // Defensive: verify the post-state. Docker's `stop` returns after a SIGTERM
  // with a default 10s grace; if the container ignores SIGTERM and we proceed
  // before it's actually down, the next spec sees a zombie.
  const after = await inspectState(service)
  if (after !== 'exited' && after !== 'not-found')
    throw new Error(`[chaos] stopService(${service}) — state is ${after} after stop`)
}

/**
 * Start a compose service and wait for its health check to go green.
 * Raises if `service` has no healthcheck defined (caller should use
 * `startServiceUnchecked` in that case and assert liveness differently).
 */
export async function startService(
  service: ChaosService,
  opts: ChaosOptions = {},
): Promise<void> {
  await docker(['start', service])
  await waitForHealthy(service, opts.healthyTimeoutMs ?? 30_000)
}

/**
 * Start a compose service WITHOUT waiting on a health check. Use for
 * actor-host and similar services that don't define a Docker healthcheck
 * in the compose file — caller is responsible for asserting liveness via
 * an application-layer probe (bus-probe, HTTP probe, etc.).
 */
export async function startServiceUnchecked(service: ChaosService): Promise<void> {
  await docker(['start', service])
}

/**
 * Poll Docker's healthcheck until it's `healthy` or timeout. Throws on
 * `unhealthy` so a spec fails fast rather than timing out on a stuck
 * health probe.
 */
export async function waitForHealthy(
  service: ChaosService,
  timeoutMs: number = 30_000,
): Promise<void> {
  const deadline = Date.now() + timeoutMs
  // 500ms between polls — docker's healthcheck interval is typically 5-10s,
  // but we don't know when inside that window we've started polling; 500ms
  // keeps round-trip noise low without hammering the socket.
  while (Date.now() < deadline) {
    const h = await inspectHealth(service)
    if (h === 'healthy')
      return
    if (h === 'unhealthy')
      throw new Error(`[chaos] waitForHealthy(${service}) — reported unhealthy`)
    if (h === 'no-healthcheck')
      throw new Error(
        `[chaos] waitForHealthy(${service}) — no healthcheck defined; ` +
        'use startServiceUnchecked + an app-layer probe',
      )
    await sleep(500)
  }
  throw new Error(`[chaos] waitForHealthy(${service}) — timed out after ${timeoutMs}ms`)
}

/**
 * Wrap a block in a service outage: stops the service, runs the block,
 * restarts the service. Always restarts — even on block failure — so a
 * crashing spec doesn't leave the stack in a broken state for the next
 * spec. Caller is responsible for idempotency of the wrapped block.
 */
export async function withServiceDown<T>(
  service: ChaosService,
  block: () => Promise<T>,
  opts: ChaosOptions = {},
): Promise<T> {
  await stopService(service)
  try {
    return await block()
  }
  finally {
    // Use unchecked start here because the block may have crashed before
    // the service was expected to come back — restarting-and-waiting for
    // health would mask the original failure. Caller can await
    // waitForHealthy explicitly when they need the strong guarantee.
    await startServiceUnchecked(service)
    if (opts.healthyTimeoutMs !== 0) {
      try {
        await waitForHealthy(service, opts.healthyTimeoutMs ?? 30_000)
      }
      catch {
        // Swallow — the block's error (if any) is the load-bearing signal.
      }
    }
  }
}

/**
 * Block all outbound network from a Playwright browser context. Uses
 * Playwright's built-in `setOffline` so the SPA sees the same fetch +
 * websocket failures a real device on airplane-mode would. Restores
 * on call to `restoreNetwork`.
 *
 * Use for EPIC-E2E-K offline-pwa flows and EPIC-E2E-J-09 SignalR drop
 * simulation. Scoped to the browser context, not the compose stack —
 * the backend keeps running; only the client thinks the network is
 * gone.
 */
export async function blockNetwork(context: BrowserContext): Promise<void> {
  await context.setOffline(true)
}

export async function restoreNetwork(context: BrowserContext): Promise<void> {
  await context.setOffline(false)
}

/**
 * Block a specific URL pattern at the Playwright page level while
 * letting everything else through. Useful for simulating a single
 * backend endpoint being down (e.g. kill SignalR hub while letting
 * REST keep working — EPIC-E2E-J-09).
 *
 * Returns an unroute function so specs can compose multiple blocks
 * cleanly.
 */
export async function blockRoute(
  page: Page,
  urlPattern: string | RegExp,
): Promise<() => Promise<void>> {
  const handler = (route: import('@playwright/test').Route) => route.abort('internetdisconnected')
  await page.route(urlPattern, handler)
  return async () => { await page.unroute(urlPattern, handler) }
}

// ── Internals ──────────────────────────────────────────────────────────────

type DockerHealth = 'healthy' | 'unhealthy' | 'starting' | 'no-healthcheck' | 'not-found'
type DockerState = 'running' | 'exited' | 'restarting' | 'paused' | 'dead' | 'created' | 'not-found'

async function inspectHealth(service: ChaosService): Promise<DockerHealth> {
  try {
    const { stdout } = await docker([
      'inspect',
      '--format',
      '{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}',
      service,
    ])
    const h = stdout.trim()
    if (h === 'healthy' || h === 'unhealthy' || h === 'starting' || h === 'no-healthcheck')
      return h
    return 'no-healthcheck'
  }
  catch (err) {
    if (isNoSuchContainer(err))
      return 'not-found'
    throw err
  }
}

async function inspectState(service: ChaosService): Promise<DockerState> {
  try {
    const { stdout } = await docker(['inspect', '--format', '{{.State.Status}}', service])
    const s = stdout.trim() as DockerState
    return s
  }
  catch (err) {
    if (isNoSuchContainer(err))
      return 'not-found'
    throw err
  }
}

async function docker(args: string[]): Promise<{ stdout: string; stderr: string }> {
  return execFileAsync('docker', args, {
    // 60s ceiling — `docker stop` defaults to 10s for SIGTERM + small grace,
    // and healthcheck polls never run through this helper. Anything longer
    // is a real hang.
    timeout: 60_000,
  })
}

function isNoSuchContainer(err: unknown): boolean {
  if (err && typeof err === 'object' && 'stderr' in err && typeof err.stderr === 'string')
    return err.stderr.includes('No such container') || err.stderr.includes('no such container')
  return false
}

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms))
}
