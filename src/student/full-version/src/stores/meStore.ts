import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

/**
 * `meStore` — mirrors the backend `/api/me` payload that STB-00 will return.
 * STU-W-02 ships a stub that lets E2E tests seed `onboardedAt` directly.
 * STU-W-03 will wire the real `/api/me` fetch on login.
 *
 * FIND-ux-010: like `authStore`, the profile is now persisted to
 * `localStorage['cena-mock-me']` so hard-refreshes preserve `onboardedAt`
 * and don't bounce signed-in users to `/onboarding`. The key is the same
 * one `src/plugins/firebase.ts` already reads on boot, so firebase.ts's
 * hydration path stays authoritative for tests that seed profiles
 * externally; we just also start *writing* to it.
 */
export interface MeProfile {
  uid: string
  displayName: string
  email: string
  locale: 'en' | 'ar' | 'he'
  onboardedAt: string | null
}

const MOCK_ME_STORAGE_KEY = 'cena-mock-me'

function writeMeToStorage(profile: MeProfile | null): void {
  if (typeof window === 'undefined')
    return
  try {
    if (profile == null)
      window.localStorage.removeItem(MOCK_ME_STORAGE_KEY)
    else
      window.localStorage.setItem(MOCK_ME_STORAGE_KEY, JSON.stringify(profile))
  }
  catch {
    // Quota exceeded / privacy mode — silently ignore.
  }
}

function readMeFromStorage(): MeProfile | null {
  if (typeof window === 'undefined')
    return null
  try {
    const raw = window.localStorage.getItem(MOCK_ME_STORAGE_KEY)
    if (!raw)
      return null
    const parsed = JSON.parse(raw) as Partial<MeProfile>
    if (typeof parsed?.uid !== 'string' || parsed.uid.length === 0)
      return null

    return {
      uid: parsed.uid,
      displayName: typeof parsed.displayName === 'string' ? parsed.displayName : '',
      email: typeof parsed.email === 'string' ? parsed.email : '',
      locale: (parsed.locale === 'ar' || parsed.locale === 'he') ? parsed.locale : 'en',
      onboardedAt: typeof parsed.onboardedAt === 'string' ? parsed.onboardedAt : null,
    }
  }
  catch {
    return null
  }
}

export const useMeStore = defineStore('me', () => {
  const profile = ref<MeProfile | null>(readMeFromStorage())
  const activeSessionId = ref<string | null>(null)

  const isOnboarded = computed(() => {
    return !!(profile.value?.onboardedAt)
  })

  const hasActiveSession = computed(() => activeSessionId.value !== null)

  function __setProfile(next: MeProfile | null) {
    profile.value = next
    writeMeToStorage(next)
  }

  function __setOnboardedAt(iso: string | null) {
    if (profile.value) {
      profile.value.onboardedAt = iso
      writeMeToStorage(profile.value)
    }
  }

  function __setActiveSession(sessionId: string | null) {
    activeSessionId.value = sessionId
  }

  return {
    profile,
    activeSessionId,
    isOnboarded,
    hasActiveSession,
    __setProfile,
    __setOnboardedAt,
    __setActiveSession,
  }
})
