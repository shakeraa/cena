<script setup lang="ts">
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'
import { useApi } from '@/composables/useApi'

interface PipelinePoint {
  date: string
  created: number
  reviewed: number
  approved: number
  rejected: number
}

interface PipelineResponse {
  data: PipelinePoint[]
}

const vuetifyTheme = useTheme()

const { data: rawData, isFetching, error } = useApi<PipelineResponse>(
  '/admin/dashboard/content-pipeline',
)

const hasData = computed(() => {
  const points = rawData.value?.data ?? rawData.value
  return Array.isArray(points) && points.length > 0
})

const categories = computed(() => {
  const points = rawData.value?.data ?? rawData.value
  if (!Array.isArray(points)) return []
  return points.map((p: PipelinePoint) => {
    const d = new Date(p.date)
    return `${d.getMonth() + 1}/${d.getDate()}`
  })
})

const series = computed(() => {
  const points = rawData.value?.data ?? rawData.value
  if (!Array.isArray(points)) return []
  return [
    { name: 'Created', data: points.map((p: PipelinePoint) => p.created ?? 0) },
    { name: 'Reviewed', data: points.map((p: PipelinePoint) => p.reviewed ?? 0) },
    { name: 'Approved', data: points.map((p: PipelinePoint) => p.approved ?? 0) },
    { name: 'Rejected', data: points.map((p: PipelinePoint) => p.rejected ?? 0) },
  ]
})

const chartOptions = computed(() => {
  const currentTheme = vuetifyTheme.current.value.colors
  const variableTheme = vuetifyTheme.current.value.variables
  const isDark = vuetifyTheme.current.value.dark

  return {
    chart: {
      parentHeightOffset: 0,
      type: 'bar' as const,
      stacked: true,
      toolbar: { show: true },
      background: 'transparent',
    },
    theme: {
      mode: isDark ? 'dark' as const : 'light' as const,
    },
    colors: [currentTheme.info, currentTheme.primary, currentTheme.success, currentTheme.error],
    plotOptions: {
      bar: {
        columnWidth: '40%',
        borderRadius: 4,
        borderRadiusApplication: 'end' as const,
        borderRadiusWhenStacked: 'last' as const,
      },
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
        rotateAlways: categories.value.length > 10,
      },
      axisBorder: { show: false },
      axisTicks: { show: false },
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
      <VCardTitle>Content Pipeline</VCardTitle>
      <VCardSubtitle>Content items created, reviewed, approved, and rejected per day</VCardSubtitle>
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
        Failed to load content pipeline data. Please try again later.
      </VAlert>

      <template v-else-if="hasData">
        <VueApexCharts
          type="bar"
          :height="350"
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
            icon="tabler-chart-bar"
            size="48"
            class="mb-2"
          />
          <p>No content pipeline data available</p>
        </div>
      </div>
    </VCardText>
  </VCard>
</template>
