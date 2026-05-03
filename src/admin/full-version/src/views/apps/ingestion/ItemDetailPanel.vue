<script setup lang="ts">
import { PerfectScrollbar } from 'vue3-perfect-scrollbar'
import { $api } from '@/utils/api'
import { useIngestionJobs } from '@/composables/useIngestionJobs'
import { renderMixedMathText } from '@/utils/renderMixedMathText'
import { renderTextDiff } from '@/utils/renderTextDiff'
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
  // Visual-review (2026-05-01): the curator's side-by-side panel needs
  // the original PDF + figure crops to validate that the OCR + cleanup
  // didn't drop diagrams. Backend surfaces:
  //   - hasSourcePdf: true iff GET .../source.pdf will return bytes
  //   - figures[]:    per-figure record with the stream URL
  // Both default to false/[] for non-Bagrut items, which falls back to
  // the original text-only review experience.
  hasSourcePdf: boolean
  figures: Array<{ index: number, page: number, kind: string | null, altText: string | null, url: string }>
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

// Visual-review blob URLs (2026-05-02). The native <embed> + <img> tags
// don't carry the Authorization header, so direct src="/api/..." returns
// 401 and the browser shows the broken-document icon. Workaround: fetch
// the binary via $api (which DOES carry the JWT), turn the response into
// a Blob, expose the resulting object URL, and use THAT as the src.
// Lifecycle: revoked on item change + on drawer close + on unmount —
// without revoke, every open of the drawer leaks ~1 MB per PDF.
const pdfBlobUrl = ref<string | null>(null)
const figureBlobUrls = ref<Record<number, string>>({})

function revokeVisualReviewBlobs() {
  if (pdfBlobUrl.value) {
    URL.revokeObjectURL(pdfBlobUrl.value)
    pdfBlobUrl.value = null
  }
  for (const url of Object.values(figureBlobUrls.value))
    URL.revokeObjectURL(url)
  figureBlobUrls.value = {}
}

async function loadVisualReviewBlobs(d: ItemDetail) {
  // Always revoke first — we may be reloading for a different item.
  revokeVisualReviewBlobs()

  if (d.hasSourcePdf) {
    try {
      const blob = await $api<Blob>(
        `/admin/ingestion/items/${d.id}/source.pdf`,
        { responseType: 'blob' },
      )
      // The watcher below may have changed the active item between
      // dispatch and resolve — only attach if we're still on it.
      if (item.value?.id === d.id)
        pdfBlobUrl.value = URL.createObjectURL(blob)
    }
    catch (err) {
      // 404 (PDF not retained) or 500 — leave pdfBlobUrl null and the
      // template falls back to its info-banner. Don't surface a toast,
      // the curator will get the explainer alert below.
      console.warn('PDF blob fetch failed:', err)
    }
  }

  for (const fig of d.figures) {
    try {
      const blob = await $api<Blob>(
        `/admin/ingestion/items/${d.id}/figures/${fig.index}`,
        { responseType: 'blob' },
      )
      if (item.value?.id === d.id)
        figureBlobUrls.value = { ...figureBlobUrls.value, [fig.index]: URL.createObjectURL(blob) }
    }
    catch (err) {
      console.warn(`figure ${fig.index} blob fetch failed:`, err)
    }
  }
}

onBeforeUnmount(() => {
  revokeVisualReviewBlobs()
})

// Per-question "show diff" toggle. Keyed by question.index so each
// question card holds its own state. When true, the recreated text
// renders as a side-by-side word-level diff against the OCR raw text;
// when false, the recreated text renders as plain KaTeX-formatted output
// (the default — diff view is opt-in and recomputed on demand).
const diffOpen = ref<Record<number, boolean>>({})

function toggleDiff(questionIndex: number) {
  diffOpen.value = { ...diffOpen.value, [questionIndex]: !diffOpen.value[questionIndex] }
}

/**
 * Compute the diff between the OCR raw text and the recreated text for
 * one question. Memoised across renders by Vue's template-render
 * mechanics (toggleDiff triggers a re-render and the call returns the
 * fresh diff). For multi-question items we slice the OCR text per-index
 * by paragraph if a sentinel boundary is present, else fall back to the
 * full OCR vs the recreated question (which still surfaces the
 * insertion/deletion structure even if alignment is approximate).
 */
