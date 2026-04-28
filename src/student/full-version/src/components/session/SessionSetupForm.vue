<script setup lang="ts">
// PRR-256 / PRR-247 / ADR-0060 — exam-mode discriminator on session start.
//
// The form now emits two extra fields the server validator requires:
//   • `examScope: 'exam-prep' | 'freestyle' | undefined`
//   • `activeExamTargetId: string | undefined` (required iff examScope='exam-prep')
//
// Smart defaults:
//   • If the student has ≥1 active ExamTarget → mode toggle defaults to
//     ExamPrep, target picker defaults to the first active target.
//   • If the student has zero active targets → ExamPrep button is
//     DISABLED (with helper copy), Freestyle is the only choice.
//
// Wire-omit rule (additive contract): when the user picks Freestyle we
// OMIT both fields rather than sending `examScope:'freestyle',
// activeExamTargetId:undefined`. The server treats null/omit as legacy
// freestyle, and omitting keeps the request body compatible with any
// older server still in flight during deploy roll-outs.

import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApiQuery } from '@/composables/useApiQuery'
import type {
  ExamScope,
  ExamTargetDto,
  ExamTargetListDto,
  SessionMode,
  SessionStartRequest,
} from '@/api/types/common'

interface Props {
  loading?: boolean
}

withDefaults(defineProps<Props>(), { loading: false })

const emit = defineEmits<{
  submit: [payload: SessionStartRequest]
}>()

const { t } = useI18n()

const SUBJECTS = ['math', 'physics', 'chemistry', 'biology', 'english', 'history']
const DURATIONS = [5, 10, 15, 30, 45, 60]
const MODES: SessionMode[] = ['practice', 'challenge', 'review', 'diagnostic']

const selectedSubjects = ref<string[]>(['math'])
const durationMinutes = ref<number>(15)
const mode = ref<SessionMode>('practice')

// PRR-256 — exam-target list for the picker. Errors are non-fatal; an
// unreachable /api/me/exam-targets just means we render Freestyle-only.
const targetsQuery = useApiQuery<ExamTargetListDto>('/api/me/exam-targets')

const activeTargets = computed<ExamTargetDto[]>(() => {
  const items = targetsQuery.data.value?.items ?? []
  return items.filter(t => t.isActive)
})

const hasTargets = computed(() => activeTargets.value.length > 0)
const targetsLoading = computed(() => targetsQuery.loading.value && !targetsQuery.data.value)

// Default scope: ExamPrep when the student has at least one active
// target, Freestyle otherwise. Initialised lazily once the API call
// resolves so the user doesn't see a flicker mid-fetch.
const examScope = ref<ExamScope | null>(null)
const activeExamTargetId = ref<string | null>(null)

function applyDefaults() {
  if (examScope.value !== null) return // user has touched the toggle; respect it
  if (hasTargets.value) {
    examScope.value = 'exam-prep'
    activeExamTargetId.value = activeTargets.value[0]?.id ?? null
  }
  else {
    examScope.value = 'freestyle'
    activeExamTargetId.value = null
  }
}

// React to the targets-fetch settling.
import { watchEffect } from 'vue'
watchEffect(() => {
  if (!targetsLoading.value)
    applyDefaults()
})

function setExamScope(next: ExamScope) {
  examScope.value = next
  if (next === 'exam-prep') {
    if (!activeExamTargetId.value && activeTargets.value.length > 0)
      activeExamTargetId.value = activeTargets.value[0].id
  }
  else {
    // Freestyle MUST omit the target — server validator rejects otherwise.
    activeExamTargetId.value = null
  }
}

function isSubjectSelected(subject: string): boolean {
  return selectedSubjects.value.includes(subject)
}

function toggleSubject(subject: string) {
  const idx = selectedSubjects.value.indexOf(subject)
  if (idx >= 0)
    selectedSubjects.value.splice(idx, 1)
  else
    selectedSubjects.value.push(subject)
}

function onSubjectKeydown(event: KeyboardEvent, subject: string) {
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    toggleSubject(subject)
  }
}

