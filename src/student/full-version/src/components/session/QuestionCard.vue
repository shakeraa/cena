<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SessionHintResponseDto, SessionQuestionDto } from '@/api/types/common'
import QuestionFigure from '@/components/QuestionFigure.vue'

// FIND-pedagogy-006 — QuestionCard now surfaces the ScaffoldingService
// output that the backend computes from the student's real BKT mastery:
//
//  - ScaffoldingLevel 'Full'     → worked example block + hint button
//  - ScaffoldingLevel 'Partial'  → faded example cue (no worked example),
//                                   hint button (2 hints)
//  - ScaffoldingLevel 'HintsOnly'→ hint button only (1 hint)
//  - ScaffoldingLevel 'None'     → independent practice, no hint UI
//
// The hint button calls the parent with a `hint` event; the parent hits
// POST /api/sessions/{id}/question/{qid}/hint and passes the response
// back via the `lastHint` prop. When the backend returns 404 (no more
// hints, or scaffolding level denies them), the UI hides the button.
//
// Citations (real pedagogy, no hand-waving):
//   - Sweller et al. (1998), DOI 10.1023/A:1022193728205 — worked examples.
//   - Renkl & Atkinson (2003), DOI 10.1207/S15326985EP3801_3 — fading.
//   - Kalyuga et al. (2003), DOI 10.1207/S15326985EP3801_4 — expertise
//     reversal (scaffolds hurt experts).

interface Props {
  question: SessionQuestionDto
  locked?: boolean

  /** Most recent hint returned by the backend for THIS question, if any. */
  lastHint?: SessionHintResponseDto | null

  /** Whether a hint request is currently in flight. */
  hintLoading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  locked: false,
  lastHint: null,
  hintLoading: false,
})

const emit = defineEmits<{
  submit: [answer: string, timeSpentMs: number]
  hint: []
}>()

const { t } = useI18n()

const selected = ref<string | null>(null)
const startedAt = ref<number>(Date.now())

watch(() => props.question.questionId, () => {
  selected.value = null
  startedAt.value = Date.now()
})

const showWorkedExample = computed(() =>
  props.question.scaffoldingLevel === 'Full'
  && !!props.question.workedExample)

const showHintButton = computed(() => {
  const level = props.question.scaffoldingLevel
  if (level === 'None' || level === undefined)
    return false
  const remaining = props.question.hintsRemaining ?? 0

  // If the server fed us a concrete `hintsRemaining`, respect it. If the
  // field is omitted we fall back to "show the button" only when the
  // level advertises hints.
  if (props.question.hintsRemaining !== undefined && remaining <= 0)
    return false

  return level === 'Full' || level === 'Partial' || level === 'HintsOnly'
})

const hintsRemaining = computed(() => {
  // The parent page refreshes this after every /hint call by feeding
  // lastHint.hintsRemaining back into the question prop, but while the
  // round-trip is in flight we show the optimistic value.
  if (props.lastHint)
    return props.lastHint.hintsRemaining

  return props.question.hintsRemaining ?? props.question.hintsAvailable ?? 0
})

function handleSubmit() {
  if (!selected.value || props.locked)
    return

  const timeSpentMs = Date.now() - startedAt.value

  emit('submit', selected.value, timeSpentMs)
}

function handleHint() {
  if (props.hintLoading || !showHintButton.value)
    return
  emit('hint')
}
</script>

