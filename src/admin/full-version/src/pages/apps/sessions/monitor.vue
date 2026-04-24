<script setup lang="ts">
import { useAdminLiveStream } from '@/composables/useAdminLiveStream'

definePage({ meta: { action: 'read', subject: 'Tutoring' } })

// Matches the server's TutoringSessionSummaryDto exactly. RDY-059 adds
// QuestionsAnswered / AccuracyPercent / FocusScore — all nullable on
// the server so fresh sessions with no joined data render "—" rather
// than a false-zero signal.
interface ActiveSession {
  id: string
  sessionId: string
  studentId: string
  studentName: string
  conceptId: string
  subject: string
  methodology: string
  status: 'active' | 'completed' | 'budget_exhausted' | string
  turnCount: number
  durationSeconds: number
  tokensUsed: number
  startedAt: string
  endedAt: string | null
  questionsAnswered: number | null
  accuracyPercent: number | null
  focusScore: number | null
}

// "—" sentinel — used wherever the server explicitly returned null for
// an optional metric. Never display NaN%; that was the 2026-04-18 bug.
const DASH = '\u2014'
function fmtAccuracy(v: number | null): string {
  return v === null || v === undefined || Number.isNaN(v) ? DASH : `${Math.round(v)}%`
}
function fmtFocus(v: number | null): string {
  return v === null || v === undefined || Number.isNaN(v) ? DASH : `${Math.round(v)}`
}
function fmtCount(v: number | null): string {
  return v === null || v === undefined ? DASH : String(v)
}

function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '—'
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  if (h > 0) return `${h}h ${m}m`
  if (m > 0) return `${m}m`
  return `${seconds}s`
}

const sessions = ref<ActiveSession[]>([])
const loading = ref(true)
// RDY-060 Phase 5a: primary update path is now the admin SignalR stream.
// `autoRefresh` still controls the 30s safety poll that backstops stream
// degradation (spec §6 graceful degradation — never regress to offline).
const autoRefresh = ref(true)
// eslint-disable-next-line @typescript-eslint/no-redeclare
// RDY-060: poll interval raised from 3s to 30s — stream does the fast work.
const SAFETY_POLL_INTERVAL_MS = 30_000
const refreshInterval = ref<ReturnType<typeof setInterval>>()
const searchQuery = ref('')
const subjectFilter = ref<string | null>(null)
const sortBy = ref('startedAt')

// ── RDY-060: admin SignalR live stream ──────────────────────────────
const stream = useAdminLiveStream()
const streamStatus = stream.status
const streamConnected = computed(() => streamStatus.value === 'connected')

const fetchSessions = async () => {
  try {
    // Server returns { items: [...] } (admin paged-list envelope).
    // Also accept { sessions: [...] } and a raw array for forward-compat
    // with any future contract tweaks; fall back to [] so computed
    // helpers like .filter / .length never blow up on an object.
    const data = await $api<{ items?: ActiveSession[]; sessions?: ActiveSession[] } | ActiveSession[]>(
      '/admin/tutoring/sessions?status=active&itemsPerPage=100',
    )
    const arr = Array.isArray(data)
      ? data
      : (data.items ?? data.sessions ?? [])
    sessions.value = Array.isArray(arr) ? arr : []
  }
  catch (e) {
    console.error('Failed to fetch active sessions:', e)
    sessions.value = []
  }
  finally {
    loading.value = false
  }
}

// RDY-060 Phase 5a: 30s safety poll (not 3s). The stream does the
// fast-path live updates; this interval only catches missed events in
// degraded mode. The arch test AdminSpaPagesHaveNoFastPolling validates
// that setInterval callbacks at <30s on admin pages carry an allowlist
// comment, so future dashboards don't re-introduce 3s churn.
const startPolling = () => {
  stopPolling()
  // 30s safety-poll: belt-and-suspenders for stream outages. Allowlisted.
  refreshInterval.value = setInterval(fetchSessions, SAFETY_POLL_INTERVAL_MS)
}

const stopPolling = () => {
  if (refreshInterval.value) {
    clearInterval(refreshInterval.value)
    refreshInterval.value = undefined
  }
}

