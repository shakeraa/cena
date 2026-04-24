<script setup lang="ts">
import { $api } from '@/utils/api'
import EquityAlerts from '@/views/apps/cultural/EquityAlerts.vue'

definePage({ meta: { action: 'read', subject: 'Pedagogy' } })

// ── Types ──

interface DistributionEntry {
  context: string
  count: number
  percentage: number
}

interface ResilienceEntry {
  context: string
  avgScore: number
}

interface MethodEffectiveness {
  method: string
  scores: Record<string, number> // culturalContext → success rate (0–100)
}

interface FocusPatternEntry {
  context: string
  avgFocusScore: number
  avgSessionMinutes: number
}

// ── State ──

const loading = ref(true)
const error = ref<string | null>(null)

const distribution = ref<DistributionEntry[]>([])
const resilience = ref<ResilienceEntry[]>([])
const methodEffectiveness = ref<MethodEffectiveness[]>([])
const focusPatterns = ref<FocusPatternEntry[]>([])

// ── Fetch ──

const fetchDistribution = async () => {
  try {
    const data = await $api<{ items: DistributionEntry[] }>('/admin/cultural/distribution')
    distribution.value = data.items ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch cultural distribution:', err)
    error.value = err.message ?? 'Failed to load cultural distribution'
  }
}

const fetchResilience = async () => {
  try {
    const data = await $api<{ items: ResilienceEntry[] }>('/admin/cultural/resilience')
    resilience.value = data.items ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch resilience data:', err)
    error.value = err.message ?? 'Failed to load resilience comparison'
  }
}

const fetchMethodEffectiveness = async () => {
  try {
    const data = await $api<{ methods: MethodEffectiveness[] }>('/admin/cultural/method-effectiveness')
    methodEffectiveness.value = data.methods ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch method effectiveness:', err)
    error.value = err.message ?? 'Failed to load method effectiveness'
  }
}

const fetchFocusPatterns = async () => {
  try {
    const data = await $api<{ items: FocusPatternEntry[] }>('/admin/cultural/focus-patterns')
    focusPatterns.value = data.items ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch focus patterns:', err)
    error.value = err.message ?? 'Failed to load focus patterns'
  }
}

const fetchAll = async () => {
  loading.value = true
  error.value = null
  await Promise.all([
    fetchDistribution(),
    fetchResilience(),
    fetchMethodEffectiveness(),
    fetchFocusPatterns(),
  ])
  loading.value = false
}

onMounted(fetchAll)

// ── Chart theme tokens ──

const labelColor = 'rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity))'
const borderColor = 'rgba(var(--v-border-color), var(--v-border-opacity))'

const contextColors: Record<string, string> = {
  HebrewDominant: '#7367F0',
  ArabicDominant: '#28C76F',
  Bilingual: '#FF9F43',
  Unknown: '#EA5455',
}

// ── Distribution Donut ──

const donutOptions = computed(() => ({
  chart: { type: 'donut' as const, parentHeightOffset: 0 },
  labels: distribution.value.map(d => d.context),
  colors: distribution.value.map(d => contextColors[d.context] ?? '#82868B'),
  legend: {
    position: 'bottom' as const,
    labels: { colors: labelColor },
  },
  plotOptions: {
    pie: {
      donut: {
        size: '65%',
        labels: {
          show: true,
          total: {
            show: true,
            label: 'Total Students',
            formatter: () => String(distribution.value.reduce((sum, d) => sum + d.count, 0)),
          },
        },
      },
    },
  },
  dataLabels: {
    enabled: true,
    formatter: (_val: number, opts: { seriesIndex: number }) => {
      const entry = distribution.value[opts.seriesIndex]
      return entry ? `${entry.percentage}%` : ''
    },
  },
  tooltip: {
    y: {
      formatter: (val: number) => `${val} students`,
    },
  },
}))

const donutSeries = computed(() => distribution.value.map(d => d.count))

// ── Resilience Grouped Bar ──

