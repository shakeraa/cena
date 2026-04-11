<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import QuestionCard from '@/components/session/QuestionCard.vue'
import AnswerFeedback from '@/components/session/AnswerFeedback.vue'
import { $api } from '@/api/$api'
import type {
  SessionAnswerResponseDto,
  SessionQuestionDto,
} from '@/api/types/common'

definePage({
  meta: {
    layout: 'blank',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.session',
    hideSidebar: true,
    breadcrumbs: false,
  },
})

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const sessionId = String(route.params.sessionId)

const question = ref<SessionQuestionDto | null>(null)
const feedback = ref<SessionAnswerResponseDto | null>(null)
const loading = ref(true)
const submitting = ref(false)
const completing = ref(false)
const error = ref<string | null>(null)

async function loadCurrentQuestion() {
  loading.value = true
  error.value = null
  try {
    question.value = await $api<SessionQuestionDto>(`/api/sessions/${sessionId}/current-question`)
  }
  catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
  finally {
    loading.value = false
  }
}

async function completeSession() {
  completing.value = true
  // The summary page calls /complete itself — we just navigate there.
  await router.push(`/session/${sessionId}/summary`)
}

async function handleAnswer(answer: string, timeSpentMs: number) {
  if (!question.value)
    return

  submitting.value = true
  error.value = null
  try {
    const resp = await $api<SessionAnswerResponseDto>(
      `/api/sessions/${sessionId}/answer`,
      {
        method: 'POST' as any,
        body: {
          questionId: question.value.questionId,
          answer,
          timeSpentMs,
        } as any,
      },
    )

    feedback.value = resp

    // Show feedback for ~1.6s then auto-advance
    setTimeout(async () => {
      feedback.value = null
      if (resp.nextQuestionId) {
        await loadCurrentQuestion()
      }
      else {
        await completeSession()
      }
    }, 1600)
  }
  catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
  finally {
    submitting.value = false
  }
}

function handleExit() {
  router.push('/home')
}

onMounted(loadCurrentQuestion)
</script>

<template>
  <div
    class="session-runner-page"
    data-testid="session-runner-page"
  >
    <div class="session-runner-page__header d-flex align-center justify-space-between pa-4">
      <VBtn
        variant="text"
        prepend-icon="tabler-x"
        :aria-label="t('session.runner.exitAria')"
        data-testid="runner-exit"
        @click="handleExit"
      >
        {{ t('session.runner.exit') }}
      </VBtn>
      <div class="text-caption text-medium-emphasis">
        {{ t('session.runner.sessionIdLabel') }} {{ sessionId.slice(0, 8) }}
      </div>
    </div>

    <div class="session-runner-page__body pa-4 pa-md-6">
      <div
        v-if="loading && !question"
        class="d-flex justify-center py-12"
        data-testid="runner-loading"
      >
        <VProgressCircular indeterminate />
      </div>

      <VAlert
        v-else-if="error"
        type="error"
        variant="tonal"
        data-testid="runner-error"
      >
        {{ error }}
      </VAlert>

      <div
        v-else-if="completing"
        class="text-center py-12"
        data-testid="runner-completing"
      >
        <VProgressCircular
          indeterminate
          size="40"
          class="mb-4"
        />
        <div class="text-body-1">
          {{ t('session.runner.completing') }}
        </div>
      </div>

      <AnswerFeedback
        v-else-if="feedback"
        :feedback="feedback"
      />

      <QuestionCard
        v-else-if="question"
        :question="question"
        :locked="submitting"
        @submit="handleAnswer"
      />
    </div>
  </div>
</template>

<style scoped>
.session-runner-page {
  min-block-size: 100dvh;
  display: flex;
  flex-direction: column;
  background-color: rgb(var(--v-theme-background));
}

.session-runner-page__header {
  border-block-end: 1px solid rgb(var(--v-theme-on-surface) / 0.08);
}

.session-runner-page__body {
  flex: 1;
  max-inline-size: 720px;
  inline-size: 100%;
  margin-inline: auto;
}
</style>