<template>
  <VCard
    class="question-card pa-6"
    variant="flat"
    data-testid="question-card"
  >
    <div class="d-flex align-center justify-space-between mb-4">
      <VChip
        size="small"
        variant="tonal"
        color="primary"
      >
        {{ question.subject }}
      </VChip>
      <div
        class="text-caption text-medium-emphasis"
        data-testid="question-progress"
      >
        {{ t('session.runner.questionProgress', {
          current: question.questionIndex + 1,
          total: question.totalQuestions,
        }) }}
      </div>
    </div>

    <VProgressLinear
      :model-value="(question.questionIndex / question.totalQuestions) * 100"
      color="primary"
      height="6"
      rounded
      class="mb-6"
      :aria-label="t('session.runner.progressAria', {
        current: question.questionIndex + 1,
        total: question.totalQuestions,
      })"
    />

    <h2
      class="text-h5 mb-6"
      data-testid="question-prompt"
    >
      {{ question.prompt }}
    </h2>

    <!-- FIGURE-004: Render question figure if present (function plot, geometry, physics, raster) -->
    <QuestionFigure
      v-if="question.figureSpec"
      :spec="question.figureSpec"
      class="mb-6"
      data-testid="question-figure"
    />

    <!--
      FIND-pedagogy-006 — Worked example block for novice learners
      (ScaffoldingLevel === 'Full'). The worked example body comes from
      the authored QuestionDocument.Explanation, surfaced by the backend
      only when the student's BKT mastery falls below the Full threshold
      (< 0.20). Cite: Sweller et al. 1998 (worked example effect).
    -->
    <VAlert
      v-if="showWorkedExample"
      type="info"
      variant="tonal"
      icon="tabler-bulb"
      class="mb-6"
      data-testid="question-worked-example"
    >
      <div class="text-subtitle-2 mb-2">
        {{ t('session.runner.workedExampleLabel') }}
      </div>
      <div class="text-body-2">
        {{ question.workedExample }}
      </div>
    </VAlert>

    <!--
      Last-requested hint lives above the choices so it doesn't fight
      with the Submit button for focus. Only rendered when the parent
      passes one back via `lastHint`.
    -->
    <VAlert
      v-if="lastHint"
      type="warning"
      variant="tonal"
      icon="tabler-help-hexagon"
      class="mb-4"
      data-testid="question-hint-display"
    >
      <div class="text-caption text-medium-emphasis mb-1">
        {{ t('session.runner.hintLevel', { level: lastHint.hintLevel }) }}
      </div>
      <div class="text-body-2">
        {{ lastHint.hintText }}
      </div>
    </VAlert>

    <div
      class="question-card__choices"
      role="radiogroup"
      :aria-label="t('session.runner.choicesAria')"
    >
      <VCard
        v-for="choice in question.choices"
        :key="choice"
        :variant="selected === choice ? 'flat' : 'outlined'"
        :color="selected === choice ? 'primary' : undefined"
        class="question-card__choice pa-4 cursor-pointer"
        :data-testid="`choice-${choice}`"
        role="radio"
        :aria-checked="selected === choice"
        tabindex="0"
        @click="locked ? null : (selected = choice)"
        @keydown.enter.prevent="locked ? null : (selected = choice)"
        @keydown.space.prevent="locked ? null : (selected = choice)"
      >
        <div class="d-flex align-center">
          <VIcon
            :icon="selected === choice ? 'tabler-circle-check-filled' : 'tabler-circle'"
            size="24"
            class="me-3"
            aria-hidden="true"
          />
          <span class="text-body-1">{{ choice }}</span>
        </div>
      </VCard>
    </div>

    <div class="d-flex justify-space-between align-center mt-6">
      <!--
        FIND-pedagogy-006 — Hint button. Hidden entirely when the
        scaffolding level is 'None' (experts) or when hintsRemaining
        reaches zero (expertise reversal effect — never burden
        higher-mastery students with extra scaffolds).
      -->
      <VBtn
        v-if="showHintButton"
        variant="tonal"
        color="warning"
        prepend-icon="tabler-bulb-filled"
        :disabled="locked || hintLoading"
        :loading="hintLoading"
        data-testid="question-hint-request"
        @click="handleHint"
      >
        {{ t('session.runner.requestHint', { remaining: hintsRemaining }) }}
      </VBtn>
      <span
        v-else
        aria-hidden="true"
      />

      <VBtn
        color="primary"
        size="large"
        :disabled="!selected || locked"
        prepend-icon="tabler-check"
        data-testid="question-submit"
        @click="handleSubmit"
      >
        {{ t('session.runner.submitAnswer') }}
      </VBtn>
    </div>
  </VCard>
</template>

<style scoped>
.question-card__choices {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.question-card__choice {
  transition: transform 0.1s ease-out, border-color 0.1s ease-out;
}

.question-card__choice:hover {
  transform: translateY(-1px);
}

.question-card__choice:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}
</style>
