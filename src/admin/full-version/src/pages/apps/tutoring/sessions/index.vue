<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Tutoring' } })

interface TutoringSession {
  id: string
  sessionId: string
  studentId: string
  studentName: string
  status: string
  methodology: string
  turnCount: number
  durationSeconds: number
  tokensUsed: number
  startedAt: string
  endedAt: string | null
}

interface SessionListResponse {
  items: TutoringSession[]
  totalCount: number
  page: number
  pageSize: number
}

interface TutoringAnalytics {
  activeSessionCount: number
  avgTurnsPerSession: number
  resolutionRate: number
  sessionsToday: number
  sessionsThisWeek: number
  avgBudgetUsagePercent: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const sessions = ref<TutoringSession[]>([])
const totalCount = ref(0)
const analytics = ref<TutoringAnalytics>({
  activeSessionCount: 0,
  avgTurnsPerSession: 0,
  resolutionRate: 0,
  sessionsToday: 0,
  sessionsThisWeek: 0,
  avgBudgetUsagePercent: 0,
})

const page = ref(1)
const pageSize = ref(20)
const studentSearch = ref('')
const statusFilter = ref<string | null>(null)
const dateFrom = ref('')
const dateTo = ref('')
const autoRefresh = ref(false)

let refreshTimer: ReturnType<typeof setInterval> | null = null

const router = useRouter()

const statusOptions = [
  { title: 'All', value: null },
  { title: 'Active', value: 'active' },
  { title: 'Completed', value: 'completed' },
  { title: 'Budget Exhausted', value: 'budget_exhausted' },
  { title: 'Safety Blocked', value: 'safety_blocked' },
  { title: 'Timeout', value: 'timeout' },
]

const statusColor = (status: string): string => {
  switch (status) {
    case 'active': return 'info'
    case 'completed': return 'success'
    case 'budget_exhausted': return 'warning'
    case 'safety_blocked': return 'error'
    case 'timeout': return 'secondary'
    default: return 'default'
  }
}

const headers = [
  { title: 'Student', key: 'studentName', sortable: true },
  { title: 'Status', key: 'status', sortable: true },
  { title: 'Methodology', key: 'methodology', sortable: false },
  { title: 'Turns', key: 'turnCount', sortable: true },
  { title: 'Duration', key: 'durationSeconds', sortable: true },
  { title: 'Budget Used', key: 'tokensUsed', sortable: true },
  { title: 'Started At', key: 'startedAt', sortable: true },
]

const formatDuration = (seconds: number): string => {
  if (seconds < 60) return `${seconds}s`
  const min = Math.floor(seconds / 60)
  const sec = seconds % 60

  return sec > 0 ? `${min}m ${sec}s` : `${min}m`
}

const formatDate = (iso: string): string => {
  if (!iso) return '-'

  return new Date(iso).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const fetchSessions = async () => {
  try {
    const params = new URLSearchParams({
      page: page.value.toString(),
      pageSize: pageSize.value.toString(),
    })

    if (studentSearch.value)
      params.set('studentId', studentSearch.value)
    if (statusFilter.value)
      params.set('status', statusFilter.value)
    if (dateFrom.value)
      params.set('from', dateFrom.value)
    if (dateTo.value)
      params.set('to', dateTo.value)

    const data = await $api<SessionListResponse>(`/admin/tutoring/sessions?${params}`)

    sessions.value = data.items ?? []
    totalCount.value = data.totalCount ?? 0
  }
  catch (err: any) {
    console.error('Failed to fetch tutoring sessions:', err)
    error.value = err.message ?? 'Failed to load tutoring sessions'
  }
}

const fetchAnalytics = async () => {
  try {
    const data = await $api<TutoringAnalytics>('/admin/tutoring/analytics')

    analytics.value = {
      activeSessionCount: data.activeSessionCount ?? 0,
      avgTurnsPerSession: data.avgTurnsPerSession ?? 0,
      resolutionRate: data.resolutionRate ?? 0,
      sessionsToday: data.sessionsToday ?? 0,
      sessionsThisWeek: data.sessionsThisWeek ?? 0,
      avgBudgetUsagePercent: data.avgBudgetUsagePercent ?? 0,
    }
  }
  catch (err: any) {
    console.error('Failed to fetch tutoring analytics:', err)
  }
}

const fetchAll = async () => {
  loading.value = true
  await Promise.all([fetchSessions(), fetchAnalytics()])
  loading.value = false
}

const onPageUpdate = async (options: { page: number; itemsPerPage: number }) => {
  page.value = options.page
  pageSize.value = options.itemsPerPage
  await fetchSessions()
}

const onRowClick = (_event: Event, row: { item: TutoringSession }) => {
  router.push({ name: 'apps-tutoring-sessions-id', params: { id: row.item.sessionId || row.item.id } })
}

watch(autoRefresh, val => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
  if (val)
    refreshTimer = setInterval(fetchAll, 30000)
})

watch([studentSearch, statusFilter, dateFrom, dateTo], () => {
  page.value = 1
  fetchSessions()
})

onMounted(fetchAll)

onUnmounted(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
})

