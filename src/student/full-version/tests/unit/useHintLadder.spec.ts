/**
 * prr-205 — useHintLadder state-machine unit tests.
 *
 * Exercises the composable's pure state transitions. The backend is
 * mocked via module replacement so no real HTTP traffic is made.
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, ref } from 'vue'

const postHintNextMock = vi.fn()

vi.mock('@/api/sessions', () => ({
  postHintNext: (...args: any[]) => postHintNextMock(...args),
}))

// Import after the mock so the composable picks up the mocked module.
const { useHintLadder } = await import('@/composables/useHintLadder')

function makeLadder(opts?: { mastery?: 'low' | 'mid' | 'high' | 'unknown' }) {
  const questionId = ref<string | null>('q1')
  const mastery = ref<'low' | 'mid' | 'high' | 'unknown'>(opts?.mastery ?? 'low')

  const ladder = useHintLadder({
    sessionId: 's1',
    questionId: () => questionId.value,
    masteryBucket: () => mastery.value,
  })

  return { ladder, questionId, mastery }
}

describe('useHintLadder', () => {
  beforeEach(() => {
    postHintNextMock.mockReset()
  })

  it('starts empty with ladder visible for low-mastery students', () => {
    const { ladder } = makeLadder({ mastery: 'low' })

    expect(ladder.rungs.value).toEqual([])
    expect(ladder.loading.value).toBe(false)
    expect(ladder.nextRungAvailable.value).toBe(true)
    expect(ladder.shouldAutoShow.value).toBe(true)
    expect(ladder.visible.value).toBe(true)
  })

  it('keeps ladder collapsed for high-mastery students until explicit surface()', () => {
    const { ladder } = makeLadder({ mastery: 'high' })

    expect(ladder.shouldAutoShow.value).toBe(false)
    expect(ladder.visible.value).toBe(false)
    ladder.surface()
    expect(ladder.visible.value).toBe(true)
  })

  it('appends a rung from a successful postHintNext response', async () => {
    const { ladder } = makeLadder()

    postHintNextMock.mockResolvedValueOnce({
      rung: 1,
      body: 'think about the equation type',
      rungSource: 'template',
      maxRungReached: 1,
      nextRungAvailable: true,
    })

    await ladder.requestNext()

    expect(postHintNextMock).toHaveBeenCalledWith('s1', 'q1')
    expect(ladder.rungs.value).toHaveLength(1)
    expect(ladder.rungs.value[0]).toEqual({
      hintLevel: 1,
      hintText: 'think about the equation type',
      rungSource: 'template',
    })
    expect(ladder.nextRungAvailable.value).toBe(true)
  })

  it('treats 404 as ladder-exhausted and clears the more-hint button', async () => {
    const { ladder } = makeLadder()
    const err: any = new Error('Not Found')

    err.statusCode = 404
    postHintNextMock.mockRejectedValueOnce(err)

    await ladder.requestNext()
    expect(ladder.nextRungAvailable.value).toBe(false)
    expect(ladder.error.value).toBeNull()
    expect(ladder.rungs.value).toHaveLength(0)
  })

  it('surfaces the ladder when an explicit "I\'m stuck" lands before any rungs', () => {
    const { ladder } = makeLadder({ mastery: 'high' })

    expect(ladder.visible.value).toBe(false)
    ladder.surface()
    expect(ladder.expanded.value).toBe(true)
    expect(ladder.visible.value).toBe(true)
  })

  it('resets on question change', async () => {
    const { ladder, questionId } = makeLadder()

    postHintNextMock.mockResolvedValueOnce({
      rung: 1,
      body: 'r1',
      rungSource: 'template',
      maxRungReached: 1,
      nextRungAvailable: true,
    })
    await ladder.requestNext()
    expect(ladder.rungs.value).toHaveLength(1)

    questionId.value = 'q2'
    await nextTick()
    expect(ladder.rungs.value).toHaveLength(0)
    expect(ladder.nextRungAvailable.value).toBe(true)
    expect(ladder.expanded.value).toBe(false)
  })

  it('ignores concurrent requests while loading', async () => {
    const { ladder } = makeLadder()
    let resolveFn!: (v: any) => void
    postHintNextMock.mockImplementationOnce(
      () => new Promise(resolve => { resolveFn = resolve }),
    )

    const p1 = ladder.requestNext()

    // While the first request is pending, a second call should no-op.
    await ladder.requestNext()
    expect(postHintNextMock).toHaveBeenCalledTimes(1)
    resolveFn({
      rung: 1, body: 'r1', rungSource: 'template', maxRungReached: 1, nextRungAvailable: true,
    })
    await p1
    expect(ladder.rungs.value).toHaveLength(1)
  })
})
