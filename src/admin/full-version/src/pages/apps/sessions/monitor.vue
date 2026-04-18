<script setup lang="ts">
definePage({ meta: { action: 'read', subject: 'Tutoring' } })

interface ActiveSession {
  sessionId: string
  studentId: string
  studentName: string
  subject: string
  questionCount: number
  accuracy: number
  focusScore: number
  startedAt: string
  methodology: string
  status: 'healthy' | 'struggling' | 'confused' | 'stagnation'
}

const sessions = ref<ActiveSession[]>([])
const loading = ref(true)
const autoRefresh = ref(true)
const refreshInterval = ref<ReturnType<typeof setInterval>>()
const countdown = ref(3)
const searchQuery = ref('')
const subjectFilter = ref<string | null>(null)
const sortBy = ref('startedAt')

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

const startPolling = () => {
  stopPolling()
  countdown.value = 3
  refreshInterval.value = setInterval(() => {
    countdown.value--
    if (countdown.value <= 0) {
      fetchSessions()
      countdown.value = 3
    }
  }, 1000)
}

const stopPolling = () => {
  if (refreshInterval.value) {
    clearInterval(refreshInterval.value)
    refreshInterval.value = undefined
  }
}

watch(autoRefresh, (val) => {
  if (val) startPolling()
  else stopPolling()
})

onMounted(() => {
  fetchSessions()
  if (autoRefresh.value) startPolling()
})

onUnmounted(() => stopPolling())

const statusColor = (status: string) => {
  switch (status) {
    case 'healthy': return 'success'
    case 'struggling': return 'warning'
    case 'confused': return 'error'
    case 'stagnation': return 'error'
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
const avgFocus = computed(() => {
  if (!sessions.value.length) return 0
  return Math.round(sessions.value.reduce((sum, s) => sum + (s.focusScore ?? 0), 0) / sessions.value.length)
})
const confusedCount = computed(() => sessions.value.filter(s => s.status === 'confused' || s.status === 'stagnation').length)
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
            <VAvatar color="info" variant="tonal" size="42"><VIcon icon="tabler-eye" /></VAvatar>
            <div>
              <div class="text-h5">{{ avgFocus }}%</div>
              <div class="text-body-2 text-medium-emphasis">Avg Focus Score</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3">
            <VAvatar :color="confusedCount > 0 ? 'error' : 'success'" variant="tonal" size="42">
              <VIcon :icon="confusedCount > 0 ? 'tabler-alert-triangle' : 'tabler-check'" />
            </VAvatar>
            <div>
              <div class="text-h5">{{ confusedCount }}</div>
              <div class="text-body-2 text-medium-emphasis">Confused / Stagnation</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="3">
        <VCard>
          <VCardText class="d-flex align-center gap-3">
            <VSwitch v-model="autoRefresh" label="Auto-refresh" density="compact" hide-details />
            <VProgressCircular
              v-if="autoRefresh"
              :model-value="((3 - countdown) / 3) * 100"
              size="32"
              width="3"
              color="primary"
            >
              <span class="text-caption">{{ countdown }}</span>
            </VProgressCircular>
            <VBtn v-else icon size="small" @click="fetchSessions"><VIcon icon="tabler-refresh" /></VBtn>
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
                <span class="text-medium-emphasis">Questions</span>
                <span>{{ session.questionCount }}</span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Accuracy</span>
                <span :class="session.accuracy >= 70 ? 'text-success' : session.accuracy >= 40 ? 'text-warning' : 'text-error'">
                  {{ Math.round(session.accuracy) }}%
                </span>
              </div>
              <div class="d-flex justify-space-between text-body-2">
                <span class="text-medium-emphasis">Focus</span>
                <VProgressLinear
                  :model-value="session.focusScore"
                  :color="session.focusScore >= 70 ? 'success' : session.focusScore >= 40 ? 'warning' : 'error'"
                  height="6"
                  rounded
                  style="max-inline-size: 100px"
                />
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