const statCards = computed(() => [
  { title: 'Active Sessions', value: analytics.value.activeSessionCount, icon: 'tabler-player-play', color: 'info' },
  { title: 'Avg Turns/Session', value: analytics.value.avgTurnsPerSession.toFixed(1), icon: 'tabler-arrows-exchange', color: 'primary' },
  { title: 'Resolution Rate', value: `${(analytics.value.resolutionRate * 100).toFixed(1)}%`, icon: 'tabler-target', color: 'success' },
  { title: 'Sessions Today', value: analytics.value.sessionsToday, icon: 'tabler-calendar-event', color: 'warning' },
])
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Tutoring Sessions
        </h4>
        <div class="text-body-1">
          Monitor active and historical AI tutoring sessions
        </div>
      </div>

      <VCheckbox
        v-model="autoRefresh"
        label="Auto-refresh (30s)"
      />
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

    <!-- Analytics Stat Cards -->
    <VRow class="mb-6">
      <VCol
        v-for="stat in statCards"
        :key="stat.title"
        cols="12"
        sm="6"
        lg="3"
      >
        <VCard :loading="loading">
          <VCardText class="d-flex align-center gap-x-4">
            <VAvatar
              variant="tonal"
              :color="stat.color"
              rounded
              size="48"
            >
              <VIcon
                :icon="stat.icon"
                size="28"
              />
            </VAvatar>
            <div>
              <h4 class="text-h4">
                {{ stat.value }}
              </h4>
              <div class="text-body-2 text-medium-emphasis">
                {{ stat.title }}
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Filters -->
    <VCard class="mb-6">
      <VCardText>
        <VRow>
          <VCol
            cols="12"
            md="4"
          >
            <VTextField
              v-model="studentSearch"
              label="Search by Student ID"
              prepend-inner-icon="tabler-search"
              clearable
              density="compact"
            />
          </VCol>
          <VCol
            cols="12"
            md="2"
          >
            <VSelect
              v-model="statusFilter"
              :items="statusOptions"
              label="Status"
              density="compact"
              clearable
            />
          </VCol>
          <VCol
            cols="12"
            md="3"
          >
            <VTextField
              v-model="dateFrom"
              label="From Date"
              type="date"
              density="compact"
              clearable
            />
          </VCol>
          <VCol
            cols="12"
            md="3"
          >
            <VTextField
              v-model="dateTo"
              label="To Date"
              type="date"
              density="compact"
              clearable
            />
          </VCol>
        </VRow>
      </VCardText>
    </VCard>

    <!-- Sessions Table -->
    <VCard>
      <VCardText>
        <VProgressLinear
          v-if="loading && !sessions.length"
          indeterminate
          class="mb-4"
        />

        <VDataTableServer
          :headers="headers"
          :items="sessions"
          :items-length="totalCount"
          :items-per-page="pageSize"
          :page="page"
          :loading="loading"
          hover
          @update:options="onPageUpdate"
          @click:row="onRowClick"
        >
          <template #item.studentName="{ item }">
            <div class="font-weight-medium">
              {{ item.studentName || item.studentId }}
            </div>
            <div
              v-if="item.studentName && item.studentName !== item.studentId"
              class="text-caption text-medium-emphasis"
            >
              {{ item.studentId }}
            </div>
          </template>

          <template #item.status="{ item }">
            <VChip
              :color="statusColor(item.status)"
              label
              size="small"
            >
              {{ item.status.replace(/_/g, ' ') }}
            </VChip>
          </template>

          <template #item.methodology="{ item }">
            <span class="text-body-2">{{ item.methodology || '-' }}</span>
          </template>

          <template #item.durationSeconds="{ item }">
            {{ formatDuration(item.durationSeconds) }}
          </template>

          <template #item.tokensUsed="{ item }">
            {{ item.tokensUsed > 0 ? item.tokensUsed.toLocaleString() : '-' }}
          </template>

          <template #item.startedAt="{ item }">
            {{ formatDate(item.startedAt) }}
          </template>

          <template #no-data>
            <div class="text-center py-4 text-disabled">
              No tutoring sessions found
            </div>
          </template>
        </VDataTableServer>
      </VCardText>
    </VCard>
  </div>
</template>
