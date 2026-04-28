<script setup lang="ts">
// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) runner page.
//
// Drives a single exam-prep run from start to submit. Two sections:
//   * Part A — every question is mandatory; ordered list, free-text input.
//   * Part B — student picks K of M (real Bagrut "choose subset" rule); only
//              picks count toward the mark sheet.
//
// Server-authoritative timer: the runner displays the remaining time
// computed against `state.deadline` from the server. The timer cannot
// reach back into the run state — it only renders. If the server
// reports `isExpired`, the submit button auto-fires (last-write-wins;
// idempotent on the server).
//
// Privacy / safety:
//   - No hints, no per-question feedback during the run (ExamFormat
//     invariant, mirrors Ministry exam day).
//   - No streak / loss-aversion copy (GD-004).
//   - All math LTR-isolated in <bdi>.
// =============================================================================

import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import {
  getMockExamRunState,
  selectMockExamPartB,
  submitMockExamAnswer,
  submitMockExamRun,
} from '@/api/exam-prep'
import type { MockExamRunStateResponse } from '@/api/types/exam-prep'

definePage({
  meta: {
    layout: 'blank',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'examPrep.runner.title',
    hideSidebar: true,
    breadcrumbs: false,
  },
})

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const runId = computed(() => String(route.params.runId))

const state = ref<MockExamRunStateResponse | null>(null)
const answers = ref<Record<string, string>>({})
const partBPicked = ref<string[]>([])
const submitting = ref(false)
const error = ref<string | null>(null)

// Timer — recomputed every second from `deadline` so we don't drift.
const now = ref(Date.now())
let tickInterval: ReturnType<typeof setInterval> | null = null

const partBRequired = computed(() => {
  const examCode = state.value?.examCode
  // Mirror server-side ExamFormat — keeps the UI usable if state.value is
  // briefly null between fetches.
  if (examCode === '036') return 3
  return 2
})

const remainingMs = computed(() => {
  if (!state.value) return 0
  return Math.max(0, new Date(state.value.deadline).getTime() - now.value)
})

const remainingDisplay = computed(() => {
  const total = Math.floor(remainingMs.value / 1000)
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
})

const partBSelectionLocked = computed(() =>
  (state.value?.partBSelectedIds.length ?? 0) === partBRequired.value)

const allActiveQids = computed(() => {
  if (!state.value) return [] as string[]
  const partB = state.value.partBSelectedIds.length > 0
    ? state.value.partBSelectedIds
    : state.value.partBQuestionIds
  return [...state.value.partAQuestionIds, ...partB]
})

const answeredCount = computed(() =>
  Object.values(answers.value).filter(v => v.trim().length > 0).length)

async function loadState() {
  try {
    const s = await getMockExamRunState(runId.value)
    state.value = s
    if (s.isSubmitted) {
      await router.replace(`/exam-prep/${runId.value}/result`)
    }
  }
  catch {
    error.value = t('examPrep.errors.notFound')
  }
}

async function lockPartB() {
  if (partBPicked.value.length !== partBRequired.value) {
    error.value = t('examPrep.errors.pickPartB', { required: partBRequired.value })
    return
  }
  error.value = null
  try {
    state.value = await selectMockExamPartB(runId.value, {
      selectedQuestionIds: partBPicked.value,
    })
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.partBFailed')
  }
}

async function saveAnswer(qid: string) {
  const value = (answers.value[qid] ?? '').trim()
  if (!value) return
  try {
    state.value = await submitMockExamAnswer(runId.value, {
      questionId: qid,
      answer: value,
    })
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.answerFailed')
  }
}

async function submitRun() {
  if (submitting.value) return
  submitting.value = true
  try {
    // Last-chance flush of any pending answers in case onBlur didn't fire.
    for (const qid of allActiveQids.value) {
      const val = (answers.value[qid] ?? '').trim()
      if (val && !state.value?.answeredIds.includes(qid))
        await submitMockExamAnswer(runId.value, { questionId: qid, answer: val })
    }
    await submitMockExamRun(runId.value)
    await router.push(`/exam-prep/${runId.value}/result`)
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.submitFailed')
    submitting.value = false
  }
}

onMounted(async () => {
  await loadState()
  tickInterval = setInterval(() => {
    now.value = Date.now()
    if (remainingMs.value === 0 && !submitting.value && !state.value?.isSubmitted) {
      // Server-truth: deadline passed. Auto-submit (idempotent).
      submitRun()
    }
  }, 1000)
})

onBeforeUnmount(() => {
  if (tickInterval) clearInterval(tickInterval)
})
</script>

