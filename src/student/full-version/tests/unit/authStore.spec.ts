import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/authStore'

describe('authStore', () => {
  beforeEach(() => {
    // FIND-ux-010: authStore now persists to localStorage['cena-mock-auth'].
    // Clear it between tests so each spec starts from a known empty
    // state. Without this, __mockSignIn from an earlier test leaks into
    // the next one's "starts unauthed" assertion.
    if (typeof window !== 'undefined' && window.localStorage)
      window.localStorage.removeItem('cena-mock-auth')
    setActivePinia(createPinia())
  })

  it('starts unauthed with ready=false', () => {
    const store = useAuthStore()

    expect(store.isSignedIn).toBe(false)
    expect(store.ready).toBe(false)
    expect(store.uid).toBeNull()
  })

  it('persists the mock session to localStorage on __mockSignIn', () => {
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-persist', email: 'p@example.com', displayName: 'P' })

    const raw = window.localStorage.getItem('cena-mock-auth')

    expect(raw).not.toBeNull()

    const parsed = JSON.parse(raw as string)

    expect(parsed.uid).toBe('u-persist')
    expect(parsed.email).toBe('p@example.com')
    expect(parsed.displayName).toBe('P')
  })

  it('clears the mock session from localStorage on __signOut', () => {
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-signout' })
    expect(window.localStorage.getItem('cena-mock-auth')).not.toBeNull()

    store.__signOut()
    expect(window.localStorage.getItem('cena-mock-auth')).toBeNull()
  })

  it('hydrates from localStorage on store init', () => {
    // Simulate a hard-refresh mid-session: write the payload and then
    // fresh-construct the store (new pinia instance).
    window.localStorage.setItem(
      'cena-mock-auth',
      JSON.stringify({ uid: 'u-hydrate', email: 'h@example.com', displayName: 'H' }),
    )
    setActivePinia(createPinia())

    const store = useAuthStore()

    expect(store.isSignedIn).toBe(true)
    expect(store.uid).toBe('u-hydrate')
    expect(store.email).toBe('h@example.com')
    expect(store.displayName).toBe('H')
    expect(store.idToken).toBe('mock-token-u-hydrate')

    // `ready` must remain false after pure hydration — firebase.ts flips
    // it on the next microtask, mirroring the real onAuthStateChanged race.
    expect(store.ready).toBe(false)
  })

  it('flips ready after __setReady', () => {
    const store = useAuthStore()

    store.__setReady()
    expect(store.ready).toBe(true)
    expect(store.isSignedIn).toBe(false)
  })

  it('__mockSignIn sets uid + email + ready', () => {
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-1', email: 'x@example.com', displayName: 'X' })
    expect(store.isSignedIn).toBe(true)
    expect(store.uid).toBe('u-1')
    expect(store.email).toBe('x@example.com')
    expect(store.displayName).toBe('X')
    expect(store.idToken).toBe('mock-token-u-1')
    expect(store.ready).toBe(true)
  })

  it('__signOut clears uid and token but keeps ready=true', () => {
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-1' })
    store.__signOut()
    expect(store.isSignedIn).toBe(false)
    expect(store.uid).toBeNull()
    expect(store.idToken).toBeNull()
    expect(store.ready).toBe(true) // still resolved, just signed out
  })

  /**
   * FIND-qa-006 / FIND-ux-010: Hard navigation rehydration test
   *
   * Simulates a full page reload (hard navigation) where:
   * 1. User was signed in with mock auth
   * 2. User refreshes the page (F5/cmd+R)
   * 3. Store re-initializes and must rehydrate from localStorage
   *
   * This test verifies the mock auth survives the hard navigation.
   */
  it('rehydrates mock user after hard navigation (reset Pinia + re-init)', () => {
    // Step 1: Sign in as a mock user
    const store = useAuthStore()

    store.__mockSignIn({
      uid: 'u-hard-nav-test',
      email: 'hardnav@example.com',
      displayName: 'Hard Nav User',
    })

    // Verify the user is signed in
    expect(store.isSignedIn).toBe(true)
    expect(store.uid).toBe('u-hard-nav-test')

    // Step 2: Simulate hard navigation - reset Pinia and re-create store
    // This mimics what happens when the browser reloads the page
    setActivePinia(createPinia())

    // Step 3: Re-initialize the store (this happens in the new page load)
    const newStore = useAuthStore()

    // Step 4: Assert the store rehydrated from localStorage
    expect(newStore.isSignedIn).toBe(true)
    expect(newStore.uid).toBe('u-hard-nav-test')
    expect(newStore.email).toBe('hardnav@example.com')
    expect(newStore.displayName).toBe('Hard Nav User')
    expect(newStore.idToken).toBe('mock-token-u-hard-nav-test')

    // Note: `ready` stays false after hydration - firebase.ts flips it
    expect(newStore.ready).toBe(false)
  })

  /**
   * Synthetic regression test for FIND-ux-010:
   * If localStorage persistence is broken, the store should NOT be
   * signed in after a simulated hard navigation.
   */
  it('fails synthetic regression when localStorage persistence is broken', () => {
    // Sign in
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-regression-test', email: 'reg@example.com' })

    // Corrupt the localStorage entry (simulate broken persistence)
    window.localStorage.removeItem('cena-mock-auth')

    // Simulate hard navigation
    setActivePinia(createPinia())

    const newStore = useAuthStore()

    // Without localStorage persistence, the store should NOT be signed in
    expect(newStore.isSignedIn).toBe(false)
    expect(newStore.uid).toBeNull()
  })
})
