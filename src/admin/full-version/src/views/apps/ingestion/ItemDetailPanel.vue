<script setup lang="ts">
import { PerfectScrollbar } from 'vue3-perfect-scrollbar'
import { $api } from '@/utils/api'
import { useIngestionJobs } from '@/composables/useIngestionJobs'
import { renderMixedMathText, renderTextWithFigures } from '@/utils/renderMixedMathText'
import { renderTextDiff } from '@/utils/renderTextDiff'
import ConceptReviewPanel from './ConceptReviewPanel.vue'
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
  // 2026-05-03: sourcePage added so the SPA can render a per-card PDF
  // thumbnail at #page=N. Null when the page can't be inferred (legacy
  // items, non-Bagrut sources).
  recreatedQuestions: Array<{ index: number, text: string, confidence: number, sourcePage?: number | null }>
  // Visual-review (2026-05-01): the curator's side-by-side panel needs
  // the original PDF + figure crops to validate that the OCR + cleanup
  // didn't drop diagrams. Backend surfaces:
  //   - hasSourcePdf: true iff GET .../source.pdf will return bytes
  //   - figures[]:    per-figure record with the stream URL
  // Both default to false/[] for non-Bagrut items, which falls back to
  // the original text-only review experience.
  hasSourcePdf: boolean
  figures: Array<{ index: number, page: number, kind: string | null, altText: string | null, url: string }>
  // 2026-05-03: surfaced from PipelineItemDocument so the 0-figures
  // banner can refine its copy by topic. Geometry / calculus /
  // vectors / trig nearly always carry diagrams (warning stays
  // loud); algebra / probability / functions can legitimately have
  // none (warning softens to "verify"). Null when the taxonomy
  // classifier produced no match — banner falls back to the generic
  // copy. Format: dotted path like "calculus.derivative_rules" (the
  // first segment is the topic; we only inspect that prefix).
  taxonomyNode: string | null
  // "pending" | "auto_extracted" | "confirmed". Curator-confirmed
  // taxonomy is more authoritative than the heuristic seed; both
  // paths share the same bucketing logic but the banner could
  // visually distinguish them in a future iteration.
  metadataState: string | null
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

// ADR-0062 Phase 1.5 — OCR cleanup pass state, keyed by recreated-
// question index. Curator clicks "Enhance with LLM" on a card; we POST
// /enhance-text and stash the result in enhancedTexts[index]. The card
// then offers a toggle between original and enhanced views.
//
// Auto-enhance on first open (2026-05-03, gap A): when the curator
// opens an InReview Bagrut item we fire one enhance-text POST for the
// whole item and apply the result to every recreated-question card.
// `enhanceTriggeredFor` is a per-itemId guard so the auto-fire happens
// exactly once per item open even when Vite HMR / Vue strict mounts
// the component twice. The backend response is item-wide (one POST
// returns one cleaned blob — the prompt + LaTeX content concatenated),
// so we mirror it across every recreated index; per-question
// re-enhance is still available via the toggle.
const enhancedTexts = ref<Record<number, string>>({})
const enhanceLoading = ref<Record<number, boolean>>({})
const enhanceErrors = ref<Record<number, string>>({})
const showEnhanced = ref<Record<number, boolean>>({})

// Per-item auto-enhance metadata. modelUsed surfaces in the badge next
// to the confidence chip ("Enhanced via LLM (claude-3-5-haiku-...)");
// enhancedAt is captured for parity with the backend payload but not
// rendered yet (curator audit trail surface, not a UX surface).
const enhancedModelUsed = ref<string | null>(null)
const enhancedAt = ref<string | null>(null)

// Per-itemId guard so the auto-fire happens exactly once per item open.
// A Set (not Record<bool>) so we never have to worry about pruning
// false entries — presence is the truth.
const enhanceTriggeredFor = ref<Set<string>>(new Set())

// 2026-05-03 — refine the 0-figures banner by topic. The default copy
// (this is from a Bagrut math paper, almost always there's a diagram)
// is correct for geometry / calculus / vectors / trigonometry, but
// pure-algebra and probability questions can legitimately have NO
// figure. We bucket the auto-classified taxonomyNode prefix and
// pick a softer "verify diagrams aren't missed; this topic may
// legitimately have none" copy for the no-diagram-typical buckets.
//
// taxonomyNode is a dotted path like "calculus.derivative_rules" —
// only the first segment (the topic) drives bucketing.
//
// The three buckets:
//   - DIAGRAM_HEAVY      → loud warning (default)
//   - DIAGRAM_OPTIONAL   → softened verify-prompt (algebra et al.)
//   - UNKNOWN (null)     → fallback to the original generic copy
//
// Hard rule: DIAGRAM_HEAVY is the SAFE default — when in doubt,
// ask the curator to verify. Softening is reserved for topics where
// the false-positive rate of the loud warning is high enough to
// train curators to tune it out (alarm fatigue).
const DIAGRAM_HEAVY_TOPICS = new Set([
  'geometry',
  'calculus',
  'vectors',
  'trigonometry',
])

const DIAGRAM_OPTIONAL_TOPICS = new Set([
  'algebra',
  'probability',
  'functions',
])

type FiguresBanner = 'diagram-heavy' | 'diagram-optional' | 'unknown'

function bucketFromTaxonomyNode(node: string | null): FiguresBanner {
  if (!node) return 'unknown'
  const topic = node.split('.', 1)[0]?.toLowerCase().trim()
  if (!topic) return 'unknown'
  if (DIAGRAM_HEAVY_TOPICS.has(topic)) return 'diagram-heavy'
  if (DIAGRAM_OPTIONAL_TOPICS.has(topic)) return 'diagram-optional'
  return 'unknown'
}

