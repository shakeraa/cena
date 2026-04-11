<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import TimeBreakdownChart from '@/components/progress/TimeBreakdownChart.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import type { TimeBreakdownDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.learningTime',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const timeQuery = useApiQuery<TimeBreakdownDto>('/api/analytics/time-breakdown')

const totalMinutes = computed(() => {
  if (!timeQuery.data.value)
    return 0

  return timeQuery.data.value.items.reduce((acc, i) => acc + i.minutes, 0)
})

const last7DaysMinutes = computed(() => {
  if (!timeQuery.data.value)
    return 0

  const last7 = timeQuery.data.value.items.slice(-7)

  return last7.reduce((acc, i) => acc + i.minutes, 0)
})

const dayStreakCount = computed(() => {
  if (!timeQuery.data.value)
    return 0

  let count = 0
  const items = [...timeQuery.data.value.items].reverse()
  for (const item of items) {
    if (item.minutes > 0)
      count += 1
    else
      break
  }

  return count
})
</script>

<template>
  <div
    class="progress-time-page pa-4"
    data-testid="progress-time-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('progress.time.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('progress.time.subtitle') }}
    </p>

    <div
      v-if="timeQuery.loading.value && !timeQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="time-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="timeQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="time-error"
    >
      {{ timeQuery.error.value.message }}
    </VAlert>

    <template v-else-if="timeQuery.data.value">
      <VRow class="mb-4">
        <VCol
          cols="12"
          md="4"
        >
          <VCard
            variant="outlined"
            class="pa-4"
            data-testid="kpi-total-minutes"
          >
            <div class="text-caption text-medium-emphasis">
              {{ t('progress.time.kpi30dayTotal') }}
            </div>
            <div class="text-h4 font-weight-bold mt-1">
              {{ totalMinutes }}<span class="text-body-2 ms-1">min</span>
            </div>
          </VCard>
        </VCol>
        <VCol
          cols="12"
          md="4"
        >
          <VCard
            variant="outlined"
            class="pa-4"
            data-testid="kpi-7day-minutes"
          >
            <div class="text-caption text-medium-emphasis">
              {{ t('progress.time.kpi7dayTotal') }}
            </div>
            <div class="text-h4 font-weight-bold mt-1">
              {{ last7DaysMinutes }}<span class="text-body-2 ms-1">min</span>
            </div>
          </VCard>
        </VCol>
        <VCol
          cols="12"
          md="4"
        >
          <VCard
            variant="outlined"
            class="pa-4"
            data-testid="kpi-day-streak"
          >
            <div class="text-caption text-medium-emphasis">
              {{ t('progress.time.kpiDayStreak') }}
            </div>
            <div class="text-h4 font-weight-bold mt-1">
              {{ dayStreakCount }}<span class="text-body-2 ms-1">days</span>
            </div>
          </VCard>
        </VCol>
      </VRow>

      <TimeBreakdownChart :items="timeQuery.data.value.items" />
    </template>
  </div>
</template>

<style scoped>
.progress-time-page {
  max-inline-size: 1100px;
  margin-inline: auto;
}
</style>
