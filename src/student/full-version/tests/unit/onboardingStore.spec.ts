import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useOnboardingStore } from '@/stores/onboardingStore'

const STORAGE_KEY = 'cena-onboarding-state'

describe('onboardingStore', () => {
  beforeEach(() => {
    vi.unstubAllEnvs()
    localStorage.clear()
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('starts at the welcome step with a null role and en locale', () => {
    const store = useOnboardingStore()

    expect(store.step).toBe('welcome')
    expect(store.role).toBeNull()
    expect(store.locale).toBe('en')
    expect(store.stepIndex).toBe(0)
    expect(store.totalSteps).toBe(4)
  })

  it('cannot advance from role until a role is picked', () => {
    const store = useOnboardingStore()

    store.next() // welcome → role
    expect(store.step).toBe('role')
    expect(store.canAdvance).toBe(false)

    store.setRole('student')
    expect(store.canAdvance).toBe(true)
  })

  it('walks through welcome → role → language → confirm with next/back', () => {
    const store = useOnboardingStore()

    store.next()
    expect(store.step).toBe('role')

    store.setRole('test-prep')
    store.next()
    expect(store.step).toBe('language')

    store.next()
    expect(store.step).toBe('confirm')

    // back brings us back to language
    store.back()
    expect(store.step).toBe('language')
  })

  it('clamps next() at the final step', () => {
    const store = useOnboardingStore()

    store.setRole('student')
    for (let i = 0; i < 10; i++)
      store.next()

    expect(store.step).toBe('confirm')
  })

  it('clamps back() at the first step', () => {
    const store = useOnboardingStore()

    for (let i = 0; i < 5; i++)
      store.back()

    expect(store.step).toBe('welcome')
  })

  it('persists state to localStorage on change', async () => {
    const store = useOnboardingStore()

    store.setRole('homeschool')
    store.setLocale('ar')
    store.next()

    // deep watch fires on next tick; give microtask queue a chance
    await new Promise(resolve => setTimeout(resolve, 0))

    const raw = localStorage.getItem(STORAGE_KEY)

    expect(raw).toBeTruthy()

    const parsed = JSON.parse(raw!)

    expect(parsed.role).toBe('homeschool')
    expect(parsed.locale).toBe('ar')
    expect(parsed.step).toBe('role')
  })

  it('rehydrates from localStorage on store creation (Hebrew enabled)', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
      step: 'language',
      role: 'self-learner',
      locale: 'he',
      dailyTimeGoalMinutes: 30,
      subjects: [],
      completedAt: null,
    }))

    const store = useOnboardingStore()

    expect(store.step).toBe('language')
    expect(store.role).toBe('self-learner')
    expect(store.locale).toBe('he')
  })

  // FIND-pedagogy-010: regression test — stale 'he' in localStorage must
  // be sanitized to 'en' when the Hebrew gate is off.
  it('sanitizes persisted he locale to en when Hebrew is disabled', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
      step: 'language',
      role: 'self-learner',
      locale: 'he',
      dailyTimeGoalMinutes: 30,
      subjects: [],
      completedAt: null,
    }))

    const store = useOnboardingStore()

    expect(store.locale).toBe('en')
  })

  // FIND-pedagogy-010: regression test — setLocale('he') is blocked when
  // Hebrew is disabled.
  it('setLocale("he") falls back to en when Hebrew is disabled', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    const store = useOnboardingStore()

    store.setLocale('he')
    expect(store.locale).toBe('en')
  })

  // FIND-pedagogy-010: canAdvance must be false on language step when
  // locale is 'he' but Hebrew is disabled (which sanitizeLocale would have
  // blocked, but belt-and-suspenders).
  it('canAdvance is true on language step when locale is valid (en)', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    const store = useOnboardingStore()

    store.next() // welcome -> role
    store.setRole('student')
    store.next() // role -> language
    store.setLocale('en')
    expect(store.canAdvance).toBe(true)
  })

  it('reset() clears state and wipes localStorage', () => {
    const store = useOnboardingStore()

    store.setRole('student')
    store.next()
    store.reset()

    expect(store.step).toBe('welcome')
    expect(store.role).toBeNull()
    expect(store.locale).toBe('en')
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('markCompleted stamps completedAt', () => {
    const store = useOnboardingStore()

    expect(store.completedAt).toBeNull()
    store.markCompleted()
    expect(store.completedAt).toBeTruthy()
    expect(new Date(store.completedAt!).toString()).not.toBe('Invalid Date')
  })
})
