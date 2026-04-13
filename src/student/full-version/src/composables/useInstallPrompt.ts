import type { Ref } from 'vue'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * PWA-002: Custom install prompt management.
 *
 * Captures the `beforeinstallprompt` event (Chrome/Edge/Samsung) and
 * exposes reactive state so the UI can show a non-intrusive install
 * banner. Key rules:
 *
 *   - Only show after 2nd visit (no first-visit nagging)
 *   - Dismiss stores preference for 7 days (Chrome) / 14 days (iOS)
 *   - No dark patterns — "Not now" is equally prominent as "Install"
 *   - Detect standalone mode — never prompt if already installed
 */

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>
}

const VISIT_COUNT_KEY = 'cena-install-visit-count'
const DISMISS_KEY = 'cena-install-dismissed-at'
const CHROME_DISMISS_DAYS = 7
const IOS_DISMISS_DAYS = 14

export interface UseInstallPromptReturn {
  /** Whether the install banner should be shown */
  canShow: Ref<boolean>
  /** Whether the app is already installed (standalone mode) */
  isInstalled: Ref<boolean>
  /** Whether this is an iOS device without beforeinstallprompt */
  isIOS: Ref<boolean>
  /** Trigger the native install prompt (Chrome/Edge) */
  install: () => Promise<void>
  /** Dismiss the prompt for N days */
  dismiss: () => void
}

export function useInstallPrompt(): UseInstallPromptReturn {
  const deferredPrompt = ref<BeforeInstallPromptEvent | null>(null)
  const isInstalled = ref(false)
  const isIOS = ref(false)
  const dismissed = ref(false)
  const visitCount = ref(0)

  // Detect standalone mode (already installed)
  function checkInstalled(): boolean {
    if (typeof window === 'undefined')
      return false
    return window.matchMedia('(display-mode: standalone)').matches
      || (navigator as any).standalone === true
  }

  // Detect iOS Safari (no beforeinstallprompt support)
  function checkIOS(): boolean {
    if (typeof navigator === 'undefined')
      return false
    const ua = navigator.userAgent
    return /iPad|iPhone|iPod/.test(ua)
      || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)
  }

  // Check if dismiss is still within cooldown period
  function isDismissed(): boolean {
    try {
      const raw = localStorage.getItem(DISMISS_KEY)
      if (!raw)
        return false
      const dismissedAt = Number.parseInt(raw, 10)
      const days = checkIOS() ? IOS_DISMISS_DAYS : CHROME_DISMISS_DAYS
      const expiresAt = dismissedAt + (days * 24 * 60 * 60 * 1000)
      return Date.now() < expiresAt
    }
    catch {
      return false
    }
  }

  // Increment and read visit count
  function trackVisit(): number {
    try {
      const count = Number.parseInt(localStorage.getItem(VISIT_COUNT_KEY) || '0', 10) + 1
      localStorage.setItem(VISIT_COUNT_KEY, String(count))
      return count
    }
    catch {
      return 1
    }
  }

  const canShow = computed(() => {
    if (isInstalled.value)
      return false
    if (dismissed.value)
      return false
    if (visitCount.value < 2)
      return false
    // For Chrome/Edge: need the deferred prompt event
    // For iOS: show the manual guide
    return isIOS.value || deferredPrompt.value !== null
  })

  function handleBeforeInstallPrompt(e: Event) {
    // Prevent the mini-infobar from appearing on mobile
    e.preventDefault()
    deferredPrompt.value = e as BeforeInstallPromptEvent
  }

  async function install() {
    if (!deferredPrompt.value)
      return
    await deferredPrompt.value.prompt()
    const result = await deferredPrompt.value.userChoice
    if (result.outcome === 'accepted')
      isInstalled.value = true
    deferredPrompt.value = null
  }

  function dismiss() {
    dismissed.value = true
    try {
      localStorage.setItem(DISMISS_KEY, String(Date.now()))
    }
    catch {
      // Swallow quota/private-mode errors
    }
  }

  onMounted(() => {
    isInstalled.value = checkInstalled()
    isIOS.value = checkIOS()
    dismissed.value = isDismissed()
    visitCount.value = trackVisit()

    window.addEventListener('beforeinstallprompt', handleBeforeInstallPrompt)

    // Listen for app installation
    window.addEventListener('appinstalled', () => {
      isInstalled.value = true
      deferredPrompt.value = null
    })
  })

  onBeforeUnmount(() => {
    window.removeEventListener('beforeinstallprompt', handleBeforeInstallPrompt)
  })

  return { canShow, isInstalled, isIOS, install, dismiss }
}
