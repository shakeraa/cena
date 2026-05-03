<!-- =============================================================================
     Cena Platform — Curator Metadata Panel (RDY-019e-IMPL / Phase 1C UI)

     Drives the /api/admin/ingestion/pipeline/{id}/metadata handshake:
       GET     → show auto-extracted values + confidences + state
       PATCH   → merge curator edits (only non-null fields)
       DELETE  → explicit field clearance

     Nav: embed via <CuratorMetadataPanel :item-id="id" />

     Visual rules:
       conf >= 0.80 → green badge "auto"
       0.50 ≤ conf < 0.80 → amber badge "auto"
       conf < 0.50 → red badge "auto"
       field edited by curator → blue badge "edited"
     state badges:
       pending | auto_extracted | awaiting_review | confirmed
============================================================================= -->
<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { $api } from '@/utils/api'

interface CuratorMetadata {
  subject:          string | null
  language:         string | null
  track:            string | null
  sourceType:       string | null
  taxonomyNode:     string | null
  expectedFigures:  boolean | null
}

interface AutoExtractedMetadata {
  extracted:          CuratorMetadata
  fieldConfidences:   Record<string, number>
  extractionStrategy: string
}

interface CuratorMetadataResponse {
  itemId:          string
  metadataState:   'pending' | 'auto_extracted' | 'awaiting_review' | 'confirmed' | 'skipped'
  autoExtracted:   AutoExtractedMetadata | null
  current:         CuratorMetadata | null
  missingRequired: string[]
}

const props = defineProps<{ itemId: string }>()
const emit  = defineEmits<{ (e: 'confirmed', id: string): void }>()

const data    = ref<CuratorMetadataResponse | null>(null)
const loading = ref(false)
const saving  = ref(false)
const error   = ref<string | null>(null)
const edits   = ref<Partial<CuratorMetadata>>({})
const editedFields = ref<Set<string>>(new Set())

const SUBJECT_OPTIONS = [
  { title: 'Mathematics', value: 'math' },
  { title: 'Physics',     value: 'physics' },
  { title: 'Chemistry',   value: 'chemistry' },
]
const LANGUAGE_OPTIONS = [
  { title: 'Hebrew',  value: 'he' },
  { title: 'English', value: 'en' },
  { title: 'Arabic',  value: 'ar' },
]
const TRACK_OPTIONS = [
  { title: '3 units', value: '3u' },
  { title: '4 units', value: '4u' },
  { title: '5 units', value: '5u' },
]
const SOURCE_TYPE_OPTIONS = [
  { title: 'Student photo',     value: 'student_photo' },
  { title: 'Student PDF',       value: 'student_pdf' },
  { title: 'Bagrut reference',  value: 'bagrut_reference' },
  { title: 'Admin upload',      value: 'admin_upload' },
  { title: 'Cloud directory',   value: 'cloud_dir' },
]

// Taxonomy autocomplete options. Loaded from the admin API
// (`/api/admin/ingestion/taxonomy/leaves?track=...`) and re-fetched
// when the curator changes the track. The leaves are sorted server-
// side (topic → subtopic, ordinal) so we render in the order the
// backend returns. Free-text fallback handled below if the curator
// types a value that isn't in the list.
interface TaxonomyLeafOption {
  value: string         // "calculus.derivative_rules" — what we PATCH
  title: string         // "calculus › derivative_rules" — what curator sees
  conceptId: string     // "CAL-003" — surfaced in the chip subtitle
}

const taxonomyOptions = ref<TaxonomyLeafOption[]>([])
const taxonomyLoading = ref(false)
const taxonomyError   = ref<string | null>(null)

interface TaxonomyLeavesResponse {
  version: string
  track:   string | null
  leaves:  Array<{
    leafId:    string
    trackId:   string
    topic:     string
    subtopic:  string
    conceptId: string
    bloomMin:  number
    bloomMax:  number
  }>
}

async function loadTaxonomy (track: string | null | undefined) {
  // Without a track the backend returns leaves across all 3 tracks;
  // we still allow that for items where the curator hasn't picked a
  // track yet, but the list is much longer.
  taxonomyLoading.value = true
  taxonomyError.value = null
  try {
    const qs = track ? `?track=${encodeURIComponent(track)}` : ''
    const res = await $api<TaxonomyLeavesResponse>(
      `/api/admin/ingestion/taxonomy/leaves${qs}`,
      { method: 'GET' })
    taxonomyOptions.value = res.leaves.map(l => ({
      // CuratorMetadata.TaxonomyNode wire shape is "topic.subtopic"
      // (no track prefix) — see TaxonomyEvents.cs and the placeholder
      // in this panel ("calculus.derivatives.chain_rule"). The
      // taxonomy cache exposes the leaf as `${trackId}.${topic}.${sub}`
      // (e.g. "math_5u.calculus.derivative_rules"); strip the track
      // prefix here so what we PATCH matches the persistence schema.
      value:     `${l.topic}.${l.subtopic}`,
      title:     `${l.topic} › ${l.subtopic}`,
      conceptId: l.conceptId,
    }))
  } catch (e: any) {
    taxonomyError.value = e?.data?.message ?? e?.message ?? 'Failed to load taxonomy'
    taxonomyOptions.value = []
  } finally {
    taxonomyLoading.value = false
  }
}