function targetLabel(target: ExamTargetDto): string {
  // Bagrut-style examCode like 'bagrut-math-5u' — render as-is for now.
  // The שאלון codes (numeric, e.g. 35581/36991) live in QuestionPaperCodes
  // and are wrapped in <bdi dir="ltr"> at render time per memory
  // `feedback_math_always_ltr`.
  return target.examCode
}

function paperCodesLabel(target: ExamTargetDto): string {
  return (target.questionPaperCodes ?? []).join(' • ')
}

// Test-time exposure: SessionSetupForm.spec.ts drives the toggle via the
// composable rather than VBtnToggle clicks (Vuetify components-under-jsdom
// are flaky on synthesized clicks). Production callers don't read these.
defineExpose({ setExamScope, examScope, activeExamTargetId })

function handleSubmit() {
  if (selectedSubjects.value.length === 0)
    return

  // Build the payload with omit-when-undefined semantics for the two
  // optional fields. We DO NOT include `examScope: undefined` in the
  // serialized JSON (TS spread of `undefined` is a no-op for JSON, but
  // we make it explicit by branching).
  const base: SessionStartRequest = {
    subjects: [...selectedSubjects.value],
    durationMinutes: durationMinutes.value,
    mode: mode.value,
  }
  if (examScope.value === 'exam-prep' && activeExamTargetId.value) {
    base.examScope = 'exam-prep'
    base.activeExamTargetId = activeExamTargetId.value
  }
  else if (examScope.value === 'freestyle') {
    base.examScope = 'freestyle'
    // activeExamTargetId stays omitted
  }

  emit('submit', base)
}
</script>

