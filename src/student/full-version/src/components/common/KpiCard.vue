<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  label: string
  value: string | number
  trend?: number
  icon?: string
}

const props = defineProps<Props>()
const { t } = useI18n()

const trendColor = computed<'success' | 'error' | 'grey'>(() => {
  if (props.trend == null || props.trend === 0)
    return 'grey'

  return props.trend > 0 ? 'success' : 'error'
})

const trendIcon = computed(() => {
  if (props.trend == null || props.trend === 0)
    return 'tabler-minus'

  return props.trend > 0 ? 'tabler-trending-up' : 'tabler-trending-down'
})

const trendAriaLabel = computed(() => {
  if (props.trend == null || props.trend === 0)
    return t('kpi.trendFlat')

  return props.trend > 0 ? t('kpi.trendUp') : t('kpi.trendDown')
})

const trendFormatted = computed(() => {
  if (props.trend == null)
    return ''
  const sign = props.trend > 0 ? '+' : ''

  return `${sign}${props.trend}%`
})
</script>

<template>
  <VCard
    class="kpi-card pa-5"
    variant="flat"
  >
    <div class="d-flex align-start justify-space-between mb-3">
      <div class="text-caption text-medium-emphasis text-uppercase">
        {{ label }}
      </div>
      <VIcon
        v-if="icon"
        :icon="icon"
        size="20"
        color="primary"
        aria-hidden="true"
      />
    </div>
    <div class="d-flex align-center justify-space-between">
      <div class="text-h3 font-weight-bold">
        {{ value }}
      </div>
      <div
        v-if="trend != null"
        class="kpi-trend d-flex align-center"
        :aria-label="trendAriaLabel"
      >
        <!--
          Icon carries the semantic color; the percentage text uses
          high-emphasis text to preserve WCAG AA contrast on both
          light and dark backgrounds.
        -->
        <VIcon
          :icon="trendIcon"
          :color="trendColor"
          size="20"
        />
        <span class="text-body-2 ms-1 font-weight-medium text-high-emphasis">
          {{ trendFormatted }}
        </span>
      </div>
    </div>
  </VCard>
</template>

<style scoped>
.kpi-card {
  min-inline-size: 200px;
}
</style>
