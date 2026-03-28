<script setup lang="ts">
import { $api } from '@/utils/api'

interface DegradationPoint {
  minute: number
  avgFocusScore: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const chartData = ref<DegradationPoint[]>([])

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const chartSeries = computed(() => [
  {
    name: 'Avg Focus Score',
    data: chartData.value.map(p => p.avgFocusScore),
  },
])

const chartOptions = computed(() => ({
  chart: {
    type: 'line' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
    zoom: { enabled: false },
  },
  stroke: {
    curve: 'smooth' as const,
    width: 3,
    lineCap: 'round' as const,
  },
  colors: ['#9055FD'],
  markers: {
    size: 4,
    colors: '#fff',
    strokeColors: '#9055FD',
    hover: { size: 6 },
  },
  grid: {
    strokeDashArray: 8,
    borderColor,
    padding: { bottom: -10 },
  },
  xaxis: {
    categories: chartData.value.map(p => `${p.minute}m`),
    labels: {
      style: {
        colors: labelColor,
        fontSize: '13px',
        fontWeight: 400,
      },
    },
    axisBorder: { show: false },
    axisTicks: { show: false },
    title: {
      text: 'Minutes into session',
      style: { color: labelColor, fontSize: '13px' },
    },
  },
  yaxis: {
    min: 0,
    max: 100,
    tickAmount: 5,
    labels: {
      style: {
        colors: labelColor,
        fontSize: '13px',
        fontWeight: 400,
      },
      formatter: (val: number) => `${Math.round(val)}`,
    },
    title: {
      text: 'Focus Score',
      style: { color: labelColor, fontSize: '13px' },
    },
  },
  tooltip: {
    y: {
      formatter: (val: number) => `${Math.round(val)} / 100`,
    },
  },
  responsive: [
    {
      breakpoint: 600,
      options: {
        chart: { height: 250 },
      },
    },
  ],
}))

const fetchDegradationCurve = async () => {
  loading.value = true
  try {
    const data = await $api('/admin/focus/degradation-curve')

    const raw = Array.isArray(data) ? data : (data.curve ?? data.points ?? [])
    chartData.value = raw.map((p: any) => ({
      minute: p.minute ?? p.minutesIntoSession ?? 0,
      avgFocusScore: p.avgFocusScore ?? 0,
    }))
    error.value = null
  }
  catch (err: any) {
    console.error('Failed to fetch degradation curve:', err)
    error.value = err.message ?? 'Failed to load degradation data'
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchDegradationCurve)

defineExpose({ refresh: fetchDegradationCurve })
</script>

<template>
  <VCard :loading="loading">
    <VCardItem
      title="Focus Degradation Curve"
      subtitle="Average focus score over session duration (all students)"
    />

    <VCardText>
      <VAlert
        v-if="error"
        type="error"
        variant="tonal"
        class="mb-4"
      >
        {{ error }}
      </VAlert>

      <VueApexCharts
        v-if="chartData.length > 0"
        type="line"
        height="350"
        :options="chartOptions"
        :series="chartSeries"
      />

      <div
        v-else-if="!loading && !error"
        class="d-flex justify-center align-center py-8"
      >
        <span class="text-body-1 text-disabled">No degradation data available</span>
      </div>
    </VCardText>
  </VCard>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
