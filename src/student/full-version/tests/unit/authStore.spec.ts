import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/authStore'

describe('authStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts unauthed with ready=false', () => {
    const store = useAuthStore()

    expect(store.isSignedIn).toBe(false)
    expect(store.ready).toBe(false)
    expect(store.uid).toBeNull()
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
