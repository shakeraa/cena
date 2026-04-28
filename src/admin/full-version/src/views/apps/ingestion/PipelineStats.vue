<script setup lang="ts">
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'
import { $api } from '@/utils/api'

interface PipelineStatsData {
  throughputPerHour: number
  failureRatePercent: number
  avgProcessingTimeSec: number
  queueDepth: number
  throughputTrend: number[]
}

const vuetifyTheme = useTheme()

const stats = ref<PipelineStatsData | null>(null)
const loading = ref(true)

const fetchStats = async () => {
  try {
    stats.value = await $api('/admin/ingestion/stats')
  }
  catch (error) {
    console.error('Failed to fetch pipeline stats:', error)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchStats)

const sparklineOptions = computed(() => {
  const currentTheme = vuetifyTheme.current.value.colors

  return {
    chart: {
      type: 'area' as const,
      sparkline: { enabled: true },
      animations: { enabled: false },
    },
    stroke: {
      curve: 'smooth' as const,
      width: 2,
    },
    fill: {
      type: 'gradient',
      gradient: {
        shadeIntensity: 0.8,
        opacityFrom: 0.6,
        opacityTo: 0.1,
      },
    },
    colors: [currentTheme.primary],
    tooltip: {
      enabled: false,
    },
  }
})

const sparklineSeries = computed(() => [
  {
    name: 'Throughput',
    data: stats.value?.throughputTrend ?? [],
  },
])

const formatDuration = (seconds: number): string => {
  if (seconds < 60)
    return `${seconds.toFixed(0)}s`
  if (seconds < 3600)
    return `${(seconds / 60).toFixed(1)}m`

  return `${(seconds / 3600).toFixed(1)}h`
}
</script>

<template>
  <VRow>
    <VCol
      cols="12"
      sm="6"
      md="3"
    >
      <VCard :loading="loading">
        <VCardText>
          <div class="d-flex align-center justify-space-between">
            <div>
              <p class="text-body-2 text-medium-emphasis mb-1">
                Throughput
              </p>
              <h4 class="text-h4">
                {{ stats?.throughputPerHour ?? 0 }}
              </h4>
              <span class="text-body-2 text-medium-emphasis">items/hour</span>
            </div>
            <VAvatar
              color="primary"
              variant="tonal"
              rounded
              size="42"
            >
              <VIcon
                icon="tabler-gauge"
                size="26"
              />
            </VAvatar>
          </div>
          <div
            v-if="stats?.throughputTrend?.length"
            class="mt-2"
          >
            <VueApexCharts
              :options="sparklineOptions"
              :series="sparklineSeries"
              height="40"
            />
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <VCol
      cols="12"
      sm="6"
      md="3"
    >
      <VCard :loading="loading">
        <VCardText>
          <div class="d-flex align-center justify-space-between">
            <div>
              <p class="text-body-2 text-medium-emphasis mb-1">
                Failure Rate
              </p>
              <h4 class="text-h4">
                {{ stats?.failureRatePercent?.toFixed(1) ?? '0.0' }}%
              </h4>
              <span class="text-body-2 text-medium-emphasis">of processed items</span>
            </div>
            <VAvatar
              :color="(stats?.failureRatePercent ?? 0) > 5 ? 'error' : 'success'"
              variant="tonal"
              rounded
              size="42"
            >
              <VIcon
                icon="tabler-alert-triangle"
                size="26"
              />
            </VAvatar>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <VCol
      cols="12"
      sm="6"
      md="3"
    >
      <VCard :loading="loading">
        <VCardText>
          <div class="d-flex align-center justify-space-between">
            <div>
              <p class="text-body-2 text-medium-emphasis mb-1">
                Avg Processing Time
              </p>
              <h4 class="text-h4">
                {{ formatDuration(stats?.avgProcessingTimeSec ?? 0) }}
              </h4>
              <span class="text-body-2 text-medium-emphasis">per item</span>
            </div>
            <VAvatar
              color="warning"
              variant="tonal"
              rounded
              size="42"
            >
              <VIcon
                icon="tabler-clock"
                size="26"
              />
            </VAvatar>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <VCol
      cols="12"
      sm="6"
      md="3"
    >
      <VCard :loading="loading">
        <VCardText>
          <div class="d-flex align-center justify-space-between">
            <div>
              <p class="text-body-2 text-medium-emphasis mb-1">
                Queue Depth
              </p>
              <h4 class="text-h4">
                {{ stats?.queueDepth ?? 0 }}
              </h4>
              <span class="text-body-2 text-medium-emphasis">items waiting</span>
            </div>
            <VAvatar
              color="info"
              variant="tonal"
              rounded
              size="42"
            >
              <VIcon
                icon="tabler-stack-2"
                size="26"
              />
            </VAvatar>
          </div>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>
