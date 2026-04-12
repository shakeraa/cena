import type { Ref } from 'vue'
import { onMounted, ref } from 'vue'
import { $api } from '@/api/$api'

/**
 * FIND-ux-021: Typed API error that pages render via i18n, never raw strings.
 *
 * Every error surfaced by `useApiQuery` / `useApiMutation` carries an
 * `i18nKey` that templates bind to `t(error.i18nKey)`. The fallback key
 * `common.errorGeneric` is always present in all three locales.
 */
export class ApiError extends Error {
  /** i18n key that templates bind to via `t(error.i18nKey)`. */
  readonly i18nKey: string

  /** Machine-readable code for structured logging / telemetry. */
  readonly code: string

  constructor(message: string, i18nKey: string, code: string) {
    super(message)
    this.name = 'ApiError'
    this.i18nKey = i18nKey
    this.code = code
  }
}

/**
 * Map an HTTP path to a page-specific i18n error key. Falls back to
 * `common.errorGeneric` for unmapped paths.
 */
function i18nKeyForPath(path: string): string {
  if (path.startsWith('/api/me'))
    return 'home.dashboardUnavailable'
  if (path.startsWith('/api/tutor'))
    return 'tutor.threadsUnavailable'
  if (path.startsWith('/api/social'))
    return 'social.feedUnavailable'
  if (path.startsWith('/api/analytics'))
    return 'home.dashboardUnavailable'
  if (path.startsWith('/api/notifications'))
    return 'notifications.unavailable'
  if (path.startsWith('/api/knowledge') || path.startsWith('/api/content'))
    return 'knowledgeGraph.unavailable'
  if (path.startsWith('/api/gamification') || path.startsWith('/api/leaderboard'))
    return 'leaderboard.unavailable'
  if (path.startsWith('/api/sessions'))
    return 'progress.sessionsUnavailable'
  if (path.startsWith('/api/challenges'))
    return 'challenges.unavailable'
  if (path.startsWith('/api/friends'))
    return 'social.friendsUnavailable'

  return 'common.errorGeneric'
}

/**
 * Derive a machine-readable error code from a raw Error. Uses the HTTP
 * status when the underlying library (ofetch) attaches one, else falls
 * back to a generic code.
 */
function codeFromError(err: unknown): string {
  const status = (err as any)?.statusCode ?? (err as any)?.status ?? 0
  if (status === 401)
    return 'UNAUTHORIZED'
  if (status === 403)
    return 'FORBIDDEN'
  if (status === 404)
    return 'NOT_FOUND'
  if (status === 429)
    return 'RATE_LIMITED'
  if (status >= 500)
    return 'SERVER_ERROR'
  if (status > 0)
    return `HTTP_${status}`

  return 'FETCH_FAILED'
}

/**
 * Wrap a raw Error into an ApiError with the correct i18n key for a
 * given request path. Also emits a structured console.error for
 * production log aggregation / re-regression detection.
 */
function wrapError(err: unknown, path: string): ApiError {
  const raw = err instanceof Error ? err : new Error(String(err))
  const code = codeFromError(err)
  const i18nKey = i18nKeyForPath(path)

  // FIND-ux-021: structured log so production monitoring can detect
  // regressions without relying on user-visible strings.
  console.error('[ApiError]', JSON.stringify({ path, code, i18nKey, message: raw.message }))

  return new ApiError(raw.message, i18nKey, code)
}

export interface UseApiQueryResult<T> {
  data: Ref<T | null>
  error: Ref<ApiError | null>
  loading: Ref<boolean>
  refresh: () => Promise<void>
}

/**
 * Opinionated GET wrapper for Vue components. Fetches on mount by default;
 * exposes a `refresh()` for manual retries. Errors are surfaced as a
 * reactive ref of type `ApiError` (never raw Error), never thrown.
 */
export function useApiQuery<T = unknown>(
  path: string,
  opts: { immediate?: boolean } = {},
): UseApiQueryResult<T> {
  const data = ref<T | null>(null) as Ref<T | null>
  const error = ref<ApiError | null>(null) as Ref<ApiError | null>
  const loading = ref(false)

  const refresh = async () => {
    loading.value = true
    error.value = null
    try {
      data.value = await $api<T>(path)
    }
    catch (err) {
      error.value = wrapError(err, path)
    }
    finally {
      loading.value = false
    }
  }

  if (opts.immediate !== false)
    onMounted(refresh)

  return { data, error, loading, refresh }
}
