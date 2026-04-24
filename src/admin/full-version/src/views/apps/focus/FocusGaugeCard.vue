<script setup lang="ts">
import { $api } from '@/utils/api'

interface FocusOverview {
  avgFocusScore: number
  mindWanderingRate: number
  mindWanderingRateTrend: number
  microbreakCompliance: number
  microbreakComplianceTrend: number
  activeSessions: number
}

const props = defineProps<{
  pollIntervalMs?: number
}>()

const loading = ref(true)
const error = ref<string | null>(null)
const overview = ref<FocusOverview>({
  avgFocusScore: 0,
  mindWanderingRate: 0,
  mindWanderingRateTrend: 0,
  microbreakCompliance: 0,
  microbreakComplianceTrend: 0,
  activeSessions: 0,
})

let pollTimer: ReturnType<typeof setInterval> | null = null

const gaugeColor = computed(() => {
  const score = overview.value.avgFocusScore
  if (score >= 70) return '#56CA00'
  if (score >= 40) return '#FFB400'
  return '#FF4C51'
})

const gaugeChartOptions = computed(() => ({
  chart: {
    type: 'radialBar' as const,
    height: 160,
    sparkline: { enabled: true },
  },
  plotOptions: {
    radialBar: {
      startAngle: -90,
      endAngle: 90,
      hollow: { size: '60%' },
      track: {
        background: 'rgba(var(--v-theme-on-background), 0.08)',
        strokeWidth: '100%',
      },
      dataLabels: {
        name: { show: false },
        value: {
          offsetY: -2,
          fontSize: '1.5rem',
          fontWeight: 600,
          color: 'rgba(var(--v-theme-on-background), var(--v-high-emphasis-opacity))',
          formatter: (val: number) => `${Math.round(val)}`,
        },
      },
    },
  },
  fill: {
    type: 'solid',
    colors: [gaugeColor.value],
  },
  stroke: { lineCap: 'round' as const },
}))

const gaugeChartSeries = computed(() => [overview.value.avgFocusScore])

const trendIcon = (val: number) => val >= 0 ? 'tabler-trending-up' : 'tabler-trending-down'
const trendColor = (val: number) => val >= 0 ? 'success' : 'error'

const fetchOverview = async () => {
  try {
    const data = await $api('/admin/focus/overview')

    overview.value = {
      avgFocusScore: data.avgFocusScore ?? 0,
      mindWanderingRate: data.mindWanderingRate ?? 0,
      mindWanderingRateTrend: data.mindWanderingRateTrend ?? 0,
      microbreakCompliance: data.microbreakCompliance ?? 0,
      microbreakComplianceTrend: data.microbreakComplianceTrend ?? 0,
      activeSessions: data.activeSessions ?? 0,
    }

    error.value = null
  }
  catch (err: any) {
    console.error('Failed to fetch focus overview:', err)
    error.value = err.message ?? 'Failed to load focus data'
  }
  finally {
    loading.value = false
  }
}

onMounted(async () => {
  await fetchOverview()

  const interval = props.pollIntervalMs ?? 30000
  pollTimer = setInterval(fetchOverview, interval)
})

onUnmounted(() => {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})

defineExpose({ refresh: fetchOverview })
</script>

<template>
  <VRow>
    <!-- Avg Focus Score Gauge -->
    <VCol
      cols="12"
      md="3"
      sm="6"
    >
      <VCard :loading="loading">
        <VCardText class="text-center">
          <div class="text-body-1 text-medium-emphasis mb-2">
            Avg Focus Score
          </div>
          <VueApexCharts
            type="radialBar"
            height="160"
            :options="gaugeChartOptions"
            :series="gaugeChartSeries"
          />
        </VCardText>
      </VCard>
    </VCol>

    <!-- Mind Wandering Rate -->
    <VCol
      cols="12"
      md="3"
      sm="6"
    >
      <VCard :loading="loading">
        <VCardText>
          <div class="d-flex align-center gap-x-4 mb-1">
            <VAvatar
              variant="tonal"
              color="warning"
              rounded
            >
              <VIcon
                icon="tabler-brain"
                size="28"
              />
            </VAvatar>
            <h4 class="text-h4">
              {{ overview.mindWanderingRate }}%
            </h4>
          </div>
          <div class="text-body-1 mb-1">
            Mind Wandering Rate
          </div>
          <div class="d-flex gap-x-2 align-center">
            <VIcon
              :icon="trendIcon(overview.mindWanderingRateTrend)"
              :color="overview.mindWanderingRateTrend <= 0 ? 'success' : 'error'"
              size="20"
            />
            <h6 class="text-h6">
              {{ overview.mindWanderingRateTrend > 0 ? '+' : '' }}{{ overview.mindWanderingRateTrend }}%
            </h6>
            <div class="text-sm text-disabled">
              vs last week
            </div>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Microbreak Compliance -->
    <VCol
      cols="12"
      md="3"
      sm="6"
    >
      <VCard :loading="loading">
        <VCardText>
          <div class="d-flex align-center gap-x-4 mb-1">
            <VAvatar
              variant="tonal"
              color="info"
              rounded
            >
              <VIcon
                icon="tabler-coffee"
                size="28"
              />
            </VAvatar>
            <h4 class="text-h4">
              {{ overview.microbreakCompliance }}%
            </h4>
          </div>
          <div class="text-body-1 mb-1">
            Microbreak Compliance
          </div>
          <div class="d-flex gap-x-2 align-center">
            <VIcon
              :icon="trendIcon(overview.microbreakComplianceTrend)"
              :color="trendColor(overview.microbreakComplianceTrend)"
              size="20"
            />
            <h6 class="text-h6">
              {{ overview.microbreakComplianceTrend > 0 ? '+' : '' }}{{ overview.microbreakComplianceTrend }}%
            </h6>
            <div class="text-sm text-disabled">
              vs last week
            </div>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Active Sessions -->
    <VCol
      cols="12"
      md="3"
      sm="6"
    >
      <VCard :loading="loading">
        <VCardText>
          <div class="d-flex align-center gap-x-4 mb-1">
            <VAvatar
              variant="tonal"
              color="primary"
              rounded
            >
              <VIcon
                icon="tabler-users"
                size="28"
              />
            </VAvatar>
            <h4 class="text-h4">
              {{ overview.activeSessions }}
            </h4>
          </div>
          <div class="text-body-1 mb-1">
            Active Sessions
          </div>
          <div class="d-flex gap-x-2 align-center">
            <VChip
              color="primary"
              label
              size="small"
            >
              Live
            </VChip>
            <div class="text-sm text-disabled">
              polling every {{ ((pollIntervalMs ?? 30000) / 1000) }}s
            </div>
          </div>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>

  <VAlert
    v-if="error"
    type="error"
    variant="tonal"
    class="mt-4"
  >
    {{ error }}
  </VAlert>
</template>
