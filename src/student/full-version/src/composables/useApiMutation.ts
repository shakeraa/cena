import type { Ref } from 'vue'
import { ref } from 'vue'
import { $api } from '@/api/$api'

export interface UseApiMutationResult<TRes, TReq> {
  data: Ref<TRes | null>
  error: Ref<Error | null>
  loading: Ref<boolean>
  execute: (body: TReq) => Promise<TRes>
}

/**
 * POST/PUT/PATCH/DELETE wrapper. Call `execute(body)` to fire. Errors
 * propagate via both the reactive `error` ref AND as a thrown exception
 * from `execute`, so callers can await + try/catch.
 */
export function useApiMutation<TRes = unknown, TReq = unknown>(
  path: string,
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE' = 'POST',
): UseApiMutationResult<TRes, TReq> {
  const data = ref<TRes | null>(null) as Ref<TRes | null>
  const error = ref<Error | null>(null)
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
      error.value = err as Error
      throw err
    }
    finally {
      loading.value = false
    }
  }

  return { data, error, loading, execute }
}
