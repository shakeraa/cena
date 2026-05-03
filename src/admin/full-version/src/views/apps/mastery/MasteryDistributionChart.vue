<script setup lang="ts">
import { $api } from '@/utils/api'

const loading = ref(true)
const error = ref<string | null>(null)
const series = ref<number[]>([])
const categories = ref<string[]>(['0-0.2', '0.2-0.4', '0.4-0.6', '0.6-0.8', '0.8-1.0'])

interface DistributionData {
  bands: Array<{ label: string; count: number }>
}

const fetchData = async () => {
  loading.value = true
  error.value = null
  try {
    const data = await $api<DistributionData>('/admin/mastery/overview/distribution')
    if (data.bands?.length) {
      categories.value = data.bands.map(b => b.label)
      series.value = data.bands.map(b => b.count)
    }
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load mastery distribution'
    console.error('Failed to fetch mastery distribution:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchData)

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const chartOptions = computed(() => ({
  chart: {
    type: 'bar' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  plotOptions: {
    bar: {
      columnWidth: '60%',
      borderRadius: 4,
      borderRadiusApplication: 'end' as const,
      distributed: true,
    },
  },
  colors: ['#EA5455', '#FF9F43', '#FFD54F', '#28C76F', '#00CFE8'],
  dataLabels: { enabled: true },
  legend: { show: false },
  grid: {
    strokeDashArray: 8,
    borderColor,
    padding: { bottom: -10 },
  },
  xaxis: {
    categories: categories.value,
    labels: {
      style: {
        colors: labelColor,
        fontSize: '13px',
        fontWeight: 400,
      },
    },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    labels: {
      style: {
        colors: labelColor,
        fontSize: '13px',
        fontWeight: 400,
      },
    },
  },
  tooltip: {
    y: {
      formatter(val: number) {
        return `${val} students`
      },
    },
  },
}))

const chartSeries = computed(() => [
  { name: 'Students', data: series.value },
])

defineExpose({ refresh: fetchData })
</script>

<template>
  <VCard :loading="loading">
    <VCardItem title="Mastery Distribution">
      <template #subtitle>
        Students by mastery band
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
        v-if="series.length"
        type="bar"
        height="300"
        :options="chartOptions"
        :series="chartSeries"
      />

      <div
        v-else-if="!loading"
        class="d-flex align-center justify-center"
        style="min-height: 300px;"
      >
        <span class="text-disabled">No distribution data available</span>
      </div>
    </VCardText>
  </VCard>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
