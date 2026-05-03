import { onBeforeUnmount, onMounted, ref } from 'vue'
import type { Ref } from 'vue'

export interface UseVirtualKeyboardReturn {

  /** Current keyboard height in pixels (0 when closed) */
  keyboardHeight: Ref<number>

  /** Whether the virtual keyboard is currently visible */
  isKeyboardOpen: Ref<boolean>

  /** Scroll the given element into view above the keyboard */
  scrollIntoView: (el: HTMLElement) => void
}

export function useVirtualKeyboard(): UseVirtualKeyboardReturn {
  const keyboardHeight = ref(0)
  const isKeyboardOpen = ref(false)

  function handleResize() {
    if (!window.visualViewport)
      return
    const height = window.innerHeight - window.visualViewport.height

    // Only treat as keyboard if height > 100px (avoid toolbar triggers)
    keyboardHeight.value = height > 100 ? height : 0
    isKeyboardOpen.value = keyboardHeight.value > 0
    document.documentElement.style.setProperty(
      '--keyboard-height', `${keyboardHeight.value}px`,
    )
  }

  function scrollIntoView(el: HTMLElement) {
    if (!isKeyboardOpen.value)
      return

    // Use scrollIntoView with block: 'center' to position above keyboard
    el.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }

  onMounted(() => {
    if (window.visualViewport) {
      window.visualViewport.addEventListener('resize', handleResize)
      window.visualViewport.addEventListener('scroll', handleResize)
    }
  })

  onBeforeUnmount(() => {
    if (window.visualViewport) {
      window.visualViewport.removeEventListener('resize', handleResize)
      window.visualViewport.removeEventListener('scroll', handleResize)
    }
  })

  return { keyboardHeight, isKeyboardOpen, scrollIntoView }
}
