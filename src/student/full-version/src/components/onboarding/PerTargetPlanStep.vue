<script setup lang="ts">
/**
 * PerTargetPlanStep.vue — prr-221 onboarding step 4 (MVP wiring).
 *
 * Loops the list of selected exam-target drafts from the onboardingStore.
 * For each target, shows:
 *   - Sitting radio list (from catalog's availableSittings).
 *   - Weekly-hours slider 1..40.
 *   - BAGRUT-family only: question-papers multi-select (defaults all-on).
 *
 * Non-Bagrut targets skip the question-paper block. Deferred to follow-ups
 * (file notes in close-out):
 *   - Live total-hours counter with aria-live announcements
 *   - Classroom-override / skip-target path
 *   - VSlider aria-valuetext in locale numerals (PRR-232)
 *   - Free-date picker (banned by persona-a11y VDatePicker kill)
 *
 * Uses native <input type="range"> and <input type="radio"> for MVP
 * accessibility (matches ExamPlanStep.vue precedent).
 */
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  type ExamSittingDraft,
  MAX_WEEKLY_HOURS,
  MIN_WEEKLY_HOURS,
  useOnboardingStore,
} from '@/stores/onboardingStore'

const emit = defineEmits<{
  (e: 'complete'): void
}>()

const { t } = useI18n()
const onboarding = useOnboardingStore()

const targets = computed(() => onboarding.examTargets)

/** Format a sitting for display: "Summer 2026 (Moed A) — 2026-06-15". */
function sittingLabel(s: ExamSittingDraft): string {
  const season = ['summer', 'winter', 'spring', 'autumn'][s.season] ?? 'summer'
  const moed = ['A', 'B', 'C', 'Special'][s.moed] ?? 'A'

  return t('onboarding.perTargetPlan.sittingLabel', {
    season: t(`onboarding.perTargetPlan.season.${season}`),
    year: s.academicYear,
    moed,
    date: s.canonicalDate ?? '',
  })
}

function isSelectedSitting(code: string, sitting: ExamSittingDraft): boolean {
  const t = targets.value.find(x => x.examCode === code)

  return t?.sitting?.sittingCode === sitting.sittingCode
}

function selectSitting(examCode: string, sitting: ExamSittingDraft) {
  onboarding.updateExamTarget(examCode, { sitting })
}

function togglePaper(examCode: string, paperCode: string) {
  const t = targets.value.find(x => x.examCode === examCode)
  if (!t)
    return

  const next = t.questionPaperCodes.includes(paperCode)
    ? t.questionPaperCodes.filter(c => c !== paperCode)
    : [...t.questionPaperCodes, paperCode]

  onboarding.updateExamTarget(examCode, { questionPaperCodes: next })
}

function setWeeklyHours(examCode: string, hours: number) {
  const clamped = Math.max(MIN_WEEKLY_HOURS, Math.min(MAX_WEEKLY_HOURS, Math.round(hours)))

  onboarding.updateExamTarget(examCode, { weeklyHours: clamped })
}

const canContinue = computed(() => onboarding.canAdvance && onboarding.step === 'per-target-plan')

function handleContinue() {
  if (canContinue.value)
    emit('complete')
}
</script>

<template>
  <section
    data-testid="onboarding-step-per-target-plan"
    role="form"
    :aria-label="t('onboarding.perTargetPlan.title')"
  >
    <h2 class="text-h5 mb-1">
      {{ t('onboarding.perTargetPlan.title') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-5">
      {{ t('onboarding.perTargetPlan.subtitle') }}
    </p>

    <div
      v-for="(target, idx) in targets"
      :key="target.examCode"
      class="per-target-block mb-6"
      :data-testid="`per-target-block-${target.examCode}`"
    >
      <h3 class="text-h6 mb-2">
        {{ t('onboarding.perTargetPlan.targetHeader', {
          current: idx + 1,
          total: targets.length,
          name: target.displayName,
        }) }}
      </h3>

      <!-- Sitting picker -->
      <fieldset class="mb-4 per-target-fieldset">
        <legend class="text-subtitle-2 mb-2">
          {{ t('onboarding.perTargetPlan.sittingLegend') }}
        </legend>
        <div
          v-if="target.availableSittings.length === 0"
          class="text-body-2 text-medium-emphasis"
          :data-testid="`per-target-no-sittings-${target.examCode}`"
        >
          {{ t('onboarding.perTargetPlan.noSittings') }}
        </div>
        <label
          v-for="sitting in target.availableSittings"
          :key="sitting.sittingCode"
          class="d-flex align-center ga-2 mb-1 cursor-pointer"
          :data-testid="`per-target-sitting-${target.examCode}-${sitting.sittingCode}`"
        >
          <input
            type="radio"
            :name="`sitting-${target.examCode}`"
            :checked="isSelectedSitting(target.examCode, sitting)"
            @change="selectSitting(target.examCode, sitting)"
          >
          <bdi dir="ltr">{{ sittingLabel(sitting) }}</bdi>
        </label>
      </fieldset>

      <!-- Bagrut שאלון multi-pick -->
      <fieldset
        v-if="target.family === 'BAGRUT' && target.availableQuestionPaperCodes.length > 0"
        class="mb-4 per-target-fieldset"
      >
        <legend class="text-subtitle-2 mb-2">
          {{ t('onboarding.perTargetPlan.papersLegend') }}
        </legend>
        <label
          v-for="paper in target.availableQuestionPaperCodes"
          :key="paper"
          class="d-flex align-center ga-2 mb-1 cursor-pointer"
          :data-testid="`per-target-paper-${target.examCode}-${paper}`"
        >
          <input
            type="checkbox"
            :checked="target.questionPaperCodes.includes(paper)"
            @change="togglePaper(target.examCode, paper)"
          >
          <bdi dir="ltr">{{ paper }}</bdi>
        </label>
      </fieldset>

      <!-- Weekly hours slider -->
      <div class="mb-2">
        <label
          :for="`weekly-${target.examCode}`"
          class="text-subtitle-2 d-block mb-1"
        >
          {{ t('onboarding.perTargetPlan.weeklyLabel') }}
          <bdi dir="ltr">({{ target.weeklyHours }}h)</bdi>
        </label>
        <input
          :id="`weekly-${target.examCode}`"
          :value="target.weeklyHours"
          type="range"
          :min="MIN_WEEKLY_HOURS"
          :max="MAX_WEEKLY_HOURS"
          step="1"
          class="per-target-range"
          role="slider"
          :aria-valuemin="MIN_WEEKLY_HOURS"
          :aria-valuemax="MAX_WEEKLY_HOURS"
          :aria-valuenow="target.weeklyHours"
          :aria-label="t('onboarding.perTargetPlan.weeklyLabel')"
          :data-testid="`per-target-weekly-${target.examCode}`"
          @input="setWeeklyHours(target.examCode, Number(($event.target as HTMLInputElement).value))"
        >
      </div>
    </div>

    <div class="d-flex justify-end">
      <VBtn
        color="primary"
        :disabled="!canContinue"
        data-testid="per-target-plan-continue"
        @click="handleContinue"
      >
        {{ t('onboarding.next') }}
      </VBtn>
    </div>
  </section>
</template>

<style scoped>
.per-target-fieldset {
  border: 1px solid rgb(var(--v-theme-on-surface), 0.15);
  border-radius: 8px;
  padding: 0.75rem 1rem;
}

.per-target-range {
  inline-size: 100%;
}

.cursor-pointer {
  cursor: pointer;
}
</style>
