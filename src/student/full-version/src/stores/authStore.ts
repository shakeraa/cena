import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

/**
 * Auth store — student app wrapper over Firebase Auth state.
 * STU-W-02 ships a stub that starts unauthed and exposes a `__mockSignIn`
 * helper so E2E tests can simulate post-sign-in flow without real Firebase.
 * STU-W-04 will replace the internals with real Firebase Auth SDK wiring.
 */
export const useAuthStore = defineStore('auth', () => {
  const uid = ref<string | null>(null)
  const email = ref<string | null>(null)
  const displayName = ref<string | null>(null)
  const idToken = ref<string | null>(null)

  // The router guard must wait for the first auth-state resolution before
  // making decisions, otherwise the initial load redirects to /login for a
  // single frame. `ready` flips to true as soon as we know whether the user
  // is signed in.
  const ready = ref(false)

  const isSignedIn = computed(() => uid.value !== null)

  function __setReady() {
    ready.value = true
  }

  function __mockSignIn(payload: { uid: string; email?: string; displayName?: string }) {
    uid.value = payload.uid
    email.value = payload.email ?? null
    displayName.value = payload.displayName ?? null
    idToken.value = `mock-token-${payload.uid}`
    ready.value = true
  }

  function __signOut() {
    uid.value = null
    email.value = null
    displayName.value = null
    idToken.value = null
  }

  return {
    uid,
    email,
    displayName,
    idToken,
    ready,
    isSignedIn,
    __setReady,
    __mockSignIn,
    __signOut,
  }
})
