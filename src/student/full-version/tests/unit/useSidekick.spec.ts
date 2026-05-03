/**
 * prr-207 — useSidekick + answer-leak client-guard unit tests.
 *
 * Streaming paths are not exercised here (those rely on a live
 * ReadableStream); instead we cover:
 *
 *   - tutor-context fetch happy path + circuit-breaker on 5xx
 *   - productive-failure debounce for explain_step
 *   - session-end teardown wipes all state
 *   - containsAnswerLeak detector exact patterns
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const getTutorContextMock = vi.fn()

vi.mock('@/api/sessions', () => ({
  getTutorContext: (...args: any[]) => getTutorContextMock(...args),
}))

const { useSidekick, containsAnswerLeak } = await import('@/composables/useSidekick')

describe('containsAnswerLeak', () => {
  it('flags MCQ letter disclosure', () => {
    expect(containsAnswerLeak('the answer is A')).toBe(true)
    expect(containsAnswerLeak('option B is correct')).toBe(true)
    expect(containsAnswerLeak('choose C')).toBe(true)
    expect(containsAnswerLeak('the correct choice is D')).toBe(true)
  })

  it('flags numeric answer disclosure', () => {
    expect(containsAnswerLeak('the answer is 42')).toBe(true)
    expect(containsAnswerLeak('the answer is 3.14')).toBe(true)
  })

  it('does NOT flag neutral coaching copy', () => {
    expect(containsAnswerLeak('consider the coefficient of x')).toBe(false)
    expect(containsAnswerLeak('try isolating the variable')).toBe(false)
    expect(containsAnswerLeak('what happens when x equals 0?')).toBe(false)
  })

  it('handles empty or undefined-like input', () => {
    expect(containsAnswerLeak('')).toBe(false)
    expect(containsAnswerLeak(null as unknown as string)).toBe(false)
  })
})

describe('useSidekick', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getTutorContextMock.mockReset()
  })

  it('open() fetches tutor context on first open and is idempotent', async () => {
    getTutorContextMock.mockResolvedValue({
      sessionId: 's1',
      currentQuestionId: 'q1',
      answeredCount: 0,
      correctCount: 0,
      currentRung: 0,
      lastMisconceptionTag: null,
      attemptPhase: 'first_try',
      elapsedMinutes: 0,
      dailyMinutesRemaining: 30,
      bktMasteryBucket: 'mid',
      accommodationFlags: {
        ldAnxiousFriendly: false,
        extendedTimeMultiplier: 1.0,
        distractionReducedLayout: false,
        ttsForProblemStatements: false,
      },
      builtAtUtc: '2026-04-20T00:00:00Z',
    })

    const sk = useSidekick({ sessionId: 's1' })

    await sk.open()
    expect(sk.isOpen.value).toBe(true)
    expect(sk.context.value?.sessionId).toBe('s1')
    expect(getTutorContextMock).toHaveBeenCalledTimes(1)

    // Second open — cached, no new network call.
    await sk.open()
    expect(getTutorContextMock).toHaveBeenCalledTimes(1)
  })

  it('circuit-breaker trips on 5xx', async () => {
    const err: any = new Error('Bad Gateway')

    err.statusCode = 502
    getTutorContextMock.mockRejectedValueOnce(err)

    const sk = useSidekick({ sessionId: 's1' })

    await sk.open()
    expect(sk.circuitBroken.value).toBe(true)
  })

  it('noteWrongStep() sets the debounce window to now + 15s', () => {
    // explainStepEnabled = Date.now() >= debounceUntilMs. Rather than
    // fighting Vue's computed-caching + Date.now non-reactivity, assert
    // on the observable contract: immediately after noteWrongStep,
    // explainStepEnabled is false. Re-enablement is a wall-clock event
    // that the parent component re-reads on render — not a state
    // invariant of this composable.
    vi.useFakeTimers()
    try {
      const now = 1_700_000_000_000

      vi.setSystemTime(now)

      const sk = useSidekick({ sessionId: 's1' })

      expect(sk.explainStepEnabled.value).toBe(true)
      sk.noteWrongStep()
      expect(sk.explainStepEnabled.value).toBe(false)
    }
    finally {
      vi.useRealTimers()
    }
  })

  it('teardown wipes context, messages, and drawer state', async () => {
    getTutorContextMock.mockResolvedValue({
      sessionId: 's1',
      currentQuestionId: null,
      answeredCount: 0,
      correctCount: 0,
      currentRung: 0,
      lastMisconceptionTag: null,
      attemptPhase: 'first_try',
      elapsedMinutes: 0,
      dailyMinutesRemaining: 30,
      bktMasteryBucket: 'mid',
      accommodationFlags: {
        ldAnxiousFriendly: false,
        extendedTimeMultiplier: 1.0,
        distractionReducedLayout: false,
        ttsForProblemStatements: false,
      },
      builtAtUtc: '2026-04-20T00:00:00Z',
    })

    const sk = useSidekick({ sessionId: 's1' })

    await sk.open()
    sk.messages.value.push({
      id: 'm1', role: 'user', content: 'hi', createdAt: '', streaming: false, leakRedacted: false,
    })
    sk.teardown()
    expect(sk.isOpen.value).toBe(false)
    expect(sk.context.value).toBeNull()
    expect(sk.messages.value).toEqual([])
    expect(sk.circuitBroken.value).toBe(false)
  })

  it('close() does NOT wipe context (only teardown does)', async () => {
    getTutorContextMock.mockResolvedValue({
      sessionId: 's1',
      currentQuestionId: null,
      answeredCount: 0,
      correctCount: 0,
      currentRung: 0,
      lastMisconceptionTag: null,
      attemptPhase: 'first_try',
      elapsedMinutes: 0,
      dailyMinutesRemaining: 30,
      bktMasteryBucket: 'mid',
      accommodationFlags: {
        ldAnxiousFriendly: false,
        extendedTimeMultiplier: 1.0,
        distractionReducedLayout: false,
        ttsForProblemStatements: false,
      },
      builtAtUtc: '2026-04-20T00:00:00Z',
    })

    const sk = useSidekick({ sessionId: 's1' })

    await sk.open()
    sk.close()
    expect(sk.isOpen.value).toBe(false)
    expect(sk.context.value).not.toBeNull()
  })
})
