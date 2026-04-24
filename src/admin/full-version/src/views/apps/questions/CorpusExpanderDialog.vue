<!-- =============================================================================
     Cena Platform — Corpus Expander Dialog (RDY-059)

     Two-phase UX:
       1. Dry-run planner — POST /api/admin/questions/expand-corpus with
          dryRun=true; renders a plan + planned cost.
       2. Confirm → wet run (same request with dryRun=false); renders
          per-source outcomes (attempted, passed CAS, dropped).

     Operator safeguards:
       - Dry-run toggle defaults ON.
       - MaxTotalCandidates hard cap.
       - StopAfterLeafFull so full leaves aren't over-saturated.
       - SuperAdmin-only (enforced server-side).
============================================================================= -->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { $api } from '@/utils/api'

interface Props {
  modelValue: boolean
}
const props = defineProps<Props>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'expanded'): void
}>()

interface DifficultyBandConfig { min: number; max: number; count: number }
interface PerSourcePlan {
  sourceId: string
  leafId?: string | null
  currentLeafCount: number
  wouldGenerate: number
  skipReason?: string | null
}
interface PerSourceOutcome {
  sourceId: string
  attempted: number
  passedCas: number
  dropped: number
  error?: string | null
}
interface CorpusExpansionResponse {
  runId: string
  dryRun: boolean
  selector: string
  plan: PerSourcePlan[]
  outcomes?: PerSourceOutcome[] | null
  totalPlannedCandidates: number
  totalAttempted: number
  totalPassedCas: number
  totalDropped: number
  startedBy: string
  startedAt: string
  completedAt: string
}

const selector = ref<string>('seed')
const customConcept = ref<string>('')
const bands = ref<DifficultyBandConfig[]>([
  { min: 0.30, max: 0.50, count: 2 },
  { min: 0.55, max: 0.75, count: 2 },
  { min: 0.80, max: 0.95, count: 1 },
])
const stopAfterLeafFull = ref(5)
const maxTotalCandidates = ref(50)
const language = ref<string>('')

const planning = ref(false)
const running = ref(false)
const error = ref<string | null>(null)
const planResult = ref<CorpusExpansionResponse | null>(null)
const runResult  = ref<CorpusExpansionResponse | null>(null)

watch(() => props.modelValue, (open) => {
  if (!open) return
  planResult.value = null
  runResult.value = null
  error.value = null
})

const resolvedSelector = computed(() =>
  selector.value === 'concept' && customConcept.value.trim()
    ? `concept:${customConcept.value.trim().toUpperCase()}`
    : selector.value,
)

const canPlan = computed(() =>
  !planning.value
  && !running.value
  && bands.value.length > 0
  && bands.value.every(b => b.min >= 0 && b.max <= 1 && b.min <= b.max && b.count >= 1 && b.count <= 20)
  && maxTotalCandidates.value >= 1
  && (selector.value !== 'concept' || customConcept.value.trim().length > 0),
)

async function runDry() {
  planning.value = true
  error.value = null
  runResult.value = null
  try {
    planResult.value = await $api<CorpusExpansionResponse>('/admin/questions/expand-corpus', {
      method: 'POST',
      body: {
        sourceSelector: resolvedSelector.value,
        difficultyBands: bands.value,
        stopAfterLeafFull: stopAfterLeafFull.value,
        maxTotalCandidates: maxTotalCandidates.value,
        language: language.value || null,
        dryRun: true,
      },
    })
  }
  catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Plan failed'
  }
  finally {
    planning.value = false
  }
}

async function runWet() {
  running.value = true
  error.value = null
  try {
    runResult.value = await $api<CorpusExpansionResponse>('/admin/questions/expand-corpus', {
      method: 'POST',
      body: {
        sourceSelector: resolvedSelector.value,
        difficultyBands: bands.value,
        stopAfterLeafFull: stopAfterLeafFull.value,
        maxTotalCandidates: maxTotalCandidates.value,
        language: language.value || null,
        dryRun: false,
      },
    })
    emit('expanded')
  }
  catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Run failed'
  }
  finally {
    running.value = false
  }
}

function addBand() {
  bands.value.push({ min: 0.40, max: 0.60, count: 2 })
}

function removeBand(i: number) {
  bands.value.splice(i, 1)
}

function close() {
  emit('update:modelValue', false)
}
</script>

