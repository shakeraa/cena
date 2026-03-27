<script setup lang="ts">
import { $api } from '@/utils/api'
import ServiceStatusCard from '@/views/apps/system/ServiceStatusCard.vue'
import type { ServiceStatus } from '@/views/apps/system/ServiceStatusCard.vue'

definePage({ meta: { action: 'read', subject: 'System' } })

interface HealthResponse {
  services: ServiceStatus[]
}

interface SystemMetrics {
  errorRates: { timestamp: string; rate: number }[]
  activeActors: number
  queueDepths: { name: string; depth: number }[]
}

const loading = ref(true)
const error = ref<string | null>(null)
const services = ref<ServiceStatus[]>([])
const metrics = ref<SystemMetrics>({
  errorRates: [],
  activeActors: 0,
  queueDepths: [],
})

let pollTimer: ReturnType<typeof setInterval> | null = null

const errorRateChartOptions = computed(() => ({
  chart: {
    type: 'line' as const,
    height: 300,
    toolbar: { show: true },
    zoom: { enabled: true },
  },
  stroke: {
    curve: 'smooth' as const,
    width: 2,
  },
  xaxis: {
    categories: metrics.value.errorRates.map(r => {
      const d = new Date(r.timestamp)

      return d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
    }),
    labels: {
      style: { colors: 'rgba(var(--v-theme-on-background), 0.5)' },
    },
  },
  yaxis: {
    title: { text: 'Error Rate (%)' },
    labels: {
      style: { colors: 'rgba(var(--v-theme-on-background), 0.5)' },
      formatter: (val: number) => `${val.toFixed(2)}%`,
    },
    min: 0,
  },
  colors: ['#FF4C51'],
  tooltip: {
    y: {
      formatter: (val: number) => `${val.toFixed(3)}%`,
    },
  },
  grid: {
    borderColor: 'rgba(var(--v-theme-on-background), 0.08)',
  },
}))

const errorRateChartSeries = computed(() => [
  {
    name: 'Error Rate',
    data: metrics.value.errorRates.map(r => r.rate),
  },
])

const overallStatus = computed(() => {
  if (services.value.some(s => s.status === 'down'))
    return { color: 'error', label: 'Outage Detected', icon: 'tabler-circle-x' }
  if (services.value.some(s => s.status === 'degraded'))
    return { color: 'warning', label: 'Degraded Performance', icon: 'tabler-alert-triangle' }

  return { color: 'success', label: 'All Systems Operational', icon: 'tabler-circle-check' }
})

const fetchHealth = async () => {
  try {
    const data = await $api<HealthResponse>('/admin/system/health')

    services.value = (data.services ?? []).map(s => ({
      name: s.name ?? 'Unknown',
      status: s.status ?? 'down',
      uptimePercent: s.uptimePercent ?? 0,
      lastCheckAt: s.lastCheckAt ?? '',
    }))
  }
  catch (err: any) {
    console.error('Failed to fetch system health:', err)
    error.value = err.message ?? 'Failed to load system health'
  }
}

const fetchMetrics = async () => {
  try {
    const data = await $api<SystemMetrics>('/admin/system/metrics')

    metrics.value = {
      errorRates: data.errorRates ?? [],
      activeActors: data.activeActors ?? 0,
      queueDepths: data.queueDepths ?? [],
    }
  }
  catch (err: any) {
    console.error('Failed to fetch system metrics:', err)
    error.value = err.message ?? 'Failed to load system metrics'
  }
}

const fetchAll = async () => {
  loading.value = true
  await Promise.all([fetchHealth(), fetchMetrics()])
  loading.value = false
}

onMounted(async () => {
  await fetchAll()
  pollTimer = setInterval(fetchAll, 30000)
})

onUnmounted(() => {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          System Health
        </h4>
        <div class="text-body-1">
          Real-time service status, error rates, and infrastructure metrics
        </div>
      </div>

      <VChip
        :color="overallStatus.color"
        :prepend-icon="overallStatus.icon"
        label
        size="large"
      >
        {{ overallStatus.label }}
      </VChip>
    </div>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="error = null"
    >
      {{ error }}
    </VAlert>

    <!-- Service Status Cards -->
    <VRow class="mb-6">
      <VCol
        v-for="service in services"
        :key="service.name"
        cols="12"
        md="6"
        lg="4"
      >
        <ServiceStatusCard :service="service" />
      </VCol>
      <VCol
        v-if="loading && !services.length"
        cols="12"
      >
        <VSkeletonLoader type="card, card, card" />
      </VCol>
    </VRow>

    <VRow class="match-height">
      <!-- Error Rate Chart -->
      <VCol
        cols="12"
        md="8"
      >
        <VCard :loading="loading">
          <VCardItem title="Error Rate Over Time">
            <template #subtitle>
              Aggregate error rate across all services
            </template>
          </VCardItem>
          <VCardText>
            <VueApexCharts
              v-if="metrics.errorRates.length"
              type="line"
              height="300"
              :options="errorRateChartOptions"
              :series="errorRateChartSeries"
            />
            <div
              v-else-if="!loading"
              class="d-flex justify-center align-center py-12"
            >
              <span class="text-body-1 text-disabled">No error rate data available</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Active Actors & Queue Depths -->
      <VCol
        cols="12"
        md="4"
      >
        <VCard
          :loading="loading"
          class="mb-6"
        >
          <VCardText>
            <div class="d-flex align-center gap-x-4 mb-1">
              <VAvatar
                variant="tonal"
                color="primary"
                rounded
              >
                <VIcon
                  icon="tabler-topology-star-ring-3"
                  size="28"
                />
              </VAvatar>
              <h4 class="text-h4">
                {{ metrics.activeActors }}
              </h4>
            </div>
            <div class="text-body-1">
              Active Actors
            </div>
          </VCardText>
        </VCard>

        <VCard :loading="loading">
          <VCardItem title="Queue Depths" />
          <VCardText>
            <VList
              v-if="metrics.queueDepths.length"
              density="compact"
            >
              <VListItem
                v-for="queue in metrics.queueDepths"
                :key="queue.name"
              >
                <template #prepend>
                  <VIcon
                    icon="tabler-stack-2"
                    size="20"
                    class="me-2"
                  />
                </template>
                <VListItemTitle class="text-body-1">
                  {{ queue.name }}
                </VListItemTitle>
                <template #append>
                  <VChip
                    :color="queue.depth > 100 ? 'error' : queue.depth > 50 ? 'warning' : 'success'"
                    label
                    size="small"
                  >
                    {{ queue.depth }}
                  </VChip>
                </template>
              </VListItem>
            </VList>
            <div
              v-else-if="!loading"
              class="text-body-2 text-disabled text-center py-4"
            >
              No queue data
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
