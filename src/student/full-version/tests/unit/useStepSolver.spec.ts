/**
 * prr-206 — useStepSolver state-machine unit tests.
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import type { StepSolverStepDto } from '@/api/types/common'

const postStepMock = vi.fn()

vi.mock('@/api/sessions', () => ({
  postStep: (...args: any[]) => postStepMock(...args),
}))

const { useStepSolver } = await import('@/composables/useStepSolver')

function makeSolver() {
  const questionId = ref<string | null>('q1')

  const steps = ref<StepSolverStepDto[]>([
    { stepNumber: 1, instruction: 'step 1', expectedExpression: '2x + 4 = 10', hints: [] },
    { stepNumber: 2, instruction: 'step 2', expectedExpression: '2x = 6', hints: [] },
    { stepNumber: 3, instruction: 'step 3', expectedExpression: 'x = 3', hints: [] },
  ])

  const solver = useStepSolver({
    sessionId: 's1',
    questionId: () => questionId.value,
    steps: () => steps.value,
  })

  return { solver, questionId, steps }
}

describe('useStepSolver', () => {
  beforeEach(() => {
    postStepMock.mockReset()
  })

  it('initialises with the first step active and others pending', () => {
    const { solver } = makeSolver()

    expect(solver.activeStepNumber.value).toBe(1)
    expect(solver.stepStates[1].phase).toBe('typing')
    expect(solver.stepStates[2].phase).toBe('pending')
    expect(solver.stepStates[3].phase).toBe('pending')
  })

  it('unlocks the next step on correct verdict', async () => {
    const { solver } = makeSolver()

    solver.setExpression(1, '2x + 4 = 10')
    postStepMock.mockResolvedValueOnce({
      correct: true,
      astDiff: null,
      nextStepUnlocked: true,
    })
    await solver.submitStep(1)
    expect(solver.stepStates[1].phase).toBe('verified')
    expect(solver.activeStepNumber.value).toBe(2)
    expect(solver.stepStates[2].phase).toBe('typing')
  })

  it('marks step as rejected on non-equivalent', async () => {
    const { solver } = makeSolver()

    solver.setExpression(1, '2x = 10')
    postStepMock.mockResolvedValueOnce({
      correct: false,
      astDiff: 'dropped the 4',
      misconceptionTag: 'dropped_constant',
      isProductiveFailurePath: false,
    })
    await solver.submitStep(1)
    expect(solver.stepStates[1].phase).toBe('rejected')
    expect(solver.stepStates[1].astDiff).toBe('dropped the 4')
    expect(solver.stepStates[1].misconceptionTag).toBe('dropped_constant')

    // Active step doesn't advance — student retries.
    expect(solver.activeStepNumber.value).toBe(1)
  })

  it('routes productive-failure into explored phase', async () => {
    const { solver } = makeSolver()

    solver.setExpression(1, 'x + 2 = 5')
    postStepMock.mockResolvedValueOnce({
      correct: false,
      astDiff: 'you tried factoring first',
      isProductiveFailurePath: true,
    })
    await solver.submitStep(1)
    expect(solver.stepStates[1].phase).toBe('explored')
  })

  it('enters awaiting_cas phase when sidecar queued verification', async () => {
    const { solver } = makeSolver()

    solver.setExpression(1, '2x + 4 = 10')
    postStepMock.mockResolvedValueOnce({
      correct: false,
      queuedForLaterVerification: true,
    })
    await solver.submitStep(1)
    expect(solver.stepStates[1].phase).toBe('awaiting_cas')
  })

  it('tracks hintsConsumed across submissions', async () => {
    const { solver } = makeSolver()

    solver.incrementHints(1)
    solver.incrementHints(1)
    solver.setExpression(1, '2x + 4 = 10')
    postStepMock.mockResolvedValueOnce({ correct: true })
    await solver.submitStep(1)
    expect(postStepMock).toHaveBeenCalledWith(
      's1', 'q1',
      expect.objectContaining({ hintsConsumed: 2 }),
    )
  })

  it('complete is true once every step is verified', async () => {
    const { solver } = makeSolver()
    for (const stepNum of [1, 2, 3]) {
      solver.setExpression(stepNum, `step-${stepNum}`)
      postStepMock.mockResolvedValueOnce({ correct: true })
      await solver.submitStep(stepNum)
    }
    expect(solver.complete.value).toBe(true)
  })

  it('retryStep resets a rejected step to typing', async () => {
    const { solver } = makeSolver()

    solver.setExpression(1, 'wrong')
    postStepMock.mockResolvedValueOnce({ correct: false, astDiff: 'nope' })
    await solver.submitStep(1)
    expect(solver.stepStates[1].phase).toBe('rejected')
    solver.retryStep(1)
    expect(solver.stepStates[1].phase).toBe('typing')
    expect(solver.stepStates[1].astDiff).toBeNull()
  })
})