function diffForQuestion(questionIndex: number, recreatedText: string) {
  const ocr = item.value?.ocrText ?? ''
  if (!ocr || !recreatedText)
    return null

  // Try splitting the OCR text by double-newline boundaries, which is
  // how Anthropic's OCR + Mathpix typically separates question stems
  // in multi-question PDFs. If we get exactly one chunk per question,
  // align by index. Otherwise diff against the whole OCR.
  const ocrChunks = ocr.split(/\n\s*\n/)
  const ocrSlice = ocrChunks.length === item.value!.recreatedQuestions.length
    ? ocrChunks[questionIndex] ?? ''
    : ocr

  return renderTextDiff(ocrSlice, recreatedText)
}
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
  // Visual-review (2026-05-01).
  hasSourcePdf?: boolean
  figures?: Array<{ index: number, page: number, kind: string | null, altText: string | null, url: string }>
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
      hasSourcePdf: resp.hasSourcePdf ?? false,
      figures: (resp.figures ?? []) as ItemDetail['figures'],
    }

    // Fire-and-forget binary fetches for the visual-review surface.
    // Reads the freshly-set item.value, so it picks up the new id +
    // figure list without us threading them through. Awaiting would
    // make the panel block on PDF-blob latency for no reason — the
    // text-side of the panel renders immediately while embeds fade in.
    if (item.value)
      loadVisualReviewBlobs(item.value)
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
  else if (!open)
    revokeVisualReviewBlobs()   // free PDF/figure blob memory on close
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
            v-if="!item.ocrText && !(item.recreatedQuestions?.length)"
            class="text-body-2 text-disabled mb-4"
            data-test="item-detail-no-content"
          >
            No question content yet — pipeline is still processing.
          </div>

          <!-- Recreated questions (post-OCR cleanup) — what variant
               generation will seed from. Show first since this is what
               the curator approves on.
               Math LaTeX in `q.text` is KaTeX-rendered via
               renderMixedMathText (bdi dir=ltr per memory rule).
               Optional chaining on `recreatedQuestions?.length` is
               load-bearing: Vite HMR can replace the template while the
               existing `item` ref still has the pre-2026-05-01 shape
               without `recreatedQuestions`; without `?.length` Vue
               throws on undefined access and the section silently fails
               to render. -->
          <div
            v-if="item.recreatedQuestions?.length"
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
                  <div class="d-flex align-center gap-2">
                    <VChip
                      size="x-small"
                      :color="q.confidence >= 0.85 ? 'success' : q.confidence >= 0.65 ? 'warning' : 'error'"
                      label
                    >
                      {{ Math.round(q.confidence * 100) }}% confidence
                    </VChip>
                    <!-- Diff toggle. Curator clicks to compare recreated
                         vs OCR raw side-by-side with word-level
                         insertion/deletion highlights. Off by default
                         because the formatted KaTeX render is the
                         primary review surface; diff is the "what
                         changed?" deep-dive. -->
                    <VBtn
                      v-if="item.ocrText"
                      size="x-small"
                      variant="text"
                      :color="diffOpen[q.index] ? 'primary' : undefined"
                      :prepend-icon="diffOpen[q.index] ? 'tabler-eye-off' : 'tabler-git-compare'"
                      data-test="item-detail-diff-toggle"
                      @click="toggleDiff(q.index)"
                    >
                      {{ diffOpen[q.index] ? 'Hide diff' : 'Diff vs OCR' }}
                    </VBtn>
                  </div>
                </div>

                <!-- Default view: KaTeX-formatted recreated question.
                     v-html safe — see renderMixedMathText for the
                     escape + bdi-LTR + KaTeX-fallback chain. -->
                <div
                  v-if="!diffOpen[q.index]"
                  class="cena-mmt-block"
                  data-test="item-detail-recreated-text"
                  v-html="renderMixedMathText(q.text)"
                />

                <!-- Diff view: side-by-side OCR raw (left) vs recreated
                     (right) with word-level (or char-level for
                     math-heavy content) insertions and deletions
                     highlighted via <ins>/<del> tags. v-html safe —
                     renderTextDiff escapes all input segments before
                     wrapping in our own tags. -->
                <div
                  v-else-if="diffForQuestion(q.index, q.text) !== null"
                  class="cena-diff-grid"
                  data-test="item-detail-diff-view"
                >
                  <div class="cena-diff-side">
                    <div class="cena-diff-header text-caption text-medium-emphasis">
                      OCR raw
                      <VChip
                        v-if="diffForQuestion(q.index, q.text)!.ocr.changeCount > 0"
                        size="x-small"
                        color="error"
                        variant="outlined"
                        label
                        class="ms-2"
                      >
                        −{{ diffForQuestion(q.index, q.text)!.ocr.changeCount }}
                      </VChip>
                    </div>
                    <bdi
                      dir="ltr"
                      class="cena-diff-body"
                      data-test="item-detail-diff-ocr"
                      v-html="diffForQuestion(q.index, q.text)!.ocr.html"
                    />
                  </div>
                  <div class="cena-diff-side">
                    <div class="cena-diff-header text-caption text-medium-emphasis">
                      Recreated
                      <VChip
                        v-if="diffForQuestion(q.index, q.text)!.recreated.changeCount > 0"
                        size="x-small"
                        color="success"
                        variant="outlined"
                        label
                        class="ms-2"
                      >
                        +{{ diffForQuestion(q.index, q.text)!.recreated.changeCount }}
                      </VChip>
                    </div>
                    <bdi
                      dir="ltr"
                      class="cena-diff-body"
                      data-test="item-detail-diff-recreated"
                      v-html="diffForQuestion(q.index, q.text)!.recreated.html"
                    />
                  </div>
                </div>
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
                <div
                  class="cena-mmt-block"
                  data-test="item-detail-ocr-text"
                  v-html="renderMixedMathText(item.ocrText)"
                />
              </VExpansionPanelText>
            </VExpansionPanel>
          </VExpansionPanels>

          <VDivider class="mb-4" />

          <!-- Visual review (2026-05-01, reordered 2026-05-02) — original
               PDF + extracted figures. Sits BELOW Question Content so
               the recreated text the curator approves is the first
               thing on screen; the PDF is a verification surface, not
               the primary content. Curators flagged that approving from
               text alone meant they couldn't catch dropped diagrams or
               misaligned bounding boxes.
               Layout rules (within the visual block):
                 - PDF + figures → 3fr / 2fr split (PDF wider).
                 - PDF only → PDF full width.
                 - Figures only → figures full width.
                 - Neither → section hidden entirely; the legacy-item
                   VAlert below explains the empty state. -->
          <div
            v-if="item.hasSourcePdf || item.figures.length > 0"
            class="mb-4"
            data-test="item-detail-visual-review"
          >
            <h6 class="text-h6 mb-3">
              Visual review
            </h6>
            <div
              class="cena-visual-grid"
              :class="{ 'cena-visual-grid--single': !(item.hasSourcePdf && item.figures.length > 0) }"
            >
              <div
                v-if="item.hasSourcePdf"
                class="cena-visual-side cena-visual-pdf"
              >
                <div class="text-caption text-medium-emphasis mb-2 d-flex align-center">
                  <VIcon
                    icon="tabler-file-text"
                    size="16"
                    class="me-1"
                  />
                  Original PDF
                </div>
                <!-- Use blob URL set by loadVisualReviewBlobs (auth-aware
                     fetch). Direct src="/api/..." would 401 because the
                     <embed> element doesn't carry the JWT. While the blob
                     is in flight, show a lightweight skeleton-ish state. -->
                <embed
                  v-if="pdfBlobUrl"
                  :src="pdfBlobUrl"
                  type="application/pdf"
                  class="cena-visual-pdf-embed"
                  data-test="item-detail-pdf-embed"
                >
                <div
                  v-else
                  class="cena-visual-fallback text-body-2 text-disabled"
                  data-test="item-detail-pdf-loading"
                >
                  Loading PDF…
                </div>
              </div>
              <div
                v-if="item.figures.length > 0"
                class="cena-visual-side cena-visual-figures"
              >
                <div class="text-caption text-medium-emphasis mb-2 d-flex align-center">
                  <VIcon
                    icon="tabler-photo"
                    size="16"
                    class="me-1"
                  />
                  Extracted figures
                  <VChip
                    size="x-small"
                    color="success"
                    variant="outlined"
                    label
                    class="ms-2"
                  >
                    {{ item.figures.length }}
                  </VChip>
                </div>
                <div
                  class="cena-figure-grid"
                  data-test="item-detail-figure-grid"
                >
                  <a
                    v-for="fig in item.figures"
                    :key="fig.index"
                    :href="figureBlobUrls[fig.index] ?? '#'"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="cena-figure-tile"
                    :title="fig.altText ?? `Figure on page ${fig.page}`"
                    data-test="item-detail-figure-tile"
                    @click="(!figureBlobUrls[fig.index]) && $event.preventDefault()"
                  >
                    <img
                      v-if="figureBlobUrls[fig.index]"
                      :src="figureBlobUrls[fig.index]"
                      :alt="fig.altText ?? `Figure on page ${fig.page}`"
                      loading="lazy"
                    >
                    <div
                      v-else
                      class="cena-figure-tile-loading text-caption text-disabled"
                    >
                      Loading…
                    </div>
                    <span class="cena-figure-caption text-caption">
                      p{{ fig.page }}<span v-if="fig.kind"> · {{ fig.kind }}</span>
                    </span>
                  </a>
                </div>
              </div>
            </div>
          </div>

          <!-- Legacy-item explainer: shows only when there's no PDF on
               disk and no figures (item uploaded before persistent
               storage was added). Without this the curator skips from
               Question Content straight to Processing Stages with no
               hint that visual review *would* be there for newer
               uploads. -->
          <VAlert
            v-if="item.sourceType === 'bagrut' && !item.hasSourcePdf && item.figures.length === 0"
            type="info"
            variant="tonal"
            density="compact"
            class="mb-4"
            data-test="item-detail-visual-unavailable"
          >
            <span class="text-body-2">
              Visual review unavailable — this item was uploaded before persistent PDF + figure storage was added. Re-upload via the Bagrut ingest flow to enable the side-by-side viewer.
            </span>
          </VAlert>

          <VDivider
            v-if="item.hasSourcePdf || item.figures.length > 0 || (item.sourceType === 'bagrut' && !item.hasSourcePdf && item.figures.length === 0)"
            class="mb-4"
          />

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
            <!-- Move to Review — only valid as a forward transition from
                 stages upstream of InReview. Hiding it on InReview/Published/
                 Failed prevents no-op re-emission of MovedToReview_V1
                 (backend would happily re-fire the event) and the curator
                 confusion of seeing the same button on a row that's
                 already at the target stage. -->
            <VBtn
              v-if="item && !['InReview', 'Published', 'Failed'].includes(item.currentStage)"
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
                 garbage produces garbage variants and burns budget.
                 Content gate (added 2026-05-01): also require non-empty
                 recreated content. The previous gate let curators click
                 Generate on items where the OCR returned an empty stem —
                 the LLM was being asked to seed variants from "" which
                 produces hallucinations and burns budget. The button is
                 now hidden until there is actual content for the LLM
                 to anchor on. -->
            <VBtn
              v-if="item
                && item.sourceType === 'bagrut'
                && (item.currentStage === 'InReview' || item.currentStage === 'Published')
                && (item.recreatedQuestions?.length ?? 0) > 0"
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

