<script setup lang="ts">
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'
import { useApi } from '@/composables/useApi'

interface ActivityPoint {
  date: string
  dau: number
  wau: number
  mau: number
}

interface ActivityResponse {
  data: ActivityPoint[]
}

const props = withDefaults(defineProps<{
  period?: string
}>(), {
  period: '30d',
})

const vuetifyTheme = useTheme()

const { data: rawData, isFetching, error } = useApi<ActivityResponse>(
  computed(() => `/admin/dashboard/activity?period=${props.period}`),
)

const hasData = computed(() => {
  const points = rawData.value?.data ?? rawData.value
  return Array.isArray(points) && points.length > 0
})

const categories = computed(() => {
  const points = rawData.value?.data ?? rawData.value
  if (!Array.isArray(points)) return []
  return points.map((p: ActivityPoint) => {
    const d = new Date(p.date)
    return `${d.getMonth() + 1}/${d.getDate()}`
  })
})

const series = computed(() => {
  const points = rawData.value?.data ?? rawData.value
  if (!Array.isArray(points)) return []
  return [
    { name: 'DAU', data: points.map((p: ActivityPoint) => p.dau ?? 0) },
    { name: 'WAU', data: points.map((p: ActivityPoint) => p.wau ?? 0) },
    { name: 'MAU', data: points.map((p: ActivityPoint) => p.mau ?? 0) },
  ]
})

const chartOptions = computed(() => {
  const currentTheme = vuetifyTheme.current.value.colors
  const variableTheme = vuetifyTheme.current.value.variables
  const isDark = vuetifyTheme.current.value.dark

  return {
    chart: {
      parentHeightOffset: 0,
      type: 'line' as const,
      toolbar: { show: true },
      zoom: { enabled: true },
      background: 'transparent',
    },
    theme: {
      mode: isDark ? 'dark' as const : 'light' as const,
    },
    colors: [currentTheme.primary, currentTheme.success, currentTheme.warning],
    stroke: {
      width: 2,
      curve: 'smooth' as const,
    },
    grid: {
      borderColor: `rgba(${hexToRgb(currentTheme['on-surface'])},${variableTheme['border-opacity']})`,
      padding: { top: -10, bottom: -5, left: 0, right: 10 },
    },
    xaxis: {
      categories: categories.value,
      labels: {
        style: {
          colors: `rgba(${hexToRgb(currentTheme['on-surface'])},${variableTheme['disabled-opacity']})`,
          fontSize: '12px',
        },
        rotate: -45,
        rotateAlways: categories.value.length > 15,
      },
      axisBorder: { show: false },
      axisTicks: { show: false },
      tickAmount: 10,
    },
    yaxis: {
      labels: {
        style: {
          colors: `rgba(${hexToRgb(currentTheme['on-surface'])},${variableTheme['disabled-opacity']})`,
          fontSize: '12px',
        },
      },
    },
    legend: {
      position: 'top' as const,
      horizontalAlign: 'left' as const,
      labels: {
        colors: `rgba(${hexToRgb(currentTheme['on-surface'])},${variableTheme['high-emphasis-opacity']})`,
      },
    },
    dataLabels: { enabled: false },
    tooltip: {
      shared: true,
      intersect: false,
    },
  }
})
</script>

<template>
  <VCard>
    <VCardItem>
      <VCardTitle>User Activity</VCardTitle>
      <VCardSubtitle>DAU / WAU / MAU over last 30 days</VCardSubtitle>
    </VCardItem>

    <VCardText>
      <VProgressLinear
        v-if="isFetching"
        indeterminate
        color="primary"
        class="mb-4"
      />

      <VAlert
        v-else-if="error"
        type="error"
        variant="tonal"
        class="mb-4"
      >
        Failed to load user activity data. Please try again later.
      </VAlert>

      <template v-else-if="hasData">
        <VueApexCharts
          type="line"
          :height="300"
          :options="chartOptions"
          :series="series"
        />
      </template>

      <div
        v-else
        class="d-flex align-center justify-center"
        style="min-height: 300px;"
      >
        <div class="text-center text-disabled">
          <VIcon
            icon="tabler-chart-line"
            size="48"
            class="mb-2"
          />
          <p>No activity data available for this period</p>
        </div>
      </div>
    </VCardText>
  </VCard>
</template>
