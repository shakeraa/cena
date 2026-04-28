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

// ---------------------------------------------------------------------------
async function load () {
  loading.value = true
  error.value = null
  try {
    const res = await $api<CuratorMetadataResponse>(
      `/api/admin/ingestion/pipeline/${props.itemId}/metadata`,
      { method: 'GET' })
    data.value = res
    // Seed local edits with current values so field bindings work.
    edits.value = { ...(res.current ?? {}) }
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
    // Build patch with ONLY fields the curator edited.
    const patch: Partial<CuratorMetadata> = {}
    for (const f of editedFields.value) {
      (patch as any)[f] = (edits.value as any)[f]
    }
    const res = await $api<CuratorMetadataResponse>(
      `/api/admin/ingestion/pipeline/${props.itemId}/metadata`,
      { method: 'PATCH', body: patch })
    data.value = res
    edits.value = { ...(res.current ?? {}) }
    editedFields.value.clear()
    if (res.metadataState === 'confirmed') emit('confirmed', props.itemId)
  } catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Save failed'
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
    edits.value = { ...(res.current ?? {}) }
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

const canConfirm = computed(() => {
  if (!data.value) return false
  // You can save if there are any edits. Separate visual: the
  // confirm-state transition is server-side when all required set.
  return editedFields.value.size > 0 && !saving.value
})

onMounted(load)
watch(() => props.itemId, load)
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
        <!-- Missing required banner -->
        <VAlert
          v-if="data.missingRequired.length > 0"
          type="warning"
          variant="tonal"
          density="compact">
          Missing required fields: {{ data.missingRequired.join(', ') }}
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

        <!-- Taxonomy node (curator only) -->
        <div class="d-flex align-center gap-2">
          <VTextField
            v-model="edits.taxonomyNode"
            label="Taxonomy node (e.g. calculus.derivatives.chain_rule)"
            density="compact"
            clearable
            @update:model-value="markEdited('taxonomyNode')" />
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
            Save changes
          </VBtn>
        </div>
      </div>
    </VCardText>
  </VCard>
</template>
