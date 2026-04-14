<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import QuestionCard from '@/components/session/QuestionCard.vue'
import AnswerFeedback from '@/components/session/AnswerFeedback.vue'
import { $api } from '@/api/$api'
import { useCelebration } from '@/composables/useCelebration'
import { useFlowState } from '@/composables/useFlowState'
import FlowAmbientBackground from '@/components/common/FlowAmbientBackground.vue'
import type {
  SessionAnswerResponseDto,
  SessionHintResponseDto,
  SessionQuestionDto,
} from '@/api/types/common'

// FIND-pedagogy-005 — tap-to-continue feedback
//
// The previous implementation auto-dismissed feedback after a hard-coded
// ~1.6-second setTimeout, well below the floor needed for learners to
// read and reflect on a rationale (Shute 2008, DOI 10.3102/0034654307313795).
// The page now waits for an explicit `@continue` emission from
// AnswerFeedback. For correct answers it additionally offers an optional
// auto-advance after `CORRECT_AUTO_ADVANCE_MS`, but only when the
// student has not disabled auto-advance via the a11y preference.
//
// The a11y preference is stored under `cena.a11y.feedbackTiming` in
// localStorage so refreshes survive. 'manual' disables all auto-advance
// (even on correct answers), 'auto' uses the default delay, and 'slow'
// triples the delay for students who need more reading time.
const CORRECT_AUTO_ADVANCE_MS = 8000
const SLOW_MULTIPLIER = 3
type FeedbackTiming = 'auto' | 'manual' | 'slow'

function readFeedbackTimingPref(): FeedbackTiming {
  try {
    const raw = globalThis.localStorage?.getItem('cena.a11y.feedbackTiming')
    if (raw === 'manual' || raw === 'slow' || raw === 'auto')
      return raw
  }
  catch {
    // SSR / disabled storage — fall through to default
  }

  return 'manual' // conservative default: manual continue for EVERY answer
}

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
const { triggerCorrectAnswer, triggerLevelUp, triggerMasteryMilestone } = useCelebration()
const {
  currentFlowState,
  showBreakSuggestion,
  showDifficultyAdjustment,
  updateFromBackend: updateFlowState,
  dismissBreakSuggestion,
  dismissDifficultyAdjustment,
} = useFlowState()

const question = ref<SessionQuestionDto | null>(null)
const feedback = ref<SessionAnswerResponseDto | null>(null)
const lastHint = ref<SessionHintResponseDto | null>(null)
const hintLoading = ref(false)
const loading = ref(true)
const submitting = ref(false)
const completing = ref(false)
const advancing = ref(false)
const error = ref<string | null>(null)

// Active auto-advance timer so `onBeforeUnmount` / manual continue can
// cancel it before it fires. Undefined means "no auto-advance pending".
let autoAdvanceTimer: ReturnType<typeof setTimeout> | undefined