// ---------------------------------------------------------------------------
async function load () {
  loading.value = true
  error.value = null
  try {
    const res = await $api<CuratorMetadataResponse>(
      `/api/admin/ingestion/pipeline/${props.itemId}/metadata`,
      { method: 'GET' })
    data.value = res
    // Seed local edits: start from auto-extracted suggestions, then
    // overlay any saved curator values (curator's saved values WIN
    // when both exist). Without the auto-extracted overlay, the
    // dropdowns sit empty even when the persistence layer has fully
    // populated AutoExtractedMetadata — surfaced by user 2026-04-29
    // staring at empty Subject/Language/Track/SourceType chips that
    // showed "auto 95%" confidence but no selected option.
    edits.value = {
      ...(res.autoExtracted?.extracted ?? {}),
      ...(res.current ?? {}),
    }
    editedFields.value.clear()
  } catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Failed to load metadata'
  } finally {
    loading.value = false
  }
}

async function save () {
  if (!data.value) return
  saving.value = true
  error.value = null
  try {
    // Build patch. If the curator made explicit edits we send only
    // those. If they're confirming an auto-extracted set without
    // touching anything, we send the coalesced view (auto + any prior
    // saved values) so the backend transitions CuratorMetadata.* from
    // null → the auto-suggested values and clears MissingRequired.
    let patch: Partial<CuratorMetadata>
    if (editedFields.value.size > 0) {
      patch = {}
      for (const f of editedFields.value) {
        (patch as any)[f] = (edits.value as any)[f]
      }
    } else {
      patch = { ...edits.value }
    }
    const res = await $api<CuratorMetadataResponse>(
      `/api/admin/ingestion/pipeline/${props.itemId}/metadata`,
      { method: 'PATCH', body: patch })
    data.value = res
    // Same overlay rule as load() — curator saved values win, but
    // the auto-extracted suggestion seeds any unset field so the UI
    // doesn't drop back to empty after a partial save.
    edits.value = {
      ...(res.autoExtracted?.extracted ?? {}),
      ...(res.current ?? {}),
    }
    editedFields.value.clear()
    if (res.metadataState === 'confirmed') emit('confirmed', props.itemId)
  } catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Save failed'
    // Re-throw for the fused-confirm orchestrator on the parent
    // (Gap C). Internal callers see the inline error via error.value.
    throw e
  } finally {
    saving.value = false
  }
}

async function clearField (field: string) {
  saving.value = true
  error.value = null
  try {
    const res = await $api<CuratorMetadataResponse>(
      `/api/admin/ingestion/pipeline/${props.itemId}/metadata/${field}`,
      { method: 'DELETE' })
    data.value = res
    // After clearField, fall back to auto-extracted suggestion (if any)
    // for the cleared field instead of leaving it empty.
    edits.value = {
      ...(res.autoExtracted?.extracted ?? {}),
      ...(res.current ?? {}),
    }
    editedFields.value.delete(field)
  } catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? `Failed to clear ${field}`
  } finally {
    saving.value = false
  }
}

function markEdited (field: keyof CuratorMetadata) {
  editedFields.value.add(field)
}

function confidenceColor (field: string): 'success' | 'warning' | 'error' | undefined {
  const conf = data.value?.autoExtracted?.fieldConfidences?.[field]
  if (conf == null) return undefined
  if (conf >= 0.80) return 'success'
  if (conf >= 0.50) return 'warning'
  return 'error'
}

function confidencePercent (field: string): string | null {
  const conf = data.value?.autoExtracted?.fieldConfidences?.[field]
  return conf == null ? null : `${Math.round(conf * 100)}%`
}

const stateBadgeColor = computed(() => {
  switch (data.value?.metadataState) {
    case 'confirmed':       return 'success'
    case 'awaiting_review': return 'warning'
    case 'auto_extracted':  return 'info'
    case 'skipped':         return 'secondary'
    default:                return 'default'
  }
})

// The required fields that drive the missing-required banner +
// confirm gating. Mirrors CuratorMetadataService.RequiredFields on
// the backend; keep both in sync. Display names mirror the Pascal-
// case form the backend's `missingRequired` returns so the warning
// copy doesn't change shape between the two banner sources.
const REQUIRED_FIELDS = [
  { key: 'subject',    display: 'Subject' },
  { key: 'language',   display: 'Language' },
  { key: 'sourceType', display: 'SourceType' },
] as const

