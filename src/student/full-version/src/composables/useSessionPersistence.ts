import { ref } from 'vue'
import type { Ref } from 'vue'

export interface SessionSnapshot {
  sessionId: string
  currentStep: number
  startedAt: string
  lastActivityAt: string
  totalSteps: number
  answeredSteps: number[]
}

export interface UseSessionPersistenceReturn {
  /** Restore draft text for current step */
  restoreDraft: (sessionId: string, step: number) => string | null
  /** Save draft text (debounced) */
  saveDraft: (sessionId: string, step: number, text: string) => void
  /** Clear draft after successful submission */
  clearDraft: (sessionId: string, step: number) => void
  /** Save session state snapshot */
  saveSnapshot: (snapshot: SessionSnapshot) => void
  /** Restore session snapshot */
  restoreSnapshot: (sessionId: string) => SessionSnapshot | null
  /** Clear all session data */
  clearSession: (sessionId: string) => void
  /** Whether we have a recoverable session */
  hasRecoverableSession: Ref<boolean>
  /** Flush any pending debounced draft immediately */
  flush: () => void
  /** Dispose timers (call on unmount) */
  dispose: () => void
}

const DRAFT_PREFIX = 'session-draft:'
const STATE_PREFIX = 'session-state:'
const DEBOUNCE_MS = 2000
/** Sessions older than 24 hours are considered stale */
const STALE_THRESHOLD_MS = 24 * 60 * 60 * 1000

function draftKey(sessionId: string, step: number): string {
  return `${DRAFT_PREFIX}${sessionId}:${step}`
}

function stateKey(sessionId: string): string {
  return `${STATE_PREFIX}${sessionId}`
}

function safeGetItem(key: string): string | null {
  try {
    return localStorage.getItem(key)
  }
  catch {
    return null
  }
}

function safeSetItem(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  }
  catch {
    // Swallow quota / private-mode errors — persistence is best-effort
  }
}

function safeRemoveItem(key: string): void {
  try {
    localStorage.removeItem(key)
  }
  catch {
    // Swallow errors
  }
}

export function useSessionPersistence(): UseSessionPersistenceReturn {
  const hasRecoverableSession = ref(false)

  const pendingTimers = new Map<string, ReturnType<typeof setTimeout>>()
  const pendingValues = new Map<string, string>()

  function flushKey(key: string) {
    const value = pendingValues.get(key)
    if (value !== undefined) {
      safeSetItem(key, value)
      pendingValues.delete(key)
    }
    const timer = pendingTimers.get(key)
    if (timer) {
      clearTimeout(timer)
      pendingTimers.delete(key)
    }
  }

  function restoreDraft(sessionId: string, step: number): string | null {
    return safeGetItem(draftKey(sessionId, step))
  }

  function saveDraft(sessionId: string, step: number, text: string): void {
    const key = draftKey(sessionId, step)

    pendingValues.set(key, text)

    const existing = pendingTimers.get(key)
    if (existing)
      clearTimeout(existing)

    pendingTimers.set(key, setTimeout(() => {
      flushKey(key)
    }, DEBOUNCE_MS))
  }

  function clearDraft(sessionId: string, step: number): void {
    const key = draftKey(sessionId, step)

    // Cancel any pending debounced write for this draft
    const timer = pendingTimers.get(key)
    if (timer) {
      clearTimeout(timer)
      pendingTimers.delete(key)
    }
    pendingValues.delete(key)
    safeRemoveItem(key)
  }

  function saveSnapshot(snapshot: SessionSnapshot): void {
    safeSetItem(stateKey(snapshot.sessionId), JSON.stringify(snapshot))
    hasRecoverableSession.value = true
  }

  function restoreSnapshot(sessionId: string): SessionSnapshot | null {
    const raw = safeGetItem(stateKey(sessionId))
    if (!raw)
      return null

    try {
      const snapshot = JSON.parse(raw) as SessionSnapshot
      const lastActivity = new Date(snapshot.lastActivityAt).getTime()

      if (Date.now() - lastActivity > STALE_THRESHOLD_MS) {
        // Session is stale — clean up
        safeRemoveItem(stateKey(sessionId))
        hasRecoverableSession.value = false

        return null
      }

      hasRecoverableSession.value = true

      return snapshot
    }
    catch {
      safeRemoveItem(stateKey(sessionId))

      return null
    }
  }

  function clearSession(sessionId: string): void {
    // Remove the snapshot
    safeRemoveItem(stateKey(sessionId))

    // Remove all draft keys for this session
    try {
      const prefix = `${DRAFT_PREFIX}${sessionId}:`
      const keysToRemove: string[] = []

      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i)
        if (key?.startsWith(prefix))
          keysToRemove.push(key)
      }

      for (const key of keysToRemove)
        safeRemoveItem(key)
    }
    catch {
      // Swallow errors
    }

    hasRecoverableSession.value = false
  }

  function flush(): void {
    for (const key of [...pendingValues.keys()])
      flushKey(key)
  }

  function dispose(): void {
    for (const timer of pendingTimers.values())
      clearTimeout(timer)
    pendingTimers.clear()
    pendingValues.clear()
  }

  return {
    restoreDraft,
    saveDraft,
    clearDraft,
    saveSnapshot,
    restoreSnapshot,
    clearSession,
    hasRecoverableSession,
    flush,
    dispose,
  }
}
