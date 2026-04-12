<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useLocaleFormatters } from '@/composables/useLocaleFormatters'
import type { TimeBreakdownItem } from '@/api/types/common'

interface Props {
  items: TimeBreakdownItem[]
}

const props = defineProps<Props>()
const { t } = useI18n()
const { bcp47 } = useLocaleFormatters()

const maxMinutes = computed(() =>
  props.items.reduce((acc, item) => Math.max(acc, item.minutes), 1),
)

const totalMinutes = computed(() =>
  props.items.reduce((acc, item) => acc + item.minutes, 0),
)

const avgMinutes = computed(() => {
  if (props.items.length === 0)
    return 0

  return Math.round(totalMinutes.value / props.items.length)
})

function heightPercent(minutes: number): number {
  return Math.max(4, Math.round((minutes / maxMinutes.value) * 100))
}

/**
 * FIND-pedagogy-015: Uses the active i18n locale (via BCP 47 tag) instead
 * of `undefined` which falls back to the browser locale.
 */
function formatDay(iso: string): string {
  const d = new Date(iso)

  return d.toLocaleDateString(bcp47.value, { weekday: 'short' })
}

function formatDate(iso: string): string {
  const d = new Date(iso)

  return d.toLocaleDateString(bcp47.value, { month: 'short', day: 'numeric' })
}
</script>

<template>
  <VCard
    class="time-breakdown-chart pa-5"
    variant="outlined"
    data-testid="time-breakdown-chart"
  >
    <div class="d-flex align-center justify-space-between mb-5">
      <div>
        <div class="text-h6">
          {{ t('progress.time.chartTitle') }}
        </div>
        <div class="text-caption text-medium-emphasis">
          {{ t('progress.time.chartSubtitle') }}
        </div>
      </div>
      <div class="text-end">
        <div
          class="text-caption text-medium-emphasis"
        >
          {{ t('progress.time.avgPerDay') }}
        </div>
        <div
          class="text-h6 font-weight-bold"
          data-testid="time-avg"
        >
          {{ avgMinutes }}<span class="text-caption text-medium-emphasis ms-1">min</span>
        </div>
      </div>
    </div>

    <div
      class="time-breakdown-chart__bars"
      role="img"
      :aria-label="t('progress.time.chartAria', totalMinutes, { total: totalMinutes, avg: avgMinutes })"
      data-testid="time-bars"
    >
      <div
        v-for="item in items"
        :key="item.date"
        class="time-breakdown-chart__column"
        :data-testid="`time-bar-${item.date.slice(0, 10)}`"
      >
        <div class="time-breakdown-chart__bar-wrap">
          <div
            class="time-breakdown-chart__bar"
            :style="{ height: `${heightPercent(item.minutes)}%` }"
            :title="`${formatDate(item.date)}: ${item.minutes} min`"
          />
        </div>
      </div>
    </div>

    <div class="d-flex justify-space-between text-caption text-medium-emphasis mt-2">
      <span>{{ formatDay(items[0]?.date ?? '') }}</span>
      <span>{{ t('progress.time.today') }}</span>
    </div>
  </VCard>
</template>

<style scoped>
.time-breakdown-chart__bars {
  display: grid;
  grid-template-columns: repeat(30, 1fr);
  gap: 2px;
  block-size: 140px;
}

.time-breakdown-chart__column {
  display: flex;
  align-items: flex-end;
  block-size: 100%;
}

.time-breakdown-chart__bar-wrap {
  display: flex;
  align-items: flex-end;
  inline-size: 100%;
  block-size: 100%;
}

.time-breakdown-chart__bar {
  inline-size: 100%;
  background-color: rgb(var(--v-theme-primary));
  border-radius: 2px;
  transition: height 0.3s ease-out;
}

.time-breakdown-chart__bar:hover {
  background-color: rgb(var(--v-theme-primary) / 0.8);
}
</style>
