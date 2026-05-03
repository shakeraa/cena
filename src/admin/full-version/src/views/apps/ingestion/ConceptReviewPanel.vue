<!-- =============================================================================
     Cena Platform — Concept Review Panel (ADR-0062 Phase 1)

     Drives /api/admin/ingestion/items/{id}/concepts:
       GET  → load extracted + confirmed + canonical catalog (~73 leaves)
       POST → confirm a (Primary + Supporting[]) SkillCode set, emits
              QuestionConceptsConfirmed_V1 (which opens the publish-gate
              for the first 200 items per ADR-0062 §5).

     UX rules:
       - Curator MUST select exactly one Primary; the catalog filters to
         leaves that match the item's Track when known.
       - Supporting concepts are optional, multi-select.
       - One-click "Accept extracted" path when extracted set is non-empty.
       - Disabled-state banner explains the gate ("required for first 200
         items") so the curator understands why publish is blocked.
============================================================================= -->
<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { $api } from '@/utils/api'

interface ConceptDto {
  skillCode: string
  role: 'Primary' | 'Supporting'
  confidence: number
  rationale: string
  tier: string
}

interface ConceptDtoSet {
  strategy: string
  concepts: ConceptDto[]
}

interface ConceptDtoConfirmed extends ConceptDtoSet {
  action: string
  confirmedBy: string
  confirmedAt: string
}

interface ConceptCatalogEntry {
  skillCode: string
  conceptId: string
  topic: string
  subtopic: string
  tracks: string[]
  bloomMin: number
  bloomMax: number
  // 2026-05-03: localized labels keyed by language code (en/he/ar).
  // Backend ships at least an English entry per leaf; missing langs
  // fall back to English on the SPA side via topicLabel/subtopicLabel
  // below. The English topic/subtopic keys above remain canonical for
  // event sourcing — these maps are presentational only.
  topicLabels?: Record<string, string>
  subtopicLabels?: Record<string, string>
}

interface ConceptReviewResponse {
  itemId: string
  extracted: ConceptDtoSet | null
  confirmed: ConceptDtoConfirmed | null
  catalog: ConceptCatalogEntry[]
}

interface Props {
  itemId: string
  /** Optional track hint ("3u" / "4u" / "5u") so the catalog picker can pre-filter. */
  trackHint?: string | null
}

interface Emit {
  (e: 'confirmed', payload: { primary: string; supporting: string[]; action: string }): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

// 2026-05-03: i18n integration for catalog labels. The catalog
// dropdown shows topic + subtopic in the curator's current language
// (en/he/ar) — falling back to English when a translation is
// missing, then to the raw English key as a last resort.
//
// Why we don't pull the SkillCode itself through i18n: the SkillCode
// (e.g. "math.calculus.derivative-rules") is a stable identifier
// that flows through events + storage + audit logs. Curators need to
// see and reason about it (it's what the canonicaliser rejects on
// typo), so it stays English in the title slot — the Hebrew/Arabic
// names go in the descriptive subtitle slot where they help curators
// who aren't native English speakers.
const { locale } = useI18n()

function topicLabel(entry: ConceptCatalogEntry): string {
  const map = entry.topicLabels
  if (!map) return entry.topic
  return map[locale.value] || map.en || entry.topic
}

function subtopicLabel(entry: ConceptCatalogEntry): string {
  const map = entry.subtopicLabels
  if (!map) return entry.subtopic
  return map[locale.value] || map.en || entry.subtopic
}

const loading = ref(false)
const saving = ref(false)
const err = ref<string | null>(null)

const data = ref<ConceptReviewResponse | null>(null)
const primaryCode = ref<string | null>(null)
const supportingCodes = ref<string[]>([])
const trackFilter = ref<string | null>(null)

// Confirmed → seed editor with the curator's last selection so re-opening
// the panel shows what was confirmed (not the extractor's stale set).
function seedFromConfirmed(c: ConceptDtoConfirmed) {
  const primary = c.concepts.find(x => x.role === 'Primary')
  primaryCode.value = primary?.skillCode ?? null
  supportingCodes.value = c.concepts.filter(x => x.role === 'Supporting').map(x => x.skillCode)
}

// Extracted (no confirm yet) → seed editor so "Confirm extracted" is
// one click. Curator can still override before saving.
function seedFromExtracted(e: ConceptDtoSet) {
  const primary = e.concepts.find(x => x.role === 'Primary')
  primaryCode.value = primary?.skillCode ?? null
  supportingCodes.value = e.concepts.filter(x => x.role === 'Supporting').map(x => x.skillCode)
}

async function load() {
  if (!props.itemId)
    return
  loading.value = true
  err.value = null
  try {
    const resp = await $api<ConceptReviewResponse>(
      `/admin/ingestion/items/${props.itemId}/concepts`,
      { method: 'GET' },
    )
    data.value = resp
    if (resp.confirmed) seedFromConfirmed(resp.confirmed)
    else if (resp.extracted) seedFromExtracted(resp.extracted)
    // Track hint from props or from the catalog when only one track is present
    trackFilter.value = props.trackHint ?? null
  }
  catch (e: any) {
    err.value = e?.data?.message ?? e?.message ?? 'Failed to load concept review.'
  }
  finally {
    loading.value = false
  }
}

const filteredCatalog = computed<ConceptCatalogEntry[]>(() => {
  if (!data.value) return []
  if (!trackFilter.value) return data.value.catalog
  // Track strings in the SPA are the form "math_5u" but the dropdown often
  // shows just "5u" — normalise both ways.
  const want = trackFilter.value.startsWith('math_')
    ? trackFilter.value
    : `math_${trackFilter.value}`
  return data.value.catalog.filter(e => e.tracks.includes(want))
})

const canConfirm = computed(() => !!primaryCode.value && !saving.value)

const action = computed(() => {
  // CuratorAction enum names match the backend; pick by diff.
  if (!data.value?.extracted)
    return 'FullyOverridden'
  const ext = data.value.extracted.concepts
  const extPrimary = ext.find(x => x.role === 'Primary')?.skillCode ?? null
  const extSupporting = ext.filter(x => x.role === 'Supporting').map(x => x.skillCode).sort()
  const supSorted = [...supportingCodes.value].sort()
  if (extPrimary === primaryCode.value
    && JSON.stringify(extSupporting) === JSON.stringify(supSorted))
    return 'AcceptedAsExtracted'
  if (extPrimary !== primaryCode.value)
    return 'PrimaryEdited'
  if (JSON.stringify(extSupporting) !== JSON.stringify(supSorted))
    return 'SupportingEdited'
  return 'FullyOverridden'
})

async function confirm() {
  if (!primaryCode.value)
    return
  saving.value = true
  err.value = null
  try {
    const body = {
      concepts: [
        { skillCode: primaryCode.value, role: 'Primary', confidence: 1.0, rationale: 'curator-confirmed' },
        ...supportingCodes.value.map(code => ({
          skillCode: code, role: 'Supporting', confidence: 1.0, rationale: 'curator-confirmed',
        })),
      ],
      action: action.value,
      trackHint: trackFilter.value,
    }
    await $api(`/admin/ingestion/items/${props.itemId}/concepts`, {
      method: 'POST',
      body,
    })
    emit('confirmed', {
      primary: primaryCode.value!,
      supporting: [...supportingCodes.value],
      action: action.value,
    })
    await load()
  }
  catch (e: any) {
    err.value = e?.data?.message ?? e?.message ?? 'Failed to confirm concepts.'
    // Re-throw so an external orchestrator (the fused-confirm CTA on
    // the parent ItemDetailPanel) can detect the failure and stop the
    // sequence at concepts instead of falsely claiming the gate
    // opened. Internal callers swallow via the err.value path above.
    throw e
  }
  finally {
    saving.value = false
  }
}

// Expose a minimal contract for the parent's fused "Confirm metadata +
// concepts" CTA (Gap C, 2026-05-03). The parent reads `canConfirm` /
// `alreadyConfirmed` to drive the stepper indicator and calls
// `confirmExternal()` to submit. Keeping the panel's own Confirm
// button intact — curators may want to confirm panels independently.
defineExpose({
  /** True once a primary concept is selected and we're not mid-save. */
  canConfirm,
  /** True once the curator (or anyone) has confirmed concepts at least once. */
  alreadyConfirmed: computed(() => !!data.value?.confirmed),
  /**
   * Submit the current selection on behalf of an external caller.
   * Throws on failure so the orchestrator can halt the chained
   * metadata→concepts sequence and surface the error inline.
   */
  confirmExternal: confirm,
  /** Force a re-fetch — used by the parent after a successful save. */
  reload: load,
})

onMounted(load)
watch(() => props.itemId, () => { load() })
</script>

<template>
  <VCard
    flat
    class="cena-concept-review pa-3"
    data-test="concept-review-panel"
  >
    <div class="d-flex align-center mb-2">
      <h6 class="text-h6 me-2">
        Concept review
      </h6>
      <VChip
        v-if="data?.confirmed"
        size="x-small"
        color="success"
        label
      >
        confirmed
      </VChip>
      <VChip
        v-else-if="data?.extracted && data.extracted.concepts.length > 0"
        size="x-small"
        color="warning"
        label
      >
        awaiting confirm
      </VChip>
      <VChip
        v-else
        size="x-small"
        color="default"
        label
      >
        no extraction
      </VChip>
      <VSpacer />
      <VBtn
        size="small"
        variant="text"
        icon="tabler-refresh"
        :loading="loading"
        aria-label="Reload"
        @click="load"
      />
    </div>

    <VAlert
      v-if="!data?.confirmed"
      density="compact"
      type="info"
      variant="tonal"
      class="mb-3"
    >
      ADR-0062 Phase 1 calibration corpus — the first 200 Bagrut items must be
      curator-confirmed here before they can be published. Confirming emits
      <code>QuestionConceptsConfirmed_V1</code>; the publish-gate then opens.
    </VAlert>

    <VAlert
      v-if="err"
      type="error"
      density="compact"
      variant="tonal"
      class="mb-3"
    >
      {{ err }}
    </VAlert>

    <VProgressLinear
      v-if="loading"
      indeterminate
      color="primary"
    />

    <div v-if="!loading && data">
      <VRow>
        <VCol cols="12" md="6">
          <VAutocomplete
            v-model="primaryCode"
            :items="filteredCatalog"
            item-title="skillCode"
            item-value="skillCode"
            label="Primary concept (required)"
            density="comfortable"
            clearable
            data-test="concept-primary-select"
          >
            <template #item="{ props: itemProps, item }">
              <VListItem v-bind="itemProps">
                <template #title>
                  <span class="font-weight-medium">{{ item.raw.skillCode }}</span>
                </template>
                <template #subtitle>
                  <!-- Localized topic + subtopic per current vue-i18n
                       locale (en/he/ar), falling back to English then to
                       the raw English key. SkillCode itself stays English
                       in the title slot — it's a stable identifier flowing
                       through events + audit logs, not a display name.
                       Math content (none here, but pattern matters): the
                       repo rule is bdi dir=ltr; this subtitle is
                       descriptive prose in the curator's locale. -->
                  <span class="text-caption text-medium-emphasis">
                    {{ topicLabel(item.raw) }} · {{ subtopicLabel(item.raw) }} · Bloom {{ item.raw.bloomMin }}–{{ item.raw.bloomMax }}
                  </span>
                </template>
              </VListItem>
            </template>
          </VAutocomplete>
        </VCol>
        <VCol cols="12" md="6">
          <VAutocomplete
            v-model="supportingCodes"
            :items="filteredCatalog"
            item-title="skillCode"
            item-value="skillCode"
            label="Supporting concepts (optional, multi-select)"
            density="comfortable"
            multiple
            chips
            closable-chips
            clearable
            data-test="concept-supporting-select"
          >
            <template #item="{ props: itemProps, item }">
              <VListItem v-bind="itemProps">
                <template #title>
                  <span class="font-weight-medium">{{ item.raw.skillCode }}</span>
                </template>
                <template #subtitle>
                  <span class="text-caption text-medium-emphasis">
                    {{ topicLabel(item.raw) }} · {{ subtopicLabel(item.raw) }}
                  </span>
                </template>
              </VListItem>
            </template>
          </VAutocomplete>
        </VCol>
      </VRow>

      <div
        v-if="data.extracted && data.extracted.concepts.length > 0"
        class="text-body-2 text-medium-emphasis mb-2"
      >
        <span class="me-1">Extractor (rules-tier) suggested:</span>
        <VChip
          v-for="c in data.extracted.concepts"
          :key="c.skillCode"
          size="x-small"
          :color="c.role === 'Primary' ? 'primary' : 'default'"
          variant="outlined"
          label
          class="me-1"
        >
          {{ c.role === 'Primary' ? '★ ' : '' }}{{ c.skillCode }}
        </VChip>
      </div>

      <div
        v-if="data.confirmed"
        class="text-body-2 text-medium-emphasis mb-2"
      >
        <VIcon
          icon="tabler-shield-check"
          size="14"
          color="success"
          class="me-1"
        />
        Confirmed by {{ data.confirmed.confirmedBy }} ({{ data.confirmed.action }}) — gate open for this item.
      </div>

      <div class="d-flex justify-end gap-2 mt-3">
        <VBtn
          color="primary"
          :loading="saving"
          :disabled="!canConfirm"
          data-test="concept-confirm-btn"
          @click="confirm"
        >
          <VIcon
            icon="tabler-check"
            start
          />
          {{ data.confirmed ? 'Re-confirm' : (action === 'AcceptedAsExtracted' ? 'Accept extracted' : 'Confirm') }}
        </VBtn>
      </div>
    </div>
  </VCard>
</template>

<style scoped>
.cena-concept-review {
  background: rgba(var(--v-theme-surface-variant), 0.4);
  border-radius: 0.4rem;
}
</style>
