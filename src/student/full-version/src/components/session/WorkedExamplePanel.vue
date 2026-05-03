<script setup lang="ts">
/**
 * RDY-013 — WorkedExamplePanel
 *
 * Renders a step-by-step worked example (Full scaffolding) or a faded
 * worked example (Partial scaffolding) per Renkl & Atkinson (2003).
 *
 * Full mode:  All steps shown progressively via "Next step" button.
 * Faded mode: First N steps completed, remaining blanked for student input.
 *
 * Accessibility (Tamar's requirements):
 *  - Each step: aria-label="Step N: [description]"
 *  - Faded steps: opacity-based hiding (not display:none) to stay in a11y tree
 *  - Step progression: aria-live="polite" announcement
 *  - Active step: aria-current="step"
 *  - Faded blanks: aria-disabled="true"
 *  - Math wrapped in <bdi dir="ltr">
 */
import { computed, ref, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import katex from 'katex'
import type { WorkedExampleStepDto } from '@/api/types/common'

interface Props {
  /** Array of worked example steps. */
  steps: WorkedExampleStepDto[]
  /** 'Full' = progressive reveal; 'Partial' = faded (some blanked). */
  mode: 'Full' | 'Partial'
}

const props = defineProps<Props>()

const emit = defineEmits<{
  /** Emitted when a student submits an answer for a faded step. */
  'faded-answer': [stepIndex: number, answer: string]
}>()

const { t } = useI18n()

// --- Full mode: progressive reveal ---
const revealedCount = ref(1)
const stepRefs = ref<HTMLElement[]>([])

function revealNext() {
  if (revealedCount.value < props.steps.length) {
    revealedCount.value++
    nextTick(() => {
      // Focus the newly revealed step for keyboard users
      const el = stepRefs.value[revealedCount.value - 1]
      if (el) el.focus()
    })
  }
}

// Reset when steps change (new question)
watch(() => props.steps, () => {
  revealedCount.value = 1
  fadedAnswers.value = {}
  fadedResults.value = {}
}, { deep: true })

// --- Faded mode: how many steps are pre-filled ---
const fadedBoundary = computed(() => {
  // Show roughly half the steps as completed; at least 1 completed.
  return Math.max(1, Math.ceil(props.steps.length / 2))
})

const fadedAnswers = ref<Record<number, string>>({})
const fadedResults = ref<Record<number, boolean | null>>({})

function checkFadedStep(index: number) {
  const step = props.steps[index]
  if (!step) return

  const answer = (fadedAnswers.value[index] ?? '').trim()
  // Normalize: compare lowercase, strip extra whitespace
  const expected = (step.math ?? step.description).trim().toLowerCase()
  const isCorrect = answer.toLowerCase() === expected
  fadedResults.value[index] = isCorrect
  emit('faded-answer', index, answer)
}

// --- Render KaTeX safely ---
function renderMath(expression: string): string {
  try {
    return katex.renderToString(expression, {
      throwOnError: false,
      displayMode: false,
    })
  }
  catch {
    return expression
  }
}

// --- Step visibility ---
function isStepVisible(index: number): boolean {
  if (props.mode === 'Partial') return true // all visible (opacity varies)
  return index < revealedCount.value
}

function isStepFaded(index: number): boolean {
  return props.mode === 'Partial' && index >= fadedBoundary.value
}

function isActiveStep(index: number): boolean {
  if (props.mode === 'Full') return index === revealedCount.value - 1
  if (props.mode === 'Partial') return index === fadedBoundary.value
  return false
}

// Live announcement text
const announcement = computed(() => {
  if (props.mode === 'Full' && revealedCount.value <= props.steps.length) {
    const step = props.steps[revealedCount.value - 1]
    return t('session.runner.stepProgress', {
      current: revealedCount.value,
      total: props.steps.length,
      description: step?.description ?? '',
    })
  }
  return ''
})
</script>

<template>
  <div
    class="worked-example-panel"
    data-testid="worked-example-panel"
  >
    <!-- Screen-reader step announcement -->
    <div
      aria-live="polite"
      class="sr-only"
      data-testid="step-announcement"
    >
      {{ announcement }}
    </div>

    <div class="text-subtitle-2 mb-3">
      {{ mode === 'Full'
        ? t('session.runner.workedExampleLabel')
        : t('session.runner.fadedExampleLabel') }}
    </div>

    <div class="worked-example-panel__steps">
      <div
        v-for="(step, index) in steps"
        :key="index"
        :ref="(el) => { if (el) stepRefs[index] = el as HTMLElement }"
        class="worked-example-panel__step pa-3 mb-2 rounded-lg"
        :class="{
          'worked-example-panel__step--revealed': isStepVisible(index) && !isStepFaded(index),
          'worked-example-panel__step--hidden': !isStepVisible(index),
          'worked-example-panel__step--faded': isStepFaded(index),
        }"
        :aria-label="t('session.runner.stepAria', { n: index + 1, description: step.description })"
        :aria-current="isActiveStep(index) ? 'step' : undefined"
        :aria-disabled="isStepFaded(index) ? 'true' : undefined"
        :aria-hidden="false"
        :tabindex="isStepVisible(index) ? 0 : -1"
        role="listitem"
        data-testid="worked-example-step"
      >
        <div class="d-flex align-start gap-3">
          <VAvatar
            size="28"
            :color="isStepFaded(index) ? 'grey-lighten-2' : 'primary'"
            class="text-caption font-weight-bold flex-shrink-0"
          >
            {{ index + 1 }}
          </VAvatar>

          <div class="flex-grow-1">
            <!-- Step description -->
            <div class="text-body-2 font-weight-medium mb-1">
              {{ step.description }}
            </div>

            <!-- Full mode: show math + explanation -->
            <template v-if="!isStepFaded(index) && step.math">
              <bdi
                dir="ltr"
                class="worked-example-panel__math d-block mb-1"
                v-html="renderMath(step.math)"
              />
            </template>

            <div
              v-if="!isStepFaded(index) && step.explanation"
              class="text-caption text-medium-emphasis"
            >
              {{ step.explanation }}
            </div>

            <!-- Faded mode: input for student to fill in -->
            <template v-if="isStepFaded(index)">
              <div class="d-flex align-center gap-2 mt-2">
                <VTextField
                  v-model="fadedAnswers[index]"
                  :placeholder="t('session.runner.fadedStepPlaceholder')"
                  variant="outlined"
                  density="compact"
                  hide-details
                  :aria-label="t('session.runner.stepAria', { n: index + 1, description: step.description })"
                  data-testid="faded-step-input"
                  @keydown.enter.prevent="checkFadedStep(index)"
                />
                <VBtn
                  size="small"
                  variant="tonal"
                  color="primary"
                  data-testid="faded-step-check"
                  @click="checkFadedStep(index)"
                >
                  {{ t('session.runner.fadedStepCheck') }}
                </VBtn>
              </div>

              <!-- Faded step feedback — announced to screen readers -->
              <div
                v-if="fadedResults[index] !== undefined && fadedResults[index] !== null"
                aria-live="assertive"
                class="mt-1"
                data-testid="faded-step-feedback"
              >
                <span
                  v-if="fadedResults[index]"
                  class="text-success text-caption"
                >
                  {{ t('session.runner.fadedStepCorrect') }}
                </span>
                <span
                  v-else
                  class="text-error text-caption"
                >
                  {{ t('session.runner.fadedStepIncorrect', { expected: step.math ?? step.description }) }}
                </span>
              </div>
            </template>
          </div>
        </div>
      </div>
    </div>

    <!-- "Next step" button for Full mode progressive reveal -->
    <VBtn
      v-if="mode === 'Full' && revealedCount < steps.length"
      variant="tonal"
      color="primary"
      class="mt-3"
      data-testid="next-step-btn"
      @click="revealNext"
    >
      {{ t('session.runner.nextStep') }}
    </VBtn>
  </div>
</template>

<style scoped>
.worked-example-panel__step {
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  transition: opacity 0.3s ease, transform 0.3s ease;
}

.worked-example-panel__step--revealed {
  opacity: 1;
}

.worked-example-panel__step--hidden {
  opacity: 0;
  max-height: 0;
  overflow: hidden;
  padding: 0 !important;
  margin: 0 !important;
  border: none;
}

/* Tamar's a11y requirement: faded steps use opacity, not display:none,
   so they remain in the accessibility tree. */
.worked-example-panel__step--faded {
  opacity: 0.6;
}

.worked-example-panel__math {
  font-family: 'KaTeX_Main', serif;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

/* RDY-030b: prefers-reduced-motion guard (WCAG 2.3.3).
   Component-local animations/transitions reduced to an imperceptible
   0.01ms so vestibular-sensitive users don't trigger motion-related
   symptoms. Complements the global reset in styles.scss. */
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
</style>
