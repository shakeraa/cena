<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import QuestionCard from '@/components/session/QuestionCard.vue'
import AnswerFeedback from '@/components/session/AnswerFeedback.vue'
import HintLadder from '@/components/session/HintLadder.vue'
import StepSolverCard from '@/components/session/StepSolverCard.vue'
import FreeBodyDiagramConstruct from '@/components/session/FreeBodyDiagramConstruct.vue'
import Sidekick from '@/components/Sidekick.vue'
import { $api } from '@/api/$api'
import { postFbdSubmission, postStep } from '@/api/sessions'
import { useCelebration } from '@/composables/useCelebration'
import { useFlowState } from '@/composables/useFlowState'
import FlowAmbientBackground from '@/components/common/FlowAmbientBackground.vue'
import SessionTimer from '@/components/session/SessionTimer.vue'
import FatigueCheck from '@/components/session/FatigueCheck.vue'
import { useSessionPersistence } from '@/composables/useSessionPersistence'
import { useHintLadder } from '@/composables/useHintLadder'
import type {
  PhysicsFigureSpec,
  SessionAnswerResponseDto,
  SessionQuestionDto,
} from '@/api/types/common'

// FIND-pedagogy-005 — tap-to-continue feedback.
// (see prior rationale in git history; this comment is a short marker)
const CORRECT_AUTO_ADVANCE_MS = 8000
const SLOW_MULTIPLIER = 3
type FeedbackTiming = 'auto' | 'manual' | 'slow'

