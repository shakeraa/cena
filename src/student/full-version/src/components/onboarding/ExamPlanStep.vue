<script setup lang="ts">
// =============================================================================
// Cena — Onboarding Exam Plan Step (prr-148)
//
// Captures the two inputs AdaptiveScheduler.SchedulerInputs needs but
// currently has no UI for:
//   - DeadlineUtc        — target exam date (typically the Bagrut sitting)
//   - WeeklyTimeBudget   — how many hours per week the student will give
//
// Posts to PUT /api/me/study-plan when the student confirms. Validation
// mirrors the server (deadline > now + 7d, weekly ∈ [1, 40]) so the user
// sees inline errors instead of waiting for a 400 round-trip.
//
// RTL note: the label + helper copy pass through i18n. The date input and
// numeric slider value are wrapped in <bdi dir="ltr"> per the
// "math-always-LTR" convention — dates and numerics in RTL pages must
// stay LTR for correct ordering (user caught reversed equation bug).
// =============================================================================

import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { $api } from '@/utils/api'

const emit = defineEmits<{
  (e: 'skip'): void
  (e: 'complete'): void
}>()

const { t } = useI18n()

// ── Limits (must match server TryValidate in StudyPlanSettingsEndpoints) ──
const MIN_LEAD_DAYS = 7
const MIN_WEEKLY_HOURS = 1
const MAX_WEEKLY_HOURS = 40
const DEFAULT_WEEKLY_HOURS = 8

// ── Local form state ─────────────────────────────────────────────────────
// examDate is a YYYY-MM-DD string bound to <input type="date">. Kept as
// string to avoid timezone traps in <VTextField> round-trip; converted to
// UTC ISO at submit time.
const examDate = ref<string>('')
const weeklyHours = ref<number>(DEFAULT_WEEKLY_HOURS)
const submitting = ref(false)
const submitError = ref<string | null>(null)

// ── Derived validation ───────────────────────────────────────────────────
const minSelectableDate = computed(() => {
  const d = new Date()
  d.setDate(d.getDate() + MIN_LEAD_DAYS + 1)
  return d.toISOString().slice(0, 10)
})

const deadlineError = computed<string | null>(() => {
  if (!examDate.value) return null
  const picked = new Date(`${examDate.value}T00:00:00Z`)
  const minAllowed = new Date()
  minAllowed.setUTCDate(minAllowed.getUTCDate() + MIN_LEAD_DAYS)
  if (picked.getTime() <= minAllowed.getTime()) {
    return t('onboarding.examPlan.errors.deadlineTooClose', { days: MIN_LEAD_DAYS })
  }
  return null
})

const weeklyError = computed<string | null>(() => {
  if (weeklyHours.value < MIN_WEEKLY_HOURS || weeklyHours.value > MAX_WEEKLY_HOURS) {
    return t('onboarding.examPlan.errors.weeklyOutOfRange', {
      min: MIN_WEEKLY_HOURS,
      max: MAX_WEEKLY_HOURS,
    })
  }
  return null
})

const canSubmit = computed(() =>
  !submitting.value
  && !!examDate.value
  && deadlineError.value === null
  && weeklyError.value === null)

// ── Submit ───────────────────────────────────────────────────────────────
async function handleConfirm() {
  if (!canSubmit.value) return
  submitting.value = true
  submitError.value = null
  try {
    await $api('/api/me/study-plan', {
      method: 'PUT',
      body: {
        deadlineUtc: new Date(`${examDate.value}T00:00:00Z`).toISOString(),
        weeklyBudgetHours: weeklyHours.value,
      },
    })
    emit('complete')
  }
  catch (err) {
    submitError.value = (err as Error).message || t('error.serverError')
  }
  finally {
    submitting.value = false
  }
}

function handleSkip() {
  emit('skip')
}
</script>

<template>
  <section data-testid="onboarding-step-exam-plan">
    <h2 class="text-h5 mb-1">
      {{ t('onboarding.examPlan.title') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-5">
      {{ t('onboarding.examPlan.subtitle') }}
    </p>

    <!-- Exam date picker — always LTR numerics inside a bdi wrapper. -->
    <div class="mb-5">
      <label
        for="exam-plan-date"
        class="text-subtitle-2 d-block mb-1"
      >
        {{ t('onboarding.examPlan.deadlineLabel') }}
      </label>
      <bdi dir="ltr">
        <input
          id="exam-plan-date"
          v-model="examDate"
          type="date"
          class="exam-plan-date"
          :min="minSelectableDate"
          data-testid="exam-plan-date"
        >
      </bdi>
      <p
        v-if="deadlineError"
        class="text-error text-caption mt-1"
        data-testid="exam-plan-date-error"
      >
        {{ deadlineError }}
      </p>
      <p
        v-else
        class="text-caption text-medium-emphasis mt-1"
      >
        {{ t('onboarding.examPlan.deadlineHelper', { days: MIN_LEAD_DAYS }) }}
      </p>
    </div>

    <!-- Weekly-hours numeric — slider + live readout, readout LTR. -->
    <div class="mb-5">
      <label
        for="exam-plan-weekly"
        class="text-subtitle-2 d-block mb-1"
      >
        {{ t('onboarding.examPlan.weeklyLabel') }}
        <bdi dir="ltr">({{ weeklyHours }}h)</bdi>
      </label>
      <input
        id="exam-plan-weekly"
        v-model.number="weeklyHours"
        type="range"
        :min="MIN_WEEKLY_HOURS"
        :max="MAX_WEEKLY_HOURS"
        step="1"
        class="exam-plan-range"
        data-testid="exam-plan-weekly"
      >
      <p
        v-if="weeklyError"
        class="text-error text-caption mt-1"
        data-testid="exam-plan-weekly-error"
      >
        {{ weeklyError }}
      </p>
      <p
        v-else
        class="text-caption text-medium-emphasis mt-1"
      >
        {{ t('onboarding.examPlan.weeklyHelper', { min: MIN_WEEKLY_HOURS, max: MAX_WEEKLY_HOURS }) }}
      </p>
    </div>

    <div
      v-if="submitError"
      class="text-error mb-3"
      data-testid="exam-plan-error"
    >
      {{ submitError }}
    </div>

    <div class="d-flex align-center justify-space-between">
      <button
        type="button"
        class="v-btn v-btn--variant-text"
        :disabled="submitting"
        data-testid="exam-plan-skip"
        @click="handleSkip"
      >
        {{ t('onboarding.skip') }}
      </button>
      <button
        type="button"
        class="v-btn v-btn--color-primary"
        :disabled="!canSubmit"
        data-testid="exam-plan-confirm"
        @click="handleConfirm"
      >
        {{ t('onboarding.examPlan.confirm') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.exam-plan-date,
.exam-plan-range {
  inline-size: 100%;
  padding: 0.5rem 0.75rem;
  border: 1px solid rgb(var(--v-theme-on-surface), 0.2);
  border-radius: 6px;
  background: rgb(var(--v-theme-surface));
  color: rgb(var(--v-theme-on-surface));
  font-size: 1rem;
}

.exam-plan-range {
  padding: 0;
  border: none;
}

.text-error {
  color: rgb(var(--v-theme-error));
}
</style>
