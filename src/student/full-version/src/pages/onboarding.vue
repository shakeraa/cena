<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useLocale } from 'vuetify'
import OnboardingStepper from '@/components/onboarding/OnboardingStepper.vue'
import RoleSelector from '@/components/onboarding/RoleSelector.vue'
import LanguagePicker from '@/components/onboarding/LanguagePicker.vue'
import DiagnosticQuiz from '@/components/onboarding/DiagnosticQuiz.vue'
import SelfAssessmentStep from '@/components/onboarding/SelfAssessmentStep.vue'
import { useOnboardingStore } from '@/stores/onboardingStore'
import { useMeStore } from '@/stores/meStore'
import { useApiMutation } from '@/composables/useApiMutation'
import { $api } from '@/utils/api'

definePage({
  meta: {
    layout: 'blank',
    requiresAuth: true,
    requiresOnboarded: false,
    public: false,
    title: 'nav.onboarding',
    hideSidebar: true,
    breadcrumbs: false,
  },
})

const router = useRouter()
const { t, locale: i18nLocale } = useI18n()
const vuetifyLocale = useLocale()

const onboarding = useOnboardingStore()
const meStore = useMeStore()

const submitError = ref<string | null>(null)

const { execute: submitOnboarding, loading: submitting } = useApiMutation<
  { success: boolean; redirectTo: string },
  {
    role: string
    locale: string
    subjects: string[]
    dailyTimeGoalMinutes: number
    weeklySubjectTargets: { subject: string; accuracyTarget: number }[]
    diagnosticResults: { questionId: string; correct: boolean; confidencePercent: number }[] | null
    classroomCode: null
  }
>('/api/me/onboarding')

const roleLabelKey = computed(() => {
  switch (onboarding.role) {
    case 'student': return 'onboarding.role.student'
    case 'self-learner': return 'onboarding.role.selfLearner'
    case 'test-prep': return 'onboarding.role.testPrep'
    case 'homeschool': return 'onboarding.role.homeschool'
    default: return 'onboarding.role.none'
  }
})

const languageLabelKey = computed(() => {
  switch (onboarding.locale) {
    case 'ar': return 'language.arabic'
    case 'he': return 'language.hebrew'
    default: return 'language.english'
  }
})

function applyLocaleSideEffects(code: 'en' | 'ar' | 'he') {
  i18nLocale.value = code
  vuetifyLocale.current.value = code
  if (typeof document !== 'undefined') {
    document.documentElement.lang = code
    document.documentElement.dir = (code === 'ar' || code === 'he') ? 'rtl' : 'ltr'
  }
}

watch(() => onboarding.locale, next => {
  applyLocaleSideEffects(next)
}, { immediate: true })

function handleStart() {
  onboarding.next()
}

function handleNext() {
  submitError.value = null
  onboarding.next()
}

function handleBack() {
  submitError.value = null
  onboarding.back()
}

// Steps that render their OWN action row (Skip/Continue) — outer
// Next button is suppressed on these so the user sees one advance-button
// instead of two side-by-side. Back stays visible so the user can
// retreat without completing the step.
const stepOwnsAdvance = computed(() => (
  onboarding.step === 'diagnostic'
  || onboarding.step === 'self-assessment'
))

// RDY-057: post the self-assessment separately from onboarding completion.
// Best-effort — a failure here does NOT block the user's onboarding; we
// log it and let the student reach /home. The self-assessment can be
// resubmitted later via /api/me/self-assessment POST.
async function submitSelfAssessment() {
  try {
    const sa = onboarding.selfAssessment
    await $api('/api/me/self-assessment', {
      method: 'POST',
      body: {
        skipped: sa.skipped,
        subjectConfidence: sa.subjectConfidence,
        strengths: sa.strengths,
        frictionPoints: sa.frictionPoints,
        topicFeelings: sa.topicFeelings,
        freeText: sa.freeText || null,
        optInPersistent: sa.optInPersistent,
      },
    })
  }
  catch (err) {
    console.warn('[ONBOARDING] self-assessment submit failed (non-blocking):', err)
  }
}

async function handleConfirm() {
  submitError.value = null
  if (!onboarding.role)
    return

  try {
    const res = await submitOnboarding({
      role: onboarding.role,
      locale: onboarding.locale,
      subjects: onboarding.subjects,
      dailyTimeGoalMinutes: onboarding.dailyTimeGoalMinutes,
      weeklySubjectTargets: [],
      diagnosticResults: onboarding.diagnosticSkipped
        ? null
        : onboarding.diagnosticResponses.map(r => ({
          questionId: r.questionId,
          correct: r.correct,
          confidencePercent: 100,
        })),
      classroomCode: null,
    })

    // Fire the self-assessment POST AFTER the onboarding POST succeeds so
    // the caller has a valid `student_id` scope on the JWT. Awaited (not
    // fire-and-forget) so any validation error is surfaced to the user,
    // but wrapped in its own try/catch so a server-side 500 doesn't block
    // the happy path.
    await submitSelfAssessment()

    const nowIso = new Date().toISOString()

    meStore.__setOnboardedAt(nowIso)
    onboarding.markCompleted()

    await router.replace(res.redirectTo || '/home')
  }
  catch (err) {
    submitError.value = (err as Error).message || t('error.serverError')
  }
}
</script>