function readFeedbackTimingPref(): FeedbackTiming {
  try {
    const raw = globalThis.localStorage?.getItem('cena.a11y.feedbackTiming')
    if (raw === 'manual' || raw === 'slow' || raw === 'auto') return raw
  }
  catch { /* SSR / disabled storage */ }
  return 'manual'
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

const { t, locale } = useI18n()
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

const { saveSnapshot, restoreSnapshot, clearSession } = useSessionPersistence()

const sessionPaused = ref(false)
const showFatigueCheck = ref(false)
const questionsAnswered = ref(0)
const sessionStartedAt = ref(new Date().toISOString())
const pausedDurationSeconds = ref(0)
const FATIGUE_CHECK_INTERVAL = 10

const existingSnapshot = restoreSnapshot(sessionId)
if (existingSnapshot) {
  sessionStartedAt.value = existingSnapshot.startedAt
  questionsAnswered.value = existingSnapshot.answeredSteps.length
}

function togglePause() {
  sessionPaused.value = !sessionPaused.value
  if (sessionPaused.value) {
    saveSnapshot({
      sessionId,
      currentStep: questionsAnswered.value,
      startedAt: sessionStartedAt.value,
      lastActivityAt: new Date().toISOString(),
      totalSteps: 0,
      answeredSteps: Array.from({ length: questionsAnswered.value }, (_, i) => i),
    })
  }
}

function handleTimerTick(elapsed: number) {
  if (elapsed > 0 && elapsed % 60 === 0) {
    saveSnapshot({
      sessionId,
      currentStep: questionsAnswered.value,
      startedAt: sessionStartedAt.value,
      lastActivityAt: new Date().toISOString(),
      totalSteps: 0,
      answeredSteps: Array.from({ length: questionsAnswered.value }, (_, i) => i),
    })
  }
}

function handleMilestone() {
  showBreakSuggestion.value = true
}

async function handleEnergyReport(level: 'energized' | 'okay' | 'tired') {
  try {
    await $api(`/api/sessions/${sessionId}/fatigue-report`, {
      method: 'POST' as any,
      body: { energyLevel: level, questionsAnswered: questionsAnswered.value } as any,
    })
  } catch { /* best-effort */ }
}

const question = ref<SessionQuestionDto | null>(null)
const feedback = ref<SessionAnswerResponseDto | null>(null)
const loading = ref(true)
const submitting = ref(false)
const completing = ref(false)
const advancing = ref(false)
const error = ref<string | null>(null)

// prr-205 — server-authoritative HintLadder state.
const ladder = useHintLadder({
  sessionId,
  questionId: () => question.value?.questionId ?? null,
  masteryBucket: () => question.value?.bktMasteryBucket ?? 'unknown',
})

// prr-207 — Sidekick drawer ref for imperative teardown on events.
const sidekickRef = ref<InstanceType<typeof Sidekick> | null>(null)
const sidekickOpen = ref(false)

// prr-206/208 — dispatch helpers. We look at server-provided fields to
// choose the right surface: step_solver → StepSolverCard, physics.fbd+
// Construct → FreeBodyDiagramConstruct, else MCQ (QuestionCard).
const questionSurface = computed<'mcq' | 'step_solver' | 'fbd'>(() => {
  const q = question.value
  if (!q) return 'mcq'
  const figure = q.figureSpec as PhysicsFigureSpec | undefined
  if (q.methodology === 'mechanics.fbd' && figure?.diagramMode === 'Construct')
    return 'fbd'
  if (q.questionType === 'step_solver') return 'step_solver'
  return 'mcq'
})

// RTL direction from active i18n locale.
const rtlDirection = computed<'rtl' | 'ltr'>(() =>
  locale.value === 'ar' || locale.value === 'he' ? 'rtl' : 'ltr')

let autoAdvanceTimer: ReturnType<typeof setTimeout> | undefined

async function loadCurrentQuestion() {
  loading.value = true
  error.value = null
  try {
    question.value = await $api<SessionQuestionDto>(
      `/api/sessions/${sessionId}/current-question`,
    )
    if (question.value && (question.value as any).sessionStartedAt) {
      sessionStartedAt.value = (question.value as any).sessionStartedAt
    }
    // Sidekick context is fetched on first open() — no eager pre-warm here.
    // The previous eager call raced against the LearningSessionQueueProjection
    // which is async-built off the student stream; for fresh sessions it
    // produced a 404 in chrome console on every session-load. Lazy fetch
    // is fast enough on warm Marten (single LoadAsync + Redis hit).
  }
  catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
  finally {
    loading.value = false
  }
}

// prr-205 — wire "request next rung" from HintLadder + QuestionCard "I'm stuck".
async function handleRequestNextRung() {
  await ladder.requestNext()
}

function handleStuckSurface() {
  ladder.surface()
  // Also start the ladder at rung 1 if empty.
  if (ladder.rungs.value.length === 0) {
    void ladder.requestNext()
  }
}

// prr-206 — per-step submit dispatch.
async function handleStepSubmit(payload: { stepNumber: number; expression: string; timeSpentMs: number; hintsConsumed: number }) {
  if (!question.value) return
  try {
    const resp = await postStep(sessionId, question.value.questionId, payload)
    return resp
  } catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
}

// prr-208 — FBD submit dispatch.
async function handleFbdSubmit(forces: { label: string; magnitude: number; angleDeg: number }[]) {
  if (!question.value) return
  try {
    const resp = await postFbdSubmission(sessionId, question.value.questionId, {
      forces,
      timeSpentMs: 0,
    })
    return resp
  } catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
}

async function completeSession() {
  completing.value = true
  await router.push(`/session/${sessionId}/summary`)
}

function clearAutoAdvance() {
  if (autoAdvanceTimer !== undefined) {
    clearTimeout(autoAdvanceTimer)
    autoAdvanceTimer = undefined
  }
}

async function advanceAfterFeedback() {
  if (advancing.value) return
  advancing.value = true
  clearAutoAdvance()
  const resp = feedback.value
  feedback.value = null
  try {
    if (resp?.nextQuestionId) await loadCurrentQuestion()
    else await completeSession()
  }
  finally {
    advancing.value = false
  }
}

async function handleAnswer(answer: string, timeSpentMs: number) {
  if (!question.value) return
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
    questionsAnswered.value++
    if (questionsAnswered.value > 0 && questionsAnswered.value % FATIGUE_CHECK_INTERVAL === 0) {
      showFatigueCheck.value = true
    }
    if (resp.correct) {
      triggerCorrectAnswer(resp.xpAwarded ?? 10)
      if ((resp as any).levelUp && (resp as any).newLevel)
        triggerLevelUp((resp as any).newLevel)
      if ((resp as any).masteryMilestone && (resp as any).conceptName)
        triggerMasteryMilestone((resp as any).conceptName)
    }
    else {
      // prr-207 — mark wrong-step so the Sidekick's explain-step intent
      // stays disabled for 15s (productive-failure debounce).
      sidekickRef.value?.noteWrongStep?.()
    }

    if ((resp as any).flowState) {
      updateFlowState({
        fatigueLevel: (resp as any).flowState.fatigueLevel ?? 0,
        accuracyTrend: (resp as any).flowState.accuracyTrend ?? 0,
        consecutiveCorrect: (resp as any).flowState.consecutiveCorrect ?? 0,
        sessionDurationMinutes: (resp as any).flowState.sessionDurationMinutes ?? 0,
      })
    }

    const timingPref = readFeedbackTimingPref()
    if (resp.correct && timingPref !== 'manual') {
      const delay = CORRECT_AUTO_ADVANCE_MS * (timingPref === 'slow' ? SLOW_MULTIPLIER : 1)
      autoAdvanceTimer = setTimeout(() => {
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
  if (!sessionPaused.value) clearSession(sessionId)
  sidekickRef.value?.teardown?.()
  router.push('/home')
}

// Ctrl+K / Cmd+K toggle at the page level too (Sidekick also listens,
// but the page-level listener ensures focus-trap restoration is wired
// to a stable element even when the drawer is not yet mounted).
function onSidekickShortcut(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    sidekickOpen.value = !sidekickOpen.value
  }
}

onMounted(() => {
  loadCurrentQuestion()
  window.addEventListener('keydown', onSidekickShortcut)
})

onBeforeUnmount(() => {
  clearAutoAdvance()
  window.removeEventListener('keydown', onSidekickShortcut)
  sidekickRef.value?.teardown?.()
})
</script>

<template>
  <div
    class="session-runner-page"
    data-testid="session-runner-page"
  >
    <FlowAmbientBackground :flow-state="currentFlowState" />

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
      {{ $t('session.flow.difficultyAdjustment', "Here's a familiar pattern — a good warm-up for the next step.") }}
      <template #actions>
        <VBtn variant="text" @click="dismissDifficultyAdjustment">
          {{ $t('common.ok', 'OK') }}
        </VBtn>
      </template>
    </VSnackbar>

    <FatigueCheck
      v-model="showFatigueCheck"
      @energy-reported="handleEnergyReport"
      @take-break="handleExit"
    />

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

      <SessionTimer
        :started-at="sessionStartedAt"
        :paused="sessionPaused"
        :milestone-minutes="25"
        :paused-duration-seconds="pausedDurationSeconds"
        @milestone-reached="handleMilestone"
        @pause="togglePause"
        @tick="handleTimerTick"
      />

      <div class="d-flex align-center ga-2">
        <!-- prr-207 — Sidekick trigger. Persistent but never pulsing or
             badge-annotated. Ctrl+K opens; click opens. Focus returns
             to this element on drawer close. -->
        <VBtn
          variant="tonal"
          color="primary"
          size="small"
          prepend-icon="tabler-message-circle"
          :aria-label="t('sidekick.openAria')"
          data-testid="sidekick-trigger"
          @click="sidekickOpen = !sidekickOpen"
        >
          {{ t('sidekick.trigger') }}
        </VBtn>
        <div class="text-caption text-medium-emphasis">
          {{ t('session.runner.sessionIdLabel') }} {{ sessionId.slice(0, 8) }}
        </div>
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

      <!-- prr-208 — physics FBD Construct path -->
      <FreeBodyDiagramConstruct
        v-else-if="question && questionSurface === 'fbd'"
        :scene-svg="(question.figureSpec as PhysicsFigureSpec).sceneSvg ?? ''"
        :body-center="(question.figureSpec as PhysicsFigureSpec).bodyCenter ?? { x: 300, y: 200 }"
        :expected-forces="(question.figureSpec as PhysicsFigureSpec).expectedForces ?? []"
        :ariaLabel="t('session.runner.fbd.diagramAria')"
        data-testid="runner-fbd"
        @submit="handleFbdSubmit"
      />

      <!-- prr-206 — step-solver path -->
      <StepSolverCard
        v-else-if="question && questionSurface === 'step_solver' && question.steps"
        :question="{
          id: question.questionId,
          stem: question.prompt,
          subject: question.subject,
          conceptId: '',
          steps: question.steps.map(s => ({
            stepNumber: s.stepNumber,
            instruction: s.instruction,
            fadedExample: s.fadedExample,
            expectedExpression: s.expectedExpression ?? '',
            hints: s.hints ?? [],
          })),
          finalAnswer: question.finalAnswer ?? '',
          scaffoldingLevel: (question.scaffoldingLevel === 'Full' ? 'full' : question.scaffoldingLevel === 'Partial' ? 'faded' : 'none') as 'full' | 'faded' | 'none',
        }"
        :session-id="sessionId"
        data-testid="runner-step-solver"
        @step-verified="(stepNumber, isCorrect) => {
          if (!isCorrect) sidekickRef?.noteWrongStep?.()
        }"
      />

      <!-- prr-205 — MCQ/short-answer path with hint ladder wired in -->
      <template v-else-if="question && questionSurface === 'mcq'">
        <QuestionCard
          :question="question"
          :locked="submitting"
          :last-hint="null"
          :hint-loading="ladder.loading.value"
          data-testid="runner-question-card"
          @submit="handleAnswer"
          @hint="handleStuckSurface"
        />

        <!-- HintLadder only renders once the student has asked or a
             low/mid mastery student auto-sees it after first rung.
             Expertise-reversal: high-mastery students stay collapsed
             unless they explicitly press "I'm stuck". -->
        <div
          v-if="ladder.visible.value && ladder.rungs.value.length > 0"
          class="session-runner-page__ladder mt-4"
          data-testid="runner-hint-ladder"
        >
          <HintLadder
            :hints="ladder.rungs.value"
            :loading="ladder.loading.value"
            :locked="submitting"
            :hints-remaining="ladder.nextRungAvailable.value ? undefined : 0"
            @request-next-rung="handleRequestNextRung"
          />
        </div>
      </template>
    </div>

    <!-- prr-207 — Sidekick drawer. Single instance, session-scoped. -->
    <Sidekick
      ref="sidekickRef"
      v-model="sidekickOpen"
      :session-id="sessionId"
      :direction="rtlDirection"
      @fallback-to-ladder="handleStuckSurface"
      @fallbackToLadder="handleStuckSurface"
    />
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
