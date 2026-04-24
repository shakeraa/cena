<script setup lang="ts">
import StagnationMonitor from '@/views/apps/pedagogy/StagnationMonitor.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Pedagogy',
  },
})

// --- Interfaces ---
interface MethodologyEffectivenessRow {
  errorType: string
  methodologies: Record<string, number> // methodology name -> avg time-to-mastery (minutes)
}

interface MethodologyEffectivenessResponse {
  rows: MethodologyEffectivenessRow[]
  methodologies: string[] // ordered list of methodology names
}

interface SwitchTriggerRow {
  week: string
  triggers: Record<string, number> // trigger reason -> count
}

interface SwitchTriggerResponse {
  rows: SwitchTriggerRow[]
  reasons: string[]
}

interface StagnationTrendPoint {
  week: string
  events: number
  escalated: number
}

interface StagnationTrendResponse {
  points: StagnationTrendPoint[]
  escalationRate: number // 0-1 fraction
}

// --- Chart theme helpers ---
const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const methodologyColors = [
  '#7367F0', '#28C76F', '#FF9F43', '#EA5455', '#00CFE8',
  '#A8AAAE', '#FF6F61', '#6B5B95',
]

// --- Methodology Effectiveness (grouped bar) ---
const effectivenessLoading = ref(true)
const effectivenessError = ref<string | null>(null)
const effectivenessSeries = ref<ApexAxisChartSeries>([])
const effectivenessCategories = ref<string[]>([])

const fetchEffectiveness = async () => {
  effectivenessLoading.value = true
  effectivenessError.value = null
  try {
    const data = await $api<MethodologyEffectivenessResponse>(
      '/admin/pedagogy/methodology-effectiveness',
    )

    effectivenessCategories.value = data.rows.map(r => r.errorType)
    effectivenessSeries.value = data.methodologies.map((m, i) => ({
      name: m,
      data: data.rows.map(r => r.methodologies[m] ?? 0),
      color: methodologyColors[i % methodologyColors.length],
    }))
  }
  catch (err: any) {
    effectivenessError.value = err.message ?? 'Failed to load methodology effectiveness'
    console.error('Failed to fetch methodology effectiveness:', err)
  }
  finally {
    effectivenessLoading.value = false
  }
}

