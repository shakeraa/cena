<script setup lang="ts">
import { PerfectScrollbar } from 'vue3-perfect-scrollbar'
import { $api } from '@/utils/api'
import { useIngestionJobs } from '@/composables/useIngestionJobs'
import CuratorMetadataPanel from './CuratorMetadataPanel.vue'

interface ProcessingStage {
  name: string
  status: 'completed' | 'in_progress' | 'pending' | 'failed'
  startedAt: string | null
  completedAt: string | null
  error: string | null
}

interface QualityScores {
  mathCorrectness: number
  languageQuality: number
  pedagogicalQuality: number
  plagiarismScore: number
}

interface QualityGateScores {
  compositeScore: number
  gateDecision: string
  factualAccuracy: number
  languageQuality: number
  pedagogicalQuality: number
  distractorQuality: number
  stemClarity: number
  bloomAlignment: number
  structuralValidity: number
  culturalSensitivity: number
  violationCount: number
}

interface ItemDetail {
  id: string
  originalFilename: string
  // 'bagrut' added by BagrutDraftPersistence — Bagrut PDFs land in the
  // kanban as PipelineItemDocument rows with sourceType="bagrut".
  sourceType: 'url' | 's3' | 'photo' | 'batch' | 'bagrut'
  currentStage: string
  questionCount: number
  qualityScores: QualityScores | null
  qualityGate: QualityGateScores | null
  stages: ProcessingStage[]
  errors: string[]
  createdAt: string
  updatedAt: string
}

interface Props {
  itemId: string
  isOpen: boolean
}

