/**
 * prr-206 — useStepSolver composable.
 *
 * State machine for a multi-step problem:
 *
 *   pending → typing → submitted → verified | rejected → next (or complete)
 *
 * Per ADR-0002 the CAS is the sole correctness oracle; this composable
 * never decides correctness client-side — it submits to
 * `POST /api/sessions/{sid}/question/{qid}/step` and renders the verdict.
 *
 * Productive-failure (engine doc §20.3): when the server returns
 * `isProductiveFailurePath=true` the student saw a pedagogically valuable
 * wrong path. The composable surfaces this as `phase='explored'` instead
 * of `rejected` so the UI can label it as exploration, not shame.
 *
 * SRE/persona-sre (circuit-breaker): when the CAS sidecar returns
 * `queuedForLaterVerification=true` we enter `phase='awaiting_cas'` so
 * the UI renders "checking later" and the student can keep going on
 * other affordances (ladder, sidekick) while we wait.
 */

import { computed, reactive, ref, watch } from 'vue'
import { postStep } from '@/api/sessions'
import type {
  StepSolverStepDto,
  StepSolverSubmitResponseDto,
} from '@/api/types/common'

export type StepPhase =
  | 'pending' // not yet active
  | 'typing' // active, student typing
  | 'submitted' // awaiting server
  | 'verified' // CAS equivalent
  | 'rejected' // CAS non-equivalent, student retries
  | 'explored' // wrong but pedagogically productive
  | 'awaiting_cas' // CAS sidecar queued the verification

export interface StepState {
  stepNumber: number
  phase: StepPhase
  expression: string
  astDiff: string | null
  misconceptionTag: string | null
  hintsConsumed: number
  submittedAt: number | null
}

export interface UseStepSolverOptions {
  sessionId: string
  questionId: () => string | null | undefined
  steps: () => StepSolverStepDto[] | null | undefined
}

export function useStepSolver(opts: UseStepSolverOptions) {
  // Per-step state bag keyed by stepNumber.
  const stepStates = reactive<Record<number, StepState>>({})

  /** Active step number (1-indexed). null when complete. */
  const activeStepNumber = ref<number | null>(null)

  /** Global submit-in-flight flag. One outstanding request at a time. */
  const submitting = ref(false)

  /** Last server error for the active step (transport-level, not CAS verdict). */
  const error = ref<string | null>(null)

  /** True when every step has phase='verified' or 'explored'. */
  const complete = computed(() => {
    const all = opts.steps()
    if (!all || all.length === 0)
      return false

    return all.every(s => {
      const st = stepStates[s.stepNumber]

      return st?.phase === 'verified' || st?.phase === 'explored'
    })
  })

  function initSteps() {
    // Wipe prior state and re-seed from the current steps[].
    for (const k of Object.keys(stepStates))
      delete stepStates[Number(k)]
    const list = opts.steps() ?? []
    for (const s of list) {
      stepStates[s.stepNumber] = {
        stepNumber: s.stepNumber,
        phase: 'pending',
        expression: '',
        astDiff: null,
        misconceptionTag: null,
        hintsConsumed: 0,
        submittedAt: null,
      }
    }
    if (list.length > 0) {
      activeStepNumber.value = list[0].stepNumber

      const first = stepStates[list[0].stepNumber]
      if (first)
        first.phase = 'typing'
    }
    else {
      activeStepNumber.value = null
    }
  }

  watch(() => opts.questionId(), () => initSteps(), { immediate: true })
  watch(() => opts.steps()?.length, () => initSteps())

  function setExpression(stepNumber: number, value: string) {
    const s = stepStates[stepNumber]
    if (!s)
      return
    s.expression = value
    if (s.phase === 'pending')
      s.phase = 'typing'
  }

  function incrementHints(stepNumber: number) {
    const s = stepStates[stepNumber]
    if (s)
      s.hintsConsumed += 1
  }

  async function submitStep(stepNumber: number): Promise<void> {
    const qid = opts.questionId()
    if (!qid)
      return
    const s = stepStates[stepNumber]
    if (!s)
      return
    if (!s.expression.trim())
      return
    if (submitting.value)
      return

    submitting.value = true
    error.value = null
    s.phase = 'submitted'

    const submitStart = Date.now()

    s.submittedAt = submitStart

    try {
      const resp: StepSolverSubmitResponseDto = await postStep(opts.sessionId, qid, {
        stepNumber,
        expression: s.expression,
        timeSpentMs: 0, // runner will compute actual; per-step timing is a follow-up
        hintsConsumed: s.hintsConsumed,
      })

      // Route into the correct terminal phase. ADR-0002: CAS decides.
      if (resp.queuedForLaterVerification) {
        s.phase = 'awaiting_cas'
      }
      else if (resp.correct) {
        s.phase = 'verified'
        s.astDiff = null

        // Advance to the next step.
        const list = opts.steps() ?? []
        const idx = list.findIndex(x => x.stepNumber === stepNumber)
        if (idx >= 0 && idx < list.length - 1) {
          const nextStep = list[idx + 1]

          activeStepNumber.value = nextStep.stepNumber

          const ns = stepStates[nextStep.stepNumber]
          if (ns && ns.phase === 'pending')
            ns.phase = 'typing'
        }
        else {
          activeStepNumber.value = null
        }
      }
      else if (resp.isProductiveFailurePath) {
        // Treated as progress (the student learned something) but not as "right".
        s.phase = 'explored'
        s.astDiff = resp.astDiff ?? null
        s.misconceptionTag = resp.misconceptionTag ?? null
      }
      else {
        s.phase = 'rejected'
        s.astDiff = resp.astDiff ?? null
        s.misconceptionTag = resp.misconceptionTag ?? null
      }
    }
    catch (err) {
      const anyErr = err as { message?: string; statusCode?: number; status?: number }

      error.value = anyErr.message || 'step_submit_failed'
      s.phase = 'typing'
    }
    finally {
      submitting.value = false
    }
  }

  /** Retry a rejected step — resets its phase to 'typing' without wiping input. */
  function retryStep(stepNumber: number) {
    const s = stepStates[stepNumber]
    if (!s)
      return
    if (s.phase === 'rejected' || s.phase === 'explored') {
      s.phase = 'typing'
      s.astDiff = null
    }
  }

  return {
    stepStates,
    activeStepNumber,
    submitting,
    error,
    complete,
    setExpression,
    incrementHints,
    submitStep,
    retryStep,
    reset: initSteps,
  }
}
