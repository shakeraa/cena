<script setup lang="ts">
// =============================================================================
// Cena Platform — Mock-exam (Bagrut שאלון playbook) runner page.
// Phase 1D: Part-B preview + per-input draft autosave + unload guard.
//
// Sections:
//   * Part A — every question is mandatory; ordered list, free-text input.
//   * Part B — student previews each candidate (prompt + topic + bloom)
//              before locking the K-of-M selection. Once locked, only
//              picks count toward the mark sheet.
//
// UX hardening:
//   * Per-question text-field auto-saves on input via a 600ms debounce
//     (in addition to onBlur). Refreshing or losing focus mid-keystroke
//     no longer drops the answer.
//   * onbeforeunload guard — if there are unsaved drafts, warn before
//     navigation. Matches real-exam expectation that you don't
//     accidentally leave.
//   * Server-deadline-derived timer (no client drift); auto-submit
//     when deadline elapses (server is idempotent).
// =============================================================================

import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import {
  getMockExamQuestionPreview,
  getMockExamRunState,
  selectMockExamPartB,
  submitMockExamAnswer,
  submitMockExamRun,
} from '@/api/exam-prep'
import type {
  ExamPrepQuestionPreview,
  MockExamRunStateResponse,
} from '@/api/types/exam-prep'

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
const previews = ref<Record<string, ExamPrepQuestionPreview>>({})
const partBPicked = ref<string[]>([])
const submitting = ref(false)
const error = ref<string | null>(null)

// Per-question debounce timers + dirty set so the unload guard knows
// whether to prompt.
const debounceTimers: Record<string, ReturnType<typeof setTimeout>> = {}
const dirty = ref<Set<string>>(new Set())

// Timer
const now = ref(Date.now())
let tickInterval: ReturnType<typeof setInterval> | null = null

const partBRequired = computed(() => {
  const examCode = state.value?.examCode
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
      return
    }
    await loadPreviews()
  }
  catch {
    error.value = t('examPrep.errors.notFound')
  }
}

async function loadPreviews() {
  if (!state.value) return
  const ids = [...state.value.partAQuestionIds, ...state.value.partBQuestionIds]
  // Fan-out individual GETs; the per-Q endpoint is bounded + cached
  // by browser HTTP layer so this scales for the 9-13 Q range we hit.
  const results = await Promise.allSettled(
    ids.map(qid => getMockExamQuestionPreview(runId.value, qid)),
  )
  for (const r of results) {
    if (r.status === 'fulfilled')
      previews.value[r.value.questionId] = r.value
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

async function persistAnswer(qid: string) {
  const value = (answers.value[qid] ?? '').trim()
  if (!value) return
  try {
    state.value = await submitMockExamAnswer(runId.value, {
      questionId: qid,
      answer: value,
    })
    dirty.value.delete(qid)
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.answerFailed')
  }
}

function onInput(qid: string) {
  dirty.value.add(qid)
  if (debounceTimers[qid]) clearTimeout(debounceTimers[qid])
  debounceTimers[qid] = setTimeout(() => persistAnswer(qid), 600)
}

async function onBlur(qid: string) {
  if (debounceTimers[qid]) clearTimeout(debounceTimers[qid])
  await persistAnswer(qid)
}

async function submitRun() {
  if (submitting.value) return
  submitting.value = true
  try {
    // Cancel pending debounce timers + flush any unsaved.
    for (const qid of Object.keys(debounceTimers)) {
      clearTimeout(debounceTimers[qid])
    }
    for (const qid of allActiveQids.value) {
      const val = (answers.value[qid] ?? '').trim()
      if (val && !state.value?.answeredIds.includes(qid))
        await submitMockExamAnswer(runId.value, { questionId: qid, answer: val })
    }
    await submitMockExamRun(runId.value)
    dirty.value.clear()
    await router.push(`/exam-prep/${runId.value}/result`)
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.submitFailed')
    submitting.value = false
  }
}

function beforeUnloadHandler(e: BeforeUnloadEvent) {
  if (dirty.value.size === 0 || state.value?.isSubmitted) return
  e.preventDefault()
  e.returnValue = ''
}

onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnloadHandler)
  await loadState()
  tickInterval = setInterval(() => {
    now.value = Date.now()
    if (remainingMs.value === 0 && !submitting.value && !state.value?.isSubmitted) {
      submitRun()
    }
  }, 1000)
})

onBeforeUnmount(() => {
  if (tickInterval) clearInterval(tickInterval)
  window.removeEventListener('beforeunload', beforeUnloadHandler)
  for (const qid of Object.keys(debounceTimers)) {
    clearTimeout(debounceTimers[qid])
  }
})

// When state.partBSelectedIds changes (after lockPartB), pre-load
// previews for any newly active Q's (in case loadPreviews missed any).
watch(
  () => state.value?.partBSelectedIds,
  () => loadPreviews(),
)
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
          <p class="text-subtitle-1 mb-1">
            {{ t('examPrep.runner.questionLabel', { n: i + 1 }) }}
            <bdi dir="ltr" class="text-caption text-medium-emphasis ms-2">{{ qid }}</bdi>
          </p>
          <p v-if="previews[qid]" class="text-body-2 mb-2">
            <bdi dir="ltr">{{ previews[qid].prompt }}</bdi>
          </p>
          <VTextField
            v-model="answers[qid]"
            :label="t('examPrep.runner.answerLabel')"
            variant="outlined"
            density="comfortable"
            :data-testid="`exam-prep-a-${qid}`"
            @update:model-value="onInput(qid)"
            @blur="onBlur(qid)"
          />
        </div>
      </VCardText>
    </VCard>

    <!-- Part B — choose K of M with preview -->
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
          <VCard
            v-for="qid in state.partBQuestionIds"
            :key="qid"
            variant="outlined"
            class="mb-2"
            :data-testid="`exam-prep-pick-card-${qid}`"
          >
            <VCardText>
              <VCheckbox
                :value="qid"
                :model-value="partBPicked"
                hide-details
                density="compact"
                :data-testid="`exam-prep-pick-${qid}`"
                @update:model-value="(v: unknown) => partBPicked = (v as string[]) ?? []"
              >
                <template #label>
                  <span>
                    <bdi dir="ltr" class="text-caption text-medium-emphasis me-2">{{ qid }}</bdi>
                    <span v-if="previews[qid]" class="text-body-2">
                      <bdi dir="ltr">{{ previews[qid].prompt }}</bdi>
                      <span class="text-caption text-medium-emphasis ms-2">
                        ({{ previews[qid].topic ?? '—' }})
                      </span>
                    </span>
                  </span>
                </template>
              </VCheckbox>
            </VCardText>
          </VCard>
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
            <p class="text-subtitle-1 mb-1">
              {{ t('examPrep.runner.partBQuestionLabel', { n: i + 1 }) }}
              <bdi dir="ltr" class="text-caption text-medium-emphasis ms-2">{{ qid }}</bdi>
            </p>
            <p v-if="previews[qid]" class="text-body-2 mb-2">
              <bdi dir="ltr">{{ previews[qid].prompt }}</bdi>
            </p>
            <VTextField
              v-model="answers[qid]"
              :label="t('examPrep.runner.answerLabel')"
              variant="outlined"
              density="comfortable"
              :data-testid="`exam-prep-a-${qid}`"
              @update:model-value="onInput(qid)"
              @blur="onBlur(qid)"
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
        <p v-if="dirty.size > 0" class="text-caption text-warning">
          {{ t('examPrep.runner.unsavedDrafts', { n: dirty.size }) }}
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
