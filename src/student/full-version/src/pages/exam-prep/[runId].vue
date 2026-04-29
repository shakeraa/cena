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
  reportMockExamVisibility,
  selectMockExamPartB,
  submitMockExamAnswer,
  submitMockExamAnswersBulk,
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
// answers keyed on bare qid (single-cell) OR composite "qid:partId" (multi-part).
const answers = ref<Record<string, string>>({})
const previews = ref<Record<string, ExamPrepQuestionPreview>>({})
const partBPicked = ref<string[]>([])
const submitting = ref(false)
// Phase 3 #6 — separate flag for the auto-submit-on-deadline path so
// the SPA can render a distinct "Time's up — submitting..." banner.
const autoSubmitting = ref(false)
const error = ref<string | null>(null)

// Per-key debounce timers + dirty set so the unload guard knows whether
// to prompt. Keys are the same composite scheme as `answers`.
const debounceTimers: Record<string, ReturnType<typeof setTimeout>> = {}
const dirty = ref<Set<string>>(new Set())

function answerKeyFor(qid: string, subpartId?: string): string {
  return subpartId ? `${qid}:${subpartId}` : qid
}

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

async function persistAnswer(qid: string, subpartId?: string) {
  const key = answerKeyFor(qid, subpartId)
  const value = (answers.value[key] ?? '').trim()
  if (!value) return
  try {
    state.value = await submitMockExamAnswer(runId.value, {
      questionId: qid,
      answer: value,
      subpartId,
    })
    dirty.value.delete(key)
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.answerFailed')
  }
}

function onInput(qid: string, subpartId?: string) {
  const key = answerKeyFor(qid, subpartId)
  dirty.value.add(key)
  if (debounceTimers[key]) clearTimeout(debounceTimers[key])
  debounceTimers[key] = setTimeout(() => persistAnswer(qid, subpartId), 600)
}

async function onBlur(qid: string, subpartId?: string) {
  const key = answerKeyFor(qid, subpartId)
  if (debounceTimers[key]) clearTimeout(debounceTimers[key])
  await persistAnswer(qid, subpartId)
}

// PRR-281 — empty-submit confirmation modal. If the student tries to
// manually submit with zero answers (everything blank), prompt before
// proceeding. Auto-submit on deadline bypasses (real exam day: time
// expired = submit whatever you have).
const showEmptyConfirmDialog = ref(false)
const pendingSubmitOpts = ref<{ auto?: boolean }>({})

const totalAnsweredCount = computed(() => {
  // Counts both single-cell + per-subpart answers (composite keys
  // like 'qid:subpartId' are stored on the same Answers dict).
  if (!state.value) return 0
  return state.value.answeredIds.length + Object.entries(answers.value)
    .filter(([k, v]) => v.trim().length > 0 && !state.value!.answeredIds.includes(k))
    .length
})

async function submitRun(opts: { auto?: boolean } = {}) {
  if (submitting.value) return
  // PRR-281 — guard manual submits against a fully-blank run.
  if (!opts.auto && totalAnsweredCount.value === 0) {
    pendingSubmitOpts.value = opts
    showEmptyConfirmDialog.value = true
    return
  }
  submitting.value = true
  if (opts.auto) autoSubmitting.value = true
  try {
    // Cancel pending debounce timers.
    for (const key of Object.keys(debounceTimers)) {
      clearTimeout(debounceTimers[key])
    }
    // Phase 3 #8 — bulk-flush every dirty key in ONE round-trip.
    const pending: { questionId: string; answer: string; subpartId?: string }[] = []
    for (const key of [...dirty.value]) {
      const val = (answers.value[key] ?? '').trim()
      if (!val) continue
      const sepIdx = key.indexOf(':')
      const qid = sepIdx >= 0 ? key.slice(0, sepIdx) : key
      const subpartId = sepIdx >= 0 ? key.slice(sepIdx + 1) : undefined
      pending.push({ questionId: qid, answer: val, subpartId })
    }
    if (pending.length > 0)
      await submitMockExamAnswersBulk(runId.value, pending)
    await submitMockExamRun(runId.value)
    dirty.value.clear()
    await router.push(`/exam-prep/${runId.value}/result`)
  }
  catch (err: unknown) {
    error.value = (err as { data?: { error?: string } })?.data?.error
      ?? t('examPrep.errors.submitFailed')
    submitting.value = false
    autoSubmitting.value = false
  }
}

function beforeUnloadHandler(e: BeforeUnloadEvent) {
  if (dirty.value.size === 0 || state.value?.isSubmitted) return
  e.preventDefault()
  e.returnValue = ''
}

