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
export type WizardStep = 'welcome' | 'role' | 'language' | 'diagnostic' | 'self-assessment' | 'confirm'

export interface DiagnosticResponseItem {
  questionId: string
  subject: string
  correct: boolean
  difficulty: number
}

// RDY-057: self-reported affective state captured after the diagnostic.
// TopicFeeling values: 'Solid' | 'Unsure' | 'Anxious' | 'New'.
// The store keeps these as loose strings so the server-side enum remains
// the single source of truth; conversion happens on POST.
export interface SelfAssessmentState {
  skipped: boolean
  subjectConfidence: Record<string, number>  // subject → 1..5 Likert
  strengths: string[]                         // lowercase-kebab tag ids
  frictionPoints: string[]
  topicFeelings: Record<string, string>       // concept-id → TopicFeeling string
  freeText: string                            // ≤200 chars
  optInPersistent: boolean
}

export interface OnboardingState {
  step: WizardStep
  role: StudentRole | null
  locale: SupportedLocale
  dailyTimeGoalMinutes: number
  subjects: string[]
  diagnosticResponses: DiagnosticResponseItem[]
  diagnosticSkipped: boolean
  selfAssessment: SelfAssessmentState
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
  const diagnosticResponses = ref<DiagnosticResponseItem[]>((persisted as any)?.diagnosticResponses ?? [])
  const diagnosticSkipped = ref<boolean>((persisted as any)?.diagnosticSkipped ?? false)
  const selfAssessment = ref<SelfAssessmentState>({
    skipped: (persisted as any)?.selfAssessment?.skipped ?? false,
    subjectConfidence: (persisted as any)?.selfAssessment?.subjectConfidence ?? {},
    strengths: (persisted as any)?.selfAssessment?.strengths ?? [],
    frictionPoints: (persisted as any)?.selfAssessment?.frictionPoints ?? [],
    topicFeelings: (persisted as any)?.selfAssessment?.topicFeelings ?? {},
    freeText: (persisted as any)?.selfAssessment?.freeText ?? '',
    optInPersistent: (persisted as any)?.selfAssessment?.optInPersistent ?? false,
  })
  const completedAt = ref<string | null>(persisted?.completedAt ?? null)

  const STEP_ORDER: WizardStep[] = ['welcome', 'role', 'language', 'diagnostic', 'self-assessment', 'confirm']

  const stepIndex = computed(() => {
    return STEP_ORDER.indexOf(step.value)
  })

  const totalSteps = computed(() => STEP_ORDER.length)

  const progressPercent = computed(() => {
    return Math.round(((stepIndex.value + 1) / totalSteps.value) * 100)
  })

  const canAdvance = computed(() => {
    switch (step.value) {
      case 'welcome': return true
      case 'role': return role.value !== null
      case 'language': return sanitizeLocale(locale.value) === locale.value
      case 'diagnostic': return diagnosticResponses.value.length > 0 || diagnosticSkipped.value
      case 'self-assessment': return true  // always advanceable; skip is a valid outcome
      case 'confirm': return role.value !== null
      default: return false
    }
  })

  function next() {
    const idx = STEP_ORDER.indexOf(step.value)
    if (idx < STEP_ORDER.length - 1)
      step.value = STEP_ORDER[idx + 1]
  }

  function back() {
    const idx = STEP_ORDER.indexOf(step.value)
    if (idx > 0)
      step.value = STEP_ORDER[idx - 1]
  }

  function setRole(next: StudentRole) {
    role.value = next
  }

  function setLocale(next: SupportedLocale) {
    // FIND-pedagogy-010: validate through the Hebrew gate before accepting
    locale.value = sanitizeLocale(next) as SupportedLocale
  }

  function setDiagnosticResults(items: DiagnosticResponseItem[]) {
    diagnosticResponses.value = items
    diagnosticSkipped.value = false
  }

  function skipDiagnostic() {
    diagnosticResponses.value = []
    diagnosticSkipped.value = true
  }

  function setSelfAssessment(patch: Partial<SelfAssessmentState>) {
    selfAssessment.value = { ...selfAssessment.value, ...patch, skipped: false }
  }

  function skipSelfAssessment() {
    selfAssessment.value = {
      skipped: true,
      subjectConfidence: {},
      strengths: [],
      frictionPoints: [],
      topicFeelings: {},
      freeText: '',
      optInPersistent: false,
    }
  }

  function reset() {
    step.value = 'welcome'
    role.value = null
    locale.value = 'en'
    dailyTimeGoalMinutes.value = DEFAULT_DAILY_GOAL
    subjects.value = []
    diagnosticResponses.value = []
    diagnosticSkipped.value = false
    selfAssessment.value = {
      skipped: false,
      subjectConfidence: {},
      strengths: [],
      frictionPoints: [],
      topicFeelings: {},
      freeText: '',
      optInPersistent: false,
    }
    completedAt.value = null
    if (typeof localStorage !== 'undefined')
      localStorage.removeItem(STORAGE_KEY)
  }

  function markCompleted() {
    completedAt.value = new Date().toISOString()
  }

  watch(
    [step, role, locale, dailyTimeGoalMinutes, subjects, diagnosticResponses, diagnosticSkipped, selfAssessment, completedAt],
    () => {
      writePersisted({
        step: step.value,
        role: role.value,
        locale: locale.value,
        dailyTimeGoalMinutes: dailyTimeGoalMinutes.value,
        subjects: subjects.value,
        diagnosticResponses: diagnosticResponses.value,
        diagnosticSkipped: diagnosticSkipped.value,
        selfAssessment: selfAssessment.value,
        completedAt: completedAt.value,
      } as any)
    },
    { deep: true },
  )

  return {
    step,
    role,
    locale,
    dailyTimeGoalMinutes,
    subjects,
    diagnosticResponses,
    diagnosticSkipped,
    selfAssessment,
    completedAt,
    stepIndex,
    totalSteps,
    progressPercent,
    canAdvance,
    next,
    back,
    setRole,
    setLocale,
    setDiagnosticResults,
    skipDiagnostic,
    setSelfAssessment,
    skipSelfAssessment,
    reset,
    markCompleted,
  }
})
