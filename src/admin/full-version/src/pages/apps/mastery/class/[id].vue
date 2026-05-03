<script setup lang="ts">
import MasteryGrid from '@/views/apps/mastery/MasteryGrid.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Mastery',
  },
})

const route = useRoute('apps-mastery-class-id')
const classId = computed(() => route.params.id)

// --- Class overview ---
interface ClassOverview {
  classId: string
  className: string
  teacherName: string
  studentCount: number
  avgMastery: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const classData = ref<ClassOverview | null>(null)

// --- Mastery Grid data ---
interface MasteryCell {
  conceptId: string
  mastery: number | null
}

interface StudentRow {
  studentId: string
  studentName: string
  cells: MasteryCell[]
}

interface ConceptColumn {
  id: string
  name: string
}

interface ClassMasteryData {
  class: ClassOverview
  concepts: ConceptColumn[]
  students: StudentRow[]
  conceptDifficulty: Array<{ conceptName: string; avgMastery: number }>
  pacingRecommendations: Array<{ type: 'success' | 'warning' | 'info' | 'error'; message: string }>
}

const concepts = ref<ConceptColumn[]>([])
const students = ref<StudentRow[]>([])
const conceptDifficulty = ref<Array<{ conceptName: string; avgMastery: number }>>([])
const pacingRecommendations = ref<Array<{ type: 'success' | 'warning' | 'info' | 'error'; message: string }>>([])

const fetchClassData = async () => {
  loading.value = true
  error.value = null
  try {
    const data = await $api<ClassMasteryData>(`/admin/mastery/classes/${classId.value}`)
    classData.value = data.class
    concepts.value = data.concepts ?? []
    students.value = data.students ?? []
    conceptDifficulty.value = data.conceptDifficulty ?? []
    pacingRecommendations.value = data.pacingRecommendations ?? []
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load class mastery data'
    console.error('Failed to fetch class mastery:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchClassData)

// --- Concept Difficulty Chart ---
const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const sortedDifficulty = computed(() =>
  [...conceptDifficulty.value].sort((a, b) => a.avgMastery - b.avgMastery),
)

const difficultyChartOptions = computed(() => ({
  chart: {
    type: 'bar' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  plotOptions: {
    bar: {
      horizontal: true,
      borderRadius: 4,
      barHeight: '60%',
    },
  },
  colors: sortedDifficulty.value.map(d => {
    if (d.avgMastery >= 0.8) return 'rgb(var(--v-theme-success))'
    if (d.avgMastery >= 0.4) return 'rgb(var(--v-theme-warning))'
    return 'rgb(var(--v-theme-error))'
  }),
  dataLabels: {
    enabled: true,
    formatter(val: number) { return `${(val * 100).toFixed(0)}%` },
    style: { fontSize: '12px' },
  },
  xaxis: {
    min: 0,
    max: 1,
    labels: {
      style: { colors: labelColor, fontSize: '13px' },
      formatter(val: number) { return `${(val * 100).toFixed(0)}%` },
    },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    labels: {
      style: { colors: labelColor, fontSize: '12px' },
    },
  },
  grid: { strokeDashArray: 8, borderColor },
  legend: { show: false },
  tooltip: {
    y: {
      formatter(val: number) { return `${(val * 100).toFixed(1)}% avg mastery` },
    },
  },
}))

const difficultyChartSeries = computed(() => [{
  name: 'Avg Mastery',
  data: sortedDifficulty.value.map(d => ({
    x: d.conceptName,
    y: d.avgMastery,
    fillColor: d.avgMastery >= 0.8 ? '#28C76F' : d.avgMastery >= 0.4 ? '#FF9F43' : '#EA5455',
  })),
}])

const difficultyChartHeight = computed(() => Math.max(250, sortedDifficulty.value.length * 40))

// --- Pacing alert type mapping ---
const pacingAlertIcon = (type: string): string => {
  switch (type) {
    case 'success': return 'tabler-circle-check'
    case 'warning': return 'tabler-alert-triangle'
    case 'error': return 'tabler-alert-circle'
    default: return 'tabler-info-circle'
  }
}
</script>

<template>
  <div>
    <!-- Header -->
    <div class="d-flex align-center justify-space-between flex-wrap gap-4 mb-6">
      <div>
        <div class="d-flex align-center gap-2 mb-1">
          <VBtn
            icon
            variant="text"
            size="small"
            :to="{ path: '/apps/mastery/dashboard' }"
          >
            <VIcon icon="tabler-arrow-left" />
          </VBtn>
          <h4 class="text-h4">
            {{ classData?.className ?? 'Class Mastery' }}
          </h4>
        </div>
        <p
          v-if="classData"
          class="text-body-1 text-medium-emphasis mb-0 ms-10"
        >
          Teacher: {{ classData.teacherName }} &middot;
          {{ classData.studentCount }} students &middot;
          Avg mastery: {{ (classData.avgMastery * 100).toFixed(0) }}%
        </p>
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

    <!-- Mastery Grid -->
    <VCard class="mb-6">
      <VCardItem title="Mastery Grid">
        <template #subtitle>
          Students vs concepts mastery heatmap
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <MasteryGrid
          :students="students"
          :concepts="concepts"
          :loading="loading"
        />
      </VCardText>
    </VCard>

    <!-- Concept Difficulty -->
    <VCard class="mb-6">
      <VCardItem title="Concept Difficulty">
        <template #subtitle>
          Concepts sorted by class average mastery (lowest first)
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <VProgressLinear
          v-if="loading"
          indeterminate
          color="primary"
          class="mb-4"
        />

        <VueApexCharts
          v-if="sortedDifficulty.length"
          type="bar"
          :height="difficultyChartHeight"
          :options="difficultyChartOptions"
          :series="difficultyChartSeries"
        />

        <div
          v-else-if="!loading"
          class="d-flex align-center justify-center"
          style="min-height: 200px;"
        >
          <span class="text-disabled">No difficulty data available</span>
        </div>
      </VCardText>
    </VCard>

    <!-- Pacing Recommendations -->
    <VCard>
      <VCardItem title="Pacing Recommendations">
        <template #subtitle>
          Suggestions for class advancement
        </template>
      </VCardItem>

      <VDivider />

      <VCardText>
        <VProgressLinear
          v-if="loading"
          indeterminate
          color="primary"
          class="mb-4"
        />

        <div
          v-if="!loading && !pacingRecommendations.length"
          class="text-disabled"
        >
          No pacing recommendations available
        </div>

        <div
          v-else
          class="d-flex flex-column gap-4"
        >
          <VAlert
            v-for="(rec, index) in pacingRecommendations"
            :key="index"
            :type="rec.type"
            variant="tonal"
            :icon="pacingAlertIcon(rec.type)"
          >
            {{ rec.message }}
          </VAlert>
        </div>
      </VCardText>
    </VCard>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
