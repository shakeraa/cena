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
  // LanguageQuality / PedagogicalQuality are computed from per-question
  // QualityGate evaluations (averaged over the item's variants). They
  // come back null when no variants have a QualityGate evaluation yet
  // — in that case we hide the bar instead of rendering a misleading
  // constant. Was hardcoded 80 / 75 before 2026-04-30.
  languageQuality: number | null
  pedagogicalQuality: number | null
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
  // Question content surfaced for curator review on the InReview stage.
  // The panel previously only showed metadata + scores so the curator
  // had nothing to actually review (2026-05-01 user report).
  // - ocrText: raw OCR'd source PDF text (the "original question").
  // - recreatedQuestions: post-OCR cleanup output — what the re_created
  //   stage produced and what variant generation seeds from.
  ocrText: string | null
  ocrConfidence: number | null
  recreatedQuestions: Array<{ index: number, text: string, confidence: number }>
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
// 'Disabled' banner up front rather than failing on submit. cm audit
// 2026-04-30 extended the probe with aiProviderConfigured so the
// dialog also surfaces 'No API key configured' before the click —
// closes the silent-Completed pattern where the strategy reported
// 'LLM returned 0 candidates' without surfacing the real reason.
const variantsAiProviderConfigured = ref<boolean | null>(null)
const variantsAiProviderName = ref<string>('')
const variantsAiProviderReason = ref<string>('')

import { watch } from 'vue'
watch(variantsDialogOpen, async (open) => {
  if (!open) return
  variantsFlagEnabled.value = null
  variantsAiProviderConfigured.value = null
  try {
    const data = await $api<{
      bagrutSeedToLlmEnabled: boolean,
      bagrutSeedDisabledReason: string,
      aiProviderConfigured: boolean,
      aiProviderName: string,
      aiProviderNotConfiguredReason: string,
    }>('/admin/ingestion/jobs/feature-flags')
    variantsFlagEnabled.value = !!data.bagrutSeedToLlmEnabled
    variantsFlagReason.value = data.bagrutSeedDisabledReason ?? ''
    variantsAiProviderConfigured.value = !!data.aiProviderConfigured
    variantsAiProviderName.value = data.aiProviderName ?? 'AI provider'
    variantsAiProviderReason.value = data.aiProviderNotConfiguredReason ?? ''
  }
  catch {
    variantsFlagEnabled.value = false
    variantsFlagReason.value = 'Could not load feature flag state — check admin-api connectivity.'
    variantsAiProviderConfigured.value = false
    variantsAiProviderReason.value = 'Could not check AI provider configuration — check admin-api connectivity.'
  }
})

const { openDrawer: openJobsDrawer } = useIngestionJobs()

// Single computed gate the Enqueue button (and the dialog body's banner
// selection) bind to. Either gate failing blocks the submit; the
// per-banner v-if below picks the right copy.
const variantsCanEnqueue = computed(() =>
  variantsFlagEnabled.value === true && variantsAiProviderConfigured.value === true)

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
    // Close THIS drawer too — without it, ItemDetailPanel (which is itself a
    // VNavigationDrawer, not just the inner form dialog) stays mounted on top
    // of the freshly-opened IngestionJobsDrawer and the operator can't see
    // the job they just enqueued. The two-way binding in pipeline.vue picks
    // this up.
    emit('update:isOpen', false)
  }
  catch (err: any) {
    console.error('enqueueVariants failed', err)
  }
  finally {
    variantsSubmitting.value = false
  }
}

// Backend response shape (snake-→camel by ASP.NET JSON defaults):
//   { id, sourceFilename, sourceType, sourceUrl, submittedAt, completedAt,
//     currentStage, stageHistory: [...], ocrResult, quality:
//     { mathCorrectness, languageQuality, pedagogicalQuality, plagiarismScore },
//     extractedQuestions: [...] }
// Frontend ItemDetail interface uses different names (originalFilename,
// stages, createdAt, qualityScores, errors). Pre-existing mismatch —
// adapt at the boundary so the rest of the component doesn't churn.
interface BackendStageInfo {
  stage: string
  startedAt: string | null
  completedAt: string | null
  status: string
  errorMessage: string | null
}
interface BackendQuality {
  mathCorrectness: number
  languageQuality: number
  pedagogicalQuality: number
  plagiarismScore: number
}
interface BackendDetailResponse {
  id: string
  sourceFilename: string
  sourceType: string
  sourceUrl: string | null
  submittedAt: string
  completedAt: string | null
  currentStage: string
  stageHistory: BackendStageInfo[]
  ocrResult: { extractedText: string, confidence: number } | null
  quality: BackendQuality | null
  extractedQuestions: Array<{ index: number, text: string, confidence: number }>
}

