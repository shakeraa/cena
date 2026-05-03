import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useMeStore } from '@/stores/meStore'

describe('meStore', () => {
  beforeEach(() => {
    // FIND-ux-010: meStore now persists to localStorage['cena-mock-me'].
    // Clear it between tests so each spec starts from a known empty
    // state; otherwise assertions on `profile === null` would leak
    // from a previous test that wrote a profile.
    if (typeof window !== 'undefined' && window.localStorage)
      window.localStorage.removeItem('cena-mock-me')
    setActivePinia(createPinia())
  })

  it('starts with no profile and not onboarded', () => {
    const store = useMeStore()

    expect(store.profile).toBeNull()
    expect(store.isOnboarded).toBe(false)
    expect(store.hasActiveSession).toBe(false)
  })

  it('isOnboarded is false when onboardedAt is null', () => {
    const store = useMeStore()

    store.__setProfile({
      uid: 'u-1',
      displayName: 'X',
      email: 'x@example.com',
      locale: 'en',
      onboardedAt: null,
    })
    expect(store.isOnboarded).toBe(false)
  })

  it('isOnboarded is true when onboardedAt is set', () => {
    const store = useMeStore()

    store.__setProfile({
      uid: 'u-1',
      displayName: 'X',
      email: 'x@example.com',
      locale: 'en',
      onboardedAt: '2026-04-10T12:00:00Z',
    })
    expect(store.isOnboarded).toBe(true)
  })

  it('__setOnboardedAt mutates the profile in place', () => {
    const store = useMeStore()

    store.__setProfile({
      uid: 'u-1',
      displayName: 'X',
      email: 'x@example.com',
      locale: 'en',
      onboardedAt: null,
    })
    store.__setOnboardedAt('2026-04-10T12:00:00Z')
    expect(store.isOnboarded).toBe(true)
    expect(store.profile?.onboardedAt).toBe('2026-04-10T12:00:00Z')
  })

  it('hasActiveSession reflects activeSessionId', () => {
    const store = useMeStore()

    store.__setActiveSession('session-123')
    expect(store.hasActiveSession).toBe(true)
    expect(store.activeSessionId).toBe('session-123')
    store.__setActiveSession(null)
    expect(store.hasActiveSession).toBe(false)
  })
})