const effectivenessOptions = computed(() => ({
  chart: {
    type: 'bar' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  plotOptions: {
    bar: {
      columnWidth: '70%',
      borderRadius: 4,
      borderRadiusApplication: 'end' as const,
    },
  },
  dataLabels: { enabled: false },
  legend: {
    position: 'top' as const,
    labels: { colors: labelColor },
  },
  grid: {
    strokeDashArray: 8,
    borderColor,
    padding: { bottom: -10 },
  },
  xaxis: {
    categories: effectivenessCategories.value,
    labels: {
      style: { colors: labelColor, fontSize: '12px' },
      rotate: -45,
      rotateAlways: effectivenessCategories.value.length > 5,
    },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    title: { text: 'Avg Time-to-Mastery (min)', style: { color: labelColor } },
    labels: { style: { colors: labelColor, fontSize: '13px' } },
  },
  tooltip: {
    y: { formatter: (val: number) => `${val.toFixed(1)} min` },
  },
}))

// --- Switch Triggers (stacked bar) ---
const switchLoading = ref(true)
const switchError = ref<string | null>(null)
const switchSeries = ref<ApexAxisChartSeries>([])
const switchCategories = ref<string[]>([])

const switchTriggerColors = ['#7367F0', '#FF9F43', '#EA5455', '#28C76F', '#00CFE8', '#A8AAAE']

const fetchSwitchTriggers = async () => {
  switchLoading.value = true
  switchError.value = null
  try {
    const data = await $api<SwitchTriggerResponse>(
      '/admin/pedagogy/methodology-effectiveness',
    )

    // The switch trigger data comes from the same endpoint or a dedicated one.
    // Using methodology-effectiveness which may include switch data as nested.
    // If the backend provides a separate endpoint, update the URL here.
    // For now, we derive from the effectiveness endpoint's rows as well.
    // Actually, this should be a separate concern. Let's fetch from the same endpoint
    // and populate from the switchTriggers field if present.
    const switchData = await $api<SwitchTriggerResponse>(
      '/admin/pedagogy/switch-triggers',
    )

    switchCategories.value = switchData.rows.map(r => r.week)
    switchSeries.value = switchData.reasons.map((reason, i) => ({
      name: reason,
      data: switchData.rows.map(r => r.triggers[reason] ?? 0),
      color: switchTriggerColors[i % switchTriggerColors.length],
    }))
  }
  catch (err: any) {
    switchError.value = err.message ?? 'Failed to load switch triggers'
    console.error('Failed to fetch switch triggers:', err)
  }
  finally {
    switchLoading.value = false
  }
}

const switchOptions = computed(() => ({
  chart: {
    type: 'bar' as const,
    stacked: true,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  plotOptions: {
    bar: {
      columnWidth: '50%',
      borderRadius: 4,
      borderRadiusApplication: 'end' as const,
      borderRadiusWhenStacked: 'last' as const,
    },
  },
  dataLabels: { enabled: false },
  legend: {
    position: 'top' as const,
    labels: { colors: labelColor },
  },
  grid: {
    strokeDashArray: 8,
    borderColor,
    padding: { bottom: -10 },
  },
  xaxis: {
    categories: switchCategories.value,
    labels: { style: { colors: labelColor, fontSize: '12px' } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    title: { text: 'Switch Count', style: { color: labelColor } },
    labels: { style: { colors: labelColor, fontSize: '13px' } },
  },
}))

// --- Stagnation Trend (line chart) ---
const stagnationLoading = ref(true)
const stagnationError = ref<string | null>(null)
const stagnationSeries = ref<ApexAxisChartSeries>([])
const stagnationCategories = ref<string[]>([])
const escalationRate = ref(0)

const fetchStagnationTrend = async () => {
  stagnationLoading.value = true
  stagnationError.value = null
  try {
    const data = await $api<StagnationTrendResponse>(
      '/admin/pedagogy/stagnation-trend',
    )

    stagnationCategories.value = data.points.map(p => p.week)
    stagnationSeries.value = [
      {
        name: 'Stagnation Events',
        data: data.points.map(p => p.events),
      },
      {
        name: 'Escalated',
        data: data.points.map(p => p.escalated),
      },
    ]
    escalationRate.value = data.escalationRate
  }
  catch (err: any) {
    stagnationError.value = err.message ?? 'Failed to load stagnation trend'
    console.error('Failed to fetch stagnation trend:', err)
  }
  finally {
    stagnationLoading.value = false
  }
}

const stagnationOptions = computed(() => ({
  chart: {
    type: 'line' as const,
    parentHeightOffset: 0,
    toolbar: { show: false },
  },
  stroke: {
    width: [3, 2],
    dashArray: [0, 5],
    curve: 'smooth' as const,
  },
  colors: ['#7367F0', '#EA5455'],
  dataLabels: { enabled: false },
  legend: {
    position: 'top' as const,
    labels: { colors: labelColor },
  },
  grid: {
    strokeDashArray: 8,
    borderColor,
    padding: { bottom: -10 },
  },
  xaxis: {
    categories: stagnationCategories.value,
    labels: { style: { colors: labelColor, fontSize: '12px' } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    title: { text: 'Events', style: { color: labelColor } },
    labels: { style: { colors: labelColor, fontSize: '13px' } },
  },
  markers: { size: 4 },
}))

const escalationRateDisplay = computed(() =>
  `${(escalationRate.value * 100).toFixed(1)}%`,
)

const escalationRateColor = computed(() => {
  if (escalationRate.value >= 0.3) return 'error'
  if (escalationRate.value >= 0.15) return 'warning'
  return 'success'
})

// --- Fetch all data on mount ---
onMounted(() => {
  fetchEffectiveness()
  fetchSwitchTriggers()
  fetchStagnationTrend()
})
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between flex-wrap gap-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Methodology & Stagnation Analytics
        </h4>
        <p class="text-body-1 text-medium-emphasis mb-0">
          MCM graph effectiveness, methodology switches, and stagnation detection
        </p>
      </div>
    </div>

    <!-- Escalation Rate Card + Methodology Effectiveness -->
    <VRow class="match-height mb-2">
      <!-- Escalation Rate -->
      <VCol
        cols="12"
        md="3"
      >
        <VCard :loading="stagnationLoading">
          <VCardText class="d-flex flex-column align-center justify-center py-8">
            <VAvatar
              variant="tonal"
              :color="escalationRateColor"
              rounded
              size="56"
              class="mb-4"
            >
              <VIcon
                icon="tabler-alert-octagon"
                size="32"
              />
            </VAvatar>
            <h3 class="text-h3 mb-1">
              {{ escalationRateDisplay }}
            </h3>
            <span class="text-body-1 text-medium-emphasis">Escalation Rate</span>
            <span class="text-caption text-disabled mt-1">
              Stagnation events that required escalation
            </span>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Methodology Effectiveness (grouped bar) -->
      <VCol
        cols="12"
        md="9"
      >
        <VCard :loading="effectivenessLoading">
          <VCardItem title="Methodology Effectiveness">
            <template #subtitle>
              Average time-to-mastery by methodology per error type
            </template>
          </VCardItem>

          <VCardText>
            <VAlert
              v-if="effectivenessError"
              type="error"
              variant="tonal"
              class="mb-4"
            >
              {{ effectivenessError }}
            </VAlert>

            <VueApexCharts
              v-if="effectivenessSeries.length"
              type="bar"
              height="350"
              :options="effectivenessOptions"
              :series="effectivenessSeries"
            />

            <div
              v-else-if="!effectivenessLoading"
              class="d-flex align-center justify-center"
              style="min-height: 350px;"
            >
              <span class="text-disabled">No effectiveness data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Switch Triggers + Stagnation Trend -->
    <VRow class="match-height mb-2">
      <!-- Switch Triggers (stacked bar) -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="switchLoading">
          <VCardItem title="Methodology Switch Triggers">
            <template #subtitle>
              Reasons for methodology switches by week
            </template>
          </VCardItem>

          <VCardText>
            <VAlert
              v-if="switchError"
              type="error"
              variant="tonal"
              class="mb-4"
            >
              {{ switchError }}
            </VAlert>

            <VueApexCharts
              v-if="switchSeries.length"
              type="bar"
              height="300"
              :options="switchOptions"
              :series="switchSeries"
            />

            <div
              v-else-if="!switchLoading"
              class="d-flex align-center justify-center"
              style="min-height: 300px;"
            >
              <span class="text-disabled">No switch trigger data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Stagnation Events per Week (line chart) -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard :loading="stagnationLoading">
          <VCardItem title="Stagnation Events per Week">
            <template #subtitle>
              Weekly stagnation detections and escalations
            </template>
          </VCardItem>

          <VCardText>
            <VAlert
              v-if="stagnationError"
              type="error"
              variant="tonal"
              class="mb-4"
            >
              {{ stagnationError }}
            </VAlert>

            <VueApexCharts
              v-if="stagnationSeries.length"
              type="line"
              height="300"
              :options="stagnationOptions"
              :series="stagnationSeries"
            />

            <div
              v-else-if="!stagnationLoading"
              class="d-flex align-center justify-center"
              style="min-height: 300px;"
            >
              <span class="text-disabled">No stagnation data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Stagnation Monitor Table -->
    <StagnationMonitor />
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
