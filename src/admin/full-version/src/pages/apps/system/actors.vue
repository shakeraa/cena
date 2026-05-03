<script setup lang="ts">
import { $api } from '@/utils/api'
import { useAdminLiveStream } from '@/composables/useAdminLiveStream'

definePage({ meta: { action: 'read', subject: 'System' } })

// RDY-060 Phase 5c: SignalR-driven actor dashboard. Drop 3s poll to
// 30s safety poll; stream handles live updates.
const SAFETY_POLL_INTERVAL_MS = 30_000

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

interface ErrorEntry {
  timestamp: string
  category: string
  subject: string
  message: string
  studentId: string | null
}

interface ActorStats {
  timestamp: string
  commandsRouted: number
  eventsPublished: number
  sessionsStarted: number
  errorsCount: number
  errorsByCategory: Record<string, number>
  recentErrors: ErrorEntry[]
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

// RDY-060 Phase 5c: live updates via admin SignalR `system` group.
// Actor-host lifecycle events (cena.system.*) + session + focus
// events all feed in; any of them can alter actor stats, so we
// refetch on every envelope. Coarse refresh is fine for a ≤100-row
// table; per-actor reducers aren't worth the complexity until we
// see pilot-scale event rates.
const stream = useAdminLiveStream()
const streamStatus = stream.status
let unsubscribeStream: (() => void) | null = null

function onAnyEnvelope() {
  if (autoRefresh.value) fetchStats()
}

onMounted(async () => {
  fetchStats()
  pollInterval = setInterval(() => {
    if (autoRefresh.value) fetchStats()
  }, SAFETY_POLL_INTERVAL_MS)
  try {
    await stream.connect()
    await stream.join('system')
    unsubscribeStream = stream.on(onAnyEnvelope)
  }
  catch (err) {
    console.warn('[actors] admin-hub unavailable; 30s poll covers it:', err)
  }
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
  unsubscribeStream?.()
  void stream.leave('system')
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

const showErrors = ref(false)

const errorCategoryColor = (cat: string): string => {
  switch (cat) {
    case 'timeout': return 'warning'
    case 'deserialization': return 'error'
    case 'activation': return 'error'
    case 'cancelled': return 'secondary'
    default: return 'info'
  }
}

const errorCategoryIcon = (cat: string): string => {
  switch (cat) {
    case 'timeout': return 'tabler-clock-x'
    case 'deserialization': return 'tabler-file-broken'
    case 'activation': return 'tabler-plug-off'
    case 'cancelled': return 'tabler-x'
    default: return 'tabler-alert-triangle'
  }
}

const errorHeaders = [
  { title: 'Time', key: 'timestamp' },
  { title: 'Category', key: 'category' },
  { title: 'Subject', key: 'subject' },
  { title: 'Message', key: 'message' },
  { title: 'Student', key: 'studentId' },
]
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div class="d-flex align-center gap-4">
        <VBtn
          icon="tabler-arrow-left"
          variant="text"
          @click="$router.back()"
        />
        <div>
          <h4 class="text-h4 mb-1">
            Actor Dashboard
          </h4>
          <p class="text-body-1 text-medium-emphasis mb-0">
            Real-time view of active Proto.Actor virtual actors and NATS bus throughput
          </p>
        </div>
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
      <template #title>
        Actor Host Unreachable
      </template>
      The Actor Host (port 5119) is not running. Start it with:
      <code class="d-block mt-2">cd src/actors/Cena.Actors.Host && dotnet run</code>
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
        <VCard
          :class="{ 'cursor-pointer': (stats?.errorsCount ?? 0) > 0 }"
          @click="(stats?.errorsCount ?? 0) > 0 && (showErrors = !showErrors)"
        >
          <VCardText class="text-center">
            <div class="text-h4 text-error">
              {{ stats?.errorsCount ?? 0 }}
            </div>
            <div class="text-body-2 text-medium-emphasis">
              Errors
              <VIcon
                v-if="(stats?.errorsCount ?? 0) > 0"
                :icon="showErrors ? 'tabler-chevron-up' : 'tabler-chevron-down'"
                size="14"
              />
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

    <!-- Error Breakdown Panel -->
    <VExpandTransition>
      <VRow
        v-if="showErrors && stats && stats.errorsCount > 0"
        class="mb-6"
      >
        <!-- Error Categories -->
        <VCol
          cols="12"
          md="4"
        >
          <VCard>
            <VCardItem title="Error Categories" />
            <VDivider />
            <VCardText>
              <div
                v-if="stats.errorsByCategory && Object.keys(stats.errorsByCategory).length"
                class="d-flex flex-column gap-3"
              >
                <div
                  v-for="(count, category) in stats.errorsByCategory"
                  :key="category"
                  class="d-flex align-center justify-space-between"
                >
                  <div class="d-flex align-center gap-2">
                    <VAvatar
                      :color="errorCategoryColor(String(category))"
                      variant="tonal"
                      size="32"
                      rounded
                    >
                      <VIcon
                        :icon="errorCategoryIcon(String(category))"
                        size="18"
                      />
                    </VAvatar>
                    <div>
                      <div class="text-body-2 font-weight-medium text-capitalize">
                        {{ category }}
                      </div>
                      <div class="text-caption text-medium-emphasis">
                        {{ category === 'timeout' ? 'Actor cold-start exceeded timeout' : category === 'deserialization' ? 'Invalid message format' : category === 'activation' ? 'Actor DI/constructor failure' : category === 'cancelled' ? 'Request cancelled' : 'Unexpected error' }}
                      </div>
                    </div>
                  </div>
                  <VChip
                    :color="errorCategoryColor(String(category))"
                    label
                    size="small"
                  >
                    {{ count }}
                  </VChip>
                </div>
              </div>
              <div
                v-else
                class="text-center text-medium-emphasis pa-4"
              >
                No categorized errors
              </div>
            </VCardText>
          </VCard>
        </VCol>

        <!-- Recent Errors Table -->
        <VCol
          cols="12"
          md="8"
        >
          <VCard>
            <VCardItem title="Recent Errors">
              <template #subtitle>
                Last 20 errors with full context
              </template>
            </VCardItem>
            <VDivider />
            <VDataTable
              :headers="errorHeaders"
              :items="stats.recentErrors ?? []"
              density="compact"
              :items-per-page="10"
              class="text-no-wrap"
            >
              <template #item.timestamp="{ item }">
                <span class="text-caption">{{ formatTime(item.timestamp) }}</span>
              </template>
              <template #item.category="{ item }">
                <VChip
                  :color="errorCategoryColor(item.category)"
                  label
                  size="x-small"
                >
                  <VIcon
                    :icon="errorCategoryIcon(item.category)"
                    size="14"
                    start
                  />
                  {{ item.category }}
                </VChip>
              </template>
              <template #item.subject="{ item }">
                <code class="text-caption">{{ item.subject }}</code>
              </template>
              <template #item.message="{ item }">
                <span class="text-caption text-truncate d-inline-block" style="max-inline-size: 300px;">
                  {{ item.message }}
                </span>
              </template>
              <template #item.studentId="{ item }">
                <code v-if="item.studentId" class="text-caption">{{ item.studentId }}</code>
                <span v-else class="text-disabled">--</span>
              </template>
            </VDataTable>
          </VCard>
        </VCol>
      </VRow>
    </VExpandTransition>

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
