import { ofetch } from 'ofetch'
import type { $Fetch, FetchContext, FetchOptions } from 'ofetch'
import { useAuthStore } from '@/stores/authStore'

/**
 * Typed HTTP client for the Cena student web.
 *
 * Handles:
 *   - Firebase ID token attachment on every request
 *   - Correlation ID injection + preservation across retries
 *   - 401 → token refresh + single retry
 *   - 429 → Retry-After backoff (jittered) + retry up to 3 times
 *   - 502/503/504 → exponential backoff retry up to 3 times
 *   - Typed response generics: `$api<Dto>(path)`
 *
 * STU-W-04 will replace `getIdToken()` / `refreshIdToken()` with real
 * Firebase Auth SDK calls. For STU-W-03 the auth store provides stub
 * hooks that tests can drive via `__mockSignIn`.
 */

export interface ApiRequestMetadata {
  correlationId: string
  retriedAuth: boolean
  retryCount: number
}

interface MetaOptions extends FetchOptions {
  _meta?: ApiRequestMetadata
}

const MAX_RETRIES = 3

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms))
}

function jitter(ms: number): number {
  return ms + Math.floor(Math.random() * 500)
}

function newCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function')
    return crypto.randomUUID()

  // Fallback for environments without crypto.randomUUID
  return `cid-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}

/**
 * Hook the auth store exposes to the API layer. STU-W-02 provided the
 * store; STU-W-03 consumes it here. Real Firebase refresh will arrive
 * in STU-W-04.
 */
async function readTokenFromStore(forceRefresh: boolean): Promise<string | null> {
  const auth = useAuthStore()

  // STU-W-03 stub: on `forceRefresh`, a future Firebase `getIdToken(true)`
  // call will be made here. For now both paths read the current value.
  void forceRefresh

  return auth.idToken
}

async function getCurrentToken(): Promise<string | null> {
  return readTokenFromStore(false)
}

async function refreshToken(): Promise<string | null> {
  return readTokenFromStore(true)
}

function ensureMeta(options: MetaOptions): ApiRequestMetadata {
  if (!options._meta) {
    options._meta = {
      correlationId: newCorrelationId(),
      retriedAuth: false,
      retryCount: 0,
    }
  }

  return options._meta
}

function attachHeaders(options: MetaOptions, token: string | null) {
  const meta = ensureMeta(options)
  const headers = new Headers(options.headers as HeadersInit | undefined)

  headers.set('Accept', 'application/json')
  headers.set('X-Correlation-Id', meta.correlationId)
  if (token)
    headers.set('Authorization', `Bearer ${token}`)
  else
    headers.delete('Authorization')
  options.headers = headers
}

async function onRequest(ctx: FetchContext) {
  const options = ctx.options as MetaOptions

  ensureMeta(options)

  const token = await getCurrentToken()

  attachHeaders(options, token)
}

async function onResponseError(ctx: FetchContext & { response: Response & { _data?: unknown } }) {
  const options = ctx.options as MetaOptions
  const meta = ensureMeta(options)
  const status = ctx.response?.status ?? 0
  const request = ctx.request

  // 401: refresh the Firebase ID token once, retry once.
  if (status === 401 && !meta.retriedAuth) {
    meta.retriedAuth = true

    const fresh = await refreshToken()

    attachHeaders(options, fresh)

    // ofetch: throw the response to let the caller handle, but we want
    // a retry — so we call the raw client ourselves.
    throw new RetryMarker(request, options)
  }

  // 429: honor Retry-After if present, else 1s; jitter; retry up to MAX_RETRIES.
  if (status === 429 && meta.retryCount < MAX_RETRIES) {
    meta.retryCount += 1

    const retryAfterHeader = ctx.response?.headers.get('Retry-After')
    const retryAfterMs = retryAfterHeader ? Number.parseFloat(retryAfterHeader) * 1000 : 1000

    await sleep(jitter(retryAfterMs))
    throw new RetryMarker(request, options)
  }

  // 502/503/504: exponential backoff retry up to MAX_RETRIES.
  if ([502, 503, 504].includes(status) && meta.retryCount < MAX_RETRIES) {
    meta.retryCount += 1

    const delayMs = Math.min(1000 * 2 ** (meta.retryCount - 1), 8000)

    await sleep(jitter(delayMs))
    throw new RetryMarker(request, options)
  }

  // Non-retriable — let the caller see the error.
}

/**
 * Sentinel error thrown from `onResponseError` to signal the outer
 * wrapper to retry the request. Ofetch itself doesn't expose a clean
 * retry primitive for our error-conditional logic, so we implement
 * retries at the wrapper layer.
 */
class RetryMarker extends Error {
  constructor(public request: any, public options: MetaOptions) {
    super('retry')
    this.name = 'RetryMarker'
  }
}

const baseClient: $Fetch = ofetch.create({
  baseURL: (import.meta as any).env?.VITE_API_BASE_URL || '/api',
  retry: 0,
  onRequest,
  onResponseError,
})

/**
 * Public typed fetch entrypoint. Usage:
 *
 * ```ts
 * const summary = await $api<AnalyticsSummaryDto>('/analytics/summary')
 * ```
 */
export async function $api<T = unknown>(request: string, options: MetaOptions = {}): Promise<T> {
  ensureMeta(options)

  // Retry loop — catches `RetryMarker` exceptions and re-invokes the base client.
  let lastError: unknown

  // At most MAX_RETRIES + 1 attempts for 4xx/5xx, +1 for the 401 path.
  for (let attempt = 0; attempt < MAX_RETRIES + 2; attempt++) {
    try {
      return await baseClient<T>(request, options)
    }
    catch (err) {
      if (err instanceof RetryMarker) {
        lastError = err
        continue
      }
      throw err
    }
  }
  throw lastError
}

/**
 * Utility for tests to inspect the correlation ID the wrapper would
 * generate for a given options object.
 */
export function __readMeta(options: MetaOptions): ApiRequestMetadata | undefined {
  return options._meta
}

/**
 * Low-level retry/backoff helpers exposed for unit tests.
 */
export const __internal = {
  sleep,
  jitter,
  newCorrelationId,
  getCurrentToken,
  refreshToken,
  MAX_RETRIES,
}