interface Emit {
  (e: 'update:isOpen', value: boolean): void
  (e: 'item-updated'): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

const item = ref<ItemDetail | null>(null)
const loading = ref(false)
const actionLoading = ref(false)

// Generate-variants dialog state (option 2)
const variantsDialogOpen = ref(false)
const variantsSubmitting = ref(false)
const variantsFlagEnabled = ref<boolean | null>(null)
const variantsFlagReason = ref<string>('')
const variantsForm = ref({
  subject: 'math',
  topic: '',
  grade: '11',
  bloomsLevel: 3,
  language: 'en',
  count: 5,
  minDifficulty: 0.4,
  maxDifficulty: 0.7,
})

// ADR-0059 §15.5 + PRR-249: source-anchored variant flow is
// implementation-complete but gated on legal sign-off. Probe the
// feature-flag readback when the dialog opens so we render the
// 'Disabled' banner up front rather than failing on submit.
import { watch } from 'vue'
watch(variantsDialogOpen, async (open) => {
  if (!open) return
  variantsFlagEnabled.value = null
  try {
    const data = await $api<{ bagrutSeedToLlmEnabled: boolean, bagrutSeedDisabledReason: string }>(
      '/admin/ingestion/jobs/feature-flags',
    )
    variantsFlagEnabled.value = !!data.bagrutSeedToLlmEnabled
    variantsFlagReason.value = data.bagrutSeedDisabledReason ?? ''
  }
  catch {
    variantsFlagEnabled.value = false
    variantsFlagReason.value = 'Could not load feature flag state — check admin-api connectivity.'
  }
})

const { openDrawer: openJobsDrawer } = useIngestionJobs()

const enqueueVariants = async () => {
  if (!item.value) return
  variantsSubmitting.value = true
  try {
    await $api('/admin/ingestion/jobs/generate-variants', {
      method: 'POST',
      body: {
        draftId: item.value.id,
        count: Math.max(1, Math.min(20, variantsForm.value.count)),
        subject: variantsForm.value.subject,
        topic: variantsForm.value.topic || null,
        grade: variantsForm.value.grade,
        bloomsLevel: variantsForm.value.bloomsLevel,
        minDifficulty: variantsForm.value.minDifficulty,
        maxDifficulty: variantsForm.value.maxDifficulty,
        language: variantsForm.value.language,
      },
    })
    variantsDialogOpen.value = false
    openJobsDrawer()
  }
  catch (err: any) {
    console.error('enqueueVariants failed', err)
  }
  finally {
    variantsSubmitting.value = false
  }
}

const fetchDetail = async () => {
  if (!props.itemId)
    return

  loading.value = true
  try {
    item.value = await $api(`/admin/ingestion/items/${props.itemId}/detail`)
  }
  catch (error) {
    console.error('Failed to fetch item detail:', error)
  }
  finally {
    loading.value = false
  }
}

watch(() => props.itemId, (newId) => {
  if (newId)
    fetchDetail()
})

watch(() => props.isOpen, (open) => {
  if (open && props.itemId)
    fetchDetail()
})

const handleClose = () => {
  emit('update:isOpen', false)
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

const stageStatusIcon = (status: string): string => {
  const map: Record<string, string> = {
    completed: 'tabler-circle-check',
    in_progress: 'tabler-loader-2',
    pending: 'tabler-clock',
    failed: 'tabler-circle-x',
  }

  return map[status] ?? 'tabler-question-mark'
}

const stageStatusColor = (status: string): string => {
  const map: Record<string, string> = {
    completed: 'success',
    in_progress: 'primary',
    pending: 'secondary',
    failed: 'error',
  }

  return map[status] ?? 'default'
}

const formatTimestamp = (ts: string | null): string => {
  if (!ts)
    return '-'

  return new Date(ts).toLocaleString()
}

const retryItem = async () => {
  actionLoading.value = true
  try {
    await $api(`/admin/ingestion/items/${props.itemId}/retry`, { method: 'POST' })
    emit('item-updated')
    fetchDetail()
  }
  catch (error) {
    console.error('Retry failed:', error)
  }
  finally {
    actionLoading.value = false
  }
}

const rejectItem = async () => {
  actionLoading.value = true
  try {
    await $api(`/admin/ingestion/items/${props.itemId}/reject`, { method: 'POST' })
    emit('item-updated')
    handleClose()
  }
  catch (error) {
    console.error('Reject failed:', error)
  }
  finally {
    actionLoading.value = false
  }
}

const moveToReview = async () => {
  actionLoading.value = true
  try {
    await $api(`/admin/ingestion/items/${props.itemId}/move-to-review`, { method: 'POST' })
    emit('item-updated')
    fetchDetail()
  }
  catch (error) {
    console.error('Move to review failed:', error)
  }
  finally {
    actionLoading.value = false
  }
}
</script>

<template>
  <VNavigationDrawer
    data-allow-mismatch
    temporary
    :width="480"
    location="end"
    class="scrollable-content"
    :model-value="props.isOpen"
    @update:model-value="(val: boolean) => emit('update:isOpen', val)"
  >
    <AppDrawerHeaderSection
      title="Item Detail"
      @cancel="handleClose"
    />

    <VDivider />

    <VProgressLinear
      v-if="loading"
      indeterminate
      color="primary"
    />

    <PerfectScrollbar
      v-if="item && !loading"
      :options="{ wheelPropagation: false }"
    >
      <VCard flat>
        <VCardText>
          <!-- File Info -->
          <div class="mb-4">
            <h6 class="text-h6 mb-2">
              {{ item.originalFilename }}
            </h6>
            <div class="d-flex align-center gap-2 mb-1">
              <VChip
                :color="sourceTypeColor(item.sourceType)"
                size="small"
                label
              >
                {{ item.sourceType.toUpperCase() }}
              </VChip>
              <span class="text-body-2 text-medium-emphasis">
                {{ item.questionCount }} questions
              </span>
            </div>
            <p class="text-body-2 text-medium-emphasis mb-0">
              Created: {{ formatTimestamp(item.createdAt) }}
            </p>
            <p class="text-body-2 text-medium-emphasis mb-0">
              Updated: {{ formatTimestamp(item.updatedAt) }}
            </p>
          </div>

          <VDivider class="mb-4" />

          <!-- RDY-019e Curator Metadata handshake -->
          <CuratorMetadataPanel
            :item-id="props.itemId"
            class="mb-4"
            @confirmed="() => emit('item-updated')"
          />

          <VDivider class="mb-4" />

          <!-- Processing Stages -->
          <h6 class="text-h6 mb-3">
            Processing Stages
          </h6>
          <VTimeline
            density="compact"
            align="start"
            class="mb-4"
          >
            <VTimelineItem
              v-for="stage in item.stages"
              :key="stage.name"
              :dot-color="stageStatusColor(stage.status)"
              size="small"
            >
              <div class="d-flex align-center gap-2">
                <VIcon
                  :icon="stageStatusIcon(stage.status)"
                  :color="stageStatusColor(stage.status)"
                  size="18"
                />
                <span class="text-body-1 font-weight-medium">{{ stage.name }}</span>
              </div>
              <div
                v-if="stage.startedAt"
                class="text-body-2 text-medium-emphasis"
              >
                {{ formatTimestamp(stage.startedAt) }}
                <template v-if="stage.completedAt">
                  &rarr; {{ formatTimestamp(stage.completedAt) }}
                </template>
              </div>
              <VAlert
                v-if="stage.error"
                color="error"
                variant="tonal"
                density="compact"
                class="mt-1"
              >
                {{ stage.error }}
              </VAlert>
            </VTimelineItem>
          </VTimeline>

          <VDivider class="mb-4" />

          <!-- Quality Gate (8-dimension) -->
          <template v-if="item.qualityGate">
            <div class="d-flex align-center gap-2 mb-3">
              <h6 class="text-h6">Quality Gate</h6>
              <VChip
                size="x-small"
                :color="item.qualityGate.gateDecision === 'AutoApproved' ? 'success' : item.qualityGate.gateDecision === 'AutoRejected' ? 'error' : 'warning'"
                label
              >
                {{ item.qualityGate.gateDecision }}
              </VChip>
            </div>
            <div class="mb-4">
              <div class="d-flex align-center gap-2 mb-3">
                <span class="text-h5 font-weight-bold">{{ Math.round(item.qualityGate.compositeScore) }}</span>
                <span class="text-body-2 text-disabled">/ 100</span>
              </div>
              <div
                v-for="dim in [
                  { key: 'structuralValidity', label: 'Structural' },
                  { key: 'stemClarity', label: 'Stem Clarity' },
                  { key: 'distractorQuality', label: 'Distractors' },
                  { key: 'bloomAlignment', label: 'Bloom\'s' },
                  { key: 'factualAccuracy', label: 'Factual' },
                  { key: 'languageQuality', label: 'Language' },
                  { key: 'pedagogicalQuality', label: 'Pedagogy' },
                  { key: 'culturalSensitivity', label: 'Cultural' },
                ]"
                :key="dim.key"
                class="mb-2"
              >
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">{{ dim.label }}</span>
                  <span class="text-body-2 font-weight-medium">{{ (item.qualityGate as any)[dim.key] }}</span>
                </div>
                <VProgressLinear
                  :model-value="(item.qualityGate as any)[dim.key]"
                  :color="(item.qualityGate as any)[dim.key] >= 80 ? 'success' : (item.qualityGate as any)[dim.key] >= 60 ? 'info' : (item.qualityGate as any)[dim.key] >= 40 ? 'warning' : 'error'"
                  rounded
                  height="6"
                />
              </div>
            </div>
            <VDivider class="mb-4" />
          </template>

