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
})
