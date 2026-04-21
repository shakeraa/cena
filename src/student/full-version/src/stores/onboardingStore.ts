import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { sanitizeLocale, useAvailableLocales } from '@/composables/useAvailableLocales'
import { inferLocale } from '@/composables/useLocaleInference'

/**
 * `onboardingStore` — Pinia store backing the 3-step wizard from
 * STU-W-04C Phase A (Welcome → Role → Language). State is persisted
 * to localStorage so a mid-wizard refresh resumes at the current step.
 *
 * STU-W-04C-B extends this with Subjects, Goals, Diagnostic steps.
 * PRR-032 adds `numeralsPreference` (western vs eastern Arabic digits).
 */

export type StudentRole = 'student' | 'self-learner' | 'test-prep' | 'homeschool'
export type SupportedLocale = 'en' | 'ar' | 'he'
// PRR-221: insert `exam-targets` + `per-target-plan` between `role` and `language`.
export type WizardStep
  = 'welcome'
  | 'role'
  | 'exam-targets'
  | 'per-target-plan'
  | 'language'
  | 'diagnostic'
  | 'self-assessment'
  | 'confirm'
/** PRR-032: which numeral system to render in math output for the student. */
export type NumeralsPreference = 'western' | 'eastern'

export interface DiagnosticResponseItem {
  questionId: string
  subject: string
  correct: boolean
  difficulty: number
}

/**
 * PRR-221: an in-memory sitting tuple as drafted by the student in the
 * per-target-plan step. Wire shape maps to the server's `SittingCodeDto`
 * on POST. Season/Moed are carried as ordinal ints matching the server
 * `SittingSeason` / `SittingMoed` enums (0-indexed).
 */
export interface ExamSittingDraft {
  sittingCode: string
  academicYear: string
  season: number
  moed: number
  canonicalDate?: string
}

/**
 * PRR-221: a draft of one exam target while the student is still
 * inside the onboarding wizard. Persisted to localStorage so a
 * mid-wizard refresh resumes with the selected cards intact. Committed
 * to the server via `POST /api/me/exam-targets` on confirm.
 */
export interface ExamTargetDraft {
  /** Catalog exam code (e.g. "bagrut-math-5u"). */
  examCode: string
  /** Localized display name for UI recap. */
  displayName: string
  /** Bagrut/Standardized/etc — used to decide whether שאלון picker shows. */
  family: string
  /** Optional track code for multi-track exams. */
  track?: string
  /** Bagrut only: selected question-paper codes (defaults = all). */
  questionPaperCodes: string[]
  /** All available question-paper codes (from catalog) for the target. */
  availableQuestionPaperCodes: string[]
  /** Student-chosen sitting. */
  sitting: ExamSittingDraft | null
  /** All catalog sittings (for the sitting picker). */
  availableSittings: ExamSittingDraft[]
  /** Weekly time budget for this target (1..40). */
  weeklyHours: number
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
  /**
   * PRR-032: Numerals preference. Null = "follow locale default"
   * (inferred on read via inferNumeralsPreference); a non-null value is a
   * user override. Persisted so it survives refresh.
   */
  numeralsPreference: NumeralsPreference | null
  dailyTimeGoalMinutes: number
  subjects: string[]
  /**
   * PRR-221: student-drafted exam targets. Committed to the server on
   * confirm via POST /api/me/exam-targets. Min 1, max 4 for MVP
   * (server cap is 5; soft-warn at 4 is a follow-up).
   */
  examTargets: ExamTargetDraft[]
  diagnosticResponses: DiagnosticResponseItem[]
  diagnosticSkipped: boolean
  selfAssessment: SelfAssessmentState
  completedAt: string | null
}