// Stream handler: session / focus events mutate local rows in place.
// Initial hydrate still comes from the REST fetchSessions — the stream
// only handles deltas.
function applyStreamEnvelope(env: {
  subject: string
  payloadJson: string
}) {
  if (!env.subject.startsWith('cena.events.session.') &&
      !env.subject.startsWith('cena.events.focus.'))
    return
  // Coalesce: easiest safe thing is to refetch (cheap at ≤100 active
  // rows). Stream still delivers the signal that something changed —
  // we just don't try to patch rows in-place in v1. A later commit can
  // swap this for a fine-grained reducer when we add per-event handlers.
  fetchSessions()
}

watch(autoRefresh, (val) => {
  if (val) startPolling()
  else stopPolling()
})

let unsubscribeStream: (() => void) | null = null

onMounted(async () => {
  fetchSessions()
  if (autoRefresh.value) startPolling()
  // Open the shared admin hub + join the system group so we receive
  // session/focus events fanned in by NatsAdminBridge. Failures here
  // are non-fatal — safety poll still runs.
  try {
    await stream.connect()
    await stream.join('system')
    unsubscribeStream = stream.on(applyStreamEnvelope)
  }
  catch (err) {
    console.warn('[live-monitor] admin-hub unavailable; 30s poll covers it:', err)
  }
})

onUnmounted(() => {
  stopPolling()
  unsubscribeStream?.()
  void stream.leave('system')
})

const statusColor = (status: string) => {
  switch (status) {
    case 'active': return 'success'
    case 'completed': return 'info'
    case 'budget_exhausted': return 'error'
    case 'confused':
    case 'stagnation':
      return 'error'
    case 'healthy': return 'success'       // legacy mapping, kept for forward-compat
    case 'struggling': return 'warning'    // legacy mapping
    default: return 'default'
  }
}

const filteredSessions = computed(() => {
  let result = sessions.value
  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase()
    result = result.filter(s => s.studentName?.toLowerCase().includes(q) || s.studentId?.toLowerCase().includes(q))
  }
  if (subjectFilter.value)
    result = result.filter(s => s.subject === subjectFilter.value)
  return result
})

const totalActive = computed(() => sessions.value.length)

const totalTurns = computed(() =>
  sessions.value.reduce((sum, s) => sum + (s.turnCount ?? 0), 0),
)
const stalledCount = computed(() =>
  sessions.value.filter(s => s.status === 'budget_exhausted' || s.status === 'confused' || s.status === 'stagnation').length,
)

// RDY-059: summary-card aggregates across sessions that actually have
// data. Sessions without joined data (null) are excluded from the
// average rather than dragging it to zero.
const avgAccuracy = computed(() => {
  const withData = sessions.value.filter(s => typeof s.accuracyPercent === 'number')
  if (withData.length === 0) return null
  const sum = withData.reduce((acc, s) => acc + (s.accuracyPercent ?? 0), 0)
  return sum / withData.length
})

const avgFocusScore = computed(() => {
  const withData = sessions.value.filter(s => typeof s.focusScore === 'number')
  if (withData.length === 0) return null
  const sum = withData.reduce((acc, s) => acc + (s.focusScore ?? 0), 0)
  return sum / withData.length
})
</script>