<style scoped>
/* cena-mmt-* classes — emitted by renderMixedMathText.
   Wraps prose + KaTeX math output into a single readable block. */
.cena-mmt-block {
  font-size: 0.9rem;
  line-height: 1.55;
  word-wrap: break-word;
}

.cena-mmt-block :deep(.cena-mmt-text) {
  white-space: pre-wrap;
  font-family: inherit;
}

.cena-mmt-block :deep(.cena-mmt-math) {
  display: inline-block;
}

.cena-mmt-block :deep(.cena-mmt-math--block) {
  display: block;
  margin: 0.5rem 0;
  text-align: center;
}

.cena-mmt-block :deep(.cena-mmt-math-error) {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.875em;
  color: rgb(var(--v-theme-error));
  background: rgba(var(--v-theme-error), 0.08);
  padding: 0.1em 0.3em;
  border-radius: 0.2em;
}

/* cena-diff-* — side-by-side OCR-vs-recreated diff view
   Two columns at >=720px, stacked below. Word-level <ins>/<del>
   highlights from renderTextDiff. */
.cena-diff-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.75rem;
  margin-top: 0.25rem;
}
@media (min-width: 720px) {
  .cena-diff-grid {
    grid-template-columns: 1fr 1fr;
  }
}
.cena-diff-side {
  background: rgba(var(--v-theme-surface-variant), 0.4);
  border-radius: 0.4rem;
  padding: 0.5rem 0.75rem;
}
.cena-diff-header {
  display: flex;
  align-items: center;
  margin-block-end: 0.25rem;
  font-weight: 600;
}
.cena-diff-body {
  display: block;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.8125rem;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}