const figuresBannerBucket = computed<FiguresBanner>(() =>
  bucketFromTaxonomyNode(item.value?.taxonomyNode ?? null),
)

interface EnhanceTextResponse {
  itemId: string
  originalText: string
  enhancedText: string
  modelUsed: string | null
  enhancedAt: string
}

async function enhanceQuestion(index: number) {
  if (!item.value)
    return
  const targetId = item.value.id
  enhanceLoading.value = { ...enhanceLoading.value, [index]: true }
  enhanceErrors.value = { ...enhanceErrors.value, [index]: '' }
  try {
    const resp = await $api<EnhanceTextResponse>(
      `/admin/ingestion/items/${targetId}/enhance-text`,
      { method: 'POST' },
    )
    // Watcher may have swapped the item between dispatch and resolve
    // (e.g. curator clicked through to a sibling card). Only apply if
    // we're still on the item we fired for.
    if (item.value?.id !== targetId)
      return
    applyEnhancedToAllQuestions(resp)
  }
  catch (e: any) {
    enhanceErrors.value = {
      ...enhanceErrors.value,
      [index]: e?.data?.message ?? e?.message ?? 'Enhance failed.',
    }
  }
  finally {
    enhanceLoading.value = { ...enhanceLoading.value, [index]: false }
  }
}

/**
 * Apply an item-wide enhance response to every recreated-question
 * card and capture model metadata for the badge. The auto-fire path
 * and the manual "Re-run enhance" button both funnel through here so
 * the card UI stays consistent. showEnhanced is flipped on for every
 * index so the curator sees the cleaned view by default; the per-card
 * toggle lets them flip back to the raw recreated text on demand.
 */
function applyEnhancedToAllQuestions(resp: EnhanceTextResponse) {
  if (!item.value)
    return
  const next: Record<number, string> = { ...enhancedTexts.value }
  const nextShow: Record<number, boolean> = { ...showEnhanced.value }
  for (const q of item.value.recreatedQuestions) {
    next[q.index] = resp.enhancedText
    nextShow[q.index] = true
  }
  enhancedTexts.value = next
  showEnhanced.value = nextShow
  enhancedModelUsed.value = resp.modelUsed
  enhancedAt.value = resp.enhancedAt
}

/**
 * Auto-enhance once per item open. Fired from fetchDetail when the
 * item is an InReview Bagrut draft; idempotent across Vite HMR
 * remounts via the enhanceTriggeredFor Set.
 *
 * Honesty caveat: in Vue dev mode with strict-mode-style component
 * remounts (HMR), the watcher may fire twice in quick succession.
 * The Set guard prevents the second POST from going out — but if it
 * somehow does, the backend is idempotent (no side-effects) so the
 * worst case is a duplicate $0.0002 Anthropic call, not a data bug.
 */
async function autoEnhanceIfNeeded(d: ItemDetail) {
  const isAutoEnhanceTarget =
    d.sourceType === 'bagrut'
    && d.currentStage === 'InReview'
    && (d.recreatedQuestions?.length ?? 0) > 0

  if (!isAutoEnhanceTarget)
    return
  if (enhanceTriggeredFor.value.has(d.id))
    return

  // Mark BEFORE the await so a second concurrent caller (HMR remount,
  // double watcher) bails out — race-safe in single-threaded JS event
  // loop because the mutation happens before the microtask yield.
  enhanceTriggeredFor.value.add(d.id)

  try {
    const resp = await $api<EnhanceTextResponse>(
      `/admin/ingestion/items/${d.id}/enhance-text`,
      { method: 'POST' },
    )
    if (item.value?.id !== d.id)
      return
    applyEnhancedToAllQuestions(resp)
  }
  catch (e: any) {
    // Auto-enhance failure must NOT block the rest of the panel.
    // Surface inline via the existing enhanceErrors[0] plumbing so
    // curators see "why is the cleaned view missing?" without losing
    // the panel. If the curator hits "Enhance with LLM" manually
    // afterwards we re-run with the same itemId guard removed below.
    enhanceErrors.value = {
      ...enhanceErrors.value,
      0: e?.data?.message ?? e?.message ?? 'Auto-enhance failed — fall back to raw OCR text.',
    }
    // Allow manual retry by un-marking the guard. Without this the
    // "Re-run enhance" button would silently no-op for the rest of
    // the session after a single transient network blip.
    enhanceTriggeredFor.value.delete(d.id)
  }
}