          <!-- Legacy Quality Scores (shown when quality gate data unavailable) -->
          <template v-if="item.qualityScores && !item.qualityGate">
            <h6 class="text-h6 mb-3">
              Quality Scores
            </h6>
            <div class="mb-4">
              <div class="mb-3">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Math Correctness</span>
                  <span class="text-body-2 font-weight-medium">{{ item.qualityScores.mathCorrectness }}%</span>
                </div>
                <VProgressLinear
                  :model-value="item.qualityScores.mathCorrectness"
                  color="success"
                  rounded
                  height="8"
                />
              </div>
              <div class="mb-3">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Language Quality</span>
                  <span class="text-body-2 font-weight-medium">{{ item.qualityScores.languageQuality }}%</span>
                </div>
                <VProgressLinear
                  :model-value="item.qualityScores.languageQuality"
                  color="primary"
                  rounded
                  height="8"
                />
              </div>
              <div class="mb-3">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Pedagogical Quality</span>
                  <span class="text-body-2 font-weight-medium">{{ item.qualityScores.pedagogicalQuality }}%</span>
                </div>
                <VProgressLinear
                  :model-value="item.qualityScores.pedagogicalQuality"
                  color="info"
                  rounded
                  height="8"
                />
              </div>
              <div class="mb-3">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Plagiarism Score</span>
                  <span class="text-body-2 font-weight-medium">{{ item.qualityScores.plagiarismScore }}%</span>
                </div>
                <VProgressLinear
                  :model-value="item.qualityScores.plagiarismScore"
                  :color="item.qualityScores.plagiarismScore > 30 ? 'error' : 'success'"
                  rounded
                  height="8"
                />
              </div>
            </div>

            <VDivider class="mb-4" />
          </template>

          <!-- Errors -->
          <template v-if="item.errors.length">
            <h6 class="text-h6 mb-3">
              Errors
            </h6>
            <VAlert
              v-for="(error, idx) in item.errors"
              :key="idx"
              color="error"
              variant="tonal"
              density="compact"
              class="mb-2"
            >
              {{ error }}
            </VAlert>

            <VDivider class="my-4" />
          </template>

          <!-- Actions -->
          <h6 class="text-h6 mb-3">
            Actions
          </h6>
          <div class="d-flex gap-3 flex-wrap">
            <VBtn
              color="warning"
              variant="tonal"
              :loading="actionLoading"
              @click="retryItem"
            >
              <VIcon
                icon="tabler-refresh"
                start
              />
              Retry
            </VBtn>
            <VBtn
              color="error"
              variant="tonal"
              :loading="actionLoading"
              @click="rejectItem"
            >
              <VIcon
                icon="tabler-x"
                start
              />
              Reject
            </VBtn>
            <VBtn
              color="primary"
              variant="tonal"
              :loading="actionLoading"
              @click="moveToReview"
            >
              <VIcon
                icon="tabler-eye"
                start
              />
              Move to Review
            </VBtn>