<template>
  <VContainer v-if="state" data-testid="exam-prep-runner">
    <VRow class="align-center">
      <VCol cols="12" md="8">
        <h1 class="text-h5">
          {{ t('examPrep.runner.heading') }}
          <bdi dir="ltr" class="text-medium-emphasis ms-2">{{ state.examCode }}</bdi>
          <bdi v-if="state.paperCode" dir="ltr" class="text-medium-emphasis ms-1">/ {{ state.paperCode }}</bdi>
        </h1>
      </VCol>
      <VCol cols="12" md="4" class="d-flex justify-end">
        <VChip
          :color="remainingMs < 60_000 ? 'warning' : 'primary'"
          variant="tonal"
          size="large"
          data-testid="exam-prep-timer"
        >
          <bdi dir="ltr">{{ remainingDisplay }}</bdi>
        </VChip>
      </VCol>
    </VRow>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mt-4"
      data-testid="exam-prep-runner-error"
    >
      {{ error }}
    </VAlert>

    <!-- Part A — all mandatory -->
    <VCard class="mt-4">
      <VCardTitle>{{ t('examPrep.runner.partA') }}</VCardTitle>
      <VCardText>
        <div
          v-for="(qid, i) in state.partAQuestionIds"
          :key="qid"
          class="mb-4"
          :data-testid="`exam-prep-q-${qid}`"
        >
          <p class="text-subtitle-1 mb-2">
            {{ t('examPrep.runner.questionLabel', { n: i + 1 }) }}
            <bdi dir="ltr" class="text-caption text-medium-emphasis ms-2">{{ qid }}</bdi>
          </p>
          <VTextField
            v-model="answers[qid]"
            :label="t('examPrep.runner.answerLabel')"
            variant="outlined"
            density="comfortable"
            :data-testid="`exam-prep-a-${qid}`"
            @blur="saveAnswer(qid)"
          />
        </div>
      </VCardText>
    </VCard>

    <!-- Part B — choose K of M -->
    <VCard class="mt-4">
      <VCardTitle>
        {{ t('examPrep.runner.partB') }}
        <span class="text-caption text-medium-emphasis ms-2">
          {{ t('examPrep.runner.chooseN', { required: partBRequired, total: state.partBQuestionIds.length }) }}
        </span>
      </VCardTitle>
      <VCardText>
        <div v-if="!partBSelectionLocked" data-testid="exam-prep-part-b-picker">
          <p class="text-body-2 mb-2">{{ t('examPrep.runner.partBPickerHelp') }}</p>
          <VCheckbox
            v-for="qid in state.partBQuestionIds"
            :key="qid"
            :value="qid"
            :model-value="partBPicked"
            :label="qid"
            :data-testid="`exam-prep-pick-${qid}`"
            @update:model-value="(v: unknown) => partBPicked = (v as string[]) ?? []"
          />
          <VBtn
            color="primary"
            :disabled="partBPicked.length !== partBRequired"
            class="mt-2"
            data-testid="exam-prep-part-b-confirm"
            @click="lockPartB"
          >
            {{ t('examPrep.runner.confirmPartB') }}
          </VBtn>
        </div>

        <div v-else data-testid="exam-prep-part-b-runner">
          <div
            v-for="(qid, i) in state.partBSelectedIds"
            :key="qid"
            class="mb-4"
            :data-testid="`exam-prep-q-${qid}`"
          >
            <p class="text-subtitle-1 mb-2">
              {{ t('examPrep.runner.partBQuestionLabel', { n: i + 1 }) }}
              <bdi dir="ltr" class="text-caption text-medium-emphasis ms-2">{{ qid }}</bdi>
            </p>
            <VTextField
              v-model="answers[qid]"
              :label="t('examPrep.runner.answerLabel')"
              variant="outlined"
              density="comfortable"
              :data-testid="`exam-prep-a-${qid}`"
              @blur="saveAnswer(qid)"
            />
          </div>
        </div>
      </VCardText>
    </VCard>

    <!-- Submit -->
    <VRow class="mt-4">
      <VCol cols="12" md="6">
        <p class="text-body-2 text-medium-emphasis">
          {{ t('examPrep.runner.progress', {
            answered: answeredCount,
            total: allActiveQids.length,
          }) }}
        </p>
      </VCol>
      <VCol cols="12" md="6" class="d-flex justify-end">
        <VBtn
          color="success"
          size="large"
          :loading="submitting"
          :disabled="!partBSelectionLocked"
          data-testid="exam-prep-submit-btn"
          @click="submitRun"
        >
          {{ t('examPrep.runner.submitButton') }}
        </VBtn>
      </VCol>
    </VRow>
  </VContainer>

  <VContainer v-else>
    <VProgressLinear indeterminate color="primary" />
  </VContainer>
</template>