<template>
  <div
    class="onboarding-page"
    data-testid="onboarding-page"
  >
    <VCard
      class="onboarding-page__card pa-6 pa-md-8"
      elevation="8"
    >
      <OnboardingStepper
        :current-step="onboarding.stepIndex"
        :total-steps="onboarding.totalSteps"
      />

      <!-- STEP 1: WELCOME -->
      <section
        v-if="onboarding.step === 'welcome'"
        data-testid="onboarding-step-welcome"
        class="text-center"
      >
        <VIcon
          icon="tabler-sparkles"
          size="56"
          color="primary"
          aria-hidden="true"
          class="mb-4"
        />
        <h1 class="text-h4 mb-2">
          {{ t('onboarding.welcome.title') }}
        </h1>
        <p class="text-body-1 text-medium-emphasis mb-6">
          {{ t('onboarding.welcome.subtitle') }}
        </p>
        <VBtn
          color="primary"
          size="large"
          data-testid="onboarding-start"
          @click="handleStart"
        >
          {{ t('onboarding.getStarted') }}
        </VBtn>
      </section>

      <!-- STEP 2: ROLE -->
      <section
        v-else-if="onboarding.step === 'role'"
        data-testid="onboarding-step-role"
      >
        <h2 class="text-h5 mb-1">
          {{ t('onboarding.role.title') }}
        </h2>
        <p class="text-body-2 text-medium-emphasis mb-5">
          {{ t('onboarding.role.subtitle') }}
        </p>
        <RoleSelector
          :model-value="onboarding.role"
          @update:model-value="onboarding.setRole($event)"
        />
      </section>

      <!-- STEP 3: LANGUAGE -->
      <section
        v-else-if="onboarding.step === 'language'"
        data-testid="onboarding-step-language"
      >
        <h2 class="text-h5 mb-1">
          {{ t('onboarding.language.title') }}
        </h2>
        <p class="text-body-2 text-medium-emphasis mb-5">
          {{ t('onboarding.language.subtitle') }}
        </p>
        <LanguagePicker
          :model-value="onboarding.locale"
          @update:model-value="onboarding.setLocale($event)"
        />
      </section>

      <!-- STEP 4: DIAGNOSTIC QUIZ -->
      <section
        v-else-if="onboarding.step === 'diagnostic'"
        data-testid="onboarding-step-diagnostic"
      >
        <DiagnosticQuiz
          :subjects="onboarding.subjects"
          @complete="(responses) => { onboarding.setDiagnosticResults(responses); onboarding.next() }"
          @skip="() => { onboarding.skipDiagnostic(); onboarding.next() }"
        />
      </section>

      <!-- STEP 5: SELF-ASSESSMENT (RDY-057) -->
      <SelfAssessmentStep
        v-else-if="onboarding.step === 'self-assessment'"
        @skip="() => onboarding.next()"
        @complete="() => onboarding.next()"
      />

      <!-- STEP 6: CONFIRM -->
      <section
        v-else-if="onboarding.step === 'confirm'"
        data-testid="onboarding-step-confirm"
      >
        <h2 class="text-h5 mb-1">
          {{ t('onboarding.confirm.title') }}
        </h2>
        <p class="text-body-2 text-medium-emphasis mb-5">
          {{ t('onboarding.confirm.subtitle') }}
        </p>
        <!-- FIND-ux-onboarding: confirm-step summary. Use a bordered,
             transparent surface so the card inherits the outer VCard's
             theme color instead of the Material 'surface-variant' token
             which renders as light-grey on both light AND dark themes
             and fails contrast for the subtitle text. -->
        <VList
          class="mb-4 rounded-lg onboarding-summary-list"
          :bg-color="'transparent'"
          border
        >
          <VListItem
            prepend-icon="tabler-user"
            :title="t('onboarding.confirm.roleLabel')"
            :subtitle="t(roleLabelKey)"
            data-testid="confirm-role"
          />
          <VListItem
            prepend-icon="tabler-language"
            :title="t('onboarding.confirm.languageLabel')"
            :subtitle="t(languageLabelKey)"
            data-testid="confirm-language"
          />
        </VList>

        <VAlert
          v-if="submitError"
          type="error"
          variant="tonal"
          class="mb-4"
          data-testid="onboarding-error"
        >
          {{ submitError }}
        </VAlert>
      </section>

      <!-- NAV BUTTONS -->
      <div
        v-if="onboarding.step !== 'welcome'"
        class="onboarding-page__nav d-flex align-center justify-space-between mt-6"
      >
        <VBtn
          variant="text"
          :disabled="submitting"
          data-testid="onboarding-back"
          @click="handleBack"
        >
          <VIcon
            icon="tabler-arrow-left"
            start
            class="flip-in-rtl"
            aria-hidden="true"
          />
          {{ t('onboarding.back') }}
        </VBtn>

        <VBtn
          v-if="onboarding.step !== 'confirm' && !stepOwnsAdvance"
          color="primary"
          :disabled="!onboarding.canAdvance"
          data-testid="onboarding-next"
          @click="handleNext"
        >
          {{ t('onboarding.next') }}
          <VIcon
            icon="tabler-arrow-right"
            end
            class="flip-in-rtl"
            aria-hidden="true"
          />
        </VBtn>

        <VBtn
          v-else-if="onboarding.step === 'confirm'"
          color="primary"
          :loading="submitting"
          :disabled="!onboarding.canAdvance || submitting"
          data-testid="onboarding-submit"
          @click="handleConfirm"
        >
          {{ t('onboarding.confirm.cta') }}
        </VBtn>
      </div>
    </VCard>
  </div>
</template>

<style scoped>
.onboarding-page {
  min-block-size: 100dvh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.5rem;
}

.onboarding-page__card {
  inline-size: 100%;
  max-inline-size: 640px;
}

/* Lift subtitle contrast on the confirm-step summary so "Self-learner"
   and "English" read cleanly on both themes. Vuetify's default subtitle
   opacity (0.6) is too faint when the list bg is transparent. */
.onboarding-summary-list :deep(.v-list-item-subtitle) {
  --v-medium-emphasis-opacity: 0.85;

  opacity: var(--v-medium-emphasis-opacity);
}
</style>