<template>
  <form
    class="session-setup-form"
    data-testid="session-setup-form"
    @submit.prevent="handleSubmit"
  >
    <!-- PRR-256: ExamPrep / Freestyle mode toggle. Renders BEFORE subjects
         so the question pool's filter shape is the first thing the
         student commits to. -->
    <section
      class="mb-6"
      data-testid="setup-exam-scope-section"
    >
      <div
        id="exam-scope-group-label"
        class="text-subtitle-1 mb-3"
      >
        {{ t('session.setup.examScopeLabel') }}
      </div>
      <VBtnToggle
        :model-value="examScope"
        mandatory
        color="primary"
        variant="outlined"
        divided
        :aria-labelledby="'exam-scope-group-label'"
        data-testid="setup-exam-scope"
        class="flex-wrap ga-1"
        @update:model-value="(v: ExamScope | null) => v && setExamScope(v)"
      >
        <VBtn
          value="exam-prep"
          :disabled="!hasTargets && !targetsLoading"
          :aria-label="t('session.setup.examScope.examPrepAria')"
          data-testid="setup-exam-scope-exam-prep"
        >
          {{ t('session.setup.examScope.examPrep') }}
        </VBtn>
        <VBtn
          value="freestyle"
          :aria-label="t('session.setup.examScope.freestyleAria')"
          data-testid="setup-exam-scope-freestyle"
        >
          {{ t('session.setup.examScope.freestyle') }}
        </VBtn>
      </VBtnToggle>

      <!-- Helper copy is purely informative — no countdown, no streak,
           no time-pressure. ADR-0048-compliant. -->
      <p
        v-if="examScope === 'exam-prep' && hasTargets"
        class="text-caption text-medium-emphasis mt-2"
        data-testid="setup-exam-scope-helper"
      >
        {{ t('session.setup.examScope.examPrepHelper') }}
      </p>
      <p
        v-else-if="examScope === 'freestyle'"
        class="text-caption text-medium-emphasis mt-2"
        data-testid="setup-exam-scope-helper"
      >
        {{ t('session.setup.examScope.freestyleHelper') }}
      </p>
      <p
        v-else-if="!hasTargets && !targetsLoading"
        class="text-caption text-medium-emphasis mt-2"
        data-testid="setup-exam-scope-no-targets"
      >
        {{ t('session.setup.examScope.noTargetsHelper') }}
      </p>
    </section>

    <!-- Active-target picker — only when ExamPrep is selected and at
         least one target exists. שאלון codes are wrapped in
         <bdi dir="ltr"> so RTL pages render the digit groups left-to-
         right (memory: feedback_math_always_ltr). -->
    <section
      v-if="examScope === 'exam-prep' && hasTargets"
      class="mb-6"
      data-testid="setup-target-section"
    >
      <div
        id="target-group-label"
        class="text-subtitle-1 mb-3"
      >
        {{ t('session.setup.targetLabel') }}
      </div>
      <VSelect
        :model-value="activeExamTargetId"
        :items="activeTargets"
        :item-title="targetLabel"
        item-value="id"
        :aria-label="t('session.setup.targetAria')"
        variant="outlined"
        density="comfortable"
        data-testid="setup-target-select"
        @update:model-value="(v: string | null) => activeExamTargetId = v"
      >
        <template #selection="{ item }">
          <span data-testid="setup-target-selected">
            {{ targetLabel(item.raw) }}
            <span
              v-if="paperCodesLabel(item.raw)"
              class="text-caption text-medium-emphasis ms-1"
            >
              <bdi dir="ltr">{{ paperCodesLabel(item.raw) }}</bdi>
            </span>
          </span>
        </template>
        <template #item="{ props: itemProps, item }">
          <VListItem
            v-bind="itemProps"
            :data-testid="`setup-target-option-${item.raw.id}`"
            :title="targetLabel(item.raw)"
          >
            <template #subtitle>
              <bdi dir="ltr">{{ paperCodesLabel(item.raw) }}</bdi>
            </template>
          </VListItem>
        </template>
      </VSelect>
    </section>

    <section class="mb-6">
      <div
        id="subject-group-label"
        class="text-subtitle-1 mb-3"
      >
        {{ t('session.setup.subjectsLabel') }}
      </div>
      <div
        role="group"
        :aria-label="t('session.setup.subjectChipGroupLabel')"
        aria-labelledby="subject-group-label"
        class="d-flex flex-wrap ga-2"
        data-testid="setup-subjects"
      >
        <VChip
          v-for="s in SUBJECTS"
          :key="s"
          role="button"
          :aria-pressed="isSubjectSelected(s)"
          :aria-label="t(`session.setup.subjects.${s}`)"
          :color="isSubjectSelected(s) ? 'primary' : undefined"
          :variant="isSubjectSelected(s) ? 'flat' : 'outlined'"
          :data-testid="`setup-subject-${s}`"
          size="default"
          tabindex="0"
          @click="toggleSubject(s)"
          @keydown="onSubjectKeydown($event, s)"
        >
          {{ t(`session.setup.subjects.${s}`) }}
        </VChip>
      </div>
    </section>

    <section class="mb-6">
      <div class="text-subtitle-1 mb-3">
        {{ t('session.setup.durationLabel') }}
      </div>
      <VBtnToggle
        v-model="durationMinutes"
        mandatory
        color="primary"
        variant="outlined"
        divided
        data-testid="setup-duration"
        class="flex-wrap ga-1"
      >
        <VBtn
          v-for="d in DURATIONS"
          :key="d"
          :value="d"
          :data-testid="`setup-duration-${d}`"
        >
          <bdi>{{ t('session.setup.durationMinutes', { minutes: d }, { plural: d }) }}</bdi>
        </VBtn>
      </VBtnToggle>
    </section>

    <section class="mb-6">
      <div class="text-subtitle-1 mb-3">
        {{ t('session.setup.modeLabel') }}
      </div>
      <VBtnToggle
        v-model="mode"
        mandatory
        color="primary"
        variant="outlined"
        divided
        data-testid="setup-mode"
        class="flex-wrap ga-1"
      >
        <VBtn
          v-for="m in MODES"
          :key="m"
          :value="m"
          :data-testid="`setup-mode-${m}`"
        >
          {{ t(`session.setup.modes.${m}`) }}
        </VBtn>
      </VBtnToggle>
    </section>

    <VBtn
      type="submit"
      color="primary"
      size="large"
      block
      :loading="loading"
      :disabled="loading || selectedSubjects.length === 0 || (examScope === 'exam-prep' && !activeExamTargetId)"
      prepend-icon="tabler-player-play"
      data-testid="setup-start"
    >
      {{ t('session.setup.startCta') }}
    </VBtn>
  </form>
</template>
