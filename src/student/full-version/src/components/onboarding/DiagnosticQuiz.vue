<script setup lang="ts">
/**
 * DiagnosticQuiz.vue — RDY-023: Onboarding diagnostic quiz
 *
 * 5-10 questions per subject across difficulty bands (easy/medium/hard).
 * 2-minute timer. No scaffolding or hints. Estimates IRT theta → BKT P_Initial.
 * Skip option: "I'll start from scratch" → P_Initial = 0.10.
 */
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'

export interface DiagnosticQuestion {
  questionId: string
  subject: string
  difficulty: number
  band: 'easy' | 'medium' | 'hard'
  questionText: string
  options: { key: string; text: string }[]
  correctOptionKey: string
}

export interface DiagnosticResponse {
  questionId: string
  subject: string
  correct: boolean
  difficulty: number
}

interface Props {
  /** Subjects selected for diagnostic */
  subjects: string[]
}

interface Emits {
  (e: 'complete', responses: DiagnosticResponse[]): void
  (e: 'skip'): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const { t } = useI18n()

// ─── State ───────────────────────────────────────────────────────────
const loading = ref(true)
const questions = ref<DiagnosticQuestion[]>([])
const currentIndex = ref(0)
const responses = ref<DiagnosticResponse[]>([])
const selectedKey = ref<string | null>(null)
const showFeedback = ref(false)
const feedbackCorrect = ref(false)

// Timer: 2 minutes total
const TOTAL_SECONDS = 120
const secondsLeft = ref(TOTAL_SECONDS)
let timerInterval: ReturnType<typeof setInterval> | null = null

const currentQuestion = computed(() => questions.value[currentIndex.value] ?? null)
const progress = computed(() =>
  questions.value.length > 0
    ? Math.round((currentIndex.value / questions.value.length) * 100)
    : 0,
)
const timerDisplay = computed(() => {
  const m = Math.floor(secondsLeft.value / 60)
  const s = secondsLeft.value % 60

  return `${m}:${s.toString().padStart(2, '0')}`
})
const timerWarning = computed(() => secondsLeft.value <= 30)

// ─── Lifecycle ───────────────────────────────────────────────────────
onMounted(async () => {
  await fetchItems()
  startTimer()
})

onUnmounted(() => {
  if (timerInterval)
    clearInterval(timerInterval)
})

// ─── Data fetching ───────────────────────────────────────────────────
async function fetchItems() {
  loading.value = true
  try {
    const subjectParam = props.subjects.join(',')
    const res = await fetch(`/api/diagnostic/items?subjects=${encodeURIComponent(subjectParam)}`, {
      credentials: 'include',
    })

    if (!res.ok)
      throw new Error(`HTTP ${res.status}`)

    const data = await res.json()

    questions.value = data.items ?? []
  }
  catch {
    // Fallback: skip diagnostic if items unavailable
    questions.value = []
  }
  finally {
    loading.value = false
  }

  // If no questions available, auto-skip
  if (questions.value.length === 0)
    emit('skip')
}

// ─── Timer ───────────────────────────────────────────────────────────
function startTimer() {
  timerInterval = setInterval(() => {
    secondsLeft.value--
    if (secondsLeft.value <= 0) {
      if (timerInterval)
        clearInterval(timerInterval)
      finishQuiz()
    }
  }, 1000)
}

// ─── Answer handling ─────────────────────────────────────────────────
function selectOption(key: string) {
  if (showFeedback.value)
    return
  selectedKey.value = key
}

function confirmAnswer() {
  if (!selectedKey.value || !currentQuestion.value)
    return

  const q = currentQuestion.value
  const correct = selectedKey.value === q.correctOptionKey

  feedbackCorrect.value = correct
  showFeedback.value = true

  responses.value.push({
    questionId: q.questionId,
    subject: q.subject,
    correct,
    difficulty: q.difficulty,
  })

  // Brief feedback then advance
  setTimeout(() => {
    showFeedback.value = false
    selectedKey.value = null
    feedbackCorrect.value = false

    if (currentIndex.value < questions.value.length - 1) {
      currentIndex.value++
    }
    else {
      finishQuiz()
    }
  }, 600)
}

function finishQuiz() {
  if (timerInterval)
    clearInterval(timerInterval)
  emit('complete', responses.value)
}

function handleSkip() {
  if (timerInterval)
    clearInterval(timerInterval)
  emit('skip')
}

function onOptionKeydown(event: KeyboardEvent, key: string) {
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    selectOption(key)
  }
}
</script>

