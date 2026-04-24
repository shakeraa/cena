<script setup lang="ts">
import PipelineStats from '@/views/apps/ingestion/PipelineStats.vue'
import ItemDetailPanel from '@/views/apps/ingestion/ItemDetailPanel.vue'
import UploadDialog from '@/views/apps/ingestion/UploadDialog.vue'
import BagrutUploadDialog from '@/views/apps/ingestion/BagrutUploadDialog.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Content',
  },
})

interface PipelineItem {
  id: string
  sourceFilename: string
  sourceType: 'url' | 's3' | 'photo' | 'batch'
  questionCount: number
  qualityScore: number | null
  stageEnteredAt: string
  hasFailed: boolean
  errorSummary: string | null
}

interface PipelineStage {
  name: string
  key: string
  items: PipelineItem[]
}

const STAGES = [
  { name: 'Incoming', key: 'incoming' },
  { name: 'OCR Processing', key: 'ocr_processing' },
  { name: 'Segmented', key: 'segmented' },
  { name: 'Normalized', key: 'normalized' },
  { name: 'Classified', key: 'classified' },
  { name: 'Deduplicated', key: 'deduplicated' },
  { name: 'Re-Created', key: 're_created' },
  { name: 'In Review', key: 'in_review' },
  { name: 'Published', key: 'published' },
] as const

const pipelineData = ref<PipelineStage[]>(
  STAGES.map(stage => ({ name: stage.name, key: stage.key, items: [] })),
)
const loading = ref(true)
const selectedItemId = ref('')
const isDetailOpen = ref(false)
const isUploadOpen = ref(false)
const isBagrutOpen = ref(false)   // RDY-057 — Bagrut-specific ingest dialog
let refreshInterval: ReturnType<typeof setInterval> | null = null

// ── Phase 3.4: review-screen filters (pure client-side on cached data) ──
type SourceType = PipelineItem['sourceType']
const SOURCE_TYPES: readonly SourceType[] = ['url', 's3', 'photo', 'batch']
const sourceFilter  = ref<SourceType[]>([])        // empty = all
const errorOnly     = ref(false)
const stuckOnly     = ref(false)                   // > 30 min in current stage
const searchText    = ref('')
const STUCK_THRESHOLD_MIN = 30

const isStuck = (item: PipelineItem): boolean => {
  const minutes = (Date.now() - new Date(item.stageEnteredAt).getTime()) / 60_000

  return minutes > STUCK_THRESHOLD_MIN
}

const matchesFilters = (item: PipelineItem): boolean => {
  if (sourceFilter.value.length && !sourceFilter.value.includes(item.sourceType as SourceType))
    return false
  if (errorOnly.value && !item.hasFailed)
    return false
  if (stuckOnly.value && !isStuck(item))
    return false

  const q = searchText.value.trim().toLowerCase()
  if (q && !item.sourceFilename.toLowerCase().includes(q))
    return false

  return true
}

const filteredPipeline = computed<PipelineStage[]>(() =>
  pipelineData.value.map(stage => ({
    name: stage.name,
    key: stage.key,
    items: stage.items.filter(matchesFilters),
  })),
)

const activeFilterCount = computed(() => {
  let count = 0
  if (sourceFilter.value.length) count++
  if (errorOnly.value) count++
  if (stuckOnly.value) count++
  if (searchText.value.trim()) count++

  return count
})

const resetFilters = () => {
  sourceFilter.value = []
  errorOnly.value = false
  stuckOnly.value = false
  searchText.value = ''
}

// ── Aggregate health snapshot across the whole board ────────────────────
const totalsSummary = computed(() => {
  const all = pipelineData.value.flatMap(s => s.items)
  const failed = all.filter(i => i.hasFailed).length
  const stuck  = all.filter(i => !i.hasFailed && isStuck(i)).length
  const visible = filteredPipeline.value.flatMap(s => s.items).length

  return { total: all.length, visible, failed, stuck }
})

const fetchPipeline = async () => {
  try {
    const response = await $api('/admin/ingestion/pipeline-status') as Record<string, PipelineItem[]>

    pipelineData.value = STAGES.map(stage => ({
      name: stage.name,
      key: stage.key,
      items: response[stage.key] ?? [],
    }))
  }
  catch (error) {
    console.error('Failed to fetch pipeline status:', error)
  }
  finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchPipeline()
  refreshInterval = setInterval(fetchPipeline, 30_000)
})

onBeforeUnmount(() => {
  if (refreshInterval) {
    clearInterval(refreshInterval)
    refreshInterval = null
  }
})

const openDetail = (itemId: string) => {
  selectedItemId.value = itemId
  isDetailOpen.value = true
}

const handleItemUpdated = () => {
  fetchPipeline()
}

const handleUploaded = () => {
  fetchPipeline()
}

const sourceTypeColor = (type: string): string => {
  const map: Record<string, string> = {
    url: 'info',
    s3: 'primary',
    photo: 'warning',
    batch: 'secondary',
  }

  return map[type] ?? 'default'
}

