<script setup lang="ts">
import PipelineStats from '@/views/apps/ingestion/PipelineStats.vue'
import ItemDetailPanel from '@/views/apps/ingestion/ItemDetailPanel.vue'
import UploadDialog from '@/views/apps/ingestion/UploadDialog.vue'
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

const pipelineData = ref<PipelineStage[]>([])
const loading = ref(true)
const selectedItemId = ref('')
const isDetailOpen = ref(false)
const isUploadOpen = ref(false)
let refreshInterval: ReturnType<typeof setInterval> | null = null

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

    <!-- Pipeline Board Header -->
    <VCard class="mb-6">
      <VCardText class="d-flex align-center justify-space-between py-3">
        <div>
          <h5 class="text-h5">
            Content Ingestion Pipeline
          </h5>
          <span class="text-body-2 text-medium-emphasis">
            Auto-refreshes every 30 seconds
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
        v-for="stage in pipelineData"
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

              <div class="d-flex align-center justify-space-between mt-1">
                <span class="text-caption text-disabled">
                  {{ formatTimeAgo(item.stageEnteredAt) }}
                </span>
                <VIcon
                  v-if="item.hasFailed"
                  icon="ri-error-warning-fill"
                  color="error"
                  size="16"
                />
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
  </div>
</template>

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
