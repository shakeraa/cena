<!-- =============================================================================
     Cena Platform — Ingestion Jobs Drawer

     Right-side drawer surfacing in-flight + recent ingestion jobs (Bagrut
     PDF, cloud-dir batch, generate-variants). Two tabs:

       - Active    → queued + running, progress bar + cancel
       - History   → completed/failed/cancelled, click to inspect

     Bottom half: per-job log panel. When a job is selected, polls
     /jobs/{id}/logs every 1.5s while the job is active; one-shot
     fetch on terminal. Newest entries at the bottom; auto-scroll.
============================================================================= -->
<script setup lang="ts">
import { onMounted, onBeforeUnmount, computed, ref, watch, nextTick } from 'vue'
import {
  useIngestionJobs,
  type IngestionJobSummary,
  type JobLogEntry,
  type GenerateVariantsResult,
} from '@/composables/useIngestionJobs'

const {
  jobs,
  loading,
  drawerOpen,
  fetchJobs,
  fetchLogs,
  fetchJobDetail,
  startPolling,
  stopPolling,
  cancelJob,
  deleteJob,
  closeDrawer,
} = useIngestionJobs()

const activeTab = ref<'active' | 'history'>('active')
const selectedJobId = ref<string | null>(null)
const logEntries = ref<JobLogEntry[]>([])
const logsLoading = ref(false)
const logsContainer = ref<HTMLElement | null>(null)
let logPollHandle: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  fetchJobs()
  startPolling()
})

onBeforeUnmount(() => {
  stopPolling()
  stopLogPolling()
})

const groupedJobs = computed(() => {
  const active: IngestionJobSummary[] = []
  const history: IngestionJobSummary[] = []
  for (const j of jobs.value) {
    if (j.status === 'queued' || j.status === 'running') active.push(j)
    else history.push(j)
  }
  return { active, history }
})

// When the active list changes and there's no selection, auto-select
// the first active job so the log panel always has something to show
// while a job is running.
watch(() => groupedJobs.value.active.map(j => j.id).join(','), () => {
  if (!selectedJobId.value && groupedJobs.value.active.length > 0)
    selectedJobId.value = groupedJobs.value.active[0].id
})

// Tab-switch UX fix: when the user clicks the Active or History tab and
// the currently-selected job isn't in the visible tab's list, auto-pick
// the first job in the visible list. Otherwise the bottom log panel
// keeps showing the previous tab's selection — confusing on the
// Active→History→Active path the user reported.
watch(activeTab, (tab) => {
  const list = tab === 'active' ? groupedJobs.value.active : groupedJobs.value.history
  if (list.length === 0) {
    selectedJobId.value = null
    return
  }
  const stillVisible = list.some(j => j.id === selectedJobId.value)
  if (!stillVisible)
    selectedJobId.value = list[0].id
})

const selectedJob = computed(() =>
  selectedJobId.value
    ? jobs.value.find(j => j.id === selectedJobId.value) ?? null
    : null,
)

const isActive = (j: IngestionJobSummary | null) =>
  !!j && (j.status === 'queued' || j.status === 'running')

function selectJob(jobId: string) {
  selectedJobId.value = jobId
  // Switch to the right tab so the user sees the row they clicked.
  const job = jobs.value.find(j => j.id === jobId)
  if (job) activeTab.value = isActive(job) ? 'active' : 'history'
}

async function loadLogs(jobId: string) {
  // (C) Skip if a previous fetch is still in flight. Without this the
  // 1.5s poll could overlap a slow request and flip logsLoading on/off
  // repeatedly, leaving the panel stuck on "Loading…".
  if (logsLoading.value) return
  logsLoading.value = true
  try {
    logEntries.value = await fetchLogs(jobId, 200)
    await nextTick()
    if (logsContainer.value)
      logsContainer.value.scrollTop = logsContainer.value.scrollHeight
  }
  finally {
    logsLoading.value = false
  }
}

