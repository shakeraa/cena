<script setup lang="ts">
// =============================================================================
// Cena — Onboarding Self-Assessment Step (RDY-057)
//
// Captures affective / self-concept state:
//   - subject-level confidence (1-5 Likert)
//   - self-identified strengths + friction points (chip multi-select)
//   - topic feelings (solid / unsure / anxious / new) — with text labels,
//     never emoji-only (RDY-015 a11y + RDY-030 criteria)
//   - optional free-text (≤200 chars)
//
// The whole step fits in ~60-90 seconds per Dr. Lior (§4 time budget);
// longer would be bucketed into sub-steps.
// =============================================================================

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useOnboardingStore } from '@/stores/onboardingStore'

const emit = defineEmits<{
  (e: 'skip'): void
  (e: 'complete'): void
}>()

const { t } = useI18n()
const onboarding = useOnboardingStore()

// Stable tag ids — i18n on render; never send display strings to the server.
const STRENGTH_TAGS = [
  'visualizer',
  'step-by-step',
  'formula-memorizer',
  'real-examples',
  'enjoys-proofs',
  'pattern-finder',
  'verbal-explainer',
  'patient-practice',
] as const

const FRICTION_TAGS = [
  'word-problems',
  'test-anxiety',
  'no-starting-point',
  'loses-track',
  'memorization',
  'symbolic-notation',
  'time-pressure',
  'multi-step',
] as const

// Canonical Bagrut-aligned subjects. Keys match the server-side concept
// taxonomy so the subject-confidence map keys are stable.
const SUBJECT_KEYS = [
  'algebra',
  'functions',
  'calculus',
  'geometry',
  'trigonometry',
  'probability',
  'statistics',
  'vectors',
] as const

// 4 topic-feeling values with text labels paired to emoji. Never emoji-only.
const FEELINGS = [
  { value: 'Solid', emoji: '😊', i18n: 'onboarding.selfAssessment.feeling.solid' },
  { value: 'Unsure', emoji: '🤔', i18n: 'onboarding.selfAssessment.feeling.unsure' },
  { value: 'Anxious', emoji: '😰', i18n: 'onboarding.selfAssessment.feeling.anxious' },
  { value: 'New', emoji: '❌', i18n: 'onboarding.selfAssessment.feeling.new' },
] as const

// Subjects shown for topic-feelings are the same taxonomy — keeps the
// step scoped. Post-pilot we can expand per-concept if signal is useful.
const TOPIC_KEYS = SUBJECT_KEYS

const FREE_TEXT_MAX = 200

const freeTextCount = computed(() => onboarding.selfAssessment.freeText.length)
const freeTextOk = computed(() => freeTextCount.value <= FREE_TEXT_MAX)

function setConfidence(subject: string, value: number) {
  onboarding.setSelfAssessment({
    subjectConfidence: {
      ...onboarding.selfAssessment.subjectConfidence,
      [subject]: value,
    },
  })
}

function toggleStrength(tag: string) {
  const cur = onboarding.selfAssessment.strengths
  const next = cur.includes(tag) ? cur.filter(t => t !== tag) : [...cur, tag]
  onboarding.setSelfAssessment({ strengths: next })
}

function toggleFriction(tag: string) {
  const cur = onboarding.selfAssessment.frictionPoints
  const next = cur.includes(tag) ? cur.filter(t => t !== tag) : [...cur, tag]
  onboarding.setSelfAssessment({ frictionPoints: next })
}

function setFeeling(topic: string, value: string) {
  onboarding.setSelfAssessment({
    topicFeelings: {
      ...onboarding.selfAssessment.topicFeelings,
      [topic]: value,
    },
  })
}

function onFreeTextInput(e: Event) {
  const val = (e.target as HTMLTextAreaElement).value
  onboarding.setSelfAssessment({ freeText: val.slice(0, FREE_TEXT_MAX) })
}

function handleSkip() {
  onboarding.skipSelfAssessment()
  emit('skip')
}

function handleComplete() {
  emit('complete')
}
</script>