function toggleEnhanced(index: number) {
  showEnhanced.value = { ...showEnhanced.value, [index]: !showEnhanced.value[index] }
}

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
  // ADR-0062 Phase 1.5 (2026-05-03) — enhanced text persisted on the
  // BagrutDraftPayloadDocument by the /enhance-text endpoint. Present
  // when the draft has been enhanced at least once. The SPA uses these
  // to render the cleaned view immediately on panel open WITHOUT
  // firing a fresh /enhance-text POST — the cache layer makes that
  // call cheap, the persistence layer makes it unnecessary.
  enhancedText?: string | null
  enhancedAt?: string | null
  enhancedBy?: string | null
  // 2026-05-03 — auto-classified taxonomy seed + curator-confirmed
  // override. The 0-figures banner reads the topic prefix to decide
  // whether 0 figures is suspicious or normal.
  taxonomyNode?: string | null
  metadataState?: string | null
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

  // Per-item state reset. Without this, switching from item A (which
  // has been enhanced) to item B leaks A's enhanced text into B's
  // recreated cards via the index-keyed enhancedTexts map. The
  // enhanceTriggeredFor Set is keyed by id and is intentionally NOT
  // reset — it's the cross-item idempotency guard.
  enhancedTexts.value = {}
  enhanceLoading.value = {}
  enhanceErrors.value = {}
  showEnhanced.value = {}
  enhancedModelUsed.value = null
  enhancedAt.value = null

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
      taxonomyNode: resp.taxonomyNode ?? null,
      metadataState: resp.metadataState ?? null,
    }

    // ADR-0062 Phase 1.5 (2026-05-03) — if the backend persisted the
    // enhanced text on the draft, apply it locally and mark the
    // auto-enhance trigger as already-fired. This is what makes "open
    // a previously enhanced item" instant: the SPA shows the cleaned
    // view from cache without firing the /enhance-text POST. The
    // backend's draft persistence is the source of truth; the
    // sha256-keyed cache is a deduplication mechanism that lives
    // inside the enhancer service.
    if (resp.enhancedText && item.value) {
      const next: Record<number, string> = {}
      const nextShow: Record<number, boolean> = {}
      for (const q of item.value.recreatedQuestions) {
        next[q.index] = resp.enhancedText
        nextShow[q.index] = true
      }
      enhancedTexts.value = next
      showEnhanced.value = nextShow
      enhancedModelUsed.value = resp.enhancedBy ?? null
      enhancedAt.value = resp.enhancedAt ?? null
      // Pre-mark the auto-enhance guard so autoEnhanceIfNeeded skips —
      // we already have the result; firing a network call would just
      // hit the cache and waste a round-trip.
      enhanceTriggeredFor.value.add(resp.id)
    }

    // Fire-and-forget binary fetches for the visual-review surface.
    // Reads the freshly-set item.value, so it picks up the new id +
    // figure list without us threading them through. Awaiting would
    // make the panel block on PDF-blob latency for no reason — the
    // text-side of the panel renders immediately while embeds fade in.
    if (item.value) {
      loadVisualReviewBlobs(item.value)
      // Gap A — auto-enhance on first open. Fire-and-forget; the
      // panel renders immediately and the badge + cleaned text fade
      // in when the response lands. No-op when the persisted-enhanced-
      // text branch above already pre-marked the guard.
      autoEnhanceIfNeeded(item.value)
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
  else if (!open)
    revokeVisualReviewBlobs()   // free PDF/figure blob memory on close
})

const handleClose = () => {
  emit('update:isOpen', false)
}

// =============================================================================
// Gap C — Single fused "Confirm metadata + concepts" CTA (2026-05-03).
//
// Curators currently click two confirms in sequence: Save in
// CuratorMetadataPanel, then Confirm in ConceptReviewPanel. The fused
// CTA at the top of the modal runs both in order (metadata first, then
// concepts) so they don't have to scroll. Both individual buttons stay
// — curators may still want to confirm panels independently when
// debugging a specific gate.
//
// Contract with the children: each child exposes via defineExpose:
//   - canConfirm: ref<boolean>     — readiness to submit
//   - alreadyConfirmed: ref<boolean> — current persisted state
//   - confirmExternal: () => Promise<void> — submit, throws on failure
//   - reload: () => Promise<void>  — refetch state
//
// Failure semantics: if metadata save throws, we DO NOT call concept
// confirm — surface metadata's error inline and bail. If metadata
// succeeds and concept confirm throws, we leave the metadata as
// confirmed (it's persisted; idempotent re-save would be wasteful)
// and surface concept's error inline.
// =============================================================================

const metadataPanelRef = ref<{
  canConfirm: { value: boolean }
  alreadyConfirmed: { value: boolean }
  confirmExternal: () => Promise<void>
  reload: () => Promise<void>
} | null>(null)

const conceptPanelRef = ref<{
  canConfirm: { value: boolean }
  alreadyConfirmed: { value: boolean }
  confirmExternal: () => Promise<void>
  reload: () => Promise<void>
} | null>(null)

const fusedSubmitting = ref(false)
const fusedError = ref<string | null>(null)

// Stepper state — derived from the children's exposed refs. Using
// computed instead of v-model so the indicator stays a single source
// of truth (the panels' own state) rather than a parent shadow copy.
const metadataStepDone = computed(() =>
  metadataPanelRef.value?.alreadyConfirmed.value === true)
const conceptsStepDone = computed(() => {
  if (!item.value || item.value.sourceType !== 'bagrut')
    return true   // non-Bagrut items skip concept review entirely
  return conceptPanelRef.value?.alreadyConfirmed.value === true
})
const fusedReady = computed(() => metadataStepDone.value && conceptsStepDone.value)

// Fused button is enabled when there is *something to do*. Either:
//   - metadata is unconfirmed but the metadata panel is ready to save
//   - concepts unconfirmed but the concept panel has a primary picked
// (or both). If both gates are already green, the button hides into
// the "Ready to publish" state.
const fusedCanSubmit = computed(() => {
  if (fusedSubmitting.value)
    return false
  if (fusedReady.value)
    return false
  const metaReady = metadataStepDone.value || (metadataPanelRef.value?.canConfirm.value === true)
  const conceptReady =
    conceptsStepDone.value
    || item.value?.sourceType !== 'bagrut'
    || (conceptPanelRef.value?.canConfirm.value === true)
  return metaReady && conceptReady
})