function startLogPolling() {
  stopLogPolling()
  if (!selectedJobId.value) return
  // (B) Only poll while the selected job is still active. Terminal
  // jobs had their logs loaded once on selection — that's enough.
  // Previously the `|| logEntries.length === 0` clause caused infinite
  // polling on terminal jobs that legitimately produced no logs.
  if (!isActive(selectedJob.value)) return
  logPollHandle = setInterval(() => {
    if (!selectedJobId.value) {
      stopLogPolling()
      return
    }
    if (!isActive(selectedJob.value)) {
      stopLogPolling()
      return
    }
    loadLogs(selectedJobId.value)
  }, 1500)
}

function stopLogPolling() {
  if (logPollHandle) {
    clearInterval(logPollHandle)
    logPollHandle = null
  }
}

// Per-selected-job parsed result for GenerateVariants. Hydrated when
// the selected job is terminal+completed AND type === 'generatevariants'.
// Fetched once via /jobs/{id} (which carries ResultJson on the detail
// DTO; the LIST endpoint deliberately omits it to keep the drawer's
// 3s job-list poll cheap). Persisted variant IDs render as router-link
// chips so the curator can deep-link straight to the question editor.
const variantsResult = ref<GenerateVariantsResult | null>(null)
const variantsResultJobId = ref<string | null>(null)
const variantsResultError = ref<string | null>(null)

const isVariantsType = (type: string | null | undefined) =>
  type?.toLowerCase() === 'generatevariants'

async function maybeLoadVariantsResult(jobId: string) {
  variantsResult.value = null
  variantsResultError.value = null
  variantsResultJobId.value = jobId

  const job = jobs.value.find(j => j.id === jobId)
  if (!job || !isVariantsType(job.type) || job.status !== 'completed') return

  const detail = await fetchJobDetail(jobId)
  if (!detail || variantsResultJobId.value !== jobId) return  // selection moved on

  if (!detail.resultJson) return
  try {
    variantsResult.value = JSON.parse(detail.resultJson) as GenerateVariantsResult
  }
  catch (err: any) {
    variantsResultError.value = `Could not parse variant result: ${err?.message ?? err}`
  }
}

watch(selectedJobId, async (id) => {
  logEntries.value = []
  if (!id) return
  await Promise.all([loadLogs(id), maybeLoadVariantsResult(id)])
  startLogPolling()
})

// Re-hydrate the result block when a selected variants job transitions
// into 'completed' (so user sees the result without having to click
// elsewhere and back). Status changes propagate via the 3s job-list
// poll already.
watch(() => selectedJob.value?.status, (status, prev) => {
  if (status === 'completed' && prev && prev !== 'completed'
      && selectedJob.value && isVariantsType(selectedJob.value.type)) {
    maybeLoadVariantsResult(selectedJob.value.id)
  }
})

watch(() => selectedJob.value?.status, (status, prev) => {
  // Stop log polling once the selected job hits a terminal state.
  if (prev && status && prev !== status
      && (status === 'completed' || status === 'failed' || status === 'cancelled')) {
    stopLogPolling()
    if (selectedJobId.value) loadLogs(selectedJobId.value)
  }
})

// When the drawer is closed, free polling resources.
watch(drawerOpen, (open) => {
  if (!open) stopLogPolling()
  else if (selectedJobId.value) startLogPolling()
})

const statusColor = (status: string) => {
  switch (status) {
    case 'completed': return 'success'
    case 'failed':    return 'error'
    case 'cancelled': return 'warning'
    case 'running':   return 'info'
    case 'queued':    return 'secondary'
    default:          return 'default'
  }
}

const statusIcon = (status: string) => {
  switch (status) {
    case 'completed': return 'tabler-circle-check'
    case 'failed':    return 'tabler-alert-circle'
    case 'cancelled': return 'tabler-circle-x'
    case 'running':   return 'tabler-loader-2'
    case 'queued':    return 'tabler-clock'
    default:          return 'tabler-question-mark'
  }
}

const elapsed = (job: IngestionJobSummary): string => {
  const start = job.startedAt ?? job.createdAt
  const end = job.completedAt ?? new Date().toISOString()
  const ms = Math.max(0, new Date(end).getTime() - new Date(start).getTime())
  if (ms < 1000) return `${ms}ms`
  const s = Math.floor(ms / 1000)
  if (s < 60) return `${s}s`
  const m = Math.floor(s / 60)
  const rs = s - m * 60
  if (m < 60) return `${m}m ${rs}s`
  const h = Math.floor(m / 60)
  const rm = m - h * 60
  return `${h}h ${rm}m`
}