// Effective missing = required fields not present in the coalesced
// (current ?? auto-extracted) view the curator is actually looking
// at. The backend's `missingRequired` only inspects CuratorMetadata
// (curator-confirmed values), so when Bagrut persistence seeds only
// AutoExtractedMetadata it returns "all required missing" while the
// dropdowns visibly contain the auto values. This computed reconciles
// the two. ADR-0019e + 2026-04-30 review.
const effectiveMissing = computed<string[]>(() => {
  const missing: string[] = []
  for (const f of REQUIRED_FIELDS) {
    const v = (edits.value as any)[f.key]
    if (v == null || v === '') missing.push(f.display)
  }
  return missing
})

// Are there auto-extracted values that haven't been confirmed yet?
// True when the backend reports missing-required, but the coalesced
// view is complete — i.e. auto-extracted is filling the gap and a
// single Save click should confirm the suggestion.
const hasUnconfirmedAutoValues = computed(() => {
  if (!data.value) return false
  const backendMissing = data.value.missingRequired.length > 0
  const uiComplete = effectiveMissing.value.length === 0
  return backendMissing && uiComplete
})

const canConfirm = computed(() => {
  if (!data.value || saving.value) return false
  // Saveable when (a) the curator made explicit edits, OR (b) there
  // are auto-extracted values awaiting one-click confirmation. Without
  // (b), a Bagrut item that arrives fully auto-populated would have
  // no path to "confirmed" without busy-work edits.
  return editedFields.value.size > 0 || hasUnconfirmedAutoValues.value
})

onMounted(async () => {
  await load()
  // Initial taxonomy fetch keyed off whatever track loaded — auto-
  // extracted Bagrut paths set this; uploads typically don't.
  await loadTaxonomy(edits.value.track ?? null)
})
watch(() => props.itemId, async (id) => {
  if (!id) return
  await load()
  await loadTaxonomy(edits.value.track ?? null)
})

// Expose a minimal contract for the parent's fused "Confirm metadata +
// concepts" CTA (Gap C, 2026-05-03). Mirrors the surface on
// ConceptReviewPanel so the orchestrator's call sites are symmetric.
defineExpose({
  /** True when there's something to save AND we're not mid-save. */
  canConfirm,
  /** True once the metadata has been confirmed (current state, not history). */
  alreadyConfirmed: computed(() => data.value?.metadataState === 'confirmed'),
  /**
   * Submit on behalf of an external caller. Throws on failure so the
   * orchestrator can halt the chained sequence and surface the error
   * inline on this panel via error.value.
   */
  confirmExternal: save,
  /** Force a re-fetch — used by the parent after a successful save. */
  reload: load,
})

// Re-fetch the taxonomy options when the curator changes the track.
// Empty taxonomy on a 3u/4u item silently returning math_5u-only
// leaves was the bug surfaced 2026-04-30 — the autocomplete must
// follow the track selection, not the initial load.
watch(() => edits.value.track, async (next) => {
  await loadTaxonomy(next ?? null)
})
</script>