async function confirmFused() {
  if (!item.value)
    return
  fusedSubmitting.value = true
  fusedError.value = null
  try {
    // Step 1: metadata. Skip if already confirmed.
    if (!metadataStepDone.value) {
      if (!metadataPanelRef.value) {
        fusedError.value = 'Metadata panel not ready — refresh and try again.'
        return
      }
      try {
        await metadataPanelRef.value.confirmExternal()
      }
      catch (e: any) {
        // Don't proceed to concepts — metadata's own error banner
        // surfaces the failure inline on its panel. Set a top-level
        // hint so the fused stepper indicator also reads the gate.
        fusedError.value = `Metadata save failed: ${e?.data?.message ?? e?.message ?? 'unknown error'}`
        return
      }
    }

    // Step 2: concepts (Bagrut only).
    if (item.value.sourceType === 'bagrut' && !conceptsStepDone.value) {
      if (!conceptPanelRef.value) {
        fusedError.value = 'Concept panel not ready — refresh and try again.'
        return
      }
      try {
        await conceptPanelRef.value.confirmExternal()
      }
      catch (e: any) {
        fusedError.value = `Concept confirm failed: ${e?.data?.message ?? e?.message ?? 'unknown error'}`
        return
      }
    }

    emit('item-updated')
    fetchDetail()
  }
  finally {
    fusedSubmitting.value = false
  }
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
  <!-- 2026-05-03: switched from VNavigationDrawer (480px right-side
       drawer) to a fullscreen VDialog so the curator has horizontal
       real estate to review the extracted question alongside the PDF
       and figures inline. Math (LaTeX) is rendered inline via
       renderMixedMathText; figures are co-located with the recreated
       question text inside Question Content. -->
  <VDialog
    data-allow-mismatch
    fullscreen
    scrollable
    transition="dialog-bottom-transition"
    class="cena-item-detail-dialog"
    :model-value="props.isOpen"
    @update:model-value="(val: boolean) => emit('update:isOpen', val)"
  >
    <VCard class="cena-item-detail-card">
      <VToolbar
        color="surface"
        density="compact"
        class="cena-item-detail-toolbar"
      >
        <VToolbarTitle class="text-h6">
          Item Detail
        </VToolbarTitle>
        <VSpacer />
        <VBtn
          icon="tabler-x"
          variant="text"
          aria-label="Close"
          data-test="item-detail-close"
          @click="handleClose"
        />
      </VToolbar>

      <VDivider />

      <VProgressLinear
        v-if="loading"
        indeterminate
        color="primary"
      />

      <PerfectScrollbar
        v-if="item && !loading"
        :options="{ wheelPropagation: false }"
        class="cena-item-detail-scroll"
      >
        <VCard
          flat
          class="cena-item-detail-content"
        >
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

          <!-- Gap C — fused gate progress + single confirm CTA (2026-05-03).
               Stepper reads the children's persisted state directly so
               there's a single source of truth; the fused button calls
               both confirms in sequence (metadata → concepts) and
               surfaces failure inline.
               a11y: aria-current marks the active step; aria-label on
               the stepper container announces the progress for screen
               readers. Each step has aria-label="Step N: <name> —
               <state>" so SR users hear the gate status without sight. -->
          <div
            v-if="item"
            class="cena-fused-stepper-row mb-4"
            data-test="confirm-stepper"
            role="group"
            aria-label="Publish gate progress"
          >
            <div class="cena-fused-steps">
              <div
                class="cena-fused-step"
                :class="{
                  'cena-fused-step--done': metadataStepDone,
                  'cena-fused-step--active': !metadataStepDone,
                }"
                :aria-current="!metadataStepDone ? 'step' : undefined"
                aria-label="Step 1 of 3: Metadata"
              >
                <VIcon
                  :icon="metadataStepDone ? 'tabler-circle-check' : 'tabler-circle-dot'"
                  :color="metadataStepDone ? 'success' : 'primary'"
                  size="20"
                />
                <span class="cena-fused-step-label">
                  Metadata{{ metadataStepDone ? ' ✓' : '' }}
                </span>
              </div>
              <span class="cena-fused-step-arrow">→</span>
              <div
                v-if="item.sourceType === 'bagrut'"
                class="cena-fused-step"
                :class="{
                  'cena-fused-step--done': conceptsStepDone,
                  'cena-fused-step--active': metadataStepDone && !conceptsStepDone,
                  'cena-fused-step--blocked': !metadataStepDone && !conceptsStepDone,
                }"
                :aria-current="metadataStepDone && !conceptsStepDone ? 'step' : undefined"
                aria-label="Step 2 of 3: Concepts"
              >
                <VIcon
                  :icon="conceptsStepDone ? 'tabler-circle-check' : 'tabler-circle-dot'"
                  :color="conceptsStepDone ? 'success' : (metadataStepDone ? 'primary' : 'disabled')"
                  size="20"
                />
                <span class="cena-fused-step-label">
                  Concepts{{ conceptsStepDone ? ' ✓' : '' }}
                </span>
              </div>
              <span
                v-if="item.sourceType === 'bagrut'"
                class="cena-fused-step-arrow"
              >→</span>
              <div
                class="cena-fused-step"
                :class="{
                  'cena-fused-step--done': fusedReady,
                  'cena-fused-step--blocked': !fusedReady,
                }"
                aria-label="Step 3 of 3: Ready to publish"
              >
                <VIcon
                  :icon="fusedReady ? 'tabler-rocket' : 'tabler-lock'"
                  :color="fusedReady ? 'success' : 'disabled'"
                  size="20"
                />
                <span class="cena-fused-step-label">
                  {{ fusedReady ? 'Ready to publish' : 'Publish locked' }}
                </span>
              </div>
            </div>

            <VBtn
              color="primary"
              variant="elevated"
              :disabled="!fusedCanSubmit"
              :loading="fusedSubmitting"
              data-test="confirm-fused-button"
              prepend-icon="tabler-check-all"
              @click="confirmFused"
            >
              Confirm metadata + concepts
            </VBtn>
          </div>

          <VAlert
            v-if="fusedError"
            type="error"
            density="compact"
            variant="tonal"
            class="mb-4"
            data-test="confirm-fused-error"
          >
            {{ fusedError }}
          </VAlert>

          <!-- RDY-019e Curator Metadata handshake -->
          <CuratorMetadataPanel
            ref="metadataPanelRef"
            :item-id="props.itemId"
            class="mb-4"
            @confirmed="() => emit('item-updated')"
          />

          <!-- ADR-0062 Phase 1 — Concept review panel. Bagrut-only for
               now (the closed-set catalog covers math). Confirming here
               emits QuestionConceptsConfirmed_V1 and opens the
               first-200-items publish gate for this item. -->
          <ConceptReviewPanel
            v-if="item.sourceType === 'bagrut'"
            ref="conceptPanelRef"
            :item-id="props.itemId"
            class="mb-4"
            @confirmed="() => emit('item-updated')"
          />

          <!-- 0-figures banner — copy varies by auto-classified topic.
               Geometry / calculus / vectors / trigonometry nearly always
               carry diagrams (warning stays loud — type=warning).
               Algebra / probability / functions can legitimately have
               none (softer info-tone "verify, not block" prompt).
               Unknown taxonomy → keep the original generic copy.

               Why we don't suppress the banner entirely for the soft
               case: even an algebra question can lose a referenced
               number-line diagram in OCR; we're trading "loud false
               positive" for "soft true reminder". Banner is never
               a publish-blocker — it's a verify-prompt regardless of
               taxonomy. -->
          <VAlert
            v-if="item.sourceType === 'bagrut' && item.figures.length === 0 && figuresBannerBucket === 'diagram-heavy'"
            type="warning"
            density="compact"
            variant="tonal"
            class="mb-4"
            data-test="item-detail-no-figures-warning"
            data-test-bucket="diagram-heavy"
          >
            <strong>OCR figure extraction returned 0 figures.</strong>
            Bagrut {{ item.taxonomyNode?.split('.', 1)[0] || 'math' }} questions almost always include diagrams. Verify against the original PDF below — if a diagram is visibly present but missing here, flag the item for re-OCR rather than approving.
          </VAlert>
          <VAlert
            v-else-if="item.sourceType === 'bagrut' && item.figures.length === 0 && figuresBannerBucket === 'diagram-optional'"
            type="info"
            density="compact"
            variant="tonal"
            class="mb-4"
            data-test="item-detail-no-figures-warning"
            data-test-bucket="diagram-optional"
          >
            <strong>OCR figure extraction returned 0 figures.</strong>
            This {{ item.taxonomyNode?.split('.', 1)[0] || 'algebra' }} question may legitimately have no diagram — verify against the original PDF below, but a missing figure is not necessarily an OCR defect.
          </VAlert>
          <VAlert
            v-else-if="item.sourceType === 'bagrut' && item.figures.length === 0"
            type="warning"
            density="compact"
            variant="tonal"
            class="mb-4"
            data-test="item-detail-no-figures-warning"
            data-test-bucket="unknown"
          >
            <strong>OCR figure extraction returned 0 figures.</strong>
            Bagrut math papers almost always include diagrams. Verify against the original PDF below — if a diagram is visibly present but missing here, flag the item for re-OCR rather than approving.
          </VAlert>

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
               Fullscreen modal layout (2026-05-03): when figures exist
               on the item, lay out the recreated questions on the left
               and a sticky figures column on the right so the curator
               can validate "this stem references THAT diagram" without
               scrolling. Below 1100px the grid collapses to a single
               column (figures stack under text). Items without figures
               fall back to a full-width single-column layout —
               unchanged behaviour for non-Bagrut sources.
               Optional chaining on `recreatedQuestions?.length` is
               load-bearing: Vite HMR can replace the template while the
               existing `item` ref still has the pre-2026-05-01 shape
               without `recreatedQuestions`; without `?.length` Vue
               throws on undefined access and the section silently fails
               to render. -->
          <div
            v-if="item.recreatedQuestions?.length"
            class="mb-4 cena-content-grid"
            :class="{ 'cena-content-grid--with-figures': item.figures.length > 0 }"
            data-test="item-detail-recreated"
          >
            <div class="cena-content-text">
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
                    Question {{ q.index + 1 }}<span
                      v-if="q.sourcePage"
                      class="ms-2 text-disabled"
                    >· p{{ q.sourcePage }}</span>
                  </span>
                  <div class="d-flex align-center gap-2">
                    <VChip
                      size="x-small"
                      :color="q.confidence >= 0.85 ? 'success' : q.confidence >= 0.65 ? 'warning' : 'error'"
                      label
                    >
                      {{ Math.round(q.confidence * 100) }}% confidence
                    </VChip>
                    <!-- "Enhanced via LLM" badge (Gap A, 2026-05-03).
                         Surfaces the model that produced the cleaned
                         text next to the confidence chip so curators
                         know what they're reviewing without digging
                         into the network tab. Hidden until enhance
                         lands; auto-enhance fires on item open for
                         InReview Bagrut items so this badge is the
                         default state, not the exception. -->
                    <VChip
                      v-if="enhancedTexts[q.index] && showEnhanced[q.index]"
                      size="x-small"
                      color="primary"
                      variant="tonal"
                      label
                      data-test="item-detail-auto-enhanced-badge"
                    >
                      <VIcon
                        icon="tabler-sparkles"
                        size="12"
                        start
                      />
                      Enhanced via LLM<span
                        v-if="enhancedModelUsed"
                        class="ms-1"
                      >({{ enhancedModelUsed }})</span>
                    </VChip>
                    <!-- ADR-0062 Phase 1.5 — LLM cleanup pass. Wraps math
                         in LaTeX delimiters, restores paragraph breaks,
                         marks figure anchors. Real Anthropic call (cost
                         metered via the question-generation cost path).
                         Three-state button:
                           - never enhanced: "Enhance with LLM" → fires.
                           - enhanced + showing: "Show original" → toggle.
                           - enhanced + hiding: "Show enhanced" → toggle.
                         Re-run path: when the curator wants a fresh
                         enhance (e.g. after editing the source text),
                         shift-click would be ideal but we don't have
                         the modifier UX yet — instead, "Re-run enhance"
                         appears as the action label after the first
                         run lands so it's discoverable. -->
                    <VBtn
                      v-if="item.sourceType === 'bagrut'"
                      size="x-small"
                      variant="text"
                      :loading="enhanceLoading[q.index]"
                      :color="enhancedTexts[q.index] && showEnhanced[q.index] ? 'primary' : undefined"
                      :prepend-icon="enhancedTexts[q.index] ? (showEnhanced[q.index] ? 'tabler-eye-off' : 'tabler-eye') : 'tabler-wand'"
                      data-test="item-detail-enhance-toggle"
                      @click="enhancedTexts[q.index] ? toggleEnhanced(q.index) : enhanceQuestion(q.index)"
                    >
                      {{ enhancedTexts[q.index]
                        ? (showEnhanced[q.index] ? 'Show original' : 'Show enhanced')
                        : 'Enhance with LLM' }}
                    </VBtn>
                    <!-- Re-run enhance — only after first run, separate
                         from the toggle so the curator never accidentally
                         spends another LLM call when they meant to flip
                         the view. Loading indicator is shared with the
                         toggle (both bind to enhanceLoading[index]). -->
                    <VBtn
                      v-if="item.sourceType === 'bagrut' && enhancedTexts[q.index]"
                      size="x-small"
                      variant="text"
                      :loading="enhanceLoading[q.index]"
                      prepend-icon="tabler-refresh"
                      data-test="item-detail-enhance-rerun"
                      @click="enhanceQuestion(q.index)"
                    >
                      Re-run enhance
                    </VBtn>
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

                <VAlert
                  v-if="enhanceErrors[q.index]"
                  type="error"
                  density="compact"
                  variant="tonal"
                  class="mb-2"
                >
                  {{ enhanceErrors[q.index] }}
                </VAlert>

                <!-- Recreated card body. 2-column inside the card when a
                     sourcePage is known: text on the left, PDF page
                     thumbnail on the right (anchored to #page=N so the
                     curator sees exactly the page this stem came from).
                     Below 900px the thumbnail wraps under the text. -->
                <div
                  v-if="!diffOpen[q.index]"
                  class="cena-recreated-card"
                  :class="{ 'cena-recreated-card--with-page': q.sourcePage && pdfBlobUrl }"
                >
                  <!-- Default view: KaTeX-formatted recreated question,
                       OR enhanced version when toggled. The enhanced
                       text from the OCR-cleanup LLM contains
                       [[FIGURE:p<page>]] markers; we split on those and
                       render an inline PDF-page embed for each marker
                       (Gap B, 2026-05-03). The text fragments still go
                       through the same KaTeX-aware renderMixedMathText
                       path. v-html on text fragments is safe per the
                       renderer's escape + bdi-LTR + KaTeX-fallback chain.
                       Toggling between enhanced/original re-runs the
                       splitter — markers only appear in enhanced text,
                       so the original recreated form skips the figure
                       fragments and renders as a single html block. -->
                  <div
                    class="cena-mmt-block cena-recreated-text"
                    data-test="item-detail-recreated-text"
                  >
                    <template
                      v-for="(frag, fi) in renderTextWithFigures(
                        showEnhanced[q.index] && enhancedTexts[q.index]
                          ? enhancedTexts[q.index]
                          : q.text,
                      )"
                      :key="`f-${q.index}-${fi}`"
                    >
                      <span
                        v-if="frag.kind === 'html'"
                        v-html="frag.html"
                      />
                      <!-- Figure anchor (Gap B): inline PDF-page embed.
                           When pdfBlobUrl is loaded the embed renders
                           a compact ~180×180 thumbnail anchored to
                           #page=N. While the blob is in flight the
                           skeleton placeholder shows. Bare [[FIGURE]]
                           with no page renders a generic placeholder
                           and the "page unknown" hint so the curator
                           can verify against the visual-review PDF
                           below. data-test on every variant for e2e. -->
                      <span
                        v-else
                        class="cena-figure-anchor-wrapper"
                      >
                        <span
                          v-if="frag.page && pdfBlobUrl"
                          class="cena-figure-anchor"
                          data-test="figure-anchor-embed"
                        >
                          <embed
                            :src="`${pdfBlobUrl}#page=${frag.page}&toolbar=0&navpanes=0`"
                            type="application/pdf"
                            class="cena-figure-anchor-embed"
                            :title="`Figure (page ${frag.page})`"
                          >
                          <span class="cena-figure-anchor-caption text-caption text-medium-emphasis">
                            Figure (page {{ frag.page }})
                          </span>
                        </span>
                        <span
                          v-else-if="frag.page && !pdfBlobUrl"
                          class="cena-figure-anchor cena-figure-anchor--loading"
                          data-test="figure-anchor-loading"
                        >
                          <span class="cena-figure-anchor-loading-body text-caption text-disabled">
                            Loading figure…
                          </span>
                          <span class="cena-figure-anchor-caption text-caption text-medium-emphasis">
                            Figure (page {{ frag.page }})
                          </span>
                        </span>
                        <span
                          v-else
                          class="cena-figure-anchor cena-figure-anchor--unknown"
                          data-test="figure-anchor-unknown"
                        >
                          <VIcon
                            icon="tabler-photo-question"
                            size="20"
                            class="text-medium-emphasis"
                          />
                          <span class="cena-figure-anchor-caption text-caption text-medium-emphasis">
                            Figure (page unknown — verify against the PDF below)
                          </span>
                        </span>
                      </span>
                    </template>
                  </div>
                  <!-- PDF page thumbnail anchored to the source page.
                       The browser's PDF viewer honours #page=N so we
                       jump to the exact page without rendering it
                       client-side (no PDF.js dependency). Keep the
                       embed compact so it doesn't dominate the card. -->
                  <div
                    v-if="q.sourcePage && pdfBlobUrl"
                    class="cena-recreated-page"
                    data-test="item-detail-recreated-page"
                  >
                    <div class="text-caption text-medium-emphasis mb-1">
                      Source page {{ q.sourcePage }}
                    </div>
                    <!-- 2026-05-04: added zoom=page-fit so Chromium's PDF
                         viewer scales the page to the embed's visible area
                         instead of rendering at natural DPI and cropping
                         everything below the first ~280px. The fragment
                         params (page, toolbar, navpanes, zoom) are honored
                         by every browser's native PDF viewer; pdfium /
                         Firefox PDF.js / WebKit all accept the same set. -->
                    <embed
                      :src="`${pdfBlobUrl}#page=${q.sourcePage}&toolbar=0&navpanes=0&zoom=page-fit`"
                      type="application/pdf"
                      class="cena-recreated-page-embed"
                    >
                  </div>
                </div>

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

            <!-- Inline figures column (2026-05-03 fullscreen layout).
                 Co-located with the recreated text so the curator can
                 verify "this stem references that diagram" without
                 scrolling between sections. Each figure tile clicks
                 through to the auth-fetched blob URL in a new tab for
                 a closer look. Hidden when item.figures is empty —
                 the parent grid drops back to a single column then. -->
            <aside
              v-if="item.figures.length > 0"
              class="cena-content-figures"
              data-test="item-detail-recreated-figures"
            >
              <div class="text-body-2 text-medium-emphasis mb-2 d-flex align-center">
                <VIcon
                  icon="tabler-photo"
                  size="16"
                  class="me-1"
                />
                Figures referenced
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
              <div class="cena-figure-grid cena-figure-grid--inline">
                <a
                  v-for="fig in item.figures"
                  :key="`inline-${fig.index}`"
                  :href="figureBlobUrls[fig.index] ?? '#'"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="cena-figure-tile"
                  :title="fig.altText ?? `Figure on page ${fig.page}`"
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
            </aside>
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
    </VCard>
  </VDialog>
