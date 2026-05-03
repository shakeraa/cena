<script setup lang="ts">
/**
 * PerTargetDiagnosticQuiz.vue — prr-228 per-target diagnostic block
 *
 * Replaces the legacy single-pool DiagnosticQuiz for multi-target students.
 * Runs one block PER exam target, 6-8 items each, easy-first, adaptive
 * stop on BKT-posterior convergence. Skip-this-item and skip-this-target
 * are always visible.
 *
 * Design pins (ADR-0050 + 2026-04-21 tightening):
 *   - 6-8 items per target (FloorCap..CeilingCap from server response)
 *   - "Calibration, not testing" framing — no score/grade/pass language
 *   - Provenance label visible per item (ADR-0043)
 *   - TopicFeeling post-block (RDY-057 enum reuse)
 *   - Skip counts toward cap but does NOT penalise the mastery estimate
 *
 * Completion SLO: 85% (see shipgate telemetry counters on the server).
 */
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { $api } from '@/utils/api'

interface PerTargetDiagnosticItem {
  itemId: string
  skillCode: string
  difficultyIrt: number
  band: string
  questionText: string
  options: { key: string; text: string }[]
  correctOptionKey: string
  provenanceSource: string
  provenanceLabel: string
}

interface BlockResponseItem {
  itemId: string
  skillCode: string
  correct: boolean
  skipped: boolean
  difficultyIrt: number
}

interface PerTargetBlockResponse {
  examTargetCode: string
  items: PerTargetDiagnosticItem[]
  floorCap: number
  ceilingCap: number
}

interface Props {
  /** Exam target we're calibrating against. */
  examTargetCode: string
  /** Human-readable label for the target (e.g. "Bagrut Math 5-unit"). */
  targetLabel: string
  /** Optional index for progress UI — e.g. "block 1 of 2". */
  blockIndex?: number
  blockTotal?: number
}

