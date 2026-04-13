import { ref, onMounted, onBeforeUnmount } from 'vue'
import type { Ref } from 'vue'

export interface UseTabVisibilityReturn {
  isVisible: Ref<boolean>
  lastHiddenAt: Ref<Date | null>
  hiddenDurationMs: Ref<number>
}

export function useTabVisibility(): UseTabVisibilityReturn {
  const isVisible = ref(!document.hidden)
  const lastHiddenAt = ref<Date | null>(null)
  const hiddenDurationMs = ref(0)

  function handleVisibilityChange() {
    isVisible.value = !document.hidden
    if (document.hidden) {
      lastHiddenAt.value = new Date()
    }
    else if (lastHiddenAt.value) {
      hiddenDurationMs.value = Date.now() - lastHiddenAt.value.getTime()
    }
  }

  onMounted(() => {
    document.addEventListener('visibilitychange', handleVisibilityChange)
  })

  onBeforeUnmount(() => {
    document.removeEventListener('visibilitychange', handleVisibilityChange)
  })

  return { isVisible, lastHiddenAt, hiddenDurationMs }
}