<template>
  <VDialog
    :model-value="props.modelValue"
    max-width="820"
    persistent
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
  >
    <VCard>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon icon="tabler-bolt" />
        <span>Populate question bank</span>
        <VSpacer />
        <VBtn icon="tabler-x" variant="text" size="small" @click="close" />
      </VCardTitle>

      <VCardText class="pt-3">
        <VAlert type="info" variant="tonal" density="compact" class="mb-4">
          Bulk expand the bank from a source set. Every candidate is CAS-verified
          (ADR-0002); dropped candidates are reported per-source.
        </VAlert>

        <!-- Source selector -->
        <div class="d-flex gap-3 mb-3">
          <VSelect
            v-model="selector"
            :items="[
              { title: 'Seed questions (CreatedBy=System or SourceType=seed)', value: 'seed' },
              { title: 'Bagrut-referenced questions', value: 'bagrut' },
              { title: 'All published', value: 'all' },
              { title: 'Concept id…', value: 'concept' },
            ]"
            label="Source selector"
            density="compact"
          />
          <VTextField
            v-if="selector === 'concept'"
            v-model="customConcept"
            label="Concept id (e.g. CAL-003)"
            density="compact"
            placeholder="CAL-003"
          />
        </div>

        <!-- Difficulty bands -->
        <div class="text-subtitle-2 mb-1">
          Difficulty bands
        </div>
        <div
          v-for="(b, i) in bands"
          :key="i"
          class="d-flex align-center gap-2 mb-2"
        >
          <VTextField
            v-model.number="b.min"
            label="min"
            type="number"
            density="compact"
            style="max-width: 100px;"
            step="0.05"
            min="0"
            max="1"
          />
          <VTextField
            v-model.number="b.max"
            label="max"
            type="number"
            density="compact"
            style="max-width: 100px;"
            step="0.05"
            min="0"
            max="1"
          />
          <VTextField
            v-model.number="b.count"
            label="count"
            type="number"
            density="compact"
            style="max-width: 100px;"
            min="1"
            max="20"
          />
          <VBtn
            icon="tabler-trash"
            variant="text"
            size="small"
            :disabled="bands.length <= 1"
            @click="removeBand(i)"
          />
        </div>
        <VBtn
          size="small"
          variant="text"
          prepend-icon="tabler-plus"
          class="mb-3"
          @click="addBand"
        >
          Add band
        </VBtn>

        <!-- Guards -->
        <div class="d-flex gap-3 mb-3">
          <VTextField
            v-model.number="stopAfterLeafFull"
            label="Stop after leaf has ≥"
            type="number"
            density="compact"
            min="1"
            style="max-width: 200px;"
          />
          <VTextField
            v-model.number="maxTotalCandidates"
            label="Max total candidates"
            type="number"
            density="compact"
            min="1"
            style="max-width: 200px;"
          />
          <VSelect
            v-model="language"
            label="Language"
            :items="[
              { title: 'Inherit from source', value: '' },
              { title: 'Hebrew', value: 'he' },
              { title: 'English', value: 'en' },
              { title: 'Arabic', value: 'ar' },
            ]"
            density="compact"
            style="max-width: 200px;"
          />
        </div>

        <VAlert
          v-if="error"
          type="error"
          variant="tonal"
          density="compact"
          class="mb-3"
        >
          {{ error }}
        </VAlert>

        <!-- Plan -->
        <template v-if="planResult">
          <VDivider class="mb-3" />
          <div class="text-subtitle-2 mb-2">
            Plan (run: <code>{{ planResult.runId }}</code>)
          </div>
          <div class="d-flex flex-wrap gap-1 mb-3">
            <VChip color="primary" size="small" variant="tonal">
              {{ planResult.plan.length }} sources
            </VChip>
            <VChip color="success" size="small" variant="tonal">
              {{ planResult.totalPlannedCandidates }} would-generate
            </VChip>
            <VChip
              v-if="planResult.plan.some(p => p.skipReason)"
              color="warning"
              size="small"
              variant="tonal"
            >
              {{ planResult.plan.filter(p => p.skipReason).length }} skipped
            </VChip>
          </div>
          <VList density="compact" max-height="240" class="overflow-auto mb-3">
            <VListItem
              v-for="p in planResult.plan"
              :key="p.sourceId"
              :title="p.sourceId"
              :subtitle="p.leafId ? `leaf ${p.leafId} · current ${p.currentLeafCount}` : 'no leaf'"
            >
              <template #append>
                <VChip
                  v-if="p.skipReason"
                  size="x-small"
                  color="warning"
                >
                  {{ p.skipReason }}
                </VChip>
                <VChip
                  v-else
                  size="x-small"
                  color="primary"
                >
                  +{{ p.wouldGenerate }}
                </VChip>
              </template>
            </VListItem>
          </VList>
        </template>

        <!-- Run outcomes -->
        <template v-if="runResult">
          <VDivider class="mb-3" />
          <div class="text-subtitle-2 mb-2">
            Run result (<code>{{ runResult.runId }}</code>)
          </div>
          <div class="d-flex flex-wrap gap-1 mb-3">
            <VChip color="primary" size="small" variant="tonal">
              {{ runResult.totalAttempted }} attempted
            </VChip>
            <VChip color="success" size="small" variant="tonal">
              {{ runResult.totalPassedCas }} passed CAS
            </VChip>
            <VChip v-if="runResult.totalDropped > 0" color="error" size="small" variant="tonal">
              {{ runResult.totalDropped }} dropped
            </VChip>
          </div>
          <VList density="compact" max-height="240" class="overflow-auto">
            <VListItem
              v-for="o in (runResult.outcomes || [])"
              :key="o.sourceId"
              :title="o.sourceId"
              :subtitle="o.error ?? `attempted ${o.attempted} · passed ${o.passedCas} · dropped ${o.dropped}`"
            >
              <template #append>
                <VChip
                  v-if="o.error"
                  size="x-small"
                  color="error"
                >
                  error
                </VChip>
                <VChip
                  v-else-if="o.dropped > 0"
                  size="x-small"
                  color="warning"
                >
                  +{{ o.passedCas }} / −{{ o.dropped }}
                </VChip>
                <VChip
                  v-else
                  size="x-small"
                  color="success"
                >
                  +{{ o.passedCas }}
                </VChip>
              </template>
            </VListItem>
          </VList>
        </template>
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="close">
          Close
        </VBtn>
        <VBtn
          v-if="!planResult"
          color="primary"
          :disabled="!canPlan"
          :loading="planning"
          @click="runDry"
        >
          Plan (dry-run)
        </VBtn>
        <VBtn
          v-else-if="!runResult"
          color="error"
          :loading="running"
          @click="runWet"
        >
          Confirm + run ({{ planResult.totalPlannedCandidates }} candidates)
        </VBtn>
        <VBtn
          v-else
          variant="text"
          @click="planResult = null; runResult = null"
        >
          Start over
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