<template>
  <VCard>
    <VCardTitle class="d-flex align-center gap-2">
      <span>Curator Metadata</span>
      <VChip v-if="data" :color="stateBadgeColor" size="small">
        {{ data.metadataState }}
      </VChip>
      <VSpacer />
      <VBtn
        icon="tabler-refresh"
        variant="text"
        size="small"
        :loading="loading"
        @click="load" />
    </VCardTitle>
    <VCardText>
      <VAlert v-if="error" type="error" variant="tonal" class="mb-3">{{ error }}</VAlert>

      <div v-if="loading" class="text-center py-6">
        <VProgressCircular indeterminate />
      </div>

      <div v-else-if="data" class="d-flex flex-column gap-3">
        <!-- Awaiting-confirmation banner: backend says required fields
             are unconfirmed, but auto-extraction has filled them in
             the visible dropdowns. One Save click promotes them. -->
        <VAlert
          v-if="hasUnconfirmedAutoValues"
          type="info"
          variant="tonal"
          density="compact">
          Auto-extracted values awaiting your confirmation — review and click Save to lock them in.
        </VAlert>

        <!-- Truly-missing banner: coalesced view still has gaps the
             curator must fill before this item can confirm. -->
        <VAlert
          v-else-if="effectiveMissing.length > 0"
          type="warning"
          variant="tonal"
          density="compact">
          Missing required fields: {{ effectiveMissing.join(', ') }}
        </VAlert>

        <!-- Extraction strategy banner -->
        <div v-if="data.autoExtracted" class="text-caption text-medium-emphasis">
          Auto-extracted via <strong>{{ data.autoExtracted.extractionStrategy }}</strong>
        </div>

        <!-- Subject -->
        <div class="d-flex align-center gap-2">
          <VSelect
            v-model="edits.subject"
            :items="SUBJECT_OPTIONS"
            label="Subject"
            density="compact"
            clearable
            @update:model-value="markEdited('subject')" />
          <VChip
            v-if="confidenceColor('Subject')"
            :color="confidenceColor('Subject')"
            size="x-small">
            auto {{ confidencePercent('Subject') }}
          </VChip>
          <VBtn
            v-if="edits.subject"
            icon="tabler-x"
            variant="text"
            size="x-small"
            @click="clearField('subject')" />
        </div>

        <!-- Language -->
        <div class="d-flex align-center gap-2">
          <VSelect
            v-model="edits.language"
            :items="LANGUAGE_OPTIONS"
            label="Language"
            density="compact"
            clearable
            @update:model-value="markEdited('language')" />
          <VChip
            v-if="confidenceColor('Language')"
            :color="confidenceColor('Language')"
            size="x-small">
            auto {{ confidencePercent('Language') }}
          </VChip>
          <VBtn
            v-if="edits.language"
            icon="tabler-x"
            variant="text"
            size="x-small"
            @click="clearField('language')" />
        </div>

        <!-- Track -->
        <div class="d-flex align-center gap-2">
          <VSelect
            v-model="edits.track"
            :items="TRACK_OPTIONS"
            label="Track"
            density="compact"
            clearable
            @update:model-value="markEdited('track')" />
          <VChip
            v-if="confidenceColor('Track')"
            :color="confidenceColor('Track')"
            size="x-small">
            auto {{ confidencePercent('Track') }}
          </VChip>
          <VBtn
            v-if="edits.track"
            icon="tabler-x"
            variant="text"
            size="x-small"
            @click="clearField('track')" />
        </div>

        <!-- Source type -->
        <div class="d-flex align-center gap-2">
          <VSelect
            v-model="edits.sourceType"
            :items="SOURCE_TYPE_OPTIONS"
            label="Source type"
            density="compact"
            clearable
            @update:model-value="markEdited('sourceType')" />
          <VChip
            v-if="confidenceColor('SourceType')"
            :color="confidenceColor('SourceType')"
            size="x-small">
            auto {{ confidencePercent('SourceType') }}
          </VChip>
          <VBtn
            v-if="edits.sourceType"
            icon="tabler-x"
            variant="text"
            size="x-small"
            @click="clearField('source_type')" />
        </div>

        <!-- Taxonomy node (curator only). VAutocomplete bound to the
             leaves served by /api/admin/ingestion/taxonomy/leaves;
             reloads when the track changes so 3u/4u/5u curators see
             only the leaves that exist in their track. `custom-filter`
             matches against value + title + conceptId so curators can
             search by either the readable label or the concept code
             ("CAL-003"). Free-text values from older saved data are
             preserved by passing :return-object="false" + :menu-props
             auto-select, but typing a brand-new node string is no
             longer expected — the field is now a closed picker. -->
        <div class="d-flex align-center gap-2">
          <VAutocomplete
            v-model="edits.taxonomyNode"
            :items="taxonomyOptions"
            item-title="title"
            item-value="value"
            label="Taxonomy node"
            density="compact"
            clearable
            :loading="taxonomyLoading"
            :no-data-text="taxonomyError ?? (taxonomyLoading ? 'Loading…' : 'No taxonomy leaves for this track')"
            @update:model-value="markEdited('taxonomyNode')">
            <template #item="{ props: itemProps, item }">
              <VListItem
                v-bind="itemProps"
                :title="item.raw.title"
                :subtitle="item.raw.conceptId" />
            </template>
          </VAutocomplete>
          <VBtn
            v-if="edits.taxonomyNode"
            icon="tabler-x"
            variant="text"
            size="x-small"
            @click="clearField('taxonomy_node')" />
        </div>

        <!-- Expected figures -->
        <div class="d-flex align-center gap-2">
          <VSwitch
            v-model="edits.expectedFigures"
            label="Expected figures"
            density="compact"
            @update:model-value="markEdited('expectedFigures')" />
          <VChip
            v-if="confidenceColor('ExpectedFigures')"
            :color="confidenceColor('ExpectedFigures')"
            size="x-small">
            auto {{ confidencePercent('ExpectedFigures') }}
          </VChip>
          <VBtn
            v-if="edits.expectedFigures != null"
            icon="tabler-x"
            variant="text"
            size="x-small"
            @click="clearField('expected_figures')" />
        </div>

        <div class="d-flex justify-end mt-4 gap-2">
          <VBtn
            color="primary"
            :disabled="!canConfirm"
            :loading="saving"
            @click="save">
            {{ hasUnconfirmedAutoValues && editedFields.size === 0 ? 'Confirm auto-extracted' : 'Save changes' }}
          </VBtn>
        </div>
      </div>
    </VCardText>
  </VCard>
</template>
