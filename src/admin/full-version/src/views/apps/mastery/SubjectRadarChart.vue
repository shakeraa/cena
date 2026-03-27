<script setup lang="ts">
import { $api } from '@/utils/api'

const loading = ref(true)
const error = ref<string | null>(null)
const subjects = ref<string[]>([])
const averages = ref<number[]>([])

interface SubjectBreakdown {
  subjects: Array<{ name: string; avgMastery: number }>
}

const fetchData = async () => {
  loading.value = true
  error.value = null
  try {
    const data = await $api<SubjectBreakdown>('/admin/mastery/overview/subjects')
    if (data.subjects?.length) {
      subjects.value = data.subjects.map(s => s.name)
      averages.value = data.subjects.map(s => Math.round(s.avgMastery * 100))
    }
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load subject breakdown'
    console.error('Failed to fetch subject breakdown:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchData)

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'

const chartOptions = computed(() => ({
  chart: {
    type: 'radar' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  colors: ['rgb(var(--v-theme-primary))'],
  fill: {
    opacity: 0.2,
  },
  stroke: {
    width: 2,
  },
  markers: {
    size: 4,
    hover: { size: 6 },
  },
  xaxis: {
    categories: subjects.value,
    labels: {
      style: {
        colors: Array(subjects.value.length).fill(labelColor),
        fontSize: '13px',
      },
    },
  },
  yaxis: {
    min: 0,
    max: 100,
    tickAmount: 5,
    labels: {
      formatter(val: number) {
        return `${val}%`
      },
    },
  },
  dataLabels: { enabled: false },
  legend: { show: false },
}))

const chartSeries = computed(() => [
  { name: 'Avg Mastery', data: averages.value },
])

defineExpose({ refresh: fetchData })
</script>

<template>
  <VCard :loading="loading">
    <VCardItem title="Subject Breakdown">
      <template #subtitle>
        Average mastery per subject
      </template>
    </VCardItem>

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
        v-if="averages.length"
        type="radar"
        height="300"
        :options="chartOptions"
        :series="chartSeries"
      />

      <div
        v-else-if="!loading"
        class="d-flex align-center justify-center"
        style="min-height: 300px;"
      >
        <span class="text-disabled">No subject data available</span>
      </div>
    </VCardText>
  </VCard>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
