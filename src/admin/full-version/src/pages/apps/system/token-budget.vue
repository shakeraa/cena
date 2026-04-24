<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'manage', subject: 'System' } })

interface StudentUsage {
  studentId: string
  tokensToday: number
  dailyLimit: number
  percentUsed: number
  costEstimate: number
}

interface TokenBudgetResponse {
  totalTokensToday: number
  estimatedCost: number
  studentsNearLimit: number
  budgetUtilizationPercent: number
  students: StudentUsage[]
}

interface TrendPoint {
  date: string
  tokens: number
}

const loading = ref(true)
const budget = ref<TokenBudgetResponse>({
  totalTokensToday: 0,
  estimatedCost: 0,
  studentsNearLimit: 0,
  budgetUtilizationPercent: 0,
  students: [],
})
const trend = ref<TrendPoint[]>([])

const fetchData = async () => {
  loading.value = true
  try {
    const [budgetData, trendData] = await Promise.all([
      $api<TokenBudgetResponse>('/admin/system/token-budget'),
      $api<TrendPoint[]>('/admin/system/token-budget/trend?days=7'),
    ])
    budget.value = budgetData
    trend.value = trendData ?? []
  }
  catch (err: any) {
    console.error('Failed to fetch token budget:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchData)

const statCards = computed(() => [
  {
    title: "Today's Total Tokens",
    value: budget.value.totalTokensToday.toLocaleString(),
    icon: 'tabler-hash',
    color: 'primary',
  },
  {
    title: 'Estimated Cost',
    value: `$${budget.value.estimatedCost.toFixed(2)}`,
    icon: 'tabler-currency-dollar',
    color: 'success',
  },
  {
    title: 'Students Near Limit',
    value: String(budget.value.studentsNearLimit),
    icon: 'tabler-alert-triangle',
    color: 'warning',
  },
  {
    title: 'Budget Utilization',
    value: `${budget.value.budgetUtilizationPercent.toFixed(1)}%`,
    icon: 'tabler-gauge',
    color: 'info',
  },
])

const trendChartOptions = computed(() => ({
  chart: {
    type: 'line' as const,
    height: 350,
    toolbar: { show: true },
    zoom: { enabled: true },
  },
  stroke: {
    curve: 'smooth' as const,
    width: 3,
  },
  xaxis: {
    categories: trend.value.map(t => {
      const d = new Date(t.date)

      return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
    }),
    labels: {
      style: { colors: 'rgba(var(--v-theme-on-background), 0.5)' },
    },
  },
  yaxis: {
    title: { text: 'Tokens' },
    labels: {
      style: { colors: 'rgba(var(--v-theme-on-background), 0.5)' },
      formatter: (val: number) => val >= 1000 ? `${(val / 1000).toFixed(0)}k` : String(val),
    },
    min: 0,
  },
  colors: ['#7C4DFF'],
  tooltip: {
    y: {
      formatter: (val: number) => val.toLocaleString(),
    },
  },
  grid: {
    borderColor: 'rgba(var(--v-theme-on-background), 0.08)',
  },
}))

const trendChartSeries = computed(() => [
  {
    name: 'Tokens',
    data: trend.value.map(t => t.tokens),
  },
])

const usageColor = (percent: number) => {
  if (percent >= 80) return 'error'
  if (percent >= 50) return 'warning'

  return 'success'
}

const studentHeaders = [
  { title: 'Student ID', key: 'studentId' },
  { title: 'Tokens Today', key: 'tokensToday' },
  { title: 'Daily Limit', key: 'dailyLimit' },
  { title: '% Used', key: 'percentUsed' },
  { title: 'Cost Estimate', key: 'costEstimate' },
]

const sortedStudents = computed(() =>
  [...(budget.value.students ?? [])].sort((a, b) => b.tokensToday - a.tokensToday),
)
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h4 class="text-h4">
          Token Budget
        </h4>
        <p class="text-body-1 mb-0">
          Monitor AI token usage, costs, and student-level consumption
        </p>
      </div>
      <VBtn
        variant="tonal"
        color="primary"
        size="small"
        prepend-icon="tabler-refresh"
        :loading="loading"
        @click="fetchData"
      >
        Refresh
      </VBtn>
    </div>

    <!-- Stat Cards -->
    <VRow class="mb-6">
      <VCol
        v-for="stat in statCards"
        :key="stat.title"
        cols="12"
        sm="6"
        md="3"
      >
        <VCard :loading="loading">
          <VCardText>
            <div class="d-flex justify-space-between align-center">
              <div>
                <div class="text-body-2 text-medium-emphasis mb-1">
                  {{ stat.title }}
                </div>
                <h4 class="text-h4">
                  {{ stat.value }}
                </h4>
              </div>
              <VAvatar
                :color="stat.color"
                variant="tonal"
                rounded
                size="42"
              >
                <VIcon
                  :icon="stat.icon"
                  size="26"
                />
              </VAvatar>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Trend Chart -->
    <VCard
      :loading="loading"
      class="mb-6"
    >
      <VCardItem title="Daily Token Usage (Last 7 Days)">
        <template #subtitle>
          Total tokens consumed per day across all students
        </template>
      </VCardItem>
      <VCardText>
        <VueApexCharts
          v-if="trend.length"
          type="line"
          height="350"
          :options="trendChartOptions"
          :series="trendChartSeries"
        />
        <div
          v-else-if="!loading"
          class="d-flex justify-center align-center py-12"
        >
          <span class="text-body-1 text-disabled">No trend data available</span>
        </div>
      </VCardText>
    </VCard>

    <!-- Student Usage Table -->
    <VCard :loading="loading">
      <VCardItem title="Student Token Usage">
        <template #subtitle>
          Per-student breakdown sorted by usage
        </template>
      </VCardItem>
      <VCardText>
        <VDataTable
          :headers="studentHeaders"
          :items="sortedStudents"
          :items-per-page="15"
          density="comfortable"
        >
          <template #item.tokensToday="{ item }">
            {{ item.tokensToday.toLocaleString() }}
          </template>
          <template #item.dailyLimit="{ item }">
            {{ item.dailyLimit.toLocaleString() }}
          </template>
          <template #item.percentUsed="{ item }">
            <VChip
              :color="usageColor(item.percentUsed)"
              size="small"
              label
            >
              {{ item.percentUsed.toFixed(1) }}%
            </VChip>
          </template>
          <template #item.costEstimate="{ item }">
            ${{ item.costEstimate.toFixed(4) }}
          </template>
        </VDataTable>
      </VCardText>
    </VCard>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