const isTerminal = (status: string) =>
  status === 'completed' || status === 'failed' || status === 'cancelled'

const logLevelColor = (level: string) => {
  switch (level) {
    case 'error': return 'text-error'
    case 'warn':  return 'text-warning'
    default:      return 'text-medium-emphasis'
  }
}

const formatTimestamp = (ts: string) => {
  const d = new Date(ts)
  return `${d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}`
}

// Truncate sample stems for the persisted-IDs chip labels. Only used
// when we have at least as many samples as IDs (positionally aligned;
// the strategy emits sample[].Take(5) so for a count > 5 only the
// first 5 stems are matched and the rest fall back to bare IDs).
const stemForIndex = (idx: number): string | null => {
  const s = variantsResult.value?.sample?.[idx]?.stem
  if (!s) return null
  return s.length > 60 ? `${s.slice(0, 57)}…` : s
}

const showFailures = ref(false)
</script>

<template>
  <VNavigationDrawer
    v-model="drawerOpen"
    location="end"
    :width="520"
    temporary
  >
    <div class="d-flex flex-column" style="block-size: 100vh;">
      <!-- Header -->
      <div class="d-flex align-center pa-4 border-b">
        <VIcon icon="tabler-list-details" class="me-2" />
        <span class="text-h6">Ingestion Jobs</span>
        <VSpacer />
        <VBtn
          icon
          variant="text"
          size="small"
          :loading="loading"
          @click="fetchJobs"
        >
          <VIcon icon="tabler-refresh" />
          <VTooltip activator="parent" location="bottom">Refresh</VTooltip>
        </VBtn>
        <VBtn
          icon
          variant="text"
          size="small"
          @click="closeDrawer"
        >
          <VIcon icon="tabler-x" />
        </VBtn>
      </div>

      <!-- Tabs -->
      <VTabs v-model="activeTab" density="compact" class="border-b">
        <VTab value="active">
          Active
          <VChip
            v-if="groupedJobs.active.length > 0"
            color="info"
            size="x-small"
            variant="tonal"
            class="ms-2"
          >
            {{ groupedJobs.active.length }}
          </VChip>
        </VTab>
        <VTab value="history">
          History
          <VChip
            v-if="groupedJobs.history.length > 0"
            size="x-small"
            variant="tonal"
            class="ms-2"
          >
            {{ groupedJobs.history.length }}
          </VChip>
        </VTab>
      </VTabs>

      <!-- Top half: list of jobs (active or history) -->
      <div
        class="pa-3"
        style="overflow-y: auto; flex: 0 0 50%; min-block-size: 200px;"
      >
        <VTabsWindow v-model="activeTab">
          <VTabsWindowItem value="active">
            <div
              v-if="!loading && groupedJobs.active.length === 0"
              class="text-center pa-6 text-medium-emphasis"
            >
              No active jobs.<br>
              Upload a Bagrut PDF or trigger a cloud-dir scan.
            </div>

            <VCard
              v-for="job in groupedJobs.active"
              :key="job.id"
              variant="outlined"
              class="mb-3"
              :color="selectedJobId === job.id ? 'primary' : undefined"
              style="cursor: pointer;"
              @click="selectJob(job.id)"
            >
              <VCardText class="pa-3">
                <div class="d-flex align-center gap-2 mb-1">
                  <VIcon
                    :icon="statusIcon(job.status)"
                    :color="statusColor(job.status)"
                    size="20"
                    :class="{ 'spin': job.status === 'running' }"
                  />
                  <VChip
                    :color="statusColor(job.status)"
                    size="x-small"
                    variant="tonal"
                    label
                  >
                    {{ job.status }}
                  </VChip>
                  <VChip size="x-small" variant="tonal" label>
                    {{ job.type }}
                  </VChip>
                  <VSpacer />
                  <span class="text-caption text-medium-emphasis">{{ elapsed(job) }}</span>
                </div>
                <div class="text-body-2 font-weight-medium mb-1" style="word-break: break-word;">
                  {{ job.label }}
                </div>
                <VProgressLinear
                  :model-value="job.progressPct"
                  :indeterminate="job.status === 'queued' || job.progressPct === 0"
                  :color="statusColor(job.status)"
                  height="4"
                  class="mb-1"
                />
                <div class="d-flex align-center mt-1">
                  <span class="text-caption text-medium-emphasis flex-grow-1">
                    {{ job.progressMessage ?? `${job.progressPct}%` }}
                  </span>
                  <VBtn
                    size="x-small"
                    variant="tonal"
                    color="warning"
                    :disabled="job.cancelRequested"
                    @click.stop="cancelJob(job.id)"
                  >
                    {{ job.cancelRequested ? 'Cancelling…' : 'Cancel' }}
                  </VBtn>
                </div>
              </VCardText>
            </VCard>
          </VTabsWindowItem>

          <VTabsWindowItem value="history">
            <div
              v-if="!loading && groupedJobs.history.length === 0"
              class="text-center pa-6 text-medium-emphasis"
            >
              No past jobs yet.
            </div>

            <VCard
              v-for="job in groupedJobs.history"
              :key="job.id"
              variant="outlined"
              class="mb-2"
              :color="selectedJobId === job.id ? 'primary' : undefined"
              style="cursor: pointer;"
              @click="selectJob(job.id)"
            >
              <VCardText class="pa-3">
                <div class="d-flex align-center gap-2 mb-1">
                  <VIcon
                    :icon="statusIcon(job.status)"
                    :color="statusColor(job.status)"
                    size="18"
                  />
                  <VChip :color="statusColor(job.status)" size="x-small" variant="tonal" label>
                    {{ job.status }}
                  </VChip>
                  <VChip size="x-small" variant="tonal" label>
                    {{ job.type }}
                  </VChip>
                  <VSpacer />
                  <span class="text-caption text-medium-emphasis">{{ elapsed(job) }}</span>
                </div>
                <div class="text-body-2 mb-1" style="word-break: break-word;">
                  {{ job.label }}
                </div>
                <div
                  v-if="job.errorMessage"
                  class="text-caption text-error mb-1"
                  style="word-break: break-word;"
                >
                  {{ job.errorMessage }}
                </div>
                <div class="d-flex align-center mt-1">
                  <VSpacer />
                  <VBtn
                    v-if="isTerminal(job.status)"
                    size="x-small"
                    variant="text"
                    color="error"
                    @click.stop="deleteJob(job.id)"
                  >
                    <VIcon icon="tabler-trash" size="14" />
                  </VBtn>
                </div>
              </VCardText>
            </VCard>
          </VTabsWindowItem>
        </VTabsWindow>
      </div>

      <!-- Variant-result block: only renders for a completed
           generate_variants job once /jobs/{id} returns ResultJson.
           Surfaces the full ledger (persisted IDs, persist failures,
           summary counts) so the curator can act on the variants
           without leaving the drawer to hunt them down in the kanban. -->
      <div
        v-if="selectedJob && isVariantsType(selectedJob.type) && selectedJob.status === 'completed'"
        class="border-t px-3 py-2"
        style="background: rgb(var(--v-theme-surface)); flex: 0 0 auto;"
        data-test="variants-result-block"
      >
        <div v-if="variantsResultError" class="text-caption text-error">
          {{ variantsResultError }}
        </div>
        <div v-else-if="!variantsResult" class="text-caption text-medium-emphasis">
          Loading variant result…
        </div>
        <template v-else>
          <div class="d-flex align-center mb-2">
            <VIcon icon="tabler-sparkles" size="16" class="me-1 text-info" />
            <span class="text-body-2 font-weight-medium">
              Generated {{ variantsResult.generated }} ·
              {{ variantsResult.passedQualityGate }} passed QG ·
              {{ variantsResult.persistedQuestionIds.length }} persisted
            </span>
          </div>

          <div
            v-if="variantsResult.persistedQuestionIds.length === 0"
            class="text-caption text-medium-emphasis mb-2"
            data-test="variants-result-empty"
          >
            No variants persisted. See the log panel below for per-candidate drop reasons (quality gate / CAS gate).
          </div>
          <div v-else class="mb-2" data-test="variants-result-persisted">
            <div class="text-caption text-medium-emphasis mb-1">
              Persisted variants (click to open in editor):
            </div>
            <div class="d-flex flex-wrap" style="gap: 6px;">
              <RouterLink
                v-for="(qid, idx) in variantsResult.persistedQuestionIds"
                :key="qid"
                :to="{ name: 'apps-questions-edit-id', params: { id: qid } }"
                class="text-decoration-none"
              >
                <VChip
                  size="small"
                  variant="tonal"
                  color="success"
                  prepend-icon="tabler-external-link"
                  :title="stemForIndex(idx) ?? qid"
                >
                  {{ stemForIndex(idx) ?? qid }}
                </VChip>
              </RouterLink>
            </div>
          </div>

          <div
            v-if="variantsResult.persistFailures.length > 0"
            data-test="variants-result-failures"
          >
            <VBtn
              size="x-small"
              variant="text"
              color="warning"
              :prepend-icon="showFailures ? 'tabler-chevron-down' : 'tabler-chevron-right'"
              @click="showFailures = !showFailures"
            >
              {{ variantsResult.persistFailures.length }} persist failure{{ variantsResult.persistFailures.length === 1 ? '' : 's' }}
            </VBtn>
            <div v-if="showFailures" class="mt-1 ps-3 text-caption text-warning">
              <div v-for="(f, idx) in variantsResult.persistFailures" :key="idx">
                · {{ f }}
              </div>
            </div>
          </div>
        </template>
      </div>

      <!-- Bottom half: log panel for the selected job -->
      <div
        class="border-t d-flex flex-column"
        style="flex: 1 1 50%; min-block-size: 200px; background: rgb(var(--v-theme-surface));"
      >
        <div class="d-flex align-center px-3 py-2 border-b">
          <VIcon icon="tabler-terminal-2" size="18" class="me-2" />
          <span class="text-body-2 font-weight-medium">
            {{ selectedJob ? `Logs · ${selectedJob.label}` : 'Logs' }}
          </span>
          <VSpacer />
          <VChip
            v-if="selectedJob"
            :color="statusColor(selectedJob.status)"
            size="x-small"
            variant="tonal"
            label
          >
            {{ selectedJob.status }}
          </VChip>
          <VBtn
            v-if="selectedJob"
            icon
            variant="text"
            size="x-small"
            :loading="logsLoading"
            class="ms-1"
            @click="loadLogs(selectedJob.id)"
          >
            <VIcon icon="tabler-refresh" size="14" />
          </VBtn>
        </div>

        <div
          ref="logsContainer"
          class="flex-grow-1 px-3 py-2"
          style="overflow-y: auto; font-family: 'JetBrains Mono', 'Fira Code', monospace; font-size: 12px; line-height: 1.5;"
        >
          <div
            v-if="!selectedJob"
            class="text-center pa-4 text-medium-emphasis"
          >
            Select a job above to view its log stream.
          </div>
          <div
            v-else-if="logEntries.length === 0"
            class="text-center pa-4 text-medium-emphasis text-caption"
          >
            <span v-if="logsLoading">Loading…</span>
            <span v-else>No log entries yet.</span>
          </div>
          <div
            v-for="(entry, idx) in logEntries"
            :key="idx"
            :class="logLevelColor(entry.level)"
          >
            <span class="text-disabled">{{ formatTimestamp(entry.timestamp) }}</span>
            <span class="ms-2">{{ entry.message }}</span>
          </div>
        </div>
      </div>
    </div>
  </VNavigationDrawer>
</template>

<style scoped>
.spin {
  animation: spin 1.6s linear infinite;
}
@keyframes spin {
  from { transform: rotate(0deg); }
  to   { transform: rotate(360deg); }
}
.border-b {
  border-block-end: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}
.border-t {
  border-block-start: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}
</style>