const cardColor = (item: PipelineItem): string => {
  if (item.hasFailed)
    return 'error'

  const enteredAt = new Date(item.stageEnteredAt).getTime()
  const now = Date.now()
  const minutesInStage = (now - enteredAt) / 60_000

  if (minutesInStage > 5)
    return 'warning'

  return 'success'
}

const cardBorderClass = (item: PipelineItem): string => {
  const color = cardColor(item)

  return `border-s-${color} border-s-4`
}

const formatTimeAgo = (timestamp: string): string => {
  const diff = Date.now() - new Date(timestamp).getTime()
  const minutes = Math.floor(diff / 60_000)

  if (minutes < 1)
    return 'just now'
  if (minutes < 60)
    return `${minutes}m ago`

  const hours = Math.floor(minutes / 60)

  if (hours < 24)
    return `${hours}h ago`

  return `${Math.floor(hours / 24)}d ago`
}
</script>

<template>
  <div>
    <!-- Stats Section -->
    <PipelineStats class="mb-6" />

    <!-- Pipeline Board Header + Filter Bar (Phase 3.4 review screen) -->
    <VCard class="mb-6">
      <VCardText class="d-flex align-center justify-space-between py-3 flex-wrap gap-3">
        <div>
          <h5 class="text-h5">
            Content Ingestion Pipeline
          </h5>
          <span class="text-body-2 text-medium-emphasis">
            Auto-refreshes every 30 seconds
            <span v-if="totalsSummary.total > 0" class="ms-2">
              —
              <strong>{{ totalsSummary.total }}</strong> items,
              <span v-if="totalsSummary.failed" class="text-error">
                <strong>{{ totalsSummary.failed }}</strong> failed
              </span>
              <span v-if="totalsSummary.failed && totalsSummary.stuck"> · </span>
              <span v-if="totalsSummary.stuck" class="text-warning">
                <strong>{{ totalsSummary.stuck }}</strong> stuck &gt; {{ STUCK_THRESHOLD_MIN }}m
              </span>
              <span
                v-if="activeFilterCount > 0"
                class="ms-2"
              >
                · showing <strong>{{ totalsSummary.visible }}</strong>
              </span>
            </span>
          </span>
        </div>
        <VBtn
          variant="tonal"
          color="primary"
          :loading="loading"
          @click="fetchPipeline"
        >
          <VIcon
            icon="ri-refresh-line"
            start
          />
          Refresh
        </VBtn>
      </VCardText>

      <VDivider />

      <VCardText class="d-flex align-center flex-wrap gap-3 py-3">
        <VTextField
          v-model="searchText"
          density="compact"
          variant="outlined"
          prepend-inner-icon="ri-search-line"
          placeholder="Search filename…"
          hide-details
          clearable
          style="max-inline-size: 260px;"
        />

        <VSelect
          v-model="sourceFilter"
          :items="SOURCE_TYPES"
          label="Source type"
          density="compact"
          variant="outlined"
          multiple
          chips
          closable-chips
          hide-details
          clearable
          style="min-inline-size: 220px;"
        />

        <VCheckbox
          v-model="errorOnly"
          label="Errors only"
          density="compact"
          hide-details
          color="error"
        />

        <VCheckbox
          v-model="stuckOnly"
          :label="`Stuck > ${STUCK_THRESHOLD_MIN}m`"
          density="compact"
          hide-details
          color="warning"
        />

        <VSpacer />

        <VBtn
          v-if="activeFilterCount > 0"
          variant="text"
          size="small"
          color="secondary"
          @click="resetFilters"
        >
          <VIcon icon="ri-close-line" start />
          Clear ({{ activeFilterCount }})
        </VBtn>
      </VCardText>
    </VCard>

    <!-- Kanban Board -->
    <div
      v-if="loading && !pipelineData.length"
      class="d-flex justify-center pa-8"
    >
      <VProgressCircular
        indeterminate
        color="primary"
        size="48"
      />
    </div>

    <div
      v-else
      class="pipeline-board"
    >
      <div
        v-for="stage in filteredPipeline"
        :key="stage.key"
        class="pipeline-column"
      >
        <!-- Column Header -->
        <div class="pipeline-column-header pa-3 rounded-t">
          <div class="d-flex align-center justify-space-between">
            <span class="text-body-1 font-weight-semibold">{{ stage.name }}</span>
            <VChip
              size="small"
              :color="stage.items.length > 0 ? 'primary' : 'default'"
              variant="tonal"
            >
              {{ stage.items.length }}
            </VChip>
          </div>
        </div>

        <!-- Column Body -->
        <div class="pipeline-column-body pa-2">
          <div
            v-if="!stage.items.length"
            class="text-center pa-4"
          >
            <span class="text-body-2 text-disabled">No items</span>
          </div>

          <VCard
            v-for="item in stage.items"
            :key="item.id"
            class="pipeline-card mb-2 cursor-pointer"
            :class="cardBorderClass(item)"
            variant="outlined"
            density="compact"
            hover
            @click="openDetail(item.id)"
          >
            <VCardText class="pa-3">
              <div class="d-flex align-center justify-space-between mb-1">
                <span
                  class="text-body-2 font-weight-medium text-truncate"
                  style="max-inline-size: 160px;"
                  :title="item.sourceFilename"
                >
                  {{ item.sourceFilename }}
                </span>
                <VChip
                  :color="sourceTypeColor(item.sourceType)"
                  size="x-small"
                  label
                >
                  {{ item.sourceType }}
                </VChip>
              </div>

              <div class="d-flex align-center gap-2 text-body-2 text-medium-emphasis">
                <span>{{ item.questionCount }} Q</span>
                <template v-if="item.qualityScore !== null">
                  <VDivider
                    vertical
                    class="mx-1"
                  />
                  <span>{{ item.qualityScore }}%</span>
                </template>
              </div>

              <div class="d-flex align-center justify-space-between mt-1 gap-1">
                <span class="text-caption text-disabled">
                  {{ formatTimeAgo(item.stageEnteredAt) }}
                </span>
                <div class="d-flex align-center gap-1">
                  <VIcon
                    v-if="!item.hasFailed && isStuck(item)"
                    icon="ri-time-line"
                    color="warning"
                    size="16"
                    :title="`Stuck > ${STUCK_THRESHOLD_MIN}m`"
                  />
                  <VIcon
                    v-if="item.hasFailed"
                    icon="ri-error-warning-fill"
                    color="error"
                    size="16"
                    :title="item.errorSummary ?? 'Ingest failure — open for details'"
                  />
                </div>
              </div>

              <!-- Inline error summary preview (one-liner, truncated) -->
              <div
                v-if="item.hasFailed && item.errorSummary"
                class="text-caption text-error text-truncate mt-1"
                :title="item.errorSummary"
              >
                {{ item.errorSummary }}
              </div>
            </VCardText>
          </VCard>
        </div>
      </div>
    </div>

    <!-- Upload FAB -->
    <VBtn
      icon
      color="primary"
      size="large"
      class="pipeline-fab"
      elevation="8"
      @click="isUploadOpen = true"
    >
      <VIcon
        icon="ri-add-line"
        size="28"
      />
      <VTooltip
        activator="parent"
        location="start"
      >
        Upload Content
      </VTooltip>
    </VBtn>

    <!-- Item Detail Drawer -->
    <ItemDetailPanel
      :item-id="selectedItemId"
      :is-open="isDetailOpen"
      @update:is-open="isDetailOpen = $event"
      @item-updated="handleItemUpdated"
    />

    <!-- Upload Dialog -->
    <UploadDialog
      :is-open="isUploadOpen"
      @update:is-open="isUploadOpen = $event"
      @uploaded="handleUploaded"
    />

    <!-- RDY-057: Bagrut-specific PDF ingest (super-admin only on the server) -->
    <BagrutUploadDialog
      v-model="isBagrutOpen"
      @ingested="fetchPipeline"
    />

    <!-- Secondary FAB for Bagrut ingest -->
    <VBtn
      icon
      color="secondary"
      size="small"
      class="pipeline-bagrut-fab"
      elevation="4"
      @click="isBagrutOpen = true"
    >
      <VIcon icon="ri-file-pdf-2-line" size="22" />
      <VTooltip
        activator="parent"
        location="start"
      >
        Upload Bagrut PDF
      </VTooltip>
    </VBtn>
  </div>
