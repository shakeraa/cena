<!-- =============================================================================
     PRR-322 — Mock-exam run cost dashboard

     Operator-facing finops view of per-run + per-day cost. Three sections:
       1. Projection card — average $/run × 14d run-rate × 30 = projected
          monthly burn at current pricing. "Insufficient data" when fewer
          than 1 run in the trailing 14d window — honest, not a fake zero.
       2. Daily trend chart — 30-day cost-per-day stacked area (CAS / LLM /
          OCR). Defaults to 30 days; URL-bindable later.
       3. Per-run table — recent N rows, sortable, filterable by exam code.

     Backed by /api/admin/mock-exam-runs/cost/{runs|daily|projection}.
     Auth: ModeratorOrAbove (server-side) + the page-level CASL gate below.

     ADR alignment:
       * No streak / loss-aversion mechanics (GD-004) — this is an admin-
         facing operations page, not student UI; the ban applies anyway.
       * Labels match data (per shaker memory) — when LLM / OCR streams
         are 0 (which they are today on the mock-exam path), the chart
         segments collapse to zero-width and the table shows "—" not "0%".
============================================================================= -->
<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import VueApexCharts from 'vue3-apexcharts'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Content',
  },
})

interface CostRow {
  runId:           string
  studentId:       string
  examCode:        string
  studentTenant:   string | null
  casCallsCount:   number
  llmTokensInput:  number
  llmTokensOutput: number
  ocrCallsCount:   number
  casCostUsd:      number
  llmCostUsd:      number
  ocrCostUsd:      number
  totalUsd:        number
  computedAt:      string
}

interface DailyPoint {
  date:            string
  runCount:        number
  casCallsCount:   number
  llmTokensInput:  number
  llmTokensOutput: number
  ocrCallsCount:   number
  casCostUsd:      number
  llmCostUsd:      number
  ocrCostUsd:      number
  totalUsd:        number
}

interface ProjectionResp {
  windowDays:        number
  runsInWindow:      number
  avgUsdPerRun:      number | null
  projected30DayUsd: number | null
  sufficientData:    boolean
}

interface ListResp { total: number, limit: number, items: CostRow[] }
interface DailyResp { days: number, since: string, points: DailyPoint[] }

const loading      = ref(true)
const error        = ref<string | null>(null)
const projection   = ref<ProjectionResp | null>(null)
const daily        = ref<DailyPoint[]>([])
const rows         = ref<CostRow[]>([])
const examCodeFilter = ref<string | null>(null)
const dailyDays      = ref(30)

const fetchAll = async () => {
  loading.value = true
  error.value = null
  try {
    const [proj, d, list] = await Promise.all([
      $api<ProjectionResp>('/api/admin/mock-exam-runs/cost/projection'),
      $api<DailyResp>(`/api/admin/mock-exam-runs/cost/daily?days=${dailyDays.value}`),
      $api<ListResp>(
        examCodeFilter.value
          ? `/api/admin/mock-exam-runs/cost/runs?examCode=${encodeURIComponent(examCodeFilter.value)}`
          : '/api/admin/mock-exam-runs/cost/runs'),
    ])
    projection.value = proj
    daily.value      = d.points
    rows.value       = list.items
  } catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Failed to load cost telemetry'
  } finally {
    loading.value = false
  }
}

onMounted(fetchAll)
watch([examCodeFilter, dailyDays], fetchAll)

// ── Chart options for the daily-trend area chart. Stacked CAS / LLM / OCR
// — when LLM and OCR are zero (today's reality on mock-exam path), only the
// CAS series renders; the others collapse to zero-height and the legend
// shows them with a $0 total. Honest visual labeling.
const chartSeries = computed(() => [
  { name: 'CAS', data: daily.value.map(d => Number(d.casCostUsd)) },
  { name: 'LLM', data: daily.value.map(d => Number(d.llmCostUsd)) },
  { name: 'OCR', data: daily.value.map(d => Number(d.ocrCostUsd)) },
])

const chartOptions = computed(() => ({
  chart: { type: 'area', stacked: true, toolbar: { show: false } },
  dataLabels: { enabled: false },
  stroke: { curve: 'smooth', width: 2 },
  xaxis: {
    categories: daily.value.map(d => d.date.slice(0, 10)),
    type: 'category',
  },
  yaxis: {
    labels: { formatter: (v: number) => `$${v.toFixed(4)}` },
  },
  tooltip: { y: { formatter: (v: number) => `$${v.toFixed(6)}` } },
  legend: { position: 'top' },
  colors: ['#7367F0', '#28C76F', '#FF9F43'],   // Vuexy primary / success / warning
}))