<template>
  <div>
    <!-- Summary Bar -->
    <VRow class="mb-4">
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3">
            <VAvatar color="primary" variant="tonal" size="42"><VIcon icon="tabler-users" /></VAvatar>
            <div>
              <div class="text-h5">{{ totalActive }}</div>
              <div class="text-body-2 text-medium-emphasis">Active Sessions</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3">
            <VAvatar color="info" variant="tonal" size="42"><VIcon icon="tabler-messages" /></VAvatar>
            <div>
              <div class="text-h5">{{ totalTurns }}</div>
              <div class="text-body-2 text-medium-emphasis">Total Turns</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3">
            <VAvatar color="success" variant="tonal" size="42"><VIcon icon="tabler-target" /></VAvatar>
            <div>
              <div class="text-h5">{{ fmtAccuracy(avgAccuracy) }}</div>
              <div class="text-body-2 text-medium-emphasis">Avg Accuracy</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3">
            <VAvatar color="warning" variant="tonal" size="42"><VIcon icon="tabler-brain" /></VAvatar>
            <div>
              <div class="text-h5">{{ fmtFocus(avgFocusScore) }}</div>
              <div class="text-body-2 text-medium-emphasis">Avg Focus Score</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3">
            <VAvatar :color="stalledCount > 0 ? 'error' : 'success'" variant="tonal" size="42">
              <VIcon :icon="stalledCount > 0 ? 'tabler-alert-triangle' : 'tabler-check'" />
            </VAvatar>
            <div>
              <div class="text-h5">{{ stalledCount }}</div>
              <div class="text-body-2 text-medium-emphasis">Budget Exhausted / Stalled</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3 flex-wrap">
            <VChip
              size="small"
              :color="streamConnected ? 'success' : 'warning'"
              variant="tonal"
              :prepend-icon="streamConnected ? 'tabler-bolt' : 'tabler-bolt-off'"
            >
              {{ streamConnected ? 'Live' : streamStatus }}
            </VChip>
            <VSwitch
              v-model="autoRefresh"
              label="30s safety poll"
              density="compact"
              hide-details
            />
            <VBtn
              size="small"
              variant="text"
              @click="fetchSessions"
            >
              <VIcon icon="tabler-refresh" />
            </VBtn>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Filters -->
    <VRow class="mb-4">
      <VCol cols="12" md="4">
        <AppTextField v-model="searchQuery" placeholder="Search student..." prepend-inner-icon="tabler-search" clearable density="compact" />
      </VCol>
      <VCol cols="12" md="3">
        <AppSelect v-model="subjectFilter" :items="['Mathematics', 'Physics', 'Chemistry']" placeholder="All Subjects" clearable density="compact" />
      </VCol>
    </VRow>

    <!-- Loading -->
    <VProgressLinear v-if="loading" indeterminate color="primary" class="mb-4" />

    <!-- Empty State -->
    <VCard v-if="!loading && filteredSessions.length === 0" class="pa-8 text-center">
      <VIcon icon="tabler-mood-smile" size="64" color="success" class="mb-4" />
      <div class="text-h6">No Active Sessions</div>
      <div class="text-body-2 text-medium-emphasis">All students are currently offline.</div>
    </VCard>

    <!-- Session Grid -->
    <VRow v-else>
      <VCol v-for="session in filteredSessions" :key="session.sessionId" cols="12" sm="6" md="4" lg="3">
        <VCard
          :style="{ borderLeft: `4px solid var(--v-theme-${statusColor(session.status)})` }"
          class="session-card"
        >
          <VCardTitle class="d-flex justify-space-between align-center pb-1">
            <span class="text-body-1 font-weight-medium text-truncate">{{ session.studentName || session.studentId }}</span>
            <VChip :color="statusColor(session.status)" size="x-small" variant="tonal">
              {{ session.status }}
            </VChip>
          </VCardTitle>
          <VCardText class="pt-1">
            <div class="d-flex flex-column gap-1">
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Subject</span>
                <span>{{ session.subject }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Concept</span>
                <span class="text-truncate" style="max-inline-size: 140px">{{ session.conceptId || '—' }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Turns</span>
                <span>{{ session.turnCount ?? 0 }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Duration</span>
                <span>{{ formatDuration(session.durationSeconds) }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Tokens</span>
                <span>{{ session.tokensUsed ?? 0 }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Answered</span>
                <span>{{ fmtCount(session.questionsAnswered) }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Accuracy</span>
                <span>{{ fmtAccuracy(session.accuracyPercent) }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Focus</span>
                <span>{{ fmtFocus(session.focusScore) }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Method</span>
                <VChip size="x-small" variant="outlined">{{ session.methodology }}</VChip>
              </div>
            </div>
          </VCardText>
          <VCardActions class="pt-0">
            <VBtn
              size="small"
              variant="text"
              color="primary"
              :to="session.sessionId ? { name: 'apps-tutoring-sessions-id', params: { id: session.sessionId } } : undefined"
            >
              View Detail
            </VBtn>
          </VCardActions>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>

<style scoped>
.session-card {
  transition: box-shadow 0.2s;
}
.session-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.12);
}
</style>