// Phase-4 #1 — Visibility API wiring. Real Ministry exam day cares
// about tab-switches; our state has VisibilityEvents + we emit
// ExamVisibilityWarning_V1. Track the moment the page hides + report
// the duration on visible-again. Best-effort: fire-and-forget so a
// network blip during reporting doesn't break the runner UX.
let lastHiddenAt: number | null = null
function visibilityHandler() {
  if (state.value?.isSubmitted) return
  if (document.visibilityState === 'hidden') {
    lastHiddenAt = Date.now()
    // Report the hide immediately so a long-away student is recorded
    // even if they never come back.
    reportMockExamVisibility(runId.value, 'hidden', 0).catch(() => {})
  }
  else if (document.visibilityState === 'visible' && lastHiddenAt !== null) {
    const dur = Date.now() - lastHiddenAt
    lastHiddenAt = null
    reportMockExamVisibility(runId.value, 'visible', dur).catch(() => {})
  }
}

onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnloadHandler)
  document.addEventListener('visibilitychange', visibilityHandler)
  await loadState()
  tickInterval = setInterval(() => {
    now.value = Date.now()
    if (remainingMs.value === 0 && !submitting.value && !state.value?.isSubmitted) {
      submitRun({ auto: true })
    }
  }, 1000)
})

onBeforeUnmount(() => {
  if (tickInterval) clearInterval(tickInterval)
  window.removeEventListener('beforeunload', beforeUnloadHandler)
  document.removeEventListener('visibilitychange', visibilityHandler)
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

async function confirmEmptySubmit() {
  showEmptyConfirmDialog.value = false
  await submitRun({ ...pendingSubmitOpts.value, auto: true })
}

function cancelEmptySubmit() {
  showEmptyConfirmDialog.value = false
  pendingSubmitOpts.value = {}
}
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
      v-if="autoSubmitting"
      type="warning"
      variant="tonal"
      class="mt-4"
      data-testid="exam-prep-auto-submitting"
    >
      <VProgressCircular indeterminate size="20" width="2" class="me-3" />
      {{ t('examPrep.runner.autoSubmitting') }}
    </VAlert>

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
          <!-- Multi-part Q: one input per subpart. Single-cell: one input. -->
          <template v-if="previews[qid]?.subparts?.length">
            <div
              v-for="sp in previews[qid].subparts"
              :key="`${qid}:${sp.partId}`"
              class="mb-2"
            >
              <p class="text-body-2 mb-1">
                <strong><bdi dir="ltr">({{ sp.partId }})</bdi></strong>
                <span class="ms-2"><bdi dir="ltr">{{ sp.prompt }}</bdi></span>
                <span class="text-caption text-medium-emphasis ms-2">[{{ sp.points }} {{ t('examPrep.runner.ptsLabel') }}]</span>
              </p>
              <VTextField
                v-model="answers[`${qid}:${sp.partId}`]"
                :label="t('examPrep.runner.answerLabel')"
                variant="outlined"
                density="comfortable"
                :data-testid="`exam-prep-a-${qid}-${sp.partId}`"
                @update:model-value="onInput(qid, sp.partId)"
                @blur="onBlur(qid, sp.partId)"
              />
            </div>
          </template>
          <VTextField
            v-else
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
            <template v-if="previews[qid]?.subparts?.length">
              <div
                v-for="sp in previews[qid].subparts"
                :key="`${qid}:${sp.partId}`"
                class="mb-2"
              >
                <p class="text-body-2 mb-1">
                  <strong><bdi dir="ltr">({{ sp.partId }})</bdi></strong>
                  <span class="ms-2"><bdi dir="ltr">{{ sp.prompt }}</bdi></span>
                  <span class="text-caption text-medium-emphasis ms-2">[{{ sp.points }} {{ t('examPrep.runner.ptsLabel') }}]</span>
                </p>
                <VTextField
                  v-model="answers[`${qid}:${sp.partId}`]"
                  :label="t('examPrep.runner.answerLabel')"
                  variant="outlined"
                  density="comfortable"
                  :data-testid="`exam-prep-a-${qid}-${sp.partId}`"
                  @update:model-value="onInput(qid, sp.partId)"
                  @blur="onBlur(qid, sp.partId)"
                />
              </div>
            </template>
            <VTextField
              v-else
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

  <!-- PRR-281 — empty-submit confirmation modal -->
  <VDialog v-model="showEmptyConfirmDialog" max-width="500" data-testid="exam-prep-empty-submit-dialog">
    <VCard>
      <VCardTitle>{{ t('examPrep.runner.emptySubmitTitle') }}</VCardTitle>
      <VCardText>{{ t('examPrep.runner.emptySubmitBody') }}</VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" data-testid="exam-prep-empty-submit-cancel" @click="cancelEmptySubmit">
          {{ t('examPrep.runner.emptySubmitCancel') }}
        </VBtn>
        <VBtn color="warning" variant="flat" data-testid="exam-prep-empty-submit-confirm" @click="confirmEmptySubmit">
          {{ t('examPrep.runner.emptySubmitConfirm') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