</template>

<style scoped>
/* 2026-05-03 fullscreen-modal layout — replaces the prior 480px right-
   side VNavigationDrawer. The dialog itself fills the viewport; we use
   the toolbar as the close-affordance (drawer-style header is gone)
   and constrain the content to a comfortable reading width via
   max-inline-size on the inner card so wide monitors don't stretch
   prose to a single line. */
.cena-item-detail-card {
  block-size: 100%;
  display: flex;
  flex-direction: column;
}
.cena-item-detail-toolbar {
  flex: 0 0 auto;
}
.cena-item-detail-scroll {
  flex: 1 1 auto;
  min-block-size: 0;
}
.cena-item-detail-content {
  max-inline-size: 1400px;
  margin-inline: auto;
}

/* Question-content 2-column grid (text + figures) for fullscreen mode.
   Below 1100px the figures column wraps under the text column so the
   layout still reads on narrow displays. The text column gets the
   wider track (3fr) — recreated stem + diff is the primary review
   surface; figures are an inspector. */
.cena-content-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1.25rem;
  align-items: start;
}
@media (min-width: 1100px) {
  .cena-content-grid.cena-content-grid--with-figures {
    grid-template-columns: 3fr 2fr;
  }
}
.cena-content-text {
  min-inline-size: 0;
  /* Prevents long math expressions from forcing a horizontal scroll on
     the content column; KaTeX-rendered blocks already handle their
     own overflow inside the rendered span. */
}