async function loadCurrentQuestion() {
  loading.value = true
  error.value = null

  // FIND-pedagogy-006: reset the hint panel when a fresh question loads
  // so the previous question's hint does not leak into this one.
  lastHint.value = null
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

// FIND-pedagogy-006 — Request a progressive hint for the question
// currently in flight. Hits the new POST /hint endpoint which returns
// the next hint from HintGenerator (same service the actor-side session
// uses). A 404 from the backend is the canonical "no more hints" signal
// and the UI reacts by zeroing the remaining count so the button hides.
async function handleHintRequest() {
  if (!question.value || hintLoading.value)
    return
  hintLoading.value = true
  error.value = null
  try {
    const hint = await $api<SessionHintResponseDto>(
      `/api/sessions/${sessionId}/question/${question.value.questionId}/hint`,
      { method: 'POST' as any },
    )

    lastHint.value = hint

    // Mirror the remaining-hint count into the question prop so the
    // hint button disables when exhausted (HintsOnly, Partial, Full).
    question.value = { ...question.value, hintsRemaining: hint.hintsRemaining }
  }
  catch (err) {
    // Production-grade per the review: a 404 means "no more hints
    // available" (either exhausted budget or level=None). Reflect that
    // by zeroing the counter so the button hides, but do NOT surface an
    // error banner — the student didn't do anything wrong.
    const status = (err as { statusCode?: number; status?: number }).statusCode
      ?? (err as { statusCode?: number; status?: number }).status

    if (status === 404) {
      if (question.value)
        question.value = { ...question.value, hintsRemaining: 0 }
    }
    else {
      error.value = (err as Error).message || t('error.serverError')
    }
  }
  finally {
    hintLoading.value = false
  }
}

async function completeSession() {
  completing.value = true

  // The summary page calls /complete itself — we just navigate there.
  await router.push(`/session/${sessionId}/summary`)
}

function clearAutoAdvance() {
  if (autoAdvanceTimer !== undefined) {
    clearTimeout(autoAdvanceTimer)
    autoAdvanceTimer = undefined
  }
}

async function advanceAfterFeedback() {
  if (advancing.value)
    return
  advancing.value = true
  clearAutoAdvance()

  const resp = feedback.value

  feedback.value = null
  try {
    if (resp?.nextQuestionId)
      await loadCurrentQuestion()

    else
      await completeSession()
  }
  finally {
    advancing.value = false
  }
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

    // RDY-016: Trigger celebrations based on answer result
    if (resp.correct) {
      triggerCorrectAnswer(resp.xpAwarded ?? 10)

      // Level-up check (backend includes levelUp flag when XP crosses threshold)
      if ((resp as any).levelUp && (resp as any).newLevel) {
        triggerLevelUp((resp as any).newLevel)
      }

      // Mastery milestone check (mastery crossed 0.85)
      if ((resp as any).masteryMilestone && (resp as any).conceptName) {
        triggerMasteryMilestone((resp as any).conceptName)
      }
    }

    // RDY-016: Update flow state from backend response
    if ((resp as any).flowState) {
      updateFlowState({
        fatigueLevel: (resp as any).flowState.fatigueLevel ?? 0,
        accuracyTrend: (resp as any).flowState.accuracyTrend ?? 0,
        consecutiveCorrect: (resp as any).flowState.consecutiveCorrect ?? 0,
        sessionDurationMinutes: (resp as any).flowState.sessionDurationMinutes ?? 0,
      })
    }

    // FIND-pedagogy-005: NO hard-coded dismiss timeout. The student taps
    // the Continue button in AnswerFeedback (emits @continue) to advance.
    //
    // Exception (optional) — when the answer is correct AND the a11y
    // preference is 'auto' or 'slow', schedule a delayed auto-advance as
    // a convenience. A manual Continue button is STILL shown, and wrong
    // answers NEVER auto-advance regardless of preference (Kulhavy &
    // Stock 1989 — errors require more processing time).
    const timingPref = readFeedbackTimingPref()
    if (resp.correct && timingPref !== 'manual') {
      const delay = CORRECT_AUTO_ADVANCE_MS * (timingPref === 'slow' ? SLOW_MULTIPLIER : 1)

      autoAdvanceTimer = setTimeout(() => {
        // Only advance if the student hasn't already done it manually.
        // We intentionally do not await the promise — the timer is a
        // fire-and-forget schedule, and `advanceAfterFeedback` guards
        // against concurrent advances via the `advancing` ref.
        if (feedback.value)
          advanceAfterFeedback().catch(() => { /* handled inside */ })
      }, delay)
    }
  }
  catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
  finally {
    submitting.value = false
  }
}

function handleExit() {
  clearAutoAdvance()
  router.push('/home')
}

onMounted(loadCurrentQuestion)
onBeforeUnmount(clearAutoAdvance)
</script>

<template>
  <div
    class="session-runner-page"
    data-testid="session-runner-page"
  >
    <!-- RDY-016: Flow state ambient background -->
    <FlowAmbientBackground :flow-state="currentFlowState" />

    <!-- RDY-016: Flow state user feedback -->
    <VSnackbar
      v-model="showBreakSuggestion"
      color="warning"
      location="top"
      timeout="-1"
    >
      {{ $t('session.flow.breakSuggestion', 'Good time for a break? You seem tired.') }}
      <template #actions>
        <VBtn variant="text" @click="dismissBreakSuggestion">
          {{ $t('session.flow.continueStudying', 'Keep going') }}
        </VBtn>
        <VBtn variant="text" @click="handleExit">
          {{ $t('session.flow.takeBreak', 'Take a break') }}
        </VBtn>
      </template>
    </VSnackbar>

    <VSnackbar
      v-model="showDifficultyAdjustment"
      color="info"
      location="top"
      timeout="5000"
    >
      {{ $t('session.flow.difficultyAdjustment', "Let's try something easier to rebuild momentum.") }}
      <template #actions>
        <VBtn variant="text" @click="dismissDifficultyAdjustment">
          {{ $t('common.ok', 'OK') }}
        </VBtn>
      </template>
    </VSnackbar>
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
        :loading="advancing"
        @continue="advanceAfterFeedback"
      />

      <QuestionCard
        v-else-if="question"
        :question="question"
        :locked="submitting"
        :last-hint="lastHint"
        :hint-loading="hintLoading"
        @submit="handleAnswer"
        @hint="handleHintRequest"
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
