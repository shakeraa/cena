<script setup lang="ts">
import { $api } from '@/utils/api'
import FocusHeatmap from '@/views/apps/focus/FocusHeatmap.vue'

definePage({ meta: { action: 'read', subject: 'Focus' } })

const route = useRoute('apps-focus-class-id')
const classId = computed(() => route.params.id)

// --- Class data ---
interface ClassFocusData {
  className: string
  studentCount: number
  avgFocusScore: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const classData = ref<ClassFocusData>({ className: '', studentCount: 0, avgFocusScore: 0 })

// --- Aggregated trends ---
interface TrendPoint {
  date: string
  avgFocusScore: number
}

const trendData = ref<TrendPoint[]>([])

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const trendChartOptions = computed(() => ({
  chart: {
    type: 'line' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
    zoom: { enabled: false },
  },
  stroke: { curve: 'smooth' as const, width: 3 },
  colors: ['#56CA00'],
  markers: { size: 3, colors: '#fff', strokeColors: '#56CA00' },
  grid: { strokeDashArray: 8, borderColor },
  xaxis: {
    categories: trendData.value.map(p => p.date),
    labels: { style: { colors: labelColor, fontSize: '12px' } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    min: 0,
    max: 100,
    tickAmount: 5,
    labels: {
      style: { colors: labelColor, fontSize: '13px' },
      formatter: (v: number) => `${Math.round(v)}`,
    },
  },
  tooltip: { y: { formatter: (v: number) => `${Math.round(v)} / 100` } },
}))

const trendChartSeries = computed(() => [
  { name: 'Class Avg Focus', data: trendData.value.map(p => p.avgFocusScore) },
])

// --- Subject correlation ---
interface SubjectFocus {
  subject: string
  avgFocusScore: number
}

const subjectData = ref<SubjectFocus[]>([])

const subjectChartOptions = computed(() => ({
  chart: {
    type: 'bar' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  plotOptions: {
    bar: { borderRadius: 4, columnWidth: '50%' },
  },
  colors: ['#9055FD'],
  dataLabels: { enabled: true, formatter: (v: number) => `${Math.round(v)}` },
  xaxis: {
    categories: subjectData.value.map(s => s.subject),
    labels: { style: { colors: labelColor, fontSize: '13px' } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    min: 0,
    max: 100,
    tickAmount: 5,
    labels: {
      style: { colors: labelColor, fontSize: '13px' },
      formatter: (v: number) => `${Math.round(v)}`,
    },
    title: { text: 'Avg Focus Score', style: { color: labelColor, fontSize: '13px' } },
  },
  grid: { strokeDashArray: 8, borderColor },
  tooltip: { y: { formatter: (v: number) => `${Math.round(v)} / 100` } },
}))

const subjectChartSeries = computed(() => [
  { name: 'Avg Focus', data: subjectData.value.map(s => s.avgFocusScore) },
])

// --- Fetch ---
const fetchClassData = async () => {
  loading.value = true
  try {
    const data = await $api(`/admin/focus/classes/${classId.value}`)

    classData.value = {
      className: data.className ?? '',
      studentCount: data.studentCount ?? 0,
      avgFocusScore: data.avgFocusScore ?? 0,
    }

    trendData.value = (data.trends ?? []).map((t: any) => ({
      date: t.date,
      avgFocusScore: t.avgFocusScore ?? 0,
    }))

    subjectData.value = (data.subjectCorrelation ?? []).map((s: any) => ({
      subject: s.subject ?? '',
      avgFocusScore: s.avgFocusScore ?? 0,
    }))

    error.value = null
  }
  catch (err: any) {
    console.error('Failed to fetch class focus data:', err)
    error.value = err.message ?? 'Failed to load class data'
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchClassData)

const focusScoreColor = (score: number): string => {
  if (score >= 70) return 'success'
  if (score >= 40) return 'warning'
  return 'error'
}
</script>

<template>
  <div>
    <!-- Header -->
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <div class="d-flex align-center gap-x-2 mb-1">
          <VBtn
            icon
            variant="text"
            size="small"
            :to="{ name: 'apps-focus-dashboard' }"
          >
            <VIcon icon="tabler-arrow-left" />
          </VBtn>
          <h4 class="text-h4">
            {{ classData.className || `Class #${classId}` }}
          </h4>
          <VChip
            v-if="!loading"
            :color="focusScoreColor(classData.avgFocusScore)"
            label
            size="small"
            class="ms-2"
          >
            Avg: {{ classData.avgFocusScore }}
          </VChip>
        </div>
        <div class="text-body-1 ms-10">
          {{ classData.studentCount }} students — Class-level focus analytics
        </div>
      </div>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
    >
      {{ error }}
    </VAlert>

    <VRow
      v-if="!error"
      class="match-height"
    >
      <!-- Heatmap -->
      <VCol cols="12">
        <FocusHeatmap :class-id="classId" />
      </VCol>

      <!-- Aggregated Trends -->
      <VCol
        cols="12"
        md="7"
      >
        <VCard :loading="loading">
          <VCardItem
            title="Class Focus Trend"
            subtitle="Average focus score over time"
          />
          <VCardText>
            <VueApexCharts
              v-if="trendData.length > 0"
              type="line"
              height="300"
              :options="trendChartOptions"
              :series="trendChartSeries"
            />
            <div
              v-else-if="!loading"
              class="d-flex justify-center py-8"
            >
              <span class="text-body-1 text-disabled">No trend data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Subject Correlation -->
      <VCol
        cols="12"
        md="5"
      >
        <VCard :loading="loading">
          <VCardItem
            title="Focus by Subject"
            subtitle="Average focus score per subject"
          />
          <VCardText>
            <VueApexCharts
              v-if="subjectData.length > 0"
              type="bar"
              height="300"
              :options="subjectChartOptions"
              :series="subjectChartSeries"
            />
            <div
              v-else-if="!loading"
              class="d-flex justify-center py-8"
            >
              <span class="text-body-1 text-disabled">No subject data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
