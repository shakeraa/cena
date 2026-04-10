import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

/**
 * `meStore` — mirrors the backend `/api/me` payload that STB-00 will return.
 * STU-W-02 ships a stub that lets E2E tests seed `onboardedAt` directly.
 * STU-W-03 will wire the real `/api/me` fetch on login.
 */
export interface MeProfile {
  uid: string
  displayName: string
  email: string
  locale: 'en' | 'ar' | 'he'
  onboardedAt: string | null
}

export const useMeStore = defineStore('me', () => {
  const profile = ref<MeProfile | null>(null)
  const activeSessionId = ref<string | null>(null)

  const isOnboarded = computed(() => {
    return !!(profile.value?.onboardedAt)
  })

  const hasActiveSession = computed(() => activeSessionId.value !== null)

  function __setProfile(next: MeProfile | null) {
    profile.value = next
  }

  function __setOnboardedAt(iso: string | null) {
    if (profile.value)
      profile.value.onboardedAt = iso
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