            <!-- Option 2: AI variant generation from Bagrut drafts. Sends
                 the draft id + curator-chosen subject/grade/blooms to a
                 background job; results surface in the Ingestion Jobs
                 drawer. ADR-0043 isomorph rejector still gates each
                 candidate. -->
            <VBtn
              v-if="item && item.sourceType === 'bagrut'"
              color="info"
              variant="tonal"
              @click="variantsDialogOpen = true"
            >
              <VIcon
                icon="tabler-wand"
                start
              />
              Generate variants
            </VBtn>
          </div>
        </VCardText>
      </VCard>
    </PerfectScrollbar>

    <!-- Generate Variants dialog (option 2) -->
    <VDialog
      v-model="variantsDialogOpen"
      max-width="560"
    >
      <VCard>
        <VCardTitle class="d-flex align-center gap-2">
          <VIcon icon="tabler-wand" />
          <span>Generate AI variants</span>
          <VSpacer />
          <VBtn
            icon="tabler-x"
            variant="text"
            size="small"
            @click="variantsDialogOpen = false"
          />
        </VCardTitle>
        <VDivider />
        <VCardText class="pt-4">
          <!-- ADR-0059 §15.5 + PRR-249 feature-flag gate. Implementation
               is production-grade; legal sign-off is a separate gate. -->
          <VAlert
            v-if="variantsFlagEnabled === false"
            type="warning"
            variant="tonal"
            density="compact"
            class="mb-3"
          >
            <div class="font-weight-medium mb-1">
              Disabled pending legal sign-off (PRR-249)
            </div>
            <div class="text-body-2">
              {{ variantsFlagReason }}
            </div>
          </VAlert>
          <VAlert
            v-else
            type="info"
            variant="tonal"
            density="compact"
            class="mb-3"
          >
            ADR-0059 §15.5: the LLM receives the Bagrut draft as creative seed
            with explicit do-not-copy guardrails. Each candidate routes through
            the CAS gate (ADR-0002) + quality gate before persisting as a
            draft question with full provenance lineage to the source.
          </VAlert>
          <VRow>
            <VCol cols="6">
              <VSelect
                v-model="variantsForm.subject"
                :items="['math','physics','chemistry','biology']"
                label="Subject"
                density="compact"
              />
            </VCol>
            <VCol cols="6">
              <VSelect
                v-model="variantsForm.grade"
                :items="['9','10','11','12']"
                label="Grade"
                density="compact"
              />
            </VCol>
            <VCol cols="6">
              <VSelect
                v-model="variantsForm.bloomsLevel"
                :items="[
                  { title: '1 — Remember', value: 1 },
                  { title: '2 — Understand', value: 2 },
                  { title: '3 — Apply', value: 3 },
                  { title: '4 — Analyze', value: 4 },
                  { title: '5 — Evaluate', value: 5 },
                  { title: '6 — Create', value: 6 },
                ]"
                label="Bloom's level"
                density="compact"
              />
            </VCol>
            <VCol cols="6">
              <VSelect
                v-model="variantsForm.language"
                :items="['en','he','ar']"
                label="Language"
                density="compact"
              />
            </VCol>
            <VCol cols="6">
              <VTextField
                v-model="variantsForm.topic"
                label="Topic (optional)"
                placeholder="e.g. quadratic equations"
                density="compact"
              />
            </VCol>
            <VCol cols="6">
              <VTextField
                v-model.number="variantsForm.count"
                type="number"
                label="Count (1–20)"
                :min="1"
                :max="20"
                density="compact"
              />
            </VCol>
            <VCol cols="6">
              <VTextField
                v-model.number="variantsForm.minDifficulty"
                type="number"
                step="0.05"
                :min="0"
                :max="1"
                label="Min difficulty"
                density="compact"
              />
            </VCol>
            <VCol cols="6">
              <VTextField
                v-model.number="variantsForm.maxDifficulty"
                type="number"
                step="0.05"
                :min="0"
                :max="1"
                label="Max difficulty"
                density="compact"
              />
            </VCol>
          </VRow>
        </VCardText>
        <VDivider />
        <VCardActions class="pa-4">
          <VSpacer />
          <VBtn
            variant="text"
            @click="variantsDialogOpen = false"
          >
            Cancel
          </VBtn>
          <VBtn
            color="primary"
            :loading="variantsSubmitting"
            :disabled="variantsFlagEnabled === false"
            @click="enqueueVariants"
          >
            <VIcon
              icon="tabler-player-play"
              start
            />
            Enqueue ({{ variantsForm.count }})
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>

    <div
      v-if="!item && !loading"
      class="d-flex align-center justify-center"
      style="block-size: 100%;"
    >
      <p class="text-body-1 text-medium-emphasis">
        Select an item to view details
      </p>
    </div>
  </VNavigationDrawer>
</template>
