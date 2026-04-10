import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useOnboardingStore } from '@/stores/onboardingStore'

const STORAGE_KEY = 'cena-onboarding-state'

describe('onboardingStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
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

  it('rehydrates from localStorage on store creation', () => {
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
