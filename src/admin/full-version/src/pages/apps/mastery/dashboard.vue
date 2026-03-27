<script setup lang="ts">
import MasteryDistributionChart from '@/views/apps/mastery/MasteryDistributionChart.vue'
import SubjectRadarChart from '@/views/apps/mastery/SubjectRadarChart.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Mastery',
  },
})

// --- Overview data ---
interface OverviewData {
  totalStudents: number
  avgMastery: number
  conceptsMasteredThisWeek: number
  conceptsMasteredLastWeek: number
  atRiskCount: number
  learningVelocityTrend: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const overview = ref<OverviewData | null>(null)

const fetchOverview = async () => {
  loading.value = true
  error.value = null
  try {
    overview.value = await $api<OverviewData>('/admin/mastery/overview')
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load mastery overview'
    console.error('Failed to fetch mastery overview:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchOverview)

const velocityDisplay = computed(() => {
  if (!overview.value) return '-- '
  return String(overview.value.conceptsMasteredThisWeek)
})

const velocityTrendPercent = computed(() => {
  if (!overview.value) return 0
  const prev = overview.value.conceptsMasteredLastWeek
  if (prev === 0) return 0
  return Math.round(((overview.value.conceptsMasteredThisWeek - prev) / prev) * 100)
})

const atRiskCount = computed(() => overview.value?.atRiskCount ?? 0)

const widgetCards = computed(() => [
  {
    icon: 'tabler-chart-histogram',
    color: 'primary',
    title: 'Mastery Distribution',
    value: overview.value ? `${Math.round((overview.value.avgMastery ?? 0) * 100)}% avg` : '--',
    subtitle: `${overview.value?.totalStudents ?? 0} students tracked`,
    isHover: false,
  },
  {
    icon: 'tabler-rocket',
    color: 'success',
    title: 'Learning Velocity',
    value: `${velocityDisplay.value}/wk`,
    subtitle: 'Concepts mastered this week',
    change: velocityTrendPercent.value,
    isHover: false,
  },
  {
    icon: 'tabler-alert-triangle',
    color: 'error',
    title: 'At-Risk Students',
    value: String(atRiskCount.value),
    subtitle: 'Below 0.3 mastery threshold',
    isHover: false,
  },
])

// --- At-Risk Students Table ---
interface AtRiskStudent {
  studentId: string
  studentName: string
  avgMastery: number
  trend: 'up' | 'down' | 'flat'
  daysSinceImprovement: number
}

const atRiskLoading = ref(true)
const atRiskError = ref<string | null>(null)
const atRiskStudents = ref<AtRiskStudent[]>([])

const fetchAtRisk = async () => {
  atRiskLoading.value = true
  atRiskError.value = null
  try {
    const data = await $api<{ students: AtRiskStudent[] }>('/admin/mastery/at-risk')
    atRiskStudents.value = data.students ?? []
  }
  catch (err: any) {
    atRiskError.value = err.message ?? 'Failed to load at-risk students'
    console.error('Failed to fetch at-risk students:', err)
  }
  finally {
    atRiskLoading.value = false
  }
}

onMounted(fetchAtRisk)

const atRiskHeaders = [
  { title: 'Student', key: 'studentName' },
  { title: 'Avg Mastery', key: 'avgMastery' },
  { title: 'Trend', key: 'trend', sortable: false },
  { title: 'Days Since Improvement', key: 'daysSinceImprovement' },
  { title: 'Action', key: 'action', sortable: false },
]

const masteryColor = (val: number): string => {
  if (val >= 0.8) return 'success'
  if (val >= 0.4) return 'warning'
  return 'error'
}

const trendIcon = (trend: string): string => {
  if (trend === 'up') return 'tabler-trending-up'
  if (trend === 'down') return 'tabler-trending-down'
  return 'tabler-minus'
}

const trendColor = (trend: string): string => {
  if (trend === 'up') return 'success'
  if (trend === 'down') return 'error'
  return 'secondary'
}
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between flex-wrap gap-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Mastery & Learning Progress
        </h4>
        <p class="text-body-1 text-medium-emphasis mb-0">
          Overview of student mastery levels across all subjects
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

    <!-- Widget cards -->
    <VRow class="mb-2">
      <VCol
        v-for="(card, index) in widgetCards"
        :key="index"
        cols="12"
        md="4"
      >
        <VCard
          class="mastery-stat-card cursor-pointer"
          :loading="loading"
          :style="card.isHover
            ? `border-block-end-color: rgb(var(--v-theme-${card.color}))`
            : `border-block-end-color: rgba(var(--v-theme-${card.color}),0.38)`"
          @mouseenter="card.isHover = true"
          @mouseleave="card.isHover = false"
        >
          <VCardText>
            <div class="d-flex align-center gap-x-4 mb-1">
              <VAvatar
                variant="tonal"
                :color="card.color"
                rounded
              >
                <VIcon
                  :icon="card.icon"
                  size="28"
                />
              </VAvatar>
              <h4 class="text-h4">
                {{ card.value }}
              </h4>
            </div>
            <div class="text-body-1 mb-1">
              {{ card.title }}
            </div>
            <div class="d-flex gap-x-2 align-center">
              <span
                v-if="card.change !== undefined"
                class="text-h6"
              >
                {{ card.change > 0 ? '+' : '' }}{{ card.change }}%
              </span>
              <span class="text-sm text-disabled">{{ card.subtitle }}</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Charts row -->
    <VRow class="match-height mb-2">
      <VCol
        cols="12"
        md="6"
      >
        <MasteryDistributionChart />
      </VCol>
      <VCol
        cols="12"
        md="6"
      >
        <SubjectRadarChart />
      </VCol>
    </VRow>

    <!-- At-Risk Students Table -->
    <VCard>
      <VCardItem title="At-Risk Students">
        <template #subtitle>
          Students below 0.3 mastery threshold
        </template>
      </VCardItem>

      <VDivider />

      <VAlert
        v-if="atRiskError"
        type="error"
        variant="tonal"
        class="ma-4"
      >
        {{ atRiskError }}
      </VAlert>

      <VDataTable
        :headers="atRiskHeaders"
        :items="atRiskStudents"
        :loading="atRiskLoading"
        item-value="studentId"
        class="text-no-wrap"
      >
        <template #item.avgMastery="{ item }">
          <div
            class="d-flex align-center gap-x-3"
            style="min-inline-size: 180px;"
          >
            <VProgressLinear
              :model-value="item.avgMastery * 100"
              :color="masteryColor(item.avgMastery)"
              rounded
              :height="8"
              class="flex-grow-1"
            />
            <span class="text-body-2 font-weight-medium">
              {{ (item.avgMastery * 100).toFixed(0) }}%
            </span>
          </div>
        </template>

        <template #item.trend="{ item }">
          <VIcon
            :icon="trendIcon(item.trend)"
            :color="trendColor(item.trend)"
            size="20"
          />
        </template>

        <template #item.daysSinceImprovement="{ item }">
          <VChip
            :color="item.daysSinceImprovement > 14 ? 'error' : item.daysSinceImprovement > 7 ? 'warning' : 'secondary'"
            label
            size="small"
          >
            {{ item.daysSinceImprovement }}d
          </VChip>
        </template>

        <template #item.action="{ item }">
          <VBtn
            variant="text"
            color="primary"
            size="small"
            :to="{ path: `/apps/mastery/student/${item.studentId}` }"
          >
            View Detail
          </VBtn>
        </template>

        <template #no-data>
          <div class="text-center pa-4 text-disabled">
            No at-risk students found
          </div>
        </template>
      </VDataTable>
    </VCard>
  </div>
</template>

<style lang="scss" scoped>
@use "@core/scss/base/mixins" as mixins;

.mastery-stat-card {
  border-block-end-style: solid;
  border-block-end-width: 2px;

  &:hover {
    border-block-end-width: 3px;
    margin-block-end: -1px;

    @include mixins.elevation(8);

    transition: all 0.1s ease-out;
  }
}

.skin--bordered {
  .mastery-stat-card {
    border-block-end-width: 2px;

    &:hover {
      border-block-end-width: 3px;
      margin-block-end: -2px;
      transition: all 0.1s ease-out;
    }
  }
}
</style>
