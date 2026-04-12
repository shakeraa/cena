import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { sanitizeLocale } from '@/composables/useAvailableLocales'

/**
 * `onboardingStore` — Pinia store backing the 3-step wizard from
 * STU-W-04C Phase A (Welcome → Role → Language). State is persisted
 * to localStorage so a mid-wizard refresh resumes at the current step.
 *
 * STU-W-04C-B extends this with Subjects, Goals, Diagnostic steps.
 */

export type StudentRole = 'student' | 'self-learner' | 'test-prep' | 'homeschool'
export type SupportedLocale = 'en' | 'ar' | 'he'
export type WizardStep = 'welcome' | 'role' | 'language' | 'confirm'

export interface OnboardingState {
  step: WizardStep
  role: StudentRole | null
  locale: SupportedLocale
  dailyTimeGoalMinutes: number
  subjects: string[]
  completedAt: string | null
}

const STORAGE_KEY = 'cena-onboarding-state'
const DEFAULT_DAILY_GOAL = 15

function readPersisted(): Partial<OnboardingState> | null {
  if (typeof localStorage === 'undefined')
    return null
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? JSON.parse(raw) : null
  }
  catch {
    return null
  }
}

function writePersisted(state: OnboardingState) {
  if (typeof localStorage === 'undefined')
    return
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
  }
  catch {
    // swallow quota errors — the wizard will still work, just no resume
  }
}

export const useOnboardingStore = defineStore('onboarding', () => {
  const persisted = readPersisted()

  const step = ref<WizardStep>(persisted?.step ?? 'welcome')
  const role = ref<StudentRole | null>(persisted?.role ?? null)
  // FIND-pedagogy-010: sanitize persisted locale through the Hebrew gate
  // so a stale 'he' in localStorage doesn't survive a build-flag flip.
  const locale = ref<SupportedLocale>(
    sanitizeLocale((persisted?.locale as string) ?? 'en') as SupportedLocale,
  )
  const dailyTimeGoalMinutes = ref<number>(persisted?.dailyTimeGoalMinutes ?? DEFAULT_DAILY_GOAL)
  const subjects = ref<string[]>(persisted?.subjects ?? [])
  const completedAt = ref<string | null>(persisted?.completedAt ?? null)

  const stepIndex = computed(() => {
    const order: WizardStep[] = ['welcome', 'role', 'language', 'confirm']
    return order.indexOf(step.value)
  })

  const totalSteps = computed(() => 4) // Phase A: 4 steps total

  const progressPercent = computed(() => {
    return Math.round(((stepIndex.value + 1) / totalSteps.value) * 100)
  })

  const canAdvance = computed(() => {
    switch (step.value) {
      case 'welcome': return true
      case 'role': return role.value !== null
      case 'language': return sanitizeLocale(locale.value) === locale.value
      case 'confirm': return role.value !== null
      default: return false
    }
  })

  function next() {
    const order: WizardStep[] = ['welcome', 'role', 'language', 'confirm']
    const idx = order.indexOf(step.value)
    if (idx < order.length - 1)
      step.value = order[idx + 1]
  }

  function back() {
    const order: WizardStep[] = ['welcome', 'role', 'language', 'confirm']
    const idx = order.indexOf(step.value)
    if (idx > 0)
      step.value = order[idx - 1]
  }

  function setRole(next: StudentRole) {
    role.value = next
  }

  function setLocale(next: SupportedLocale) {
    // FIND-pedagogy-010: validate through the Hebrew gate before accepting
    locale.value = sanitizeLocale(next) as SupportedLocale
  }

  function reset() {
    step.value = 'welcome'
    role.value = null
    locale.value = 'en'
    dailyTimeGoalMinutes.value = DEFAULT_DAILY_GOAL
    subjects.value = []
    completedAt.value = null
    if (typeof localStorage !== 'undefined')
      localStorage.removeItem(STORAGE_KEY)
  }

  function markCompleted() {
    completedAt.value = new Date().toISOString()
  }

  watch(
    [step, role, locale, dailyTimeGoalMinutes, subjects, completedAt],
    () => {
      writePersisted({
        step: step.value,
        role: role.value,
        locale: locale.value,
        dailyTimeGoalMinutes: dailyTimeGoalMinutes.value,
        subjects: subjects.value,
        completedAt: completedAt.value,
      })
    },
    { deep: true },
  )

  return {
    step,
    role,
    locale,
    dailyTimeGoalMinutes,
    subjects,
    completedAt,
    stepIndex,
    totalSteps,
    progressPercent,
    canAdvance,
    next,
    back,
    setRole,
    setLocale,
    reset,
    markCompleted,
  }
})
