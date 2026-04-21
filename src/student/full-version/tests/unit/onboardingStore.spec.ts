import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import {
  DEFAULT_WEEKLY_HOURS,
  MAX_EXAM_TARGETS,
  useOnboardingStore,
  type ExamTargetDraft,
} from '@/stores/onboardingStore'

function makeTarget(overrides: Partial<ExamTargetDraft> = {}): ExamTargetDraft {
  return {
    examCode: 'bagrut-math-5u',
    displayName: 'Bagrut Math 5-unit',
    family: 'BAGRUT',
    track: '5u',
    questionPaperCodes: ['035381', '035382'],
    availableQuestionPaperCodes: ['035381', '035382'],
    sitting: {
      sittingCode: '2026-summer-A',
      academicYear: '2026',
      season: 0,
      moed: 0,
      canonicalDate: '2026-06-15',
    },
    availableSittings: [],
    weeklyHours: DEFAULT_WEEKLY_HOURS,
    ...overrides,
  }
}

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
    // PRR-221: flow is now 8 steps (welcome, role, exam-targets,
    // per-target-plan, language, diagnostic, self-assessment, confirm).
    expect(store.totalSteps).toBe(8)
  })

  it('cannot advance from role until a role is picked', () => {
    const store = useOnboardingStore()

    store.next() // welcome → role
    expect(store.step).toBe('role')
    expect(store.canAdvance).toBe(false)

    store.setRole('student')
    expect(store.canAdvance).toBe(true)
  })

  it('walks through welcome → role → exam-targets → per-target-plan → language → diagnostic → self-assessment → confirm', () => {
    const store = useOnboardingStore()

    store.next()
    expect(store.step).toBe('role')

    store.setRole('test-prep')
    store.next()
    expect(store.step).toBe('exam-targets')

    store.setExamTargets([makeTarget()])
    store.next()
    expect(store.step).toBe('per-target-plan')

    store.next()
    expect(store.step).toBe('language')

    store.next()
    expect(store.step).toBe('diagnostic')

    store.skipDiagnostic()
    store.next()
    expect(store.step).toBe('self-assessment')

    store.next()
    expect(store.step).toBe('confirm')

    store.back()
    expect(store.step).toBe('self-assessment')
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
    store.next() // role -> exam-targets
    store.setExamTargets([makeTarget()])
    store.next() // exam-targets -> per-target-plan
    store.next() // per-target-plan -> language
    store.setLocale('en')
    expect(store.step).toBe('language')
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

  // ── PRR-221 exam-target mutations ─────────────────────────────────────
  describe('exam-target drafts (PRR-221)', () => {
    it('starts with an empty examTargets list', () => {
      const store = useOnboardingStore()
      expect(store.examTargets).toEqual([])
    })

    it('cannot advance from exam-targets with zero drafts', () => {
      const store = useOnboardingStore()
      store.next() // welcome → role
      store.setRole('test-prep')
      store.next() // role → exam-targets
      expect(store.step).toBe('exam-targets')
      expect(store.canAdvance).toBe(false)
      store.setExamTargets([makeTarget()])
      expect(store.canAdvance).toBe(true)
    })

    it('addExamTarget is idempotent by examCode', () => {
      const store = useOnboardingStore()
      store.addExamTarget(makeTarget({ examCode: 'a' }))
      store.addExamTarget(makeTarget({ examCode: 'a' }))
      expect(store.examTargets.length).toBe(1)
    })

    it('caps at MAX_EXAM_TARGETS', () => {
      const store = useOnboardingStore()
      for (let i = 0; i < MAX_EXAM_TARGETS + 3; i++)
        store.addExamTarget(makeTarget({ examCode: `e-${i}` }))
      expect(store.examTargets.length).toBe(MAX_EXAM_TARGETS)
    })

    it('removeExamTarget drops by examCode', () => {
      const store = useOnboardingStore()
      store.setExamTargets([
        makeTarget({ examCode: 'a' }),
        makeTarget({ examCode: 'b' }),
      ])
      store.removeExamTarget('a')
      expect(store.examTargets.map(t => t.examCode)).toEqual(['b'])
    })

    it('updateExamTarget merges patch on matching code', () => {
      const store = useOnboardingStore()
      store.setExamTargets([makeTarget({ examCode: 'a', weeklyHours: 4 })])
      store.updateExamTarget('a', { weeklyHours: 20 })
      expect(store.examTargets[0].weeklyHours).toBe(20)
    })

    it('per-target-plan canAdvance requires every target to have sitting + weeklyHours in range', () => {
      const store = useOnboardingStore()
      store.next() // welcome → role
      store.setRole('test-prep')
      store.next() // role → exam-targets
      store.setExamTargets([makeTarget({ sitting: null })])
      store.next() // exam-targets → per-target-plan
      expect(store.step).toBe('per-target-plan')
      expect(store.canAdvance).toBe(false) // no sitting
      store.updateExamTarget('bagrut-math-5u', {
        sitting: {
          sittingCode: 's1',
          academicYear: '2026',
          season: 0,
          moed: 0,
        },
      })
      expect(store.canAdvance).toBe(true)
    })

    it('bagrut target with zero question-papers cannot advance', () => {
      const store = useOnboardingStore()
      store.next()
      store.setRole('test-prep')
      store.next()
      store.setExamTargets([makeTarget({ questionPaperCodes: [] })])
      store.next()
      expect(store.step).toBe('per-target-plan')
      expect(store.canAdvance).toBe(false)
    })
  })
})
