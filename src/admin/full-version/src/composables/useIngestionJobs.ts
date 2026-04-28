// =============================================================================
// Cena Platform — useIngestionJobs (admin SPA)
//
// Shared client for /api/admin/ingestion/jobs/*. Returns a singleton
// reactive store (jobs list, loading/error flags, polling control). Pages
// and the IngestionJobsDrawer all read the same state so the drawer
// updates without per-component duplication.
//
// Polling cadence: every 3 s while at least one job is in
// queued|running. Idles (no fetch) when everything is terminal.
// =============================================================================

import { ref, computed } from 'vue'
import { $api } from '@/utils/api'

export interface IngestionJobSummary {
  id: string
  type: string                // 'bagrut' | 'cloud_dir'
  label: string
  status: string              // 'queued' | 'running' | 'completed' | 'failed' | 'cancelled'
  progressPct: number
  progressMessage: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  errorMessage: string | null
  createdBy: string | null
  cancelRequested: boolean
}

interface ListResponse {
  jobs: IngestionJobSummary[]
  total: number
}

const jobs = ref<IngestionJobSummary[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const drawerOpen = ref(false)

let pollHandle: ReturnType<typeof setInterval> | null = null

const POLL_INTERVAL_MS = 3000

const activeCount = computed(() => jobs.value.filter(
  j => j.status === 'queued' || j.status === 'running',
).length)

const runningCount = computed(() => jobs.value.filter(j => j.status === 'running').length)

async function fetchJobs() {
  loading.value = true
  try {
    const data = await $api<ListResponse>('/admin/ingestion/jobs?limit=100')
    jobs.value = data.jobs
    error.value = null
  }
  catch (err: any) {
    error.value = err?.data?.error ?? err?.message ?? 'Failed to load jobs'
  }
  finally {
    loading.value = false
  }
}

function startPolling() {
  if (pollHandle) return
  pollHandle = setInterval(() => {
    if (activeCount.value > 0 || drawerOpen.value) {
      fetchJobs()
    }
  }, POLL_INTERVAL_MS)
}

function stopPolling() {
  if (pollHandle) {
    clearInterval(pollHandle)
    pollHandle = null
  }
}

interface BagrutMeta {
  ministrySubjectCode?: string | null
  ministryQuestionPaperCode?: string | null
  year?: number | null
  units?: number | null
  topicId?: string | null
}

async function enqueueBagrut(file: File, examCode: string, meta?: BagrutMeta): Promise<string> {
  const form = new FormData()
  form.append('file', file)
  form.append('examCode', examCode)
  if (meta?.ministrySubjectCode) form.append('ministrySubjectCode', meta.ministrySubjectCode)
  if (meta?.ministryQuestionPaperCode) form.append('ministryQuestionPaperCode', meta.ministryQuestionPaperCode)
  if (meta?.year != null) form.append('year', String(meta.year))
  if (meta?.units != null) form.append('units', String(meta.units))
  if (meta?.topicId) form.append('topicId', meta.topicId)
  const resp = await $api<{ jobId: string }>('/admin/ingestion/jobs/bagrut', {
    method: 'POST',
    body: form,
  })
  await fetchJobs()
  return resp.jobId
}

async function fetchLogs(jobId: string, tail = 200): Promise<JobLogEntry[]> {
  try {
    const data = await $api<{ entries: JobLogEntry[] }>(`/admin/ingestion/jobs/${jobId}/logs?tail=${tail}`)
    return data.entries ?? []
  }
  catch {
    return []
  }
}

export interface JobLogEntry {
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
}

async function enqueueCloudDir(payload: {
  provider: string
  bucketOrPath: string
  fileKeys?: string[] | null
  prefix?: string | null
}): Promise<string> {
  const resp = await $api<{ jobId: string }>('/admin/ingestion/jobs/cloud-dir', {
    method: 'POST',
    body: payload,
  })
  await fetchJobs()
  return resp.jobId
}

async function cancelJob(id: string): Promise<boolean> {
  try {
    await $api(`/admin/ingestion/jobs/${id}/cancel`, { method: 'POST' })
    await fetchJobs()
    return true
  }
  catch {
    return false
  }
}

async function deleteJob(id: string): Promise<boolean> {
  try {
    await $api(`/admin/ingestion/jobs/${id}`, { method: 'DELETE' })
    jobs.value = jobs.value.filter(j => j.id !== id)
    return true
  }
  catch {
    return false
  }
}

function openDrawer() {
  drawerOpen.value = true
  fetchJobs()
}

function closeDrawer() {
  drawerOpen.value = false
}

export function useIngestionJobs() {
  return {
    jobs,
    loading,
    error,
    drawerOpen,
    activeCount,
    runningCount,
    fetchJobs,
    fetchLogs,
    startPolling,
    stopPolling,
    enqueueBagrut,
    enqueueCloudDir,
    cancelJob,
    deleteJob,
    openDrawer,
    closeDrawer,
  }
}
