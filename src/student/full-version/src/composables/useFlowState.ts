// =============================================================================
// RDY-016: Flow State composable
// Maps backend cognitive load / fatigue data to flow state tokens.
// Drives FlowAmbientBackground.vue transitions.
// =============================================================================

import { ref, computed } from 'vue'

export type FlowState = 'warming' | 'approaching' | 'inFlow' | 'disrupted' | 'fatigued'

export interface FlowStateData {
  state: FlowState
  fatigueLevel: number  // 0.0-1.0
  accuracyTrend: number // -1.0 to 1.0 (negative = declining)
  consecutiveCorrect: number
  sessionDurationMinutes: number
}

const currentFlowState = ref<FlowState>('warming')
const flowData = ref<FlowStateData>({
  state: 'warming',
  fatigueLevel: 0,
  accuracyTrend: 0,
  consecutiveCorrect: 0,
  sessionDurationMinutes: 0,
})

const showBreakSuggestion = ref(false)
const showDifficultyAdjustment = ref(false)

export function useFlowState() {
  /**
   * Update flow state from backend data (SignalR push or REST poll).
   * Called when session state updates arrive.
   */
  function updateFromBackend(data: {
    fatigueLevel: number
    accuracyTrend: number
    consecutiveCorrect: number
    sessionDurationMinutes: number
  }) {
    flowData.value = {
      ...data,
      state: computeFlowState(data),
    }
    currentFlowState.value = flowData.value.state

    // Trigger UX feedback based on state
    showBreakSuggestion.value = flowData.value.state === 'fatigued'
    showDifficultyAdjustment.value = flowData.value.state === 'disrupted'
  }

  /**
   * Map fatigue + accuracy trend to flow state.
   * Based on Csikszentmihalyi's flow model: balance of challenge and skill.
   */
  function computeFlowState(data: {
    fatigueLevel: number
    accuracyTrend: number
    consecutiveCorrect: number
    sessionDurationMinutes: number
  }): FlowState {
    // Fatigued: high fatigue or very long session
    if (data.fatigueLevel > 0.7 || data.sessionDurationMinutes > 45) {
      return 'fatigued'
    }

    // Disrupted: declining accuracy trend
    if (data.accuracyTrend < -0.3) {
      return 'disrupted'
    }

    // In flow: good accuracy + moderate fatigue + some momentum
    if (data.consecutiveCorrect >= 3 && data.accuracyTrend > 0.1 && data.fatigueLevel < 0.4) {
      return 'inFlow'
    }

    // Approaching: building momentum
    if (data.consecutiveCorrect >= 1 && data.accuracyTrend >= 0) {
      return 'approaching'
    }

    // Default: warming up
    return 'warming'
  }

  const isInFlow = computed(() => currentFlowState.value === 'inFlow')
  const isFatigued = computed(() => currentFlowState.value === 'fatigued')
  const isDisrupted = computed(() => currentFlowState.value === 'disrupted')

  /**
   * User acknowledged break suggestion — dismiss it.
   */
  function dismissBreakSuggestion() {
    showBreakSuggestion.value = false
  }

  /**
   * User acknowledged difficulty adjustment — dismiss it.
   */
  function dismissDifficultyAdjustment() {
    showDifficultyAdjustment.value = false
  }

  return {
    currentFlowState,
    flowData,
    isInFlow,
    isFatigued,
    isDisrupted,
    showBreakSuggestion,
    showDifficultyAdjustment,
    updateFromBackend,
    dismissBreakSuggestion,
    dismissDifficultyAdjustment,
  }
}
