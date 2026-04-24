<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import SubjectMasteryRow from '@/components/progress/SubjectMasteryRow.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import type { MeBootstrapDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.mastery',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const meQuery = useApiQuery<MeBootstrapDto>('/api/me')

// Phase A: derive per-subject mastery from the onboarding subjects in /api/me.
// STU-W-09b will wire the real MasteryProjection once it ships.
const subjects = computed(() => {
  if (!meQuery.data.value)
    return []

  const base = meQuery.data.value.subjects.length > 0
    ? meQuery.data.value.subjects
    : ['math', 'physics', 'chemistry']

  // Deterministic pseudo-random percents seeded by subject name so the
  // display is stable across reloads but varies across subjects.
  return base.map((subject, index) => {
    const baseline = 30 + ((subject.charCodeAt(0) + index * 11) % 55)
    const attempted = 40 + ((subject.charCodeAt(1 % subject.length) * 3 + index) % 120)
    const accuracy = Math.max(40, Math.min(98, baseline + 12))

    return {
      subject,
      masteryPercent: baseline,
      questionsAttempted: attempted,
      accuracyPercent: accuracy,
    }
  })
})

const overallMastery = computed(() => {
  if (subjects.value.length === 0)
    return 0

  const sum = subjects.value.reduce((acc, s) => acc + s.masteryPercent, 0)

  return Math.round(sum / subjects.value.length)
})
</script>

<template>
  <div
    class="progress-mastery-page pa-4"
    data-testid="progress-mastery-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('progress.mastery.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('progress.mastery.subtitle') }}
    </p>

    <div
      v-if="meQuery.loading.value && !meQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="mastery-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="meQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="mastery-error"
    >
      {{ t(meQuery.error.value.i18nKey ?? 'common.errorGeneric') }}
    </VAlert>

    <template v-else-if="meQuery.data.value">
      <VCard
        variant="flat"
        color="primary"
        class="pa-5 mb-6"
        data-testid="mastery-overall"
      >
        <div class="d-flex align-center justify-space-between">
          <div>
            <div class="text-caption text-white opacity-80">
              {{ t('progress.mastery.overallLabel') }}
            </div>
            <div class="text-h3 font-weight-bold text-white">
              {{ overallMastery }}%
            </div>
          </div>
          <VIcon
            icon="tabler-target"
            size="56"
            color="yellow-accent-2"
            aria-hidden="true"
          />
        </div>
      </VCard>

      <div data-testid="mastery-subjects">
        <SubjectMasteryRow
          v-for="s in subjects"
          :key="s.subject"
          :subject="s.subject"
          :mastery-percent="s.masteryPercent"
          :questions-attempted="s.questionsAttempted"
          :accuracy-percent="s.accuracyPercent"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.progress-mastery-page {
  max-inline-size: 900px;
  margin-inline: auto;
}
</style>