interface Emits {
  /** Block completed — summary carries the server response + topic feeling. */
  (e: 'block-complete', payload: {
    examTargetCode: string
    responses: BlockResponseItem[]
    topicFeeling: 'Solid' | 'Unsure' | 'Anxious' | 'New' | null
  }): void
  /** Student skipped the whole block for this target. */
  (e: 'block-skip-target', payload: { examTargetCode: string }): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()
const { t } = useI18n()

// ─── State ─────────────────────────────────────────────────────────────
const loading = ref(true)
const block = ref<PerTargetBlockResponse | null>(null)
const currentIndex = ref(0)
const responses = ref<BlockResponseItem[]>([])
const selectedKey = ref<string | null>(null)
const posterior = ref(0.5)
const showFeelingCapture = ref(false)
const topicFeeling = ref<'Solid' | 'Unsure' | 'Anxious' | 'New' | null>(null)
const fetchError = ref<string | null>(null)

const currentItem = computed<PerTargetDiagnosticItem | null>(() =>
  block.value?.items[currentIndex.value] ?? null,
)

const totalSoFar = computed(() => responses.value.length)

const progressLabel = computed(() => {
  if (!block.value)
    return ''
  // Intentionally "calibration" / "items", not "question X of Y" to dodge
  // testing-language and dodge premature commitment to a length the
  // adaptive stop may cut short.
  return t('onboarding.perTargetDiagnostic.progress', {
    current: totalSoFar.value + 1,
    max: block.value.ceilingCap,
  })
})

// Expose a progress percentage that anchors against the FLOOR cap so the
// bar doesn't "rewind" when the block converges early; it saturates at
// 100% once FloorCap items have been served.
const progressPercent = computed(() => {
  if (!block.value)
    return 0
  return Math.min(100, Math.round((totalSoFar.value / block.value.floorCap) * 100))
})

// ─── Lifecycle ─────────────────────────────────────────────────────────
onMounted(async () => {
  await fetchBlock()
})

async function fetchBlock() {
  loading.value = true
  fetchError.value = null
  try {
    const res = await $api<PerTargetBlockResponse>(
      `/api/diagnostic/per-target/${encodeURIComponent(props.examTargetCode)}/block`,
      { credentials: 'include' },
    )
    block.value = res
    if (!res.items || res.items.length === 0) {
      // Empty catalog for this target — nothing to calibrate. Treat as a
      // "skip" outcome so the onboarding flow can move on honestly.
      emit('block-skip-target', { examTargetCode: props.examTargetCode })
      return
    }
  }
  catch (err) {
    fetchError.value = (err as Error).message
      ?? t('onboarding.perTargetDiagnostic.fetchFailed')
  }
  finally {
    loading.value = false
  }
}

// ─── Adaptive-stop logic (mirrors DiagnosticBlockSelector.cs) ──────────
// Kept in sync with the server's DiagnosticBlockThresholds constants. We
// hard-code them here rather than threading through the server response
// so the UX math is reviewable in one place.
const MIN_ITEMS_BEFORE_STOP = 4
const CONVERGENCE_BAND = 0.25

function updatePosteriorLocally(prior: number, correct: boolean, skipped: boolean) {
  if (skipped)
    return prior

  // Mirrors src/actors/Cena.Actors/Mastery/BktParameters.cs (ADR-0039 Koedinger defaults).
  // These MUST stay in sync with the backend or the adaptive-stop band
  // will diverge between the client preview and the server's authoritative
  // BKT update.
  const pSlip = 0.10
  const pGuess = 0.15
  const pLearn = 0.15

  let evidencePosterior: number
  if (correct) {
    const num = prior * (1 - pSlip)
    const den = num + (1 - prior) * pGuess
    evidencePosterior = den > 0 ? num / den : prior
  }
  else {
    const num = prior * pSlip
    const den = num + (1 - prior) * (1 - pGuess)
    evidencePosterior = den > 0 ? num / den : prior
  }
  const posteriorAfter = evidencePosterior + (1 - evidencePosterior) * pLearn
  return Math.max(0, Math.min(1, posteriorAfter))
}

function shouldStop(served: number, p: number) {
  if (!block.value)
    return false
  if (served >= block.value.ceilingCap)
    return true
  if (served < MIN_ITEMS_BEFORE_STOP)
    return false
  const converged = Math.abs(p - 0.5) >= CONVERGENCE_BAND
  return converged && served >= MIN_ITEMS_BEFORE_STOP
}

// ─── Answering ─────────────────────────────────────────────────────────
function selectOption(key: string) {
  selectedKey.value = key
}

function submitCurrent() {
  if (!currentItem.value || !selectedKey.value)
    return

  const q = currentItem.value
  const correct = selectedKey.value === q.correctOptionKey

  responses.value.push({
    itemId: q.itemId,
    skillCode: q.skillCode,
    correct,
    skipped: false,
    difficultyIrt: q.difficultyIrt,
  })

  posterior.value = updatePosteriorLocally(posterior.value, correct, false)

  advanceOrFinish()
}

function skipCurrent() {
  if (!currentItem.value)
    return

  const q = currentItem.value
  responses.value.push({
    itemId: q.itemId,
    skillCode: q.skillCode,
    correct: false,
    skipped: true,
    difficultyIrt: q.difficultyIrt,
  })
  // posterior unchanged on skip

  advanceOrFinish()
}

function advanceOrFinish() {
  selectedKey.value = null

  if (!block.value)
    return
  if (shouldStop(responses.value.length, posterior.value)) {
    showFeelingCapture.value = true
    return
  }
  if (currentIndex.value < block.value.items.length - 1) {
    currentIndex.value++
    return
  }
  // Ran out of items before converging — still move to feeling capture.
  showFeelingCapture.value = true
}

function recordFeeling(feeling: 'Solid' | 'Unsure' | 'Anxious' | 'New') {
  topicFeeling.value = feeling
  finish()
}

function skipFeeling() {
  topicFeeling.value = null
  finish()
}

async function finish() {
  // Fire the submit POST best-effort — a server error here should NOT
  // block the onboarding UX; the summary is still useful on the client
  // side for the block-complete event downstream consumers.
  try {
    await $api(
      `/api/diagnostic/per-target/${encodeURIComponent(props.examTargetCode)}/block/submit`,
      {
        method: 'POST',
        body: { responses: responses.value },
      },
    )
  }
  catch (err) {
    console.warn('[PER-TARGET-DIAG] submit failed (non-blocking):', err)
  }

  emit('block-complete', {
    examTargetCode: props.examTargetCode,
    responses: responses.value,
    topicFeeling: topicFeeling.value,
  })
}

function skipEntireTarget() {
  emit('block-skip-target', { examTargetCode: props.examTargetCode })
}
</script>

<template>
  <div
    data-testid="per-target-diagnostic-quiz"
    class="per-target-diagnostic-quiz"
  >
    <!-- Header: target name + calibration framing -->
    <div class="mb-4">
      <p class="text-caption text-medium-emphasis mb-1">
        <span v-if="blockTotal && blockTotal > 1">
          {{ t('onboarding.perTargetDiagnostic.blockCounter', { current: (blockIndex ?? 0) + 1, total: blockTotal }) }}
        </span>
      </p>
      <h3 class="text-h6 mb-1">
        {{ t('onboarding.perTargetDiagnostic.title', { target: targetLabel }) }}
      </h3>
      <p class="text-body-2 text-medium-emphasis">
        {{ t('onboarding.perTargetDiagnostic.calibrationFraming') }}
      </p>
    </div>

