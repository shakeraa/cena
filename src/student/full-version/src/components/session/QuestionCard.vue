<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SessionQuestionDto } from '@/api/types/common'

interface Props {
  question: SessionQuestionDto
  locked?: boolean
}

const props = withDefaults(defineProps<Props>(), { locked: false })
const emit = defineEmits<{
  submit: [answer: string, timeSpentMs: number]
}>()

const { t } = useI18n()

const selected = ref<string | null>(null)
const startedAt = ref<number>(Date.now())

watch(() => props.question.questionId, () => {
  selected.value = null
  startedAt.value = Date.now()
})

function handleSubmit() {
  if (!selected.value || props.locked)
    return

  const timeSpentMs = Date.now() - startedAt.value

  emit('submit', selected.value, timeSpentMs)
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

    <div class="d-flex justify-end mt-6">
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