const resilienceOptions = computed(() => ({
  chart: { type: 'bar' as const, parentHeightOffset: 0, toolbar: { show: false } },
  plotOptions: {
    bar: { columnWidth: '50%', borderRadius: 4, borderRadiusApplication: 'end' as const },
  },
  colors: resilience.value.map(r => contextColors[r.context] ?? '#82868B'),
  dataLabels: { enabled: true, formatter: (val: number) => val.toFixed(1) },
  legend: { show: false },
  grid: { strokeDashArray: 8, borderColor, padding: { bottom: -10 } },
  xaxis: {
    categories: resilience.value.map(r => r.context),
    labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: {
    min: 0,
    max: 100,
    labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
  },
  tooltip: { y: { formatter: (val: number) => `${val.toFixed(1)} / 100` } },
}))

const resilienceSeries = computed(() => [
  { name: 'Resilience Score', data: resilience.value.map(r => r.avgScore) },
])

// ── Focus Patterns Grouped Bar ──

const focusOptions = computed(() => ({
  chart: { type: 'bar' as const, parentHeightOffset: 0, toolbar: { show: false } },
  plotOptions: {
    bar: { columnWidth: '45%', borderRadius: 4, borderRadiusApplication: 'end' as const },
  },
  colors: ['#00CFE8', '#FF9F43'],
  dataLabels: { enabled: false },
  legend: {
    show: true,
    labels: { colors: labelColor },
  },
  grid: { strokeDashArray: 8, borderColor, padding: { bottom: -10 } },
  xaxis: {
    categories: focusPatterns.value.map(f => f.context),
    labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: [
    {
      title: { text: 'Focus Score', style: { color: labelColor } },
      min: 0,
      max: 100,
      labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    },
    {
      opposite: true,
      title: { text: 'Avg Session (min)', style: { color: labelColor } },
      labels: { style: { colors: labelColor, fontSize: '13px', fontWeight: 400 } },
    },
  ],
  tooltip: {
    shared: true,
    intersect: false,
  },
}))

const focusSeries = computed(() => [
  { name: 'Avg Focus Score', data: focusPatterns.value.map(f => f.avgFocusScore) },
  { name: 'Avg Session (min)', data: focusPatterns.value.map(f => f.avgSessionMinutes) },
])

// ── Heatmap helpers for Method Effectiveness ──

const culturalContexts = computed(() => {
  const set = new Set<string>()
  methodEffectiveness.value.forEach(m => {
    Object.keys(m.scores).forEach(k => set.add(k))
  })
  return Array.from(set)
})

const effectivenessColor = (rate: number): string => {
  if (rate >= 80) return '#4CAF50'
  if (rate >= 60) return '#8BC34A'
  if (rate >= 40) return '#FFC107'
  if (rate >= 20) return '#FF9800'
  return '#F44336'
}

const effectivenessTextColor = (rate: number): string => {
  if (rate >= 60) return '#1a1a1a'
  return '#ffffff'
}
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Cultural Context Analytics
        </h4>
        <div class="text-body-1">
          Student distribution, resilience, and learning patterns by linguistic context
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

    <VRow class="match-height mb-2">
      <!-- Distribution Donut -->
      <VCol
        cols="12"
        md="5"
      >
        <VCard :loading="loading">
          <VCardItem title="Student Distribution by Cultural Context">
            <template #subtitle>
              Based on language signals (onboarding, interface, typing)
            </template>
          </VCardItem>

          <VCardText>
            <VueApexCharts
              v-if="donutSeries.length"
              type="donut"
              height="350"
              :options="donutOptions"
              :series="donutSeries"
            />

            <div
              v-else-if="!loading"
              class="d-flex align-center justify-center"
              style="min-height: 350px;"
            >
              <span class="text-disabled">No distribution data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Resilience Score Comparison -->
      <VCol
        cols="12"
        md="7"
      >
        <VCard :loading="loading">
          <VCardItem title="Resilience Score by Cultural Context">
            <template #subtitle>
              Average resilience score per linguistic group
            </template>
          </VCardItem>

          <VCardText>
            <VueApexCharts
              v-if="resilienceSeries[0].data.length"
              type="bar"
              height="350"
              :options="resilienceOptions"
              :series="resilienceSeries"
            />

            <div
              v-else-if="!loading"
              class="d-flex align-center justify-center"
              style="min-height: 350px;"
            >
              <span class="text-disabled">No resilience data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Methodology Effectiveness Heatmap Table -->
    <VRow class="mb-2">
      <VCol cols="12">
        <VCard :loading="loading">
          <VCardItem title="Methodology Effectiveness by Cultural Context">
            <template #subtitle>
              Success rate (%) for each teaching method across cultural groups
            </template>
          </VCardItem>

          <VCardText>
            <div
              v-if="methodEffectiveness.length > 0"
              class="heatmap-container"
            >
              <table class="heatmap-table">
                <thead>
                  <tr>
                    <th class="method-header">
                      Method
                    </th>
                    <th
                      v-for="ctx in culturalContexts"
                      :key="ctx"
                      class="context-header"
                    >
                      {{ ctx }}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="method in methodEffectiveness"
                    :key="method.method"
                  >
                    <td class="method-name">
                      {{ method.method }}
                    </td>
                    <td
                      v-for="ctx in culturalContexts"
                      :key="ctx"
                      class="heatmap-cell"
                      :style="{
                        backgroundColor: effectivenessColor(method.scores[ctx] ?? 0),
                        color: effectivenessTextColor(method.scores[ctx] ?? 0),
                      }"
                    >
                      {{ method.scores[ctx] != null ? `${method.scores[ctx]}%` : '--' }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div
              v-else-if="!loading"
              class="d-flex align-center justify-center py-8"
            >
              <span class="text-disabled">No method effectiveness data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Focus Patterns by Cultural Context -->
    <VRow class="match-height mb-2">
      <VCol
        cols="12"
        md="7"
      >
        <VCard :loading="loading">
          <VCardItem title="Focus Patterns by Cultural Context">
            <template #subtitle>
              Average focus scores and session duration across groups
            </template>
          </VCardItem>

          <VCardText>
            <VueApexCharts
              v-if="focusSeries[0].data.length"
              type="bar"
              height="350"
              :options="focusOptions"
              :series="focusSeries"
            />

            <div
              v-else-if="!loading"
              class="d-flex align-center justify-center"
              style="min-height: 350px;"
            >
              <span class="text-disabled">No focus pattern data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Equity Alerts -->
      <VCol
        cols="12"
        md="5"
      >
        <EquityAlerts />
      </VCol>
    </VRow>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>

<style lang="scss" scoped>
.heatmap-container {
  overflow-x: auto;
}

.heatmap-table {
  border-collapse: collapse;
  font-size: 0.8125rem;
  inline-size: 100%;

  th,
  td {
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    padding-block: 8px;
    padding-inline: 12px;
    text-align: center;
    white-space: nowrap;
  }

  .method-header,
  .method-name {
    position: sticky;
    background: rgb(var(--v-theme-surface));
    inset-inline-start: 0;
    text-align: start;
    z-index: 1;
  }

  .method-header {
    z-index: 2;
  }

  .context-header {
    background: rgb(var(--v-theme-surface));
    color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
    font-size: 0.75rem;
    font-weight: 500;
  }

  .heatmap-cell {
    font-size: 0.8125rem;
    font-weight: 600;
    min-inline-size: 100px;
    transition: opacity 0.15s;

    &:hover {
      opacity: 0.85;
    }
  }
}
</style>
