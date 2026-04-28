<!-- =============================================================================
     Cena Platform — Manual Ingest Dialog

     Drives the manual side of the cloud-directory ingestion split:
       - Loads /admin/ingestion-settings to get the saved cloud directories.
       - "Refresh counts" calls /admin/ingestion/cloud-dir/list per row to
         show file count + already-ingested count.
       - User checks the dirs they want to ingest, clicks "Start" → calls
         /admin/ingestion/cloud-dir/ingest per selected dir, with empty
         FileKeys (i.e. "ingest everything new"; SHA-256 dedup gates
         already-ingested files server-side).

     Auto-watch directories are still listed here so a curator can also
     trigger them on demand without waiting for the next scan tick.
============================================================================= -->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { $api } from '@/utils/api'
import { useIngestionJobs } from '@/composables/useIngestionJobs'

interface CloudDirEntry {
  id: string
  name: string
  provider: string
  path: string
  prefix?: string | null
  enabled: boolean
  autoWatch: boolean
  watchIntervalMinutes?: number | null
}

interface CloudFileEntry {
  key: string
  filename: string
  sizeBytes: number
  contentType: string
  lastModified: string
  alreadyIngested: boolean
}

interface ListResponse {
  files: CloudFileEntry[]
  totalCount: number
}

interface IngestResponse {
  filesQueued: number
  filesSkipped: number
  batchId: string
}

interface IngestionSettingsLite {
  cloudDirectories: CloudDirEntry[]
}

const props = defineProps<{
  modelValue: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  ingested: []
}>()

const loading = ref(false)
const dirs = ref<CloudDirEntry[]>([])
const selectedIds = ref<string[]>([])
const counts = ref<Record<string, { total: number, alreadyIngested: number, error?: string }>>({})
const refreshing = ref(false)
const ingesting = ref(false)
const result = ref<{ ok: boolean, message: string } | null>(null)

const isOpen = computed({
  get: () => props.modelValue,
  set: (v: boolean) => emit('update:modelValue', v),
})

const enabledDirs = computed(() => dirs.value.filter(d => d.enabled))

const close = () => {
  isOpen.value = false
}

const fetchDirs = async () => {
  loading.value = true
  result.value = null
  try {
    const data = await $api<IngestionSettingsLite>('/admin/ingestion-settings')
    dirs.value = data.cloudDirectories ?? []
    selectedIds.value = enabledDirs.value.map(d => d.id)
  }
  catch (err: any) {
    result.value = { ok: false, message: err?.message ?? 'Failed to load cloud directories.' }
  }
  finally {
    loading.value = false
  }
}

const refreshCounts = async () => {
  if (enabledDirs.value.length === 0) return
  refreshing.value = true
  try {
    await Promise.all(enabledDirs.value.map(async (d) => {
      try {
        const list = await $api<ListResponse>('/admin/ingestion/cloud-dir/list', {
          method: 'POST',
          body: {
            provider: d.provider,
            bucketOrPath: d.path,
            prefix: d.prefix ?? null,
            continuationToken: null,
          },
        })
        const already = list.files.filter(f => f.alreadyIngested).length
        counts.value[d.id] = { total: list.totalCount, alreadyIngested: already }
      }
      catch (err: any) {
        counts.value[d.id] = { total: 0, alreadyIngested: 0, error: err?.data?.error ?? err?.message ?? 'List failed' }
      }
    }))
  }
  finally {
    refreshing.value = false
  }
}

const startIngest = async () => {
  if (selectedIds.value.length === 0) {
    result.value = { ok: false, message: 'Select at least one directory.' }
    return
  }
  ingesting.value = true
  result.value = null
  const errors: string[] = []
  const enqueued: string[] = []
  const { enqueueCloudDir, openDrawer } = useIngestionJobs()
  try {
    for (const id of selectedIds.value) {
      const dir = dirs.value.find(d => d.id === id)
      if (!dir) continue
      try {
        const jobId = await enqueueCloudDir({
          provider: dir.provider,
          bucketOrPath: dir.path,
          fileKeys: [],
          prefix: dir.prefix ?? null,
        })
        enqueued.push(jobId)
      }
      catch (err: any) {
        errors.push(`${dir.name}: ${err?.data?.error ?? err?.message ?? 'enqueue failed'}`)
      }
    }
    if (errors.length === 0) {
      result.value = {
        ok: true,
        message: `Queued ${enqueued.length} ingestion job${enqueued.length === 1 ? '' : 's'}. Track progress in the Ingestion Jobs drawer.`,
      }
      emit('ingested')
      openDrawer()
    }
    else {
      result.value = {
        ok: false,
        message: `${enqueued.length} queued. Errors: ${errors.join(' | ')}`,
      }
    }
  }
  finally {
    ingesting.value = false
  }
}

