<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'System' } })

interface ActorInfo {
  studentId: string
  sessionId: string | null
  messagesProcessed: number
  totalAttempts: number
  correctAttempts: number
  accuracy: number
  lastActivity: string
  activatedAt: string
  uptimeSeconds: number
  status: string
}

interface ActorStats {
  timestamp: string
  commandsRouted: number
  eventsPublished: number
  sessionsStarted: number
  errorsCount: number
  activeActorCount: number
  actors: ActorInfo[]
}

const stats = ref<ActorStats | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const autoRefresh = ref(true)

const fetchStats = async () => {
  try {
    // Fetch from actor host directly (proxied via Vite)
    const data = await $api<ActorStats>('/actors/stats')
    stats.value = data
    error.value = null
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to fetch actor stats'
    console.error('Failed to fetch actor stats:', err)
  }
  finally {
    loading.value = false
  }
}

let pollInterval: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  fetchStats()
  pollInterval = setInterval(() => {
    if (autoRefresh.value) fetchStats()
  }, 3000)
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
})

const headers = [
  { title: 'Student ID', key: 'studentId' },
  { title: 'Session', key: 'sessionId' },
  { title: 'Messages', key: 'messagesProcessed' },
  { title: 'Attempts', key: 'totalAttempts' },
  { title: 'Accuracy', key: 'accuracy' },
  { title: 'Uptime', key: 'uptimeSeconds' },
  { title: 'Last Activity', key: 'lastActivity' },
  { title: 'Status', key: 'status', sortable: false },
]

const formatUptime = (seconds: number): string => {
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`
  return `${Math.round(seconds / 3600)}h ${Math.round((seconds % 3600) / 60)}m`
}

const formatTime = (ts: string): string => {
  if (!ts) return '--'
  const d = new Date(ts)
  const now = new Date()
  const diffMs = now.getTime() - d.getTime()
  if (diffMs < 60000) return `${Math.round(diffMs / 1000)}s ago`
  if (diffMs < 3600000) return `${Math.round(diffMs / 60000)}m ago`
  return d.toLocaleTimeString()
}

const accuracyColor = (acc: number): string => {
  if (acc >= 70) return 'success'
  if (acc >= 40) return 'warning'
  return 'error'
}
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Actor Dashboard
        </h4>
        <p class="text-body-1 text-medium-emphasis mb-0">
          Real-time view of active Proto.Actor virtual actors and NATS bus throughput
        </p>
      </div>
      <div class="d-flex align-center gap-4">
        <VSwitch
          v-model="autoRefresh"
          label="Auto-refresh (3s)"
          hide-details
          density="compact"
        />
        <VBtn
          icon="tabler-refresh"
          variant="text"
          :loading="loading"
          @click="fetchStats"
        />
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

    <!-- Summary cards -->
    <VRow class="mb-6">
      <VCol
        cols="6"
        md="2"
      >
        <VCard>
          <VCardText class="text-center">
            <div class="text-h4">
              {{ stats?.activeActorCount ?? 0 }}
            </div>
            <div class="text-body-2 text-medium-emphasis">
              Active Actors
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol
        cols="6"
        md="2"
      >
        <VCard>
          <VCardText class="text-center">
            <div class="text-h4">
              {{ stats?.commandsRouted ?? 0 }}
            </div>
            <div class="text-body-2 text-medium-emphasis">
              Commands Routed
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol
        cols="6"
        md="2"
      >
        <VCard>
          <VCardText class="text-center">
            <div class="text-h4">
              {{ stats?.eventsPublished ?? 0 }}
            </div>
            <div class="text-body-2 text-medium-emphasis">
              Events Published
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol
        cols="6"
        md="2"
      >
        <VCard>
          <VCardText class="text-center">
            <div class="text-h4">
              {{ stats?.sessionsStarted ?? 0 }}
            </div>
            <div class="text-body-2 text-medium-emphasis">
              Sessions Started
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol
        cols="6"
        md="2"
      >
        <VCard>
          <VCardText class="text-center">
            <div class="text-h4 text-error">
              {{ stats?.errorsCount ?? 0 }}
            </div>
            <div class="text-body-2 text-medium-emphasis">
              Errors
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol
        cols="6"
        md="2"
      >
        <VCard>
          <VCardText class="text-center">
            <div class="text-h4">
              {{ stats ? (stats.commandsRouted > 0 ? Math.round(stats.eventsPublished / stats.commandsRouted * 100) : 0) : 0 }}%
            </div>
            <div class="text-body-2 text-medium-emphasis">
              Event Yield
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Actor table -->
    <VCard>
      <VCardItem title="Active Student Actors">
        <template #subtitle>
          Each row is a Proto.Actor virtual actor (grain) handling a student's learning session
        </template>
      </VCardItem>

      <VDivider />

      <VDataTable
        :headers="headers"
        :items="stats?.actors ?? []"
        :loading="loading"
        item-value="studentId"
        class="text-no-wrap"
        density="comfortable"
        :items-per-page="25"
      >
        <template #item.studentId="{ item }">
          <RouterLink
            :to="{ path: `/apps/mastery/student/${item.studentId}` }"
            class="text-body-2 font-weight-medium text-high-emphasis text-link"
          >
            {{ item.studentId }}
          </RouterLink>
        </template>

        <template #item.sessionId="{ item }">
          <code class="text-caption">{{ item.sessionId ? item.sessionId.slice(0, 12) : '--' }}</code>
        </template>

        <template #item.accuracy="{ item }">
          <VChip
            :color="accuracyColor(item.accuracy)"
            label
            size="small"
          >
            {{ item.accuracy }}%
          </VChip>
        </template>

        <template #item.uptimeSeconds="{ item }">
          {{ formatUptime(item.uptimeSeconds) }}
        </template>

        <template #item.lastActivity="{ item }">
          {{ formatTime(item.lastActivity) }}
        </template>

        <template #item.status="{ item }">
          <VChip
            :color="item.status === 'active' ? 'success' : 'secondary'"
            label
            size="small"
          >
            {{ item.status }}
          </VChip>
        </template>

        <template #no-data>
          <div class="text-center pa-8 text-medium-emphasis">
            <VIcon
              icon="tabler-robot-off"
              size="48"
              class="mb-2"
            />
            <div>No active actors. Run the emulator to spawn student actors.</div>
            <code class="mt-2 d-block">./scripts/start.sh --emulator-only --students=100 --speed=50</code>
          </div>
        </template>
      </VDataTable>
    </VCard>
  </div>
</template>
