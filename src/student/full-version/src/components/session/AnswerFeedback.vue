<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { SessionAnswerResponseDto } from '@/api/types/common'

interface Props {
  feedback: SessionAnswerResponseDto
}

defineProps<Props>()
const { t } = useI18n()
</script>

<template>
  <VCard
    :class="feedback.correct ? 'answer-feedback--correct' : 'answer-feedback--wrong'"
    class="answer-feedback pa-5"
    variant="flat"
    data-testid="answer-feedback"
    :data-correct="feedback.correct"
  >
    <div class="d-flex align-center mb-2">
      <VIcon
        :icon="feedback.correct ? 'tabler-circle-check-filled' : 'tabler-circle-x-filled'"
        :color="feedback.correct ? 'success' : 'error'"
        size="36"
        class="me-3"
        aria-hidden="true"
      />
      <div>
        <div class="text-h6">
          {{ feedback.correct ? t('session.runner.correct') : t('session.runner.wrong') }}
        </div>
        <div
          v-if="feedback.xpAwarded > 0"
          class="text-caption text-success"
          data-testid="feedback-xp"
        >
          {{ t('session.runner.xpAwarded', { xp: feedback.xpAwarded }) }}
        </div>
      </div>
    </div>
    <div
      class="text-body-2 text-medium-emphasis"
      data-testid="feedback-message"
    >
      {{ feedback.feedback }}
    </div>
  </VCard>
</template>

<style scoped>
.answer-feedback--correct {
  background-color: rgb(var(--v-theme-success) / 0.12);
  border-inline-start: 4px solid rgb(var(--v-theme-success));
}

.answer-feedback--wrong {
  background-color: rgb(var(--v-theme-error) / 0.12);
  border-inline-start: 4px solid rgb(var(--v-theme-error));
}
</style>
