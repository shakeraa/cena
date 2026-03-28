<script setup lang="ts">
import { $api } from '@/utils/api'
import ServiceStatusCard from '@/views/apps/system/ServiceStatusCard.vue'
import type { ServiceStatus } from '@/views/apps/system/ServiceStatusCard.vue'

definePage({ meta: { action: 'read', subject: 'System' } })

interface HealthResponse {
  services: ServiceStatus[]
}

interface ActorNode {
  nodeId: string
  status: string
  activeActors: number
  totalMessages: number
  cpuUsagePercent: number
  memoryUsageBytes: number
}

interface SystemMetrics {
  errorRates: { timestamp: string; rate: number }[]
  activeActors: number
  queueDepths: { name: string; depth: number }[]
}

const loading = ref(true)
const error = ref<string | null>(null)
const services = ref<ServiceStatus[]>([])
const actorNodes = ref<ActorNode[]>([])
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

const fetchActorNodes = async () => {
  try {
    const data = await $api<ActorNode[]>('/admin/system/actors')

    actorNodes.value = (data ?? []).map((n: any) => ({
      nodeId: n.nodeId ?? 'unknown',
      status: n.status ?? 'unknown',
      activeActors: n.activeActors ?? 0,
      totalMessages: n.totalMessages ?? 0,
      cpuUsagePercent: n.cpuUsagePercent ?? 0,
      memoryUsageBytes: n.memoryUsageBytes ?? 0,
    }))
  }
  catch {
    // Actor data unavailable
  }
}

const totalMessages = computed(() => actorNodes.value.reduce((sum, n) => sum + n.totalMessages, 0))