/* Per-recreated-card 2-column: text + PDF page thumbnail. The thumbnail
   is anchored to the question's sourcePage via #page=N — no PDF.js
   dependency, the browser's native PDF viewer handles it. */
.cena-recreated-card {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.75rem;
  align-items: start;
}
@media (min-width: 900px) {
  .cena-recreated-card.cena-recreated-card--with-page {
    /* 2026-05-04: bumped right column 240px → 360px so the source-page
       thumbnail has enough horizontal real estate for the browser's
       PDF viewer to render the page at a usable scale. At 240px the
       Chromium viewer aggressively crops to whatever fits the visible
       client area. The user-reported "real snapshot" defect was
       symptomatic of this: the embed showed only a 1-line strip of
       the page (e.g. just "1.6") because the page was tall but the
       embed was short and the browser zoomed to fit width. */
    grid-template-columns: 1fr 360px;
  }
}
.cena-recreated-text {
  min-inline-size: 0;
}
.cena-recreated-page-embed {
  inline-size: 100%;
  /* 2026-05-04: 280px → 460px so a portrait A4 PDF page renders at a
     usable height. Chromium's PDF viewer honours #zoom=page-fit and
     scales the page to fit the embed; with 280px height it ended up
     showing just a sliver of the page. 460px is roughly 2/3 of A4
     portrait at default DPI, which keeps the side-by-side card
     readable without dominating the panel. */
  block-size: 460px;
  border: 1px solid rgba(var(--v-theme-outline-variant), 0.6);
  border-radius: 0.25rem;
  background: rgb(var(--v-theme-surface));
}
.cena-content-figures {
  background: rgba(var(--v-theme-surface-variant), 0.4);
  border-radius: 0.4rem;
  padding: 0.75rem;
  position: sticky;
  inset-block-start: 0.5rem;
  max-block-size: calc(100vh - 8rem);
  overflow-y: auto;
}
.cena-figure-grid--inline {
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
}

