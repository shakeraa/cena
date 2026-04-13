import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessionPersistence } from '@/composables/useSessionPersistence'
import type { SessionSnapshot } from '@/composables/useSessionPersistence'

describe('useSessionPersistence', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('draft save/restore/clear', () => {
    it('saves a draft after debounce and restores it', () => {
      const { saveDraft, restoreDraft, flush } = useSessionPersistence()

      saveDraft('s1', 2, 'my answer')

      // Before debounce elapses — not saved yet
      expect(restoreDraft('s1', 2)).toBeNull()

      // After debounce (2s)
      vi.advanceTimersByTime(2000)
      expect(restoreDraft('s1', 2)).toBe('my answer')
    })

    it('uses the correct localStorage key format', () => {
      const { saveDraft } = useSessionPersistence()

      saveDraft('abc-123', 5, 'draft text')
      vi.advanceTimersByTime(2000)

      expect(localStorage.getItem('session-draft:abc-123:5')).toBe('draft text')
    })

    it('debounces multiple rapid writes', () => {
      const spy = vi.spyOn(Storage.prototype, 'setItem')
      const { saveDraft } = useSessionPersistence()

      saveDraft('s1', 1, 'a')
      vi.advanceTimersByTime(500)
      saveDraft('s1', 1, 'ab')
      vi.advanceTimersByTime(500)
      saveDraft('s1', 1, 'abc')
      vi.advanceTimersByTime(2000)

      // Only the final value should be written
      const draftCalls = spy.mock.calls.filter(c => c[0] === 'session-draft:s1:1')

      expect(draftCalls).toHaveLength(1)
      expect(draftCalls[0][1]).toBe('abc')

      spy.mockRestore()
    })

    it('clears a draft for a specific step', () => {
      const { saveDraft, clearDraft, restoreDraft } = useSessionPersistence()

      saveDraft('s1', 3, 'answer')
      vi.advanceTimersByTime(2000)
      expect(restoreDraft('s1', 3)).toBe('answer')

      clearDraft('s1', 3)
      expect(restoreDraft('s1', 3)).toBeNull()
    })

    it('clearDraft cancels a pending debounced write', () => {
      const { saveDraft, clearDraft, restoreDraft } = useSessionPersistence()

      saveDraft('s1', 1, 'pending')
      vi.advanceTimersByTime(500) // not yet saved
      clearDraft('s1', 1)
      vi.advanceTimersByTime(2000) // debounce would have fired

      expect(restoreDraft('s1', 1)).toBeNull()
    })

    it('flush() writes the pending draft immediately', () => {
      const { saveDraft, restoreDraft, flush } = useSessionPersistence()

      saveDraft('s1', 1, 'urgent')
      expect(restoreDraft('s1', 1)).toBeNull()

      flush()
      expect(restoreDraft('s1', 1)).toBe('urgent')
    })

    it('returns null for a non-existent draft', () => {
      const { restoreDraft } = useSessionPersistence()

      expect(restoreDraft('nonexistent', 99)).toBeNull()
    })
  })

  describe('snapshot save/restore', () => {
    const snapshot: SessionSnapshot = {
      sessionId: 's1',
      currentStep: 3,
      startedAt: new Date().toISOString(),
      lastActivityAt: new Date().toISOString(),
      totalSteps: 10,
      answeredSteps: [1, 2],
    }

    it('saves and restores a snapshot', () => {
      const { saveSnapshot, restoreSnapshot } = useSessionPersistence()

      saveSnapshot(snapshot)
      const restored = restoreSnapshot('s1')

      expect(restored).toEqual(snapshot)
    })

    it('uses the correct localStorage key for snapshots', () => {
      const { saveSnapshot } = useSessionPersistence()

      saveSnapshot(snapshot)
      expect(localStorage.getItem('session-state:s1')).not.toBeNull()

      const parsed = JSON.parse(localStorage.getItem('session-state:s1')!)

      expect(parsed.currentStep).toBe(3)
    })

    it('returns null for a non-existent snapshot', () => {
      const { restoreSnapshot } = useSessionPersistence()

      expect(restoreSnapshot('nonexistent')).toBeNull()
    })

    it('returns null and cleans up for a stale snapshot (>24h)', () => {
      const { saveSnapshot, restoreSnapshot } = useSessionPersistence()
      const stale: SessionSnapshot = {
        ...snapshot,
        lastActivityAt: new Date(Date.now() - 25 * 60 * 60 * 1000).toISOString(),
      }

      saveSnapshot(stale)
      const result = restoreSnapshot('s1')

      expect(result).toBeNull()
      expect(localStorage.getItem('session-state:s1')).toBeNull()
    })

    it('returns null and cleans up for corrupted JSON', () => {
      localStorage.setItem('session-state:corrupt', '{not valid json')
      const { restoreSnapshot } = useSessionPersistence()

      expect(restoreSnapshot('corrupt')).toBeNull()
      expect(localStorage.getItem('session-state:corrupt')).toBeNull()
    })
  })

  describe('hasRecoverableSession', () => {
    it('is false initially', () => {
      const { hasRecoverableSession } = useSessionPersistence()

      expect(hasRecoverableSession.value).toBe(false)
    })

    it('becomes true after saving a snapshot', () => {
      const { hasRecoverableSession, saveSnapshot } = useSessionPersistence()

      saveSnapshot({
        sessionId: 's1',
        currentStep: 1,
        startedAt: new Date().toISOString(),
        lastActivityAt: new Date().toISOString(),
        totalSteps: 5,
        answeredSteps: [],
      })

      expect(hasRecoverableSession.value).toBe(true)
    })

    it('becomes true after restoring a valid snapshot', () => {
      localStorage.setItem('session-state:s1', JSON.stringify({
        sessionId: 's1',
        currentStep: 1,
        startedAt: new Date().toISOString(),
        lastActivityAt: new Date().toISOString(),
        totalSteps: 5,
        answeredSteps: [],
      }))

      const { hasRecoverableSession, restoreSnapshot } = useSessionPersistence()

      restoreSnapshot('s1')
      expect(hasRecoverableSession.value).toBe(true)
    })

    it('becomes false after clearSession', () => {
      const { hasRecoverableSession, saveSnapshot, clearSession } = useSessionPersistence()

      saveSnapshot({
        sessionId: 's1',
        currentStep: 1,
        startedAt: new Date().toISOString(),
        lastActivityAt: new Date().toISOString(),
        totalSteps: 5,
        answeredSteps: [],
      })

      expect(hasRecoverableSession.value).toBe(true)
      clearSession('s1')
      expect(hasRecoverableSession.value).toBe(false)
    })
  })

  describe('clearSession', () => {
    it('removes snapshot and all drafts for the session', () => {
      const { saveSnapshot, saveDraft, clearSession, restoreDraft, restoreSnapshot, flush } = useSessionPersistence()

      saveSnapshot({
        sessionId: 's1',
        currentStep: 3,
        startedAt: new Date().toISOString(),
        lastActivityAt: new Date().toISOString(),
        totalSteps: 5,
        answeredSteps: [1, 2],
      })

      saveDraft('s1', 1, 'draft1')
      saveDraft('s1', 2, 'draft2')
      flush()

      // Verify data is there
      expect(restoreSnapshot('s1')).not.toBeNull()
      expect(restoreDraft('s1', 1)).toBe('draft1')

      clearSession('s1')

      expect(restoreSnapshot('s1')).toBeNull()
      expect(restoreDraft('s1', 1)).toBeNull()
      expect(restoreDraft('s1', 2)).toBeNull()
    })

    it('does not remove drafts from other sessions', () => {
      localStorage.setItem('session-draft:other:1', 'keep me')
      const { clearSession } = useSessionPersistence()

      clearSession('s1')

      expect(localStorage.getItem('session-draft:other:1')).toBe('keep me')
    })
  })

  describe('dispose', () => {
    it('cleans up timers without writing', () => {
      const { saveDraft, restoreDraft, dispose } = useSessionPersistence()

      saveDraft('s1', 1, 'pending')
      dispose()
      vi.advanceTimersByTime(5000)

      expect(restoreDraft('s1', 1)).toBeNull()
    })
  })
})
