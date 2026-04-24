// =============================================================================
// Cena Platform — sessionHideRevealStore (EPIC-PRR-H PRR-260)
//
// Session-level student-controlled toggle that hides multiple-choice answer
// options until the student clicks to reveal. Default = visible (traditional
// behavior). Implements Bjork's generation effect for students who opt in;
// zero friction for students who don't.
//
// Scope invariants from the PRR-260 task body:
//   - Default = visible across all session types
//   - Toggle persists for the duration of the session, NOT across sessions
//     (student re-opts in each session to preserve autonomy per persona-ethics)
//   - Ignored during PRR-228 diagnostic — diagnosticActive=true forces visible
//   - Author-level forceOptionsVisible=true bypasses hidden mode entirely
//   - No time-pressure mechanic — countdown/timer banned (ADR-0048)
//
// Server-side enforcement for classroom-mode (PRR-261) lives separately.
// This store is self-discipline-mode only.
// =============================================================================

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

/** Attempt-mode for the current session. */
export type AttemptMode = 'visible' | 'hidden_reveal'

export const useSessionHideRevealStore = defineStore('sessionHideReveal', () => {
  /** The mode the STUDENT picked for this session. */
  const attemptMode = ref<AttemptMode>('visible')

  /**
   * Per-question reveal state. Key = questionId; value = true once the
   * student has clicked "reveal" on that question. Cleared on session end.
   */
  const revealedByQuestion = ref<Record<string, boolean>>({})

  /**
   * Set true during the PRR-228 diagnostic flow. Forces visible mode
   * regardless of the student's pick — the diagnostic needs to observe
   * baseline answering without the hide-reveal confound.
   */
  const diagnosticActive = ref(false)

  /**
   * Effective attempt mode applied to the next question. Combines:
   *   - diagnosticActive → 'visible'
   *   - attemptMode otherwise
   * Author forceOptionsVisible is applied PER-question by the caller.
   */
  const effectiveMode = computed<AttemptMode>(() =>
    diagnosticActive.value ? 'visible' : attemptMode.value,
  )

  /** Student toggles the session-wide mode. */
  function setAttemptMode(mode: AttemptMode): void {
    attemptMode.value = mode
    // Switching TO visible unreveals no questions (they stay revealed).
    // Switching TO hidden_reveal re-hides anything not yet revealed — but
    // already-revealed stays revealed (no surprise re-hide mid-session).
  }

  /**
   * Effective per-question visibility. Authoring layer passes
   * <paramref name="forceOptionsVisible"/> for items where the options
   * ARE the question (e.g. "choose which graph is correct").
   */
  function shouldRevealOptions(questionId: string, forceOptionsVisible: boolean): boolean {
    if (forceOptionsVisible) return true
    if (effectiveMode.value === 'visible') return true
    return revealedByQuestion.value[questionId] === true
  }

  /** Student clicked "reveal" on a specific question. */
  function markRevealed(questionId: string): void {
    revealedByQuestion.value[questionId] = true
  }

  /** Set by the diagnostic flow before/after its items. */
  function setDiagnosticActive(value: boolean): void {
    diagnosticActive.value = value
  }

  /** Reset — session end. Re-opt is required on the next session. */
  function resetForNewSession(): void {
    attemptMode.value = 'visible'
    revealedByQuestion.value = {}
    diagnosticActive.value = false
  }

  return {
    attemptMode,
    effectiveMode,
    diagnosticActive,
    setAttemptMode,
    shouldRevealOptions,
    markRevealed,
    setDiagnosticActive,
    resetForNewSession,
  }
})