</template>

<style scoped>
.pipeline-bagrut-fab {
  position: fixed;
  bottom: 6rem;
  inset-inline-end: 2rem;
  z-index: 9;
}
</style>

<style lang="scss" scoped>
.pipeline-board {
  display: flex;
  gap: 16px;
  overflow-x: auto;
  padding-block-end: 16px;

  // Smooth horizontal scrolling
  scroll-behavior: smooth;
  -webkit-overflow-scrolling: touch;
}

.pipeline-column {
  flex: 0 0 240px;
  min-inline-size: 240px;
  background: rgb(var(--v-theme-surface));
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  max-block-size: calc(100vh - 320px);
}

.pipeline-column-header {
  background: rgba(var(--v-theme-on-surface), 0.04);
  border-block-end: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  position: sticky;
  inset-block-start: 0;
  z-index: 1;
}

.pipeline-column-body {
  flex: 1;
  overflow-y: auto;
}

.pipeline-card {
  transition: box-shadow 0.2s ease;

  &:hover {
    box-shadow: 0 2px 8px rgba(0, 0, 0, 10%);
  }
}

.border-s-success {
  border-inline-start: 4px solid rgb(var(--v-theme-success)) !important;
}

.border-s-warning {
  border-inline-start: 4px solid rgb(var(--v-theme-warning)) !important;
}

.border-s-error {
  border-inline-start: 4px solid rgb(var(--v-theme-error)) !important;
}

.pipeline-fab {
  position: fixed;
  inset-block-end: 32px;
  inset-inline-end: 32px;
  z-index: 10;
}
</style>
