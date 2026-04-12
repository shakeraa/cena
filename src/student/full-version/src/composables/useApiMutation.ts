import type { Ref } from 'vue'
import { ref } from 'vue'
import { $api } from '@/api/$api'
import { ApiError } from '@/composables/useApiQuery'

export interface UseApiMutationResult<TRes, TReq> {
  data: Ref<TRes | null>
  error: Ref<ApiError | null>
  loading: Ref<boolean>
  execute: (body: TReq) => Promise<TRes>
}

/**
 * Derive a machine-readable error code from a raw Error.
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
 * POST/PUT/PATCH/DELETE wrapper. Call `execute(body)` to fire. Errors
 * propagate via both the reactive `error` ref (as `ApiError` with i18nKey)
 * AND as a thrown exception from `execute`, so callers can await + try/catch.
 */
export function useApiMutation<TRes = unknown, TReq = unknown>(
  path: string,
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE' = 'POST',
): UseApiMutationResult<TRes, TReq> {
  const data = ref<TRes | null>(null) as Ref<TRes | null>
  const error = ref<ApiError | null>(null) as Ref<ApiError | null>
  const loading = ref(false)

  const execute = async (body: TReq): Promise<TRes> => {
    loading.value = true
    error.value = null
    try {
      const res = await $api<TRes>(path, { method: method as any, body: body as any })

      data.value = res

      return res
    }
    catch (err) {
      const raw = err instanceof Error ? err : new Error(String(err))
      const code = codeFromError(err)

      // FIND-ux-021: structured log for production monitoring.
      console.error('[ApiError]', JSON.stringify({ path, method, code, message: raw.message }))

      const apiErr = new ApiError(raw.message, 'common.errorGeneric', code)

      error.value = apiErr
      throw apiErr
    }
    finally {
      loading.value = false
    }
  }

  return { data, error, loading, execute }
}