<template>
  <section
    data-testid="onboarding-step-self-assessment"
  >
    <h2 class="text-h5 mb-1">
      {{ t('onboarding.selfAssessment.title') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-5">
      {{ t('onboarding.selfAssessment.subtitle') }}
    </p>

    <!-- SUBJECT CONFIDENCE (Likert) -->
    <div class="mb-6">
      <h3 class="text-subtitle-1 mb-2">
        {{ t('onboarding.selfAssessment.confidenceTitle') }}
      </h3>
      <p class="text-body-2 text-medium-emphasis mb-3">
        {{ t('onboarding.selfAssessment.confidenceSubtitle') }}
      </p>
      <div
        v-for="subject in SUBJECT_KEYS"
        :key="`conf-${subject}`"
        class="d-flex align-center ga-3 mb-2 flex-wrap"
        :data-testid="`confidence-${subject}`"
      >
        <div style="min-inline-size: 140px;">
          <span class="text-body-2">{{ t(`onboarding.selfAssessment.subject.${subject}`) }}</span>
        </div>
        <VBtnToggle
          :model-value="onboarding.selfAssessment.subjectConfidence[subject] ?? 0"
          density="compact"
          variant="outlined"
          mandatory
          class="flex-grow-0"
          @update:model-value="(v: number) => setConfidence(subject, v)"
        >
          <VBtn
            v-for="n in [1, 2, 3, 4, 5]"
            :key="n"
            :value="n"
            size="small"
            :aria-label="`${t(`onboarding.selfAssessment.subject.${subject}`)}: ${n}/5`"
          >
            {{ n }}
          </VBtn>
        </VBtnToggle>
      </div>
    </div>

    <!-- STRENGTHS -->
    <div class="mb-6">
      <h3 class="text-subtitle-1 mb-2">
        {{ t('onboarding.selfAssessment.strengthsTitle') }}
      </h3>
      <div class="d-flex flex-wrap ga-2">
        <VChip
          v-for="tag in STRENGTH_TAGS"
          :key="`s-${tag}`"
          :color="onboarding.selfAssessment.strengths.includes(tag) ? 'primary' : 'default'"
          :variant="onboarding.selfAssessment.strengths.includes(tag) ? 'flat' : 'outlined'"
          :data-testid="`strength-${tag}`"
          size="small"
          :aria-pressed="onboarding.selfAssessment.strengths.includes(tag)"
          role="button"
          @click="toggleStrength(tag)"
          @keydown.enter.prevent="toggleStrength(tag)"
          @keydown.space.prevent="toggleStrength(tag)"
        >
          {{ t(`onboarding.selfAssessment.strengthTag.${tag}`) }}
        </VChip>
      </div>
    </div>

    <!-- FRICTION POINTS -->
    <div class="mb-6">
      <h3 class="text-subtitle-1 mb-2">
        {{ t('onboarding.selfAssessment.frictionTitle') }}
      </h3>
      <div class="d-flex flex-wrap ga-2">
        <VChip
          v-for="tag in FRICTION_TAGS"
          :key="`f-${tag}`"
          :color="onboarding.selfAssessment.frictionPoints.includes(tag) ? 'warning' : 'default'"
          :variant="onboarding.selfAssessment.frictionPoints.includes(tag) ? 'flat' : 'outlined'"
          :data-testid="`friction-${tag}`"
          size="small"
          :aria-pressed="onboarding.selfAssessment.frictionPoints.includes(tag)"
          role="button"
          @click="toggleFriction(tag)"
          @keydown.enter.prevent="toggleFriction(tag)"
          @keydown.space.prevent="toggleFriction(tag)"
        >
          {{ t(`onboarding.selfAssessment.frictionTag.${tag}`) }}
        </VChip>
      </div>
    </div>

    <!-- TOPIC FEELINGS -->
    <div class="mb-6">
      <h3 class="text-subtitle-1 mb-2">
        {{ t('onboarding.selfAssessment.feelingsTitle') }}
      </h3>
      <p class="text-body-2 text-medium-emphasis mb-3">
        {{ t('onboarding.selfAssessment.feelingsSubtitle') }}
      </p>
      <div
        v-for="topic in TOPIC_KEYS"
        :key="`feel-${topic}`"
        class="d-flex align-center ga-2 mb-2 flex-wrap"
        :data-testid="`feeling-${topic}`"
      >
        <div style="min-inline-size: 140px;">
          <span class="text-body-2">{{ t(`onboarding.selfAssessment.subject.${topic}`) }}</span>
        </div>
        <div class="d-flex ga-1 flex-wrap">
          <VBtn
            v-for="f in FEELINGS"
            :key="f.value"
            size="small"
            :variant="onboarding.selfAssessment.topicFeelings[topic] === f.value ? 'flat' : 'outlined'"
            :color="onboarding.selfAssessment.topicFeelings[topic] === f.value ? 'primary' : 'default'"
            :aria-label="t(f.i18n)"
            :aria-pressed="onboarding.selfAssessment.topicFeelings[topic] === f.value"
            @click="setFeeling(topic, f.value)"
          >
            <!-- Emoji kept LTR inside bdi so RTL locales don't reorder it -->
            <bdi dir="ltr" class="me-1" aria-hidden="true">{{ f.emoji }}</bdi>
            <span>{{ t(f.i18n) }}</span>
          </VBtn>
        </div>
      </div>
    </div>

    <!-- FREE TEXT -->
    <div class="mb-4">
      <h3 class="text-subtitle-1 mb-2">
        {{ t('onboarding.selfAssessment.freeTextTitle') }}
      </h3>
      <VTextarea
        :model-value="onboarding.selfAssessment.freeText"
        :placeholder="t('onboarding.selfAssessment.freeTextPlaceholder')"
        :aria-label="t('onboarding.selfAssessment.freeTextTitle')"
        rows="2"
        auto-grow
        variant="outlined"
        density="compact"
        data-testid="self-assessment-free-text"
        :counter="FREE_TEXT_MAX"
        :maxlength="FREE_TEXT_MAX"
        hide-details="auto"
        @input="onFreeTextInput"
      />
      <div
        v-if="!freeTextOk"
        class="text-caption text-error mt-1"
      >
        {{ t('onboarding.selfAssessment.freeTextTooLong', { max: FREE_TEXT_MAX }) }}
      </div>
    </div>

    <!-- ACTION ROW -->
    <div class="d-flex align-center justify-space-between mt-4 flex-wrap ga-2">
      <VBtn
        variant="text"
        data-testid="self-assessment-skip"
        @click="handleSkip"
      >
        {{ t('onboarding.selfAssessment.skip') }}
      </VBtn>
      <VBtn
        color="primary"
        :disabled="!freeTextOk"
        data-testid="self-assessment-complete"
        @click="handleComplete"
      >
        {{ t('onboarding.selfAssessment.continue') }}
      </VBtn>
    </div>
  </section>
</template>