const stageStatusMap: Record<string, ProcessingStage['status']> = {
  processing: 'in_progress',
  completed: 'completed',
  failed: 'failed',
  pending: 'pending',
}

const fetchDetail = async () => {
  if (!props.itemId)
    return

  loading.value = true
  try {
    const resp = await $api<BackendDetailResponse>(
      `/admin/ingestion/items/${props.itemId}/detail`,
    )
    item.value = {
      id: resp.id,
      originalFilename: resp.sourceFilename,
      sourceType: (resp.sourceType as ItemDetail['sourceType']),
      currentStage: resp.currentStage,
      questionCount: resp.extractedQuestions?.length ?? 0,
      qualityScores: resp.quality
        ? {
            mathCorrectness: resp.quality.mathCorrectness,
            languageQuality: resp.quality.languageQuality,
            pedagogicalQuality: resp.quality.pedagogicalQuality,
            plagiarismScore: resp.quality.plagiarismScore,
          }
        : null,
      qualityGate: null,
      stages: (resp.stageHistory ?? []).map(s => ({
        name: s.stage,
        status: stageStatusMap[s.status] ?? 'pending',
        startedAt: s.startedAt,
        completedAt: s.completedAt,
        error: s.errorMessage,
      })),
      errors: (resp.stageHistory ?? [])
        .filter(s => s.errorMessage)
        .map(s => `${s.stage}: ${s.errorMessage}`),
      createdAt: resp.submittedAt,
      updatedAt: resp.completedAt ?? resp.submittedAt,
      ocrText: resp.ocrResult?.extractedText ?? null,
      ocrConfidence: resp.ocrResult?.confidence ?? null,
      recreatedQuestions: resp.extractedQuestions ?? [],
    }
  }
  catch (error) {
    console.error('Failed to fetch item detail:', error)
    item.value = null
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

const retryError = ref<string | null>(null)

const retryItem = async () => {
  actionLoading.value = true
  retryError.value = null
  try {
    await $api(`/admin/ingestion/items/${props.itemId}/retry`, { method: 'POST' })
    emit('item-updated')
    fetchDetail()
  }
  catch (error: any) {
    // 501 Not Implemented = honest message from the backend that retry is
    // not yet wired (see PRR-RETRY-IMPL). Render the body message inline
    // rather than swallowing as a generic console error so the curator
    // knows to re-upload.
    if (error?.response?.status === 501 || error?.statusCode === 501) {
      retryError.value = error?.data?.message
        ?? 'Retry is not yet implemented; please re-upload the file.'
    }
    else {
      retryError.value = error?.data?.message ?? error?.message ?? 'Retry failed.'
    }
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

// Approve gate state. Backend rejects with reason="metadata_unconfirmed:..."
// until the curator clicks Save in CuratorMetadataPanel; surface that as a
// readable hint instead of a generic toast so the curator knows what to do.
const approveError = ref<string | null>(null)

const approveItem = async () => {
  approveError.value = null
  actionLoading.value = true
  try {
    const res = await $api<{ success: boolean; reason?: string | null }>(
      `/admin/ingestion/items/${props.itemId}/approve`,
      { method: 'POST' })
    if (!res.success) {
      // Map backend reason codes to operator-facing copy. Anything we
      // don't recognise falls through with the raw code so the curator
      // can still file it.
      const reason = res.reason ?? ''
      if (reason.startsWith('metadata_unconfirmed'))
        approveError.value = 'Confirm the curator metadata above before publishing.'
      else if (reason.startsWith('wrong_stage'))
        approveError.value = `Cannot publish from stage ${reason.split(':')[1] ?? '?'} — only InReview items can be approved.`
      else if (reason === 'not_found')
        approveError.value = 'Item not found — it may have been removed.'
      else
        approveError.value = `Cannot publish: ${reason || 'unknown reason'}`
      return
    }
    emit('item-updated')
    fetchDetail()
  }
  catch (error: any) {
    approveError.value = error?.data?.message ?? error?.message ?? 'Approval failed'
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
                {{ item.questionCount }}
                {{ item.sourceType === 'bagrut' ? 'variants' : 'questions' }}
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

          <!-- Question Content — what the curator is reviewing.
               OCR (original) + recreated form. Without this section,
               approving was a black-box (only metadata + scores were
               visible). 2026-05-01 user report.
               Math content (LaTeX/MathML in OCR'd Bagrut text) MUST be
               LTR even on RTL admin pages — wrap in <bdi dir="ltr"> per
               feedback_math_always_ltr memory rule. Real KaTeX rendering
               is a separate follow-up; for now we show the raw LaTeX
               source which is readable enough for curator review. -->
          <h6 class="text-h6 mb-3">
            Question Content
          </h6>

          <div
            v-if="!item.ocrText && !item.recreatedQuestions.length"
            class="text-body-2 text-disabled mb-4"
            data-test="item-detail-no-content"
          >
            No question content yet — pipeline is still processing.
          </div>

          <!-- Recreated questions (post-OCR cleanup) — what variant
               generation will seed from. Show first since this is what
               the curator approves on. -->
          <div
            v-if="item.recreatedQuestions.length"
            class="mb-4"
            data-test="item-detail-recreated"
          >
            <div class="text-body-2 text-medium-emphasis mb-2 d-flex align-center">
              <VIcon
                icon="tabler-sparkles"
                size="16"
                class="me-1"
              />
              Recreated questions ({{ item.recreatedQuestions.length }}) — what variant generation will seed from
            </div>
            <VCard
              v-for="q in item.recreatedQuestions"
              :key="q.index"
              variant="outlined"
              class="mb-2"
            >
              <VCardText class="py-3">
                <div class="d-flex justify-space-between align-center mb-2">
                  <span class="text-caption text-medium-emphasis">
                    Question {{ q.index + 1 }}
                  </span>
                  <VChip
                    size="x-small"
                    :color="q.confidence >= 0.85 ? 'success' : q.confidence >= 0.65 ? 'warning' : 'error'"
                    label
                  >
                    {{ Math.round(q.confidence * 100) }}% confidence
                  </VChip>
                </div>
                <bdi
                  dir="ltr"
                  style="display: block; white-space: pre-wrap; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 0.875rem;"
                  data-test="item-detail-recreated-text"
                >{{ q.text }}</bdi>
              </VCardText>
            </VCard>
          </div>

          <!-- Original OCR text (raw) — collapsed by default; the
               curator usually trusts the recreated form but needs the
               source on hand to spot OCR errors. -->
          <VExpansionPanels
            v-if="item.ocrText"
            class="mb-4"
            data-test="item-detail-ocr-original"
          >
            <VExpansionPanel>
              <VExpansionPanelTitle>
                <span class="d-flex align-center">
                  <VIcon
                    icon="tabler-file-text"
                    size="16"
                    class="me-1"
                  />
                  Original (OCR raw text)
                  <VChip
                    v-if="item.ocrConfidence !== null"
                    size="x-small"
                    :color="item.ocrConfidence >= 0.85 ? 'success' : item.ocrConfidence >= 0.65 ? 'warning' : 'error'"
                    variant="outlined"
                    label
                    class="ms-2"
                  >
                    {{ Math.round(item.ocrConfidence * 100) }}% OCR
                  </VChip>
                </span>
              </VExpansionPanelTitle>
              <VExpansionPanelText>
                <bdi
                  dir="ltr"
                  style="display: block; white-space: pre-wrap; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 0.8125rem;"
                  data-test="item-detail-ocr-text"
                >{{ item.ocrText }}</bdi>
              </VExpansionPanelText>
            </VExpansionPanel>
          </VExpansionPanels>

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
                  <span class="text-body-2 font-weight-medium">
                    {{ item.qualityScores.languageQuality === null ? '—' : item.qualityScores.languageQuality + '%' }}
                  </span>
                </div>
                <!-- Hide progress bar when evaluator hasn't run; rendering 0%
                     would lie about a "real" zero score. cm #5 fix made the
                     backend return null instead of hardcoded 80; this matches
                     the SPA-side. -->
                <VProgressLinear
                  v-if="item.qualityScores.languageQuality !== null"
                  :model-value="item.qualityScores.languageQuality"
                  color="primary"
                  rounded
                  height="8"
                />
              </div>
              <div class="mb-3">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Pedagogical Quality</span>
                  <span class="text-body-2 font-weight-medium">
                    {{ item.qualityScores.pedagogicalQuality === null ? '—' : item.qualityScores.pedagogicalQuality + '%' }}
                  </span>
                </div>
                <VProgressLinear
                  v-if="item.qualityScores.pedagogicalQuality !== null"
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
          <!-- Retry error surface — 501 Not Implemented from backend
               renders here so curator knows to re-upload (PRR-RETRY-IMPL).  -->
          <VAlert
            v-if="retryError"
            color="warning"
            variant="tonal"
            density="compact"
            class="mb-3"
            data-test="retry-error"
          >
            {{ retryError }}
          </VAlert>
          <!-- Approve gate hint — fires when /approve returns success=false.
               The most common reason is "metadata_unconfirmed", which means
               the curator hasn't clicked Save in the CuratorMetadataPanel
               yet. Reusing the warning style keeps the visual weight low. -->
          <VAlert
            v-if="approveError"
            color="warning"
            variant="tonal"
            density="compact"
            class="mb-3"
            data-test="approve-error"
          >
            {{ approveError }}
          </VAlert>
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
            <!-- Approve & Publish — only shows on InReview items so the
                 curator never sees it on items that have already been
                 published, rejected, or are still mid-pipeline. Backend
                 enforces metadataState=confirmed; the alert above
                 surfaces the reason when the gate refuses. -->
            <VBtn
              v-if="item && item.currentStage === 'InReview'"
              color="success"
              :loading="actionLoading"
              @click="approveItem"
            >
              <VIcon
                icon="tabler-check"
                start
              />
              Approve &amp; Publish
            </VBtn>

            <!-- Option 2: AI variant generation from Bagrut drafts. Sends
                 the draft id + curator-chosen subject/grade/blooms to a
                 background job; results surface in the Ingestion Jobs
                 drawer. ADR-0043 isomorph rejector still gates each
                 candidate.
                 Stage gate: only InReview + Published. ADR-0059 §15.5 lets
                 the LLM see Bagrut drafts as creative seed, but only after
                 OCR has settled and the curator has eyes on the content
                 (InReview onward). Earlier stages (OcrProcessing, ReCreated)
                 may still have malformed text → seeding the LLM with
                 garbage produces garbage variants and burns budget. -->
            <VBtn
              v-if="item && item.sourceType === 'bagrut' && (item.currentStage === 'InReview' || item.currentStage === 'Published')"
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
          <!-- Pre-flight gate banners. Order matters: the legal gate
               wins if both gates fail (PRR-249 supersedes ops config).
               cm audit 2026-04-30: aiProviderConfigured added so the
               curator learns about a missing API key BEFORE clicking
               Enqueue, instead of via a silently-Completed job. -->
          <VAlert
            v-if="variantsFlagEnabled === false"
            type="warning"
            variant="tonal"
            density="compact"
            class="mb-3"
            data-test="variants-legal-gate-banner"
          >
            <div class="font-weight-medium mb-1">
              Disabled pending legal sign-off (PRR-249)
            </div>
            <div class="text-body-2">
              {{ variantsFlagReason }}
            </div>
          </VAlert>
          <VAlert
            v-else-if="variantsAiProviderConfigured === false"
            type="warning"
            variant="tonal"
            density="compact"
            class="mb-3"
            data-test="variants-no-api-key-banner"
          >
            <div class="font-weight-medium mb-1">
              {{ variantsAiProviderName }} not configured — cannot generate variants
            </div>
            <div class="text-body-2">
              {{ variantsAiProviderReason }}
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
            :disabled="!variantsCanEnqueue"
            @click="enqueueVariants"
            data-test="variants-enqueue-button"
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
