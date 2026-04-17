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
  messagesProcessed: number
  sessionsStarted: number
  eventsPublished: number
  actorErrors: number
  queueDepths: { name: string; depth: number }[]
}

const loading = ref(true)
const error = ref<string | null>(null)
const services = ref<ServiceStatus[]>([])
const actorNodes = ref<ActorNode[]>([])
const metrics = ref<SystemMetrics>({
  errorRates: [],
  activeActors: 0,
  messagesProcessed: 0,
  sessionsStarted: 0,
  eventsPublished: 0,
  actorErrors: 0,
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
      messagesProcessed: data.messagesProcessed ?? 0,
      sessionsStarted: data.sessionsStarted ?? 0,
      eventsPublished: data.eventsPublished ?? 0,
      actorErrors: data.actorErrors ?? 0,
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

interface NatsConnection {
  name: string
  inMsgs: number
  outMsgs: number
  inBytes: number
  outBytes: number
  subscriptions: number
}

interface NatsStats {
  streams: NatsConnection[]
  totalMessages: number
  totalBytes: number
  totalConsumers: number
  serverVersion?: string
  connections?: number
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

// RDY-017a sub-task 3: DLQ depth widget on the system health page.
// Mirrors the NatsDlqHealthCheck threshold (50 → Degraded) exactly so the
// dashboard banner agrees with what /health/ready reports to Prometheus.
interface DlqSnapshot { items: unknown[], total: number }
const DLQ_ALERT_THRESHOLD = 50
const dlqDepth = ref(0)
const dlqLoading = ref(true)
const dlqError = ref(false)

const fetchDlqDepth = async () => {
  try {
    const data = await $api<DlqSnapshot>('/admin/events/dead-letters', {
      query: { page: 1, itemsPerPage: 1 },
    })
    dlqDepth.value = data?.total ?? 0
    dlqError.value = false
  }
  catch (err) {
    console.error('Failed to fetch DLQ depth:', err)
    dlqError.value = true
  }
  finally {
    dlqLoading.value = false
  }
}

const dlqStatus = computed(() => {
  if (dlqError.value)
    return { color: 'error', label: 'Check failed', icon: 'tabler-alert-triangle' }
  if (dlqDepth.value >= DLQ_ALERT_THRESHOLD)
    return { color: 'error', label: 'Degraded', icon: 'tabler-alert-circle' }
  if (dlqDepth.value > 0)
    return { color: 'warning', label: 'Non-zero', icon: 'tabler-alert-square-rounded' }

  return { color: 'success', label: 'Healthy', icon: 'tabler-circle-check' }
})

const fetchAll = async () => {
  loading.value = true
  await Promise.all([fetchHealth(), fetchMetrics(), fetchActorNodes(), fetchNatsStats(), fetchDlqDepth()])
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
                <span class="font-weight-medium">{{ metrics.messagesProcessed.toLocaleString() }}</span>
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

        <!-- RDY-017a sub-task 3: DLQ depth widget ─────────────────── -->
        <VCard
          :loading="dlqLoading"
          class="mb-4"
        >
          <VCardItem>
            <template #title>
              <div class="d-flex align-center gap-2">
                <VIcon icon="tabler-inbox-off" size="20" />
                NATS Dead-Letter Queue
              </div>
            </template>
            <template #append>
              <VChip
                :color="dlqStatus.color"
                :prepend-icon="dlqStatus.icon"
                label
                size="small"
              >
                {{ dlqStatus.label }}
              </VChip>
            </template>
          </VCardItem>
          <VCardText>
            <div class="d-flex align-center justify-space-between mb-3">
              <div>
                <div class="text-h4">
                  {{ dlqDepth }}
                </div>
                <div class="text-body-2 text-medium-emphasis">
                  dead-lettered events
                </div>
              </div>
              <div class="text-end">
                <div class="text-body-2 text-medium-emphasis">
                  Alert threshold
                </div>
                <div class="text-body-1 font-weight-medium">
                  ≥ {{ DLQ_ALERT_THRESHOLD }}
                </div>
              </div>
            </div>

            <VAlert
              v-if="dlqDepth >= DLQ_ALERT_THRESHOLD"
              type="error"
              variant="tonal"
              density="compact"
              class="mb-3"
            >
              DLQ depth at or above the {{ DLQ_ALERT_THRESHOLD }}-event
              threshold — investigate and run
              <code>scripts/nats/nats-dlq-replay.sh</code> after fixing
              the upstream failure.
            </VAlert>

            <VBtn
              variant="tonal"
              color="primary"
              size="small"
              block
              :to="{ name: 'apps-system-dead-letters' }"
            >
              <VIcon icon="tabler-external-link" start />
              Open dead-letter queue
            </VBtn>
          </VCardText>
        </VCard>
        <!-- ────────────────────────────────────────────────────────── -->

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

    <!-- Backup Status & Observability -->
    <VRow class="mt-6">
      <!-- Backup Status -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard>
          <VCardTitle class="d-flex align-center gap-2">
            <VIcon icon="tabler-database" />
            Database Backup
          </VCardTitle>
          <VCardText>
            <VList density="compact">
              <VListItem>
                <template #prepend>
                  <VIcon
                    icon="tabler-clock"
                    color="success"
                  />
                </template>
                <VListItemTitle>Schedule</VListItemTitle>
                <VListItemSubtitle>Every 6 hours (pg_dump custom format)</VListItemSubtitle>
              </VListItem>
              <VListItem>
                <template #prepend>
                  <VIcon
                    icon="tabler-trash"
                    color="info"
                  />
                </template>
                <VListItemTitle>Retention</VListItemTitle>
                <VListItemSubtitle>7 days automatic rotation</VListItemSubtitle>
              </VListItem>
              <VListItem>
                <template #prepend>
                  <VIcon
                    icon="tabler-shield-check"
                    color="success"
                  />
                </template>
                <VListItemTitle>Recovery</VListItemTitle>
                <VListItemSubtitle>Transactional restore (--single-transaction)</VListItemSubtitle>
              </VListItem>
            </VList>
            <VAlert
              type="info"
              variant="tonal"
              density="compact"
              class="mt-3"
            >
              Backups stored in <code>postgres_backups</code> Docker volume. Use <code>config/backup/restore.sh</code> to restore.
            </VAlert>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Observability Tools -->
      <VCol
        cols="12"
        md="6"
      >
        <VCard>
          <VCardTitle class="d-flex align-center gap-2">
            <VIcon icon="tabler-chart-line" />
            Observability
          </VCardTitle>
          <VCardText>
            <div class="d-flex flex-column gap-3">
              <VBtn
                href="http://localhost:3000"
                target="_blank"
                color="primary"
                variant="outlined"
                prepend-icon="tabler-layout-dashboard"
                block
              >
                Grafana Dashboards
                <template #append>
                  <VIcon
                    icon="tabler-external-link"
                    size="14"
                  />
                </template>
              </VBtn>
              <VBtn
                href="http://localhost:16686"
                target="_blank"
                color="secondary"
                variant="outlined"
                prepend-icon="tabler-route"
                block
              >
                Jaeger Traces
                <template #append>
                  <VIcon
                    icon="tabler-external-link"
                    size="14"
                  />
                </template>
              </VBtn>
              <VBtn
                href="http://localhost:9090"
                target="_blank"
                color="info"
                variant="outlined"
                prepend-icon="tabler-chart-bar"
                block
              >
                Prometheus
                <template #append>
                  <VIcon
                    icon="tabler-external-link"
                    size="14"
                  />
                </template>
              </VBtn>
            </div>
            <VAlert
              type="warning"
              variant="tonal"
              density="compact"
              class="mt-3"
            >
              Links point to local development. Update for production deployment.
            </VAlert>
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

        <!-- NATS Connection Panels -->
        <VTable
          v-if="natsStats.streams.length"
          density="compact"
          class="mt-4"
        >
          <thead>
            <tr>
              <th>Connection</th>
              <th>In Messages</th>
              <th>Out Messages</th>
              <th>In Bytes</th>
              <th>Out Bytes</th>
              <th>Subscriptions</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="conn in natsStats.streams"
              :key="conn.name"
            >
              <td class="font-weight-medium">{{ conn.name }}</td>
              <td>{{ (conn.inMsgs ?? 0).toLocaleString() }}</td>
              <td>{{ (conn.outMsgs ?? 0).toLocaleString() }}</td>
              <td>{{ formatNatsBytes(conn.inBytes ?? 0) }}</td>
              <td>{{ formatNatsBytes(conn.outBytes ?? 0) }}</td>
              <td>
                <VChip
                  size="x-small"
                  color="primary"
                  variant="tonal"
                >
                  {{ conn.subscriptions ?? 0 }}
                </VChip>
              </td>
            </tr>
          </tbody>
        </VTable>

        <div
          v-else-if="!natsLoading"
          class="text-body-2 text-disabled text-center py-4"
        >
          No NATS connections available
        </div>
      </VCardText>
    </VCard>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
