import type { Ref } from 'vue'
import { onMounted, ref } from 'vue'
import { $api } from '@/api/$api'

export interface UseApiQueryResult<T> {
  data: Ref<T | null>
  error: Ref<Error | null>
  loading: Ref<boolean>
  refresh: () => Promise<void>
}

/**
 * Opinionated GET wrapper for Vue components. Fetches on mount by default;
 * exposes a `refresh()` for manual retries. Errors are surfaced as a
 * reactive ref, never thrown.
 */
export function useApiQuery<T = unknown>(
  path: string,
  opts: { immediate?: boolean } = {},
): UseApiQueryResult<T> {
  const data = ref<T | null>(null) as Ref<T | null>
  const error = ref<Error | null>(null)
  const loading = ref(false)

  const refresh = async () => {
    loading.value = true
    error.value = null
    try {
      data.value = await $api<T>(path)
    }
    catch (err) {
      error.value = err as Error
    }
    finally {
      loading.value = false
    }
  }

  if (opts.immediate !== false)
    onMounted(refresh)

  return { data, error, loading, refresh }
}