.cena-diff-body :deep(.cena-diff-ins) {
  background: rgba(var(--v-theme-success), 0.18);
  text-decoration: none;
  padding: 0 0.1em;
  border-radius: 0.15em;
}
.cena-diff-body :deep(.cena-diff-del) {
  background: rgba(var(--v-theme-error), 0.16);
  text-decoration: line-through;
  padding: 0 0.1em;
  border-radius: 0.15em;
}
.cena-diff-body :deep(.cena-diff-eq) {
  color: rgb(var(--v-theme-on-surface));
}

/* cena-visual-* — side-by-side original PDF + extracted figures.
   PDF gets the wider column on desktop (3fr) since it carries the
   bulk of context; figures gallery is the tighter inspector column
   (2fr). Stacked vertically below 900px so a small admin window
   doesn't squash either side past usefulness. */
.cena-visual-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
  margin-top: 0.25rem;
}
@media (min-width: 900px) {
  .cena-visual-grid {
    grid-template-columns: 3fr 2fr;
  }
}
/* When only one of {PDF, figures} is present, drop back to one column
   so the present side fills the whole row instead of leaving an empty
   half (which is what made the panel look broken with a 0-figure
   item, 2026-05-01 user report). */
.cena-visual-grid.cena-visual-grid--single {
  grid-template-columns: 1fr;
}
.cena-visual-side {
  background: rgba(var(--v-theme-surface-variant), 0.4);
  border-radius: 0.4rem;
  padding: 0.75rem;
  min-width: 0;
}
.cena-visual-pdf-embed {
  inline-size: 100%;
  /* 50vh keeps the recreated-question section visible above the fold
     in a typical drawer view. Curators can still scroll inside the PDF
     viewer to see all pages, and click the embed's own fullscreen icon
     for a closer look. The previous 70vh swallowed the screen and
     buried the recreated text below. */
  block-size: 50vh;
  min-block-size: 360px;
  border: 1px solid rgba(var(--v-theme-outline-variant), 0.6);
  border-radius: 0.25rem;
  background: rgb(var(--v-theme-surface));
}
.cena-visual-fallback {
  padding: 1.25rem 0.75rem;
  text-align: center;
  border: 1px dashed rgba(var(--v-theme-outline-variant), 0.7);
  border-radius: 0.25rem;
}
.cena-figure-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.5rem;
}
.cena-figure-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
  padding: 0.35rem;
  text-decoration: none;
  background: rgb(var(--v-theme-surface));
  border: 1px solid rgba(var(--v-theme-outline-variant), 0.6);
  border-radius: 0.25rem;
  transition: border-color 0.15s ease;
}
.cena-figure-tile:hover {
  border-color: rgb(var(--v-theme-primary));
}
.cena-figure-tile img {
  inline-size: 100%;
  block-size: auto;
  max-block-size: 120px;
  object-fit: contain;
  background: rgba(var(--v-theme-surface-variant), 0.5);
  border-radius: 0.15rem;
}
.cena-figure-tile-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  inline-size: 100%;
  min-block-size: 80px;
  background: rgba(var(--v-theme-surface-variant), 0.5);
  border-radius: 0.15rem;
}
.cena-figure-caption {
  color: rgb(var(--v-theme-on-surface-variant));
  text-align: center;
  word-break: break-word;
}
</style>