<template>
  <div
    data-testid="diagnostic-quiz"
    class="diagnostic-quiz"
  >
    <!-- Loading -->
    <div
      v-if="loading"
      class="text-center py-8"
    >
      <VProgressCircular
        indeterminate
        color="primary"
        size="48"
      />
      <p class="text-body-2 mt-4 text-medium-emphasis">
        {{ t('onboarding.diagnostic.loading') }}
      </p>
    </div>

    <!-- Quiz active -->
    <template v-else-if="currentQuestion">
      <!-- Timer + Progress -->
      <div class="d-flex align-center justify-space-between mb-4">
        <VProgressLinear
          :model-value="progress"
          color="primary"
          height="8"
          rounded
          class="flex-grow-1 me-4"
          role="progressbar"
          :aria-label="t('onboarding.diagnostic.progressLabel', { current: currentIndex + 1, total: questions.length })"
          :aria-valuenow="progress"
          aria-valuemin="0"
          aria-valuemax="100"
          data-testid="diagnostic-progress"
        />
        <VChip
          :color="timerWarning ? 'error' : 'default'"
          variant="tonal"
          size="small"
          data-testid="diagnostic-timer"
        >
          <VIcon
            icon="tabler-clock"
            size="14"
            start
            aria-hidden="true"
          />
          {{ timerDisplay }}
        </VChip>
      </div>

      <!-- Question counter -->
      <p class="text-caption text-medium-emphasis mb-2">
        {{ t('onboarding.diagnostic.questionCount', { current: currentIndex + 1, total: questions.length }) }}
      </p>

      <!-- Question text -->
      <h3
        class="text-h6 mb-4"
        data-testid="diagnostic-question-text"
      >
        {{ currentQuestion.questionText }}
      </h3>

      <!-- Options -->
      <div
        role="radiogroup"
        :aria-label="t('onboarding.diagnostic.optionsLabel')"
        class="d-flex flex-column ga-2 mb-4"
      >
        <VCard
          v-for="opt in currentQuestion.options"
          :key="opt.key"
          :variant="selectedKey === opt.key ? 'flat' : 'outlined'"
          :color="selectedKey === opt.key ? (showFeedback ? (feedbackCorrect ? 'success' : 'error') : 'primary') : undefined"
          class="pa-3 cursor-pointer"
          role="radio"
          :aria-checked="selectedKey === opt.key"
          tabindex="0"
          :data-testid="`diagnostic-option-${opt.key}`"
          @click="selectOption(opt.key)"
          @keydown="onOptionKeydown($event, opt.key)"
        >
          <span class="text-body-1">{{ opt.text }}</span>
        </VCard>
      </div>

      <!-- Confirm -->
      <div class="d-flex align-center justify-space-between">
        <VBtn
          variant="text"
          size="small"
          data-testid="diagnostic-skip"
          @click="handleSkip"
        >
          {{ t('onboarding.diagnostic.skipAll') }}
        </VBtn>

        <VBtn
          color="primary"
          :disabled="!selectedKey || showFeedback"
          data-testid="diagnostic-confirm"
          @click="confirmAnswer"
        >
          {{ t('onboarding.diagnostic.confirm') }}
        </VBtn>
      </div>
    </template>
  </div>
</template>

<style scoped>
.diagnostic-quiz {
  min-block-size: 300px;
}

.cursor-pointer {
  cursor: pointer;
}
</style>
