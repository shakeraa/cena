<script setup lang="ts">
/**
 * STEP-001: StepSolverCard.vue
 * Multi-step algebraic/calculus problem solver UI.
 * Students solve step-by-step; each step is CAS-verified before proceeding.
 *
 * Props:
 * - question: StepSolverQuestion with stem, steps, scaffolding level
 * - sessionId: current learning session
 *
 * Progressive disclosure: next step unlocks only after current verified.
 * Three scaffolding levels: Full (worked example visible), Faded (blanked),
 * Minimal (no guidance). Driven by BKT mastery.
 */

import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import StepInput from './StepInput.vue'

interface SolutionStep {
  stepNumber: number
  instruction?: string
  fadedExample?: string
  expectedExpression: string
  hints: string[]
}

interface StepSolverQuestion {
  id: string
  stem: string
  subject: string
  conceptId: string
  figureSpec?: unknown
  steps: SolutionStep[]
  finalAnswer: string
  scaffoldingLevel: 'none' | 'faded' | 'full'
}

const props = defineProps<{
  question: StepSolverQuestion
  sessionId: string
}>()

const emit = defineEmits<{
  (e: 'step-verified', stepNumber: number, isCorrect: boolean): void
  (e: 'all-steps-complete', finalAnswer: string): void
}>()

const { t } = useI18n()

const currentStep = ref(1)
const stepResults = ref<Record<number, { correct: boolean; expression: string }>>({})

const activeSteps = computed(() => {
  return props.question.steps.filter(s => s.stepNumber <= currentStep.value)
})

const allStepsComplete = computed(() => {
  return props.question.steps.every(s => stepResults.value[s.stepNumber]?.correct)
})

const progressPercent = computed(() => {
  const total = props.question.steps.length
  const done = Object.values(stepResults.value).filter(r => r.correct).length
  return Math.round((done / total) * 100)
})

function handleStepVerified(stepNumber: number, isCorrect: boolean, expression: string) {
  stepResults.value[stepNumber] = { correct: isCorrect, expression }
  emit('step-verified', stepNumber, isCorrect)

  if (isCorrect && stepNumber === currentStep.value) {
    if (currentStep.value < props.question.steps.length) {
      currentStep.value++
    } else {
      emit('all-steps-complete', props.question.finalAnswer)
    }
  }
}
</script>

<template>
  <div class="step-solver-card" role="region" :aria-label="t('session.stepSolver.title')">
    <!-- Question stem -->
    <div class="step-solver-stem">
      <bdi dir="ltr">
        <div v-html="question.stem" />
      </bdi>
    </div>

    <!-- Progress bar -->
    <div class="step-solver-progress" role="progressbar"
      :aria-valuenow="progressPercent"
      :aria-valuemin="0"
      :aria-valuemax="100">
      <div class="step-solver-progress-fill" :style="{ width: `${progressPercent}%` }" />
      <span class="step-solver-progress-label">
        {{ t('session.stepSolver.progress', { done: Object.values(stepResults).filter(r => r.correct).length, total: question.steps.length }) }}
      </span>
    </div>

    <!-- Step inputs (progressive disclosure) -->
    <div class="step-solver-steps">
      <StepInput
        v-for="step in activeSteps"
        :key="step.stepNumber"
        :step="step"
        :scaffolding-level="question.scaffoldingLevel"
        :is-active="step.stepNumber === currentStep"
        :is-verified="!!stepResults[step.stepNumber]?.correct"
        :verified-expression="stepResults[step.stepNumber]?.expression"
        @verified="(correct, expr) => handleStepVerified(step.stepNumber, correct, expr)"
      />

      <!-- Locked future steps (shown as placeholders) -->
      <div
        v-for="step in question.steps.filter(s => s.stepNumber > currentStep)"
        :key="`locked-${step.stepNumber}`"
        class="step-input-locked"
        :aria-label="t('session.stepSolver.stepLocked', { n: step.stepNumber })"
      >
        <span class="step-number">{{ step.stepNumber }}</span>
        <span class="step-locked-label">{{ t('session.stepSolver.completePrevious') }}</span>
      </div>
    </div>

    <!-- Completion -->
    <div v-if="allStepsComplete" class="step-solver-complete">
      <p>{{ t('session.stepSolver.allCorrect') }}</p>
    </div>
  </div>
</template>