const headers = [
  { title: 'Run',         key: 'runId',       sortable: false },
  { title: 'Exam',        key: 'examCode' },
  { title: 'CAS calls',   key: 'casCallsCount', align: 'end' as const },
  { title: 'LLM in/out',  key: 'llm',          align: 'end' as const, sortable: false },
  { title: 'OCR',         key: 'ocrCallsCount', align: 'end' as const },
  { title: 'Total $',     key: 'totalUsd',     align: 'end' as const },
  { title: 'Submitted',   key: 'computedAt' },
]

const fmtUsd = (v: number) => `$${v.toFixed(6)}`
const fmtIntOrDash = (v: number) => v > 0 ? v.toString() : '—'
const shortRun = (id: string) => id.length > 16 ? `${id.slice(0, 8)}…${id.slice(-6)}` : id
</script>

<template>
  <div>
    <!-- Page header -->
    <VCard class="mb-4">
      <VCardText class="d-flex align-center justify-space-between flex-wrap gap-3">
        <div>
          <h5 class="text-h5">Mock-exam Cost Telemetry</h5>
          <span class="text-body-2 text-medium-emphasis">
            Per-run cost attribution + 30-day projection. Rates configured under
            <code>Cena:MockExamCostRates</code>; reconcile against vendor invoices monthly.
          </span>
        </div>
        <VBtn
          variant="tonal"
          color="primary"
          :loading="loading"
          @click="fetchAll">
          <VIcon icon="ri-refresh-line" start />
          Refresh
        </VBtn>
      </VCardText>
    </VCard>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-4">
      {{ error }}
    </VAlert>

    <!-- Projection card -->
    <VRow class="mb-4">
      <VCol cols="12" md="4">
        <VCard>
          <VCardText>
            <div class="text-overline text-medium-emphasis">30-day projection</div>
            <div class="text-h4 mt-1">
              <template v-if="projection?.sufficientData">
                ${{ Number(projection.projected30DayUsd).toFixed(2) }}
              </template>
              <template v-else>
                <span class="text-disabled">—</span>
              </template>
            </div>
            <div class="text-caption text-medium-emphasis mt-2">
              <template v-if="projection?.sufficientData">
                from {{ projection.runsInWindow }} runs in trailing
                {{ projection.windowDays }} days
                · avg ${{ Number(projection.avgUsdPerRun).toFixed(6) }}/run
              </template>
              <template v-else>
                Insufficient data — fewer than 1 run in the last 14 days.
              </template>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Daily trend chart -->
    <VCard class="mb-4">
      <VCardText>
        <div class="d-flex align-center justify-space-between mb-3">
          <h6 class="text-h6">Daily cost trend</h6>
          <VSelect
            v-model="dailyDays"
            :items="[7, 14, 30, 60, 90]"
            label="Days"
            density="compact"
            variant="outlined"
            hide-details
            style="max-inline-size: 120px;" />
        </div>
        <div v-if="loading" class="text-center pa-6">
          <VProgressCircular indeterminate color="primary" />
        </div>
        <div v-else-if="!daily.length" class="text-center pa-6 text-disabled">
          No cost data in the selected window.
        </div>
        <VueApexCharts
          v-else
          type="area"
          height="320"
          :options="chartOptions"
          :series="chartSeries" />
      </VCardText>
    </VCard>

    <!-- Per-run table -->
    <VCard>
      <VCardText>
        <div class="d-flex align-center justify-space-between mb-3">
          <h6 class="text-h6">Recent runs</h6>
          <VTextField
            v-model="examCodeFilter"
            label="Filter by exam code"
            placeholder="806 / 807 / 036…"
            density="compact"
            variant="outlined"
            clearable
            hide-details
            style="max-inline-size: 220px;" />
        </div>
        <VDataTable
          :items="rows"
          :headers="headers"
          :loading="loading"
          density="compact"
          item-key="runId"
          no-data-text="No mock-exam runs in the selected window">
          <template #item.runId="{ item }">
            <code :title="item.runId">{{ shortRun(item.runId) }}</code>
          </template>
          <template #item.llm="{ item }">
            <template v-if="item.llmTokensInput > 0 || item.llmTokensOutput > 0">
              {{ item.llmTokensInput }}/{{ item.llmTokensOutput }}
            </template>
            <template v-else>—</template>
          </template>
          <template #item.ocrCallsCount="{ item }">
            {{ fmtIntOrDash(item.ocrCallsCount) }}
          </template>
          <template #item.totalUsd="{ item }">
            {{ fmtUsd(item.totalUsd) }}
          </template>
          <template #item.computedAt="{ item }">
            {{ new Date(item.computedAt).toLocaleString() }}
          </template>
        </VDataTable>
      </VCardText>
    </VCard>
  </div>
</template>