watch(isOpen, (open) => {
  if (open) {
    counts.value = {}
    result.value = null
    fetchDirs()
  }
})
</script>

<template>
  <VDialog
    v-model="isOpen"
    max-width="720"
    persistent
  >
    <VCard>
      <VCardTitle class="d-flex align-center gap-2 pe-4">
        <VIcon icon="tabler-folder-down" />
        <span>Manual Ingest from Cloud Directory</span>
        <VSpacer />
        <VBtn
          icon="tabler-x"
          variant="text"
          size="small"
          :disabled="ingesting"
          @click="close"
        />
      </VCardTitle>

      <VDivider />

      <VCardText class="pt-4">
        <VAlert
          v-if="result"
          :color="result.ok ? 'success' : 'error'"
          variant="tonal"
          class="mb-3"
          closable
          @click:close="result = null"
        >
          {{ result.message }}
        </VAlert>

        <div
          v-if="loading"
          class="d-flex justify-center py-8"
        >
          <VProgressCircular indeterminate />
        </div>

        <template v-else>
          <div
            v-if="enabledDirs.length === 0"
            class="text-center pa-6 text-medium-emphasis"
          >
            No enabled cloud directories. Configure one under
            <strong>Ingestion Settings</strong> first.
          </div>

          <template v-else>
            <div class="d-flex align-center mb-3 gap-2">
              <span class="text-body-2 text-medium-emphasis">
                Select directories to scan, then start. Already-ingested
                files (matched by SHA-256) are skipped automatically.
              </span>
              <VSpacer />
              <VBtn
                size="small"
                variant="text"
                :loading="refreshing"
                :disabled="ingesting"
                @click="refreshCounts"
              >
                <VIcon icon="tabler-refresh" start />
                Refresh counts
              </VBtn>
            </div>

            <VList
              density="compact"
              select-strategy="multiple"
              class="rounded border"
            >
              <VListItem
                v-for="dir in enabledDirs"
                :key="dir.id"
              >
                <template #prepend>
                  <VCheckbox
                    :model-value="selectedIds.includes(dir.id)"
                    :disabled="ingesting"
                    hide-details
                    density="compact"
                    @update:model-value="(v: boolean | null) => {
                      if (v) selectedIds.push(dir.id)
                      else selectedIds = selectedIds.filter((x: string) => x !== dir.id)
                    }"
                  />
                </template>
                <VListItemTitle class="d-flex align-center gap-2">
                  <span>{{ dir.name }}</span>
                  <VChip
                    size="x-small"
                    label
                    variant="tonal"
                  >
                    {{ dir.provider }}
                  </VChip>
                  <VChip
                    v-if="dir.autoWatch"
                    size="x-small"
                    color="success"
                    variant="tonal"
                  >
                    auto-watch
                  </VChip>
                </VListItemTitle>
                <VListItemSubtitle class="text-caption">
                  {{ dir.path }}{{ dir.prefix ? `/${dir.prefix}` : '' }}
                  <template v-if="counts[dir.id]">
                    <span v-if="counts[dir.id].error" class="text-error ms-2">
                      · {{ counts[dir.id].error }}
                    </span>
                    <span v-else class="ms-2">
                      · {{ counts[dir.id].total }} files
                      ({{ counts[dir.id].alreadyIngested }} already ingested)
                    </span>
                  </template>
                </VListItemSubtitle>
              </VListItem>
            </VList>
          </template>
        </template>
      </VCardText>

      <VDivider />

      <VCardActions class="pa-4">
        <VSpacer />
        <VBtn
          variant="text"
          :disabled="ingesting"
          @click="close"
        >
          Cancel
        </VBtn>
        <VBtn
          color="primary"
          :loading="ingesting"
          :disabled="selectedIds.length === 0 || enabledDirs.length === 0"
          @click="startIngest"
        >
          <VIcon icon="tabler-player-play" start />
          Start ingestion ({{ selectedIds.length }})
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
