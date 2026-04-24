// =============================================================================
// RDY-016: Celebration composable
// Fixed-ratio celebrations only — no streaks, loss-aversion, variable-ratio.
// All animations respect prefers-reduced-motion and are <= 500ms.
// =============================================================================

import { ref } from 'vue'

export type CelebrationType = 'correct' | 'levelUp' | 'mastery' | 'sessionComplete'

export interface CelebrationEvent {
  type: CelebrationType
  message: string
  xpAmount?: number
  level?: number
  conceptName?: string
}

const currentCelebration = ref<CelebrationEvent | null>(null)
const isAnimating = ref(false)

// Check prefers-reduced-motion
const prefersReducedMotion = typeof window !== 'undefined'
  ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
  : false

export function useCelebration() {
  function triggerCorrectAnswer(xpAmount: number) {
    currentCelebration.value = {
      type: 'correct',
      message: `+${xpAmount} XP`,
      xpAmount,
    }
    isAnimating.value = true

    // Announce via aria-live region
    announceToScreenReader(`Correct! Plus ${xpAmount} XP`)

    // Auto-dismiss after animation (500ms max per ship-gate)
    setTimeout(() => {
      isAnimating.value = false
    }, prefersReducedMotion ? 0 : 500)
  }

  function triggerLevelUp(level: number) {
    currentCelebration.value = {
      type: 'levelUp',
      message: `Level ${level}!`,
      level,
    }
    isAnimating.value = true

    // aria-live assertive for level-up (important event)
    announceToScreenReader(`Level up! You reached level ${level}`, 'assertive')

    // Confetti (only if motion allowed, < 500ms, no parallax)
    if (!prefersReducedMotion) {
      triggerConfetti()
    }

    setTimeout(() => {
      isAnimating.value = false
      currentCelebration.value = null
    }, prefersReducedMotion ? 0 : 500)
  }

  function triggerMasteryMilestone(conceptName: string) {
    currentCelebration.value = {
      type: 'mastery',
      message: `You've mastered ${conceptName}!`,
      conceptName,
    }
    isAnimating.value = true

    announceToScreenReader(`Congratulations! You've mastered ${conceptName}!`)

    setTimeout(() => {
      isAnimating.value = false
      currentCelebration.value = null
    }, prefersReducedMotion ? 0 : 500)
  }

  function triggerSessionComplete() {
    currentCelebration.value = {
      type: 'sessionComplete',
      message: 'Great session!',
    }
    isAnimating.value = true

    announceToScreenReader('Session complete! Great work!')

    setTimeout(() => {
      isAnimating.value = false
    }, prefersReducedMotion ? 0 : 500)
  }

  function dismiss() {
    currentCelebration.value = null
    isAnimating.value = false
  }

  return {
    currentCelebration,
    isAnimating,
    prefersReducedMotion,
    triggerCorrectAnswer,
    triggerLevelUp,
    triggerMasteryMilestone,
    triggerSessionComplete,
    dismiss,
  }
}

// ── Helpers ──

function announceToScreenReader(message: string, priority: 'polite' | 'assertive' = 'polite') {
  if (typeof document === 'undefined') return

  const liveRegion = document.getElementById('cena-live-region')
  if (liveRegion) {
    liveRegion.setAttribute('aria-live', priority)
    liveRegion.textContent = message
  }
}

function triggerConfetti() {
  // Lightweight confetti: create a few particles using CSS animation
  // No external library needed — canvas-confetti can be added later if desired.
  // Duration <= 500ms, no parallax, no spinning (vestibular safety).
  if (typeof document === 'undefined') return

  const container = document.createElement('div')
  container.className = 'cena-confetti-container'
  container.setAttribute('aria-hidden', 'true')
  document.body.appendChild(container)

  const colors = ['#7367F0', '#28C76F', '#FF9F43', '#EA5455', '#00CFE8']

  for (let i = 0; i < 20; i++) {
    const particle = document.createElement('div')
    particle.className = 'cena-confetti-particle'
    particle.style.setProperty('--x', `${(Math.random() - 0.5) * 200}px`)
    particle.style.setProperty('--y', `${-Math.random() * 150 - 50}px`)
    particle.style.backgroundColor = colors[i % colors.length]
    particle.style.left = `${40 + Math.random() * 20}%`
    particle.style.animationDelay = `${Math.random() * 100}ms`
    container.appendChild(particle)
  }

  // Remove after animation completes
  setTimeout(() => container.remove(), 600)
}
