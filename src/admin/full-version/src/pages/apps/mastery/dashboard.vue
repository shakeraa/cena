<script setup lang="ts">
// prr-013 Phase 2 retirement 2026-04-20:
// The "At-Risk Students" widget card and table were removed under ADR-0003 +
// RDY-080. Teacher-visible "student needs intervention" data is now
// session-scoped only (SessionRiskAssessment, rendered inside the live
// session surface — never in a daily dashboard). A follow-up RDY task will
// wire that session-scoped teacher widget; for now the dashboard shows
// only the class-aggregate distribution + velocity.
import MasteryDistributionChart from '@/views/apps/mastery/MasteryDistributionChart.vue'
import SubjectRadarChart from '@/views/apps/mastery/SubjectRadarChart.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Mastery',
  },
})

// --- Overview data (matches MasteryOverviewResponse DTO after prr-013 retirement) ---
interface DistributionPoint {
  level: string
  count: number
  percentage: number
}
interface SubjectMastery {
  subject: string
  avgMasteryLevel: number
  conceptCount: number
  masteredCount: number
}
interface OverviewData {
  distribution: DistributionPoint[]
  subjectBreakdown: SubjectMastery[]
  learningVelocity: number
  learningVelocityChange: number
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

const avgMastery = computed(() => {
  if (!overview.value?.subjectBreakdown?.length) return 0
  const sum = overview.value.subjectBreakdown.reduce((acc, s) => acc + (s.avgMasteryLevel ?? 0), 0)
  return sum / overview.value.subjectBreakdown.length
})

const totalStudents = computed(() => {
  if (!overview.value?.distribution?.length) return 0
  return overview.value.distribution.reduce((acc, d) => acc + (d.count ?? 0), 0)
})

const velocityDisplay = computed(() => {
  if (!overview.value) return '--'
  const val = overview.value.learningVelocity
  return val != null ? String(Math.round(val * 10) / 10) : '--'
})

const velocityTrendPercent = computed(() => {
  if (!overview.value) return 0
  return Math.round((overview.value.learningVelocityChange ?? 0) * 100)
})

const widgetCards = computed(() => [
  {
    icon: 'tabler-chart-histogram',
    color: 'primary',
    title: 'Mastery Distribution',
    value: overview.value ? `${Math.round(avgMastery.value * 100)}% avg` : '--',
    subtitle: `${totalStudents.value} students tracked`,
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
])
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
        md="6"
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

    <!--
      prr-013 Phase 2 retirement 2026-04-20:
      The dashboard-level "At-Risk Students" table was removed here. That
      list violated ADR-0003 (session-scope) and RDY-080 (in-surface only):
      it persisted "student needs intervention" labels across days, surfaced
      them in a cross-session dashboard, and could have fed external
      channels (parent SMS, SIS pass-back). The session-scoped replacement
      — SessionRiskAssessment rendered inside the live teacher session view
      — is tracked as a follow-up (RDY-080 / Epic-PRR-A Sprint-2).
    -->
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
