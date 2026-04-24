import { ref } from 'vue'
import type { Ref } from 'vue'

export interface QueuedSubmission {
  clientSubmissionId: string
  sessionId: string
  stepNumber: number
  answer: string
  queuedAt: string
}

export interface UseOfflineQueueReturn {

  /** Queued submissions waiting to be sent */
  queue: Ref<QueuedSubmission[]>

  /** Add a submission to the offline queue */
  enqueue: (submission: Omit<QueuedSubmission, 'clientSubmissionId' | 'queuedAt'>) => string

  /** Remove a submission after successful send */
  dequeue: (clientSubmissionId: string) => void

  /** Get all queued submissions for replay */
  getAll: () => QueuedSubmission[]

  /** Clear the entire queue */
  clear: () => void

  /** Number of pending submissions */
  pendingCount: Ref<number>
}

const STORAGE_KEY = 'cena-offline-queue'
const MAX_QUEUE_SIZE = 5

function loadFromStorage(): QueuedSubmission[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)

    return raw ? JSON.parse(raw) : []
  }
  catch {
    return []
  }
}

function persistToStorage(items: QueuedSubmission[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(items))
  }
  catch {
    // Swallow quota / private-mode errors
  }
}

export function useOfflineQueue(): UseOfflineQueueReturn {
  const queue = ref<QueuedSubmission[]>(loadFromStorage())
  const pendingCount = ref(queue.value.length)

  function persist() {
    persistToStorage(queue.value)
    pendingCount.value = queue.value.length
  }

  function enqueue(submission: Omit<QueuedSubmission, 'clientSubmissionId' | 'queuedAt'>): string {
    if (queue.value.length >= MAX_QUEUE_SIZE)
      throw new Error(`Offline queue full (max ${MAX_QUEUE_SIZE}). Cannot queue more submissions.`)

    const id = crypto.randomUUID()

    queue.value.push({
      ...submission,
      clientSubmissionId: id,
      queuedAt: new Date().toISOString(),
    })
    persist()

    return id
  }

  function dequeue(clientSubmissionId: string) {
    queue.value = queue.value.filter(s => s.clientSubmissionId !== clientSubmissionId)
    persist()
  }

  function getAll(): QueuedSubmission[] {
    return [...queue.value].sort((a, b) => a.stepNumber - b.stepNumber)
  }

  function clear() {
    queue.value = []
    persist()
  }

  return { queue, enqueue, dequeue, getAll, clear, pendingCount }
}