    <!-- Loading -->
    <div
      v-if="loading"
      class="text-center py-8"
      data-testid="per-target-diagnostic-loading"
    >
      <VProgressCircular
        indeterminate
        color="primary"
        size="48"
      />
      <p class="text-body-2 mt-4 text-medium-emphasis">
        {{ t('onboarding.perTargetDiagnostic.loading') }}
      </p>
    </div>

    <!-- Fetch error -->
    <VAlert
      v-else-if="fetchError"
      type="error"
      variant="tonal"
      class="mb-4"
      data-testid="per-target-diagnostic-error"
    >
      {{ fetchError }}
    </VAlert>

    <!-- Feeling capture (RDY-057 TopicFeeling reuse) -->
    <template v-else-if="showFeelingCapture">
      <p class="text-body-2 mb-4">
        {{ t('onboarding.perTargetDiagnostic.feelingPrompt', { target: targetLabel }) }}
      </p>
      <div
        role="radiogroup"
        :aria-label="t('onboarding.perTargetDiagnostic.feelingAria')"
        class="d-flex flex-column ga-2 mb-4"
      >
        <VBtn
          v-for="f in (['Solid', 'Unsure', 'Anxious', 'New'] as const)"
          :key="f"
          variant="outlined"
          :data-testid="`per-target-feeling-${f.toLowerCase()}`"
          @click="recordFeeling(f)"
        >
          {{ t(`onboarding.perTargetDiagnostic.feeling.${f.toLowerCase()}`) }}
        </VBtn>
      </div>
      <VBtn
        variant="text"
        size="small"
        data-testid="per-target-feeling-skip"
        @click="skipFeeling"
      >
        {{ t('onboarding.perTargetDiagnostic.feelingSkip') }}
      </VBtn>
    </template>

    <!-- Active item -->
    <template v-else-if="currentItem">
      <!-- Progress bar -->
      <div class="d-flex align-center justify-space-between mb-4">
        <VProgressLinear
          :model-value="progressPercent"
          color="primary"
          height="8"
          rounded
          class="flex-grow-1 me-4"
          role="progressbar"
          :aria-label="progressLabel"
          :aria-valuenow="progressPercent"
          aria-valuemin="0"
          aria-valuemax="100"
          data-testid="per-target-diagnostic-progress"
        />
        <VChip
          variant="tonal"
          size="small"
          data-testid="per-target-progress-chip"
        >
          {{ progressLabel }}
        </VChip>
      </div>

      <!-- Provenance label (ADR-0043 stamping) -->
      <p
        class="text-caption text-medium-emphasis mb-2"
        data-testid="per-target-provenance-label"
      >
        {{ t(currentItem.provenanceLabel) }}
      </p>

      <!-- Question -->
      <h4
        class="text-body-1 mb-4"
        data-testid="per-target-question-text"
      >
        {{ currentItem.questionText }}
      </h4>

      <!-- Options -->
      <div
        role="radiogroup"
        :aria-label="t('onboarding.perTargetDiagnostic.optionsLabel')"
        class="d-flex flex-column ga-2 mb-4"
      >
        <VCard
          v-for="opt in currentItem.options"
          :key="opt.key"
          :variant="selectedKey === opt.key ? 'flat' : 'outlined'"
          :color="selectedKey === opt.key ? 'primary' : undefined"
          class="pa-3 cursor-pointer"
          role="radio"
          :aria-checked="selectedKey === opt.key"
          tabindex="0"
          :data-testid="`per-target-option-${opt.key}`"
          @click="selectOption(opt.key)"
          @keydown.enter.prevent="selectOption(opt.key)"
          @keydown.space.prevent="selectOption(opt.key)"
        >
          <span class="text-body-1">{{ opt.text }}</span>
        </VCard>
      </div>

      <!-- Action row — skip is always visible -->
      <div class="d-flex align-center justify-space-between flex-wrap ga-2">
        <div class="d-flex ga-2 flex-wrap">
          <VBtn
            variant="text"
            size="small"
            data-testid="per-target-skip-item"
            @click="skipCurrent"
          >
            {{ t('onboarding.perTargetDiagnostic.skipItem') }}
          </VBtn>
          <VBtn
            variant="text"
            size="small"
            color="warning"
            data-testid="per-target-skip-target"
            @click="skipEntireTarget"
          >
            {{ t('onboarding.perTargetDiagnostic.skipTarget') }}
          </VBtn>
        </div>
        <VBtn
          color="primary"
          :disabled="!selectedKey"
          data-testid="per-target-submit"
          @click="submitCurrent"
        >
          {{ t('onboarding.perTargetDiagnostic.submit') }}
        </VBtn>
      </div>
    </template>
  </div>
</template>

<style scoped>
.per-target-diagnostic-quiz {
  min-block-size: 360px;
}

.cursor-pointer {
  cursor: pointer;
}
</style>