const STORAGE_KEY = 'cena-onboarding-state'
const DEFAULT_DAILY_GOAL = 15
// PRR-221: MVP per-target-plan bounds. Server cap is 5; UI limits to 4
// to leave headroom for classroom-assigned targets that land via
// EPIC-PRR-C after onboarding. A later iteration can relax to 5 with a
// soft-warn at 4 (task body).
export const MAX_EXAM_TARGETS = 4
export const MIN_WEEKLY_HOURS = 1
export const MAX_WEEKLY_HOURS = 40
export const DEFAULT_WEEKLY_HOURS = 8

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
  //
  // RDY-068 (F2): when no locale is persisted yet (first visit), infer
  // from navigator.languages with an Arabic-first bias. This removes the
  // "pick your flag" friction screen for the Arab-sector cohort per
  // Dr. Lior's panel critique (2026-04-17).
  const { locales: availableForInference } = useAvailableLocales()
  const availableCodesForInference = new Set(availableForInference.value.map(l => l.code))
  const initialLocale: SupportedLocale = persisted?.locale
    ? (sanitizeLocale(persisted.locale as string) as SupportedLocale)
    : inferLocale({ availableCodes: availableCodesForInference })
  const locale = ref<SupportedLocale>(initialLocale)

  // PRR-032: numerals preference. Default is null → "auto" (resolves to
  // eastern for ar, western otherwise). A student can flip it in settings.
  const persistedNumerals = (persisted as any)?.numeralsPreference
  const numeralsPreference = ref<NumeralsPreference | null>(
    persistedNumerals === 'eastern' || persistedNumerals === 'western'
      ? persistedNumerals
      : null,
  )

  /** Resolved preference: null → auto-infer from locale. */
  const effectiveNumerals = computed<NumeralsPreference>(() => {
    if (numeralsPreference.value) return numeralsPreference.value
    return locale.value === 'ar' ? 'eastern' : 'western'
  })

  const dailyTimeGoalMinutes = ref<number>(persisted?.dailyTimeGoalMinutes ?? DEFAULT_DAILY_GOAL)
  const subjects = ref<string[]>(persisted?.subjects ?? [])
  // PRR-221: exam-target drafts (multi-target).
  const examTargets = ref<ExamTargetDraft[]>(
    Array.isArray((persisted as any)?.examTargets)
      ? ((persisted as any).examTargets as ExamTargetDraft[])
      : [],
  )
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

  const STEP_ORDER: WizardStep[] = [
    'welcome',
    'role',
    'exam-targets',
    'per-target-plan',
    'language',
    'diagnostic',
    'self-assessment',
    'confirm',
  ]

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
      // PRR-221: need at least one target and at most 4.
      case 'exam-targets':
        return examTargets.value.length >= 1 && examTargets.value.length <= MAX_EXAM_TARGETS
      // PRR-221: every selected target needs a sitting and a weekly hours
      // value in range. `questionPaperCodes` defaults to all-checked so
      // Bagrut targets are valid as long as there's at least one paper in
      // the available list.
      case 'per-target-plan':
        return examTargets.value.length > 0 && examTargets.value.every(t =>
          t.sitting !== null
          && t.weeklyHours >= MIN_WEEKLY_HOURS
          && t.weeklyHours <= MAX_WEEKLY_HOURS
          && (t.family !== 'BAGRUT' || t.questionPaperCodes.length > 0))
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

  /**
   * PRR-032: explicit override of numerals preference. Pass `null` to
   * revert to locale-default.
   */
  function setNumeralsPreference(next: NumeralsPreference | null) {
    numeralsPreference.value = next
  }

  /**
   * PRR-221: replace the list of drafted exam targets. The caller is
   * expected to enforce the 1..MAX_EXAM_TARGETS bound at the UI layer;
   * we silently cap here as a belt-and-suspenders safeguard.
   */
  function setExamTargets(next: ExamTargetDraft[]) {
    examTargets.value = next.slice(0, MAX_EXAM_TARGETS)
  }

  /** Add a single draft if there is capacity; no-op when at cap. */
  function addExamTarget(draft: ExamTargetDraft) {
    if (examTargets.value.length >= MAX_EXAM_TARGETS) return
    if (examTargets.value.some(t => t.examCode === draft.examCode)) return
    examTargets.value = [...examTargets.value, draft]
  }

  function removeExamTarget(examCode: string) {
    examTargets.value = examTargets.value.filter(t => t.examCode !== examCode)
  }

  /** Merge-patch one draft identified by `examCode`. */
  function updateExamTarget(examCode: string, patch: Partial<ExamTargetDraft>) {
    examTargets.value = examTargets.value.map(t =>
      t.examCode === examCode ? { ...t, ...patch } : t,
    )
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
    numeralsPreference.value = null
    dailyTimeGoalMinutes.value = DEFAULT_DAILY_GOAL
    subjects.value = []
    examTargets.value = []
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
    [step, role, locale, numeralsPreference, dailyTimeGoalMinutes, subjects, examTargets, diagnosticResponses, diagnosticSkipped, selfAssessment, completedAt],
    () => {
      writePersisted({
        step: step.value,
        role: role.value,
        locale: locale.value,
        numeralsPreference: numeralsPreference.value,
        dailyTimeGoalMinutes: dailyTimeGoalMinutes.value,
        subjects: subjects.value,
        examTargets: examTargets.value,
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
    numeralsPreference,
    effectiveNumerals,
    dailyTimeGoalMinutes,
    subjects,
    examTargets,
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
    setNumeralsPreference,
    setExamTargets,
    addExamTarget,
    removeExamTarget,
    updateExamTarget,
    setDiagnosticResults,
    skipDiagnostic,
    setSelfAssessment,
    skipSelfAssessment,
    reset,
    markCompleted,
  }
})
