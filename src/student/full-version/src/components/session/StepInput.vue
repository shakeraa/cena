<script setup lang="ts">
/**
 * STEP-001: StepInput.vue
 * Single step slot within the step-solver card.
 *
 * Shows instruction label (if scaffolded), faded worked example (if novice),
 * math input field, hint button, and verification result.
 *
 * RTL support: all math rendered in <bdi dir="ltr"> blocks.
 */

import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface SolutionStep {
  stepNumber: number
  instruction?: string
  fadedExample?: string
  expectedExpression: string
  hints: string[]
}

const props = defineProps<{
  step: SolutionStep
  scaffoldingLevel: 'none' | 'faded' | 'full'
  isActive: boolean
  isVerified: boolean
  verifiedExpression?: string
}>()

const emit = defineEmits<{
  (e: 'verified', correct: boolean, expression: string): void
}>()

const { t } = useI18n()

const inputExpression = ref('')
const currentHintLevel = ref(0)
const verificationResult = ref<{ correct: boolean; message?: string } | null>(null)
const isVerifying = ref(false)

const showInstruction = computed(() =>
  props.scaffoldingLevel !== 'none' && props.step.instruction)

const showFadedExample = computed(() =>
  props.scaffoldingLevel === 'faded' && props.step.fadedExample)

const showFullExample = computed(() =>
  props.scaffoldingLevel === 'full' && props.step.expectedExpression)

const availableHints = computed(() =>
  props.step.hints.slice(0, currentHintLevel.value))

function showNextHint() {
  if (currentHintLevel.value < props.step.hints.length) {
    currentHintLevel.value++
  }
}

async function verify() {
  if (!inputExpression.value.trim() || isVerifying.value) return

  isVerifying.value = true
  try {
    // CAS verification API call (depends on CAS-002)
    const response = await fetch('/api/cas/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        submitted: inputExpression.value,
        expected: props.step.expectedExpression,
        stepNumber: props.step.stepNumber,
      }),
    })

    const result = await response.json()
    verificationResult.value = {
      correct: result.isEquivalent,
      message: result.isEquivalent
        ? t('session.stepSolver.stepCorrect')
        : t('session.stepSolver.stepIncorrect'),
    }

    emit('verified', result.isEquivalent, inputExpression.value)
  } catch {
    verificationResult.value = {
      correct: false,
      message: t('session.stepSolver.verificationError'),
    }
  } finally {
    isVerifying.value = false
  }
}
</script>

<template>
  <div
    class="step-input"
    :class="{
      'step-input--active': isActive,
      'step-input--verified': isVerified,
      'step-input--incorrect': verificationResult && !verificationResult.correct,
    }"
    :aria-label="t('session.stepSolver.step', { n: step.stepNumber })"
  >
    <!-- Step number -->
    <div class="step-input-header">
      <span class="step-number" aria-hidden="true">{{ step.stepNumber }}</span>

      <!-- Instruction (scaffolded) -->
      <p v-if="showInstruction" class="step-instruction">
        {{ step.instruction }}
      </p>
    </div>

    <!-- Faded example (partial scaffolding) -->
    <div v-if="showFadedExample" class="step-faded-example">
      <bdi dir="ltr">{{ step.fadedExample }}</bdi>
    </div>

    <!-- Full worked example (full scaffolding) -->
    <div v-if="showFullExample && !isVerified" class="step-full-example">
      <span class="step-example-label">{{ t('session.stepSolver.workedExample') }}</span>
      <bdi dir="ltr">{{ step.expectedExpression }}</bdi>
    </div>

    <!-- Input area (only when active and not yet verified) -->
    <div v-if="isActive && !isVerified" class="step-input-area">
      <bdi dir="ltr">
        <input
          v-model="inputExpression"
          type="text"
          class="step-expression-input"
          :placeholder="t('session.stepSolver.enterExpression')"
          :aria-label="t('session.stepSolver.expressionInput', { n: step.stepNumber })"
          @keydown.enter="verify"
        />
      </bdi>

      <button
        class="step-verify-btn"
        :disabled="!inputExpression.trim() || isVerifying"
        @click="verify"
      >
        {{ isVerifying ? t('session.stepSolver.checking') : t('session.stepSolver.check') }}
      </button>
    </div>

    <!-- Verified expression (show KaTeX-rendered after correct) -->
    <div v-if="isVerified && verifiedExpression" class="step-verified-expression">
      <bdi dir="ltr">{{ verifiedExpression }}</bdi>
      <span class="step-check-mark" aria-label="correct">&#x2713;</span>
    </div>

    <!-- Verification result feedback -->
    <div v-if="verificationResult && !isVerified" class="step-feedback"
      :class="verificationResult.correct ? 'step-feedback--correct' : 'step-feedback--incorrect'">
      {{ verificationResult.message }}
    </div>

    <!-- Hints -->
    <div v-if="isActive && !isVerified && step.hints.length > 0" class="step-hints">
      <button
        v-if="currentHintLevel < step.hints.length"
        class="step-hint-btn"
        @click="showNextHint"
      >
        {{ t('session.stepSolver.showHint', { n: currentHintLevel + 1, total: step.hints.length }) }}
      </button>

      <ul v-if="availableHints.length > 0" class="step-hint-list">
        <li v-for="(hint, i) in availableHints" :key="i">{{ hint }}</li>
      </ul>
    </div>
  </div>
</template>