const formatBytes = (bytes: number): string => {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / 1048576).toFixed(0)} MB`
}

const totalMemory = computed(() => formatBytes(actorNodes.value.reduce((sum, n) => sum + n.memoryUsageBytes, 0)))
const avgCpu = computed(() => {
  if (!actorNodes.value.length) return 0
  return (actorNodes.value.reduce((sum, n) => sum + n.cpuUsagePercent, 0) / actorNodes.value.length).toFixed(1)
})

interface NatsStream {
  name: string
  messageCount: number
  byteSize: number
  consumerCount: number
  lastSequence: number
  consumers: { name: string; pendingCount: number; ackPending: number; deliveredCount: number }[]
}

interface NatsStats {
  streams: NatsStream[]
  totalMessages: number
  totalBytes: number
  totalConsumers: number
}

const natsLoading = ref(true)
const natsStats = ref<NatsStats>({ streams: [], totalMessages: 0, totalBytes: 0, totalConsumers: 0 })

const fetchNatsStats = async () => {
  natsLoading.value = true
  try {
    natsStats.value = await $api<NatsStats>('/admin/system/nats-stats')
  }
  catch (err: any) {
    console.error('Failed to fetch NATS stats:', err)
  }
  finally {
    natsLoading.value = false
  }
}

const formatNatsBytes = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`

  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`
}

const fetchAll = async () => {
  loading.value = true
  await Promise.all([fetchHealth(), fetchMetrics(), fetchActorNodes(), fetchNatsStats()])
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
            <div class="d-flex align-center gap-x-4 mb-2">
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
              <div>
                <h4 class="text-h4">
                  {{ metrics.activeActors }}
                </h4>
                <div class="text-body-2 text-medium-emphasis">
                  Active Actors
                </div>
              </div>
            </div>

            <VDivider class="my-3" />

            <div class="d-flex flex-column gap-y-2">
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Messages Processed</span>
                <span class="font-weight-medium">{{ totalMessages.toLocaleString() }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Cluster Nodes</span>
                <span class="font-weight-medium">{{ actorNodes.length }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Avg CPU</span>
                <span class="font-weight-medium">{{ avgCpu }}%</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Memory</span>
                <span class="font-weight-medium">{{ totalMemory }}</span>
              </div>
            </div>

            <VDivider class="my-3" />

            <div
              v-if="actorNodes.length"
              class="d-flex flex-column gap-y-2"
            >
              <div
                v-for="node in actorNodes"
                :key="node.nodeId"
                class="d-flex align-center gap-x-2"
              >
                <VIcon
                  :icon="node.status === 'healthy' ? 'tabler-circle-check' : 'tabler-alert-circle'"
                  :color="node.status === 'healthy' ? 'success' : 'warning'"
                  size="16"
                />
                <span class="text-body-2 text-truncate flex-grow-1">{{ node.nodeId }}</span>
                <VChip
                  size="x-small"
                  variant="tonal"
                  color="primary"
                >
                  {{ node.activeActors }}
                </VChip>
              </div>
            </div>

            <div
              v-else
              class="text-body-2 text-disabled text-center py-2"
            >
              No cluster nodes detected
            </div>

            <VBtn
              variant="tonal"
              color="primary"
              size="small"
              block
              class="mt-3"
              :to="{ name: 'apps-system-actors' }"
            >
              View Actor Details
            </VBtn>
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

    <!-- NATS JetStream Health -->
    <VCard
      :loading="natsLoading"
      class="mt-6"
    >
      <VCardItem>
        <VCardTitle class="d-flex align-center gap-2">
          <VIcon
            icon="tabler-brand-nytimes"
            size="24"
          />
          NATS JetStream Health
        </VCardTitle>
      </VCardItem>
      <VCardText>
        <!-- Stat Row -->
        <VRow class="mb-4">
          <VCol
            cols="6"
            md="3"
          >
            <div class="text-body-2 text-medium-emphasis">
              Total Streams
            </div>
            <div class="text-h5">
              {{ natsStats.streams.length }}
            </div>
          </VCol>
          <VCol
            cols="6"
            md="3"
          >
            <div class="text-body-2 text-medium-emphasis">
              Total Messages
            </div>
            <div class="text-h5">
              {{ natsStats.totalMessages.toLocaleString() }}
            </div>
          </VCol>
          <VCol
            cols="6"
            md="3"
          >
            <div class="text-body-2 text-medium-emphasis">
              Total Bytes
            </div>
            <div class="text-h5">
              {{ formatNatsBytes(natsStats.totalBytes) }}
            </div>
          </VCol>
          <VCol
            cols="6"
            md="3"
          >
            <div class="text-body-2 text-medium-emphasis">
              Total Consumers
            </div>
            <div class="text-h5">
              {{ natsStats.totalConsumers }}
            </div>
          </VCol>
        </VRow>

        <!-- Stream Panels -->
        <VExpansionPanels
          v-if="natsStats.streams.length"
          variant="accordion"
        >
          <VExpansionPanel
            v-for="stream in natsStats.streams"
            :key="stream.name"
          >
            <VExpansionPanelTitle>
              <div class="d-flex align-center gap-3 w-100">
                <VIcon
                  icon="tabler-database"
                  size="20"
                />
                <span class="font-weight-medium">{{ stream.name }}</span>
                <VSpacer />
                <VChip
                  size="x-small"
                  color="primary"
                  variant="tonal"
                  class="me-2"
                >
                  {{ stream.messageCount.toLocaleString() }} msgs
                </VChip>
                <VChip
                  size="x-small"
                  color="secondary"
                  variant="tonal"
                  class="me-2"
                >
                  {{ stream.consumerCount }} consumers
                </VChip>
              </div>
            </VExpansionPanelTitle>
            <VExpansionPanelText>
              <div class="d-flex flex-wrap gap-4 mb-3">
                <div>
                  <span class="text-body-2 text-medium-emphasis">Messages:</span>
                  <span class="text-body-2 font-weight-medium ms-1">{{ stream.messageCount.toLocaleString() }}</span>
                </div>
                <div>
                  <span class="text-body-2 text-medium-emphasis">Size:</span>
                  <span class="text-body-2 font-weight-medium ms-1">{{ formatNatsBytes(stream.byteSize) }}</span>
                </div>
                <div>
                  <span class="text-body-2 text-medium-emphasis">Consumers:</span>
                  <span class="text-body-2 font-weight-medium ms-1">{{ stream.consumerCount }}</span>
                </div>
                <div>
                  <span class="text-body-2 text-medium-emphasis">Last Sequence:</span>
                  <span class="text-body-2 font-weight-medium ms-1">{{ stream.lastSequence }}</span>
                </div>
              </div>

              <VAlert
                v-for="consumer in stream.consumers.filter(c => c.pendingCount > 1000)"
                :key="consumer.name"
                type="warning"
                variant="tonal"
                density="compact"
                class="mb-2"
              >
                Consumer <strong>{{ consumer.name }}</strong> has {{ consumer.pendingCount.toLocaleString() }} pending messages
              </VAlert>

              <VTable
                v-if="stream.consumers.length"
                density="compact"
                class="mt-2"
              >
                <thead>
                  <tr>
                    <th>Consumer</th>
                    <th>Pending</th>
                    <th>Ack Pending</th>
                    <th>Delivered</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="consumer in stream.consumers"
                    :key="consumer.name"
                  >
                    <td>{{ consumer.name }}</td>
                    <td>
                      <VChip
                        :color="consumer.pendingCount > 1000 ? 'error' : consumer.pendingCount > 100 ? 'warning' : 'success'"
                        size="x-small"
                        label
                      >
                        {{ consumer.pendingCount.toLocaleString() }}
                      </VChip>
                    </td>
                    <td>{{ consumer.ackPending.toLocaleString() }}</td>
                    <td>{{ consumer.deliveredCount.toLocaleString() }}</td>
                  </tr>
                </tbody>
              </VTable>

              <div
                v-else
                class="text-body-2 text-disabled text-center py-2"
              >
                No consumers on this stream
              </div>
            </VExpansionPanelText>
          </VExpansionPanel>
        </VExpansionPanels>

        <div
          v-else-if="!natsLoading"
          class="text-body-2 text-disabled text-center py-4"
        >
          No NATS streams available
        </div>
      </VCardText>
    </VCard>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
