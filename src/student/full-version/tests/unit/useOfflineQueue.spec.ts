import { beforeEach, describe, expect, it } from 'vitest'
import { useOfflineQueue } from '@/composables/useOfflineQueue'

describe('useOfflineQueue', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('enqueues a submission and returns a client ID', () => {
    const { enqueue, queue, pendingCount } = useOfflineQueue()

    const id = enqueue({ sessionId: 's1', stepNumber: 1, answer: 'hello' })

    expect(id).toBeTruthy()
    expect(typeof id).toBe('string')
    expect(queue.value).toHaveLength(1)
    expect(queue.value[0].clientSubmissionId).toBe(id)
    expect(queue.value[0].sessionId).toBe('s1')
    expect(queue.value[0].stepNumber).toBe(1)
    expect(queue.value[0].answer).toBe('hello')
    expect(queue.value[0].queuedAt).toBeTruthy()
    expect(pendingCount.value).toBe(1)
  })

  it('dequeues a submission by clientSubmissionId', () => {
    const { enqueue, dequeue, queue, pendingCount } = useOfflineQueue()

    const id1 = enqueue({ sessionId: 's1', stepNumber: 1, answer: 'a' })
    const id2 = enqueue({ sessionId: 's1', stepNumber: 2, answer: 'b' })

    expect(queue.value).toHaveLength(2)

    dequeue(id1)
    expect(queue.value).toHaveLength(1)
    expect(queue.value[0].clientSubmissionId).toBe(id2)
    expect(pendingCount.value).toBe(1)
  })

  it('enforces MAX_QUEUE_SIZE of 5', () => {
    const { enqueue } = useOfflineQueue()

    for (let i = 0; i < 5; i++)
      enqueue({ sessionId: 's1', stepNumber: i + 1, answer: `ans-${i}` })

    expect(() => {
      enqueue({ sessionId: 's1', stepNumber: 6, answer: 'overflow' })
    }).toThrow('Offline queue full (max 5)')
  })

  it('persists to localStorage on enqueue/dequeue', () => {
    const { enqueue, dequeue } = useOfflineQueue()

    const id = enqueue({ sessionId: 's1', stepNumber: 1, answer: 'persisted' })

    const stored = JSON.parse(localStorage.getItem('cena-offline-queue')!)

    expect(stored).toHaveLength(1)
    expect(stored[0].answer).toBe('persisted')

    dequeue(id)

    const afterDequeue = JSON.parse(localStorage.getItem('cena-offline-queue')!)

    expect(afterDequeue).toHaveLength(0)
  })

  it('loads from localStorage on init', () => {
    // Pre-populate localStorage
    localStorage.setItem('cena-offline-queue', JSON.stringify([
      {
        clientSubmissionId: 'pre-1',
        sessionId: 's1',
        stepNumber: 3,
        answer: 'preloaded',
        queuedAt: '2026-01-01T00:00:00.000Z',
      },
    ]))

    const { queue, pendingCount } = useOfflineQueue()

    expect(queue.value).toHaveLength(1)
    expect(queue.value[0].answer).toBe('preloaded')
    expect(pendingCount.value).toBe(1)
  })

  it('getAll returns submissions sorted by stepNumber', () => {
    const { enqueue, getAll } = useOfflineQueue()

    enqueue({ sessionId: 's1', stepNumber: 5, answer: 'fifth' })
    enqueue({ sessionId: 's1', stepNumber: 2, answer: 'second' })
    enqueue({ sessionId: 's1', stepNumber: 3, answer: 'third' })

    const all = getAll()

    expect(all.map(s => s.stepNumber)).toEqual([2, 3, 5])
  })

  it('getAll returns a copy, not the original array', () => {
    const { enqueue, getAll, queue } = useOfflineQueue()

    enqueue({ sessionId: 's1', stepNumber: 1, answer: 'a' })

    const all = getAll()

    all.pop()
    expect(queue.value).toHaveLength(1)
  })

  it('clear removes all submissions', () => {
    const { enqueue, clear, queue, pendingCount } = useOfflineQueue()

    enqueue({ sessionId: 's1', stepNumber: 1, answer: 'a' })
    enqueue({ sessionId: 's1', stepNumber: 2, answer: 'b' })

    expect(queue.value).toHaveLength(2)

    clear()
    expect(queue.value).toHaveLength(0)
    expect(pendingCount.value).toBe(0)
    expect(JSON.parse(localStorage.getItem('cena-offline-queue')!)).toEqual([])
  })

  it('handles corrupted localStorage gracefully', () => {
    localStorage.setItem('cena-offline-queue', 'not-json')

    // Should not throw — falls back to empty array
    const { queue } = useOfflineQueue()

    expect(queue.value).toEqual([])
  })
})