/* cena-fused-stepper-row — Gap C fused confirm + stepper at top of
   the item-detail body. Lays out as: stepper on the left, button on
   the right; below 720px the button wraps under the stepper. The
   stepper itself is a flex row of inert (visual-only) step cells —
   keyboard reachable via the surrounding role=group + button focus
   target only; the steps don't take focus on their own (they're
   informative, not interactive — Vuetify VStepper's actionable steps
   would over-promise). */
.cena-fused-stepper-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.85rem 1rem;
  background: rgba(var(--v-theme-surface-variant), 0.4);
  border: 1px solid rgba(var(--v-theme-outline-variant), 0.6);
  border-radius: 0.4rem;
}
.cena-fused-steps {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
}
.cena-fused-step {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.2rem 0.5rem;
  border-radius: 0.25rem;
  background: rgb(var(--v-theme-surface));
  border: 1px solid rgba(var(--v-theme-outline-variant), 0.6);
  /* Honour reduced-motion preferences from the OS — steps fade in/out
     without animation when prefers-reduced-motion is set. */
  transition: background 0.15s ease, border-color 0.15s ease;
}
.cena-fused-step--done {
  background: rgba(var(--v-theme-success), 0.08);
  border-color: rgba(var(--v-theme-success), 0.4);
}
.cena-fused-step--active {
  background: rgba(var(--v-theme-primary), 0.08);
  border-color: rgba(var(--v-theme-primary), 0.4);
  font-weight: 600;
}
.cena-fused-step--blocked {
  opacity: 0.6;
}
.cena-fused-step-arrow {
  color: rgb(var(--v-theme-on-surface-variant));
  font-weight: 600;
}
@media (prefers-reduced-motion: reduce) {
  .cena-fused-step {
    transition: none;
  }
}

