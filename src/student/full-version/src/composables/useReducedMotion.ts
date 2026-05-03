import type { Ref } from 'vue'
import { onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * Reactive `prefers-reduced-motion` state. Updates live when the user
 * toggles the OS setting; does not just read once at mount.
 *
 * Returns `false` when `matchMedia` is unavailable (SSR, tests without
 * jsdom media query support) so animations are the default fallback.
 */
export function useReducedMotion(): Ref<boolean> {
  const prefersReduced = ref(false)

  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function')
    return prefersReduced

  const mq = window.matchMedia('(prefers-reduced-motion: reduce)')

  prefersReduced.value = mq.matches

  const handler = (event: MediaQueryListEvent) => {
    prefersReduced.value = event.matches
  }

  onMounted(() => {
    // addEventListener is the modern API; fall back to addListener for older browsers.
    if (typeof mq.addEventListener === 'function')
      mq.addEventListener('change', handler)
    else if (typeof (mq as any).addListener === 'function')
      (mq as any).addListener(handler)
  })

  onBeforeUnmount(() => {
    if (typeof mq.removeEventListener === 'function')
      mq.removeEventListener('change', handler)
    else if (typeof (mq as any).removeListener === 'function')
      (mq as any).removeListener(handler)
  })

  return prefersReduced
}