/* cena-figure-anchor — inline figure thumbnail rendered between text
   fragments split out of [[FIGURE:p<page>]] markers. Stays compact
   (~180×180) so it doesn't dominate the recreated text; the curator
   can still click through to the visual-review PDF below for a closer
   look. The wrapper is inline-block so multiple anchors can sit on
   the same paragraph; each anchor is its own visual block on a new
   line for legibility. */
.cena-figure-anchor-wrapper {
  display: inline-block;
  margin: 0.5rem 0;
}
.cena-figure-anchor {
  display: inline-flex;
  flex-direction: column;
  align-items: stretch;
  gap: 0.25rem;
  inline-size: 180px;
  vertical-align: top;
  padding: 0.35rem;
  background: rgba(var(--v-theme-surface-variant), 0.5);
  border: 1px solid rgba(var(--v-theme-outline-variant), 0.6);
  border-radius: 0.25rem;
}
.cena-figure-anchor--loading,
.cena-figure-anchor--unknown {
  align-items: center;
  justify-content: center;
  min-block-size: 120px;
  text-align: center;
}
.cena-figure-anchor-embed {
  inline-size: 100%;
  block-size: 180px;
  border: 1px solid rgba(var(--v-theme-outline-variant), 0.6);
  border-radius: 0.2rem;
  background: rgb(var(--v-theme-surface));
}
.cena-figure-anchor-loading-body {
  inline-size: 100%;
  min-block-size: 80px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(var(--v-theme-surface-variant), 0.6);
  border-radius: 0.2rem;
}
.cena-figure-anchor-caption {
  text-align: center;
  word-break: break-word;
}

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
  /* 70vh in fullscreen mode (2026-05-03) — Question Content is now
     side-by-side with figures via cena-content-grid, so the visual-
     review PDF section below it can claim more vertical space without
     burying the recreated text. Curators can still scroll inside the
     PDF viewer to see all pages and click the embed's own fullscreen
     icon for a closer look. */
  block-size: 70vh;
  min-block-size: 480px;
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
