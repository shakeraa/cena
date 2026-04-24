<!-- =============================================================================
     Cena Platform — Generate-Similar Dialog (RDY-058)

     Drives POST /api/admin/questions/{id}/generate-similar. One-click
     "make N variants of this question at difficulty X".

     Every candidate returned carries a CAS verdict (verified | failed |
     unverifiable); auto-rejected candidates are counted in
     DroppedForCasFailure + CasDropReasons. The dialog shows a compact
     summary + full drop-reason list so the curator can tell whether the
     generation run was healthy.
============================================================================= -->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { $api } from '@/utils/api'

interface Props {
  modelValue: boolean
  questionId: string | null
  sourceDifficulty?: number | null
  sourceSubject?: string | null
  sourceBloom?: number | null
}

const props = defineProps<Props>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'generated'): void
}>()

interface BatchGenerateResult {
  question: {
    stem: string
    topic?: string | null
    bloomsLevel: number
    difficulty: number
    explanation: string
  }
  passedQualityGate: boolean
  casOutcome?: string | null
  casFailureReason?: string | null
}
interface CasDropReason {
  questionStem: string
  engine: string
  reason: string
  latencyMs: number
}
interface BatchGenerateResponse {
  success: boolean
  results: BatchGenerateResult[]
  totalGenerated: number
  passedQualityGate: number
  needsReview: number
  autoRejected: number
  modelUsed: string
  error?: string | null
  droppedForCasFailure?: number
  casDropReasons?: CasDropReason[] | null
}

const count = ref(3)
const minDifficulty = ref(0.45)
const maxDifficulty = ref(0.75)
const language = ref<string>('')
const loading = ref(false)
const error = ref<string | null>(null)
const response = ref<BatchGenerateResponse | null>(null)

watch(() => props.modelValue, (open) => {
  if (!open) return
  count.value = 3
  error.value = null
  response.value = null

  const src = typeof props.sourceDifficulty === 'number' ? props.sourceDifficulty : 0.5
  minDifficulty.value = Math.max(0, +(src - 0.15).toFixed(2))
  maxDifficulty.value = Math.min(1, +(src + 0.15).toFixed(2))
  language.value = ''
})

const canSubmit = computed(() =>
  !!props.questionId && !loading.value && count.value >= 1 && count.value <= 20 &&
  minDifficulty.value <= maxDifficulty.value,
)

async function submit() {
  if (!canSubmit.value || !props.questionId) return
  loading.value = true
  error.value = null
  response.value = null
  try {
    response.value = await $api<BatchGenerateResponse>(
      `/admin/questions/${props.questionId}/generate-similar`,
      {
        method: 'POST',
        body: {
          count: count.value,
          minDifficulty: minDifficulty.value,
          maxDifficulty: maxDifficulty.value,
          language: language.value || null,
        },
      },
    )
    emit('generated')
  }
  catch (e: any) {
    error.value = e?.data?.message ?? e?.message ?? 'Generation failed'
  }
  finally {
    loading.value = false
  }
}

function close() {
  emit('update:modelValue', false)
}

function casVerdictColor(outcome?: string | null): 'success' | 'error' | 'warning' | 'default' {
  switch (outcome) {
    case 'Verified':
    case 'verified': return 'success'
    case 'Failed':
    case 'failed': return 'error'
    case 'Unverifiable':
    case 'unverifiable': return 'warning'
    default: return 'default'
  }
}
</script>

<template>
  <VDialog
    :model-value="props.modelValue"
    max-width="780"
    persistent
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
  >
    <VCard>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon icon="tabler-wand" />
        <span>Generate similar questions</span>
        <VSpacer />
        <VBtn icon="tabler-x" variant="text" size="small" @click="close" />
      </VCardTitle>

      <VCardText class="pt-3">
        <!-- Source summary -->
        <VAlert
          v-if="props.questionId"
          type="info"
          variant="tonal"
          density="compact"
          class="mb-4"
        >
          <div class="text-caption">
            Source: <code>{{ props.questionId }}</code>
            <span v-if="props.sourceSubject">&nbsp;· {{ props.sourceSubject }}</span>
            <span v-if="typeof props.sourceBloom === 'number'">&nbsp;· Bloom {{ props.sourceBloom }}</span>
            <span v-if="typeof props.sourceDifficulty === 'number'">
              &nbsp;· difficulty {{ (props.sourceDifficulty * 100).toFixed(0) }}%
            </span>
          </div>
          <div class="text-caption mt-1">
            Every variant is CAS-verified — drops are reported below.
          </div>
        </VAlert>

        <div class="mb-4">
          <label class="text-body-2 mb-1 d-block">Count (1-20)</label>
          <VSlider
            v-model="count"
            :min="1"
            :max="20"
            :step="1"
            thumb-label
            color="primary"
            density="compact"
          />
        </div>

        <div class="mb-4">
          <label class="text-body-2 mb-1 d-block">
            Difficulty band: {{ (minDifficulty * 100).toFixed(0) }}% – {{ (maxDifficulty * 100).toFixed(0) }}%
          </label>
          <VRangeSlider
            :model-value="[minDifficulty, maxDifficulty]"
            :min="0"
            :max="1"
            :step="0.05"
            color="primary"
            density="compact"
            thumb-label
            @update:model-value="(v: number[]) => { minDifficulty = v[0]; maxDifficulty = v[1] }"
          />
        </div>

        <div class="mb-4">
          <label class="text-body-2 mb-1 d-block">Language (override)</label>
          <VSelect
            v-model="language"
            :items="[
              { title: 'Inherit from source', value: '' },
              { title: 'Hebrew', value: 'he' },
              { title: 'English', value: 'en' },
              { title: 'Arabic', value: 'ar' },
            ]"
            density="compact"
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

        <div v-if="loading" class="text-center py-6">
          <VProgressCircular indeterminate color="primary" />
          <div class="text-caption mt-2">
            Generating + CAS-verifying candidates…
          </div>
        </div>

        <!-- Result summary -->
        <div v-if="response && !loading">
          <VDivider class="mb-3" />
          <div class="d-flex flex-wrap gap-1 mb-3">
            <VChip color="primary" size="small" variant="tonal">
              {{ response.totalGenerated }} generated
            </VChip>
            <VChip color="success" size="small" variant="tonal">
              {{ response.passedQualityGate }} passed gate
            </VChip>
            <VChip v-if="response.needsReview > 0" color="warning" size="small" variant="tonal">
              {{ response.needsReview }} needs review
            </VChip>
            <VChip v-if="response.autoRejected > 0" color="error" size="small" variant="tonal">
              {{ response.autoRejected }} rejected
            </VChip>
            <VChip v-if="response.droppedForCasFailure && response.droppedForCasFailure > 0" color="error" size="small" variant="tonal">
              {{ response.droppedForCasFailure }} CAS-dropped
            </VChip>
          </div>

          <!-- CAS drop details -->
          <div v-if="response.casDropReasons && response.casDropReasons.length > 0" class="mb-3">
            <div class="text-caption text-medium-emphasis mb-1">
              CAS drops
            </div>
            <VList density="compact">
              <VListItem
                v-for="(r, i) in response.casDropReasons"
                :key="i"
                :subtitle="`${r.engine} · ${r.reason} (${Math.round(r.latencyMs)}ms)`"
              >
                <template #title>
                  <bdi dir="ltr" class="text-caption" style="font-family: monospace;">
                    {{ r.questionStem }}
                  </bdi>
                </template>
              </VListItem>
            </VList>
          </div>

          <!-- Candidates -->
          <div v-if="response.results.length > 0">
            <div class="text-caption text-medium-emphasis mb-1">
              Candidates
            </div>
            <VList>
              <VListItem
                v-for="(r, i) in response.results"
                :key="i"
                :title="r.question.stem"
                :subtitle="`Bloom ${r.question.bloomsLevel} · difficulty ${(r.question.difficulty * 100).toFixed(0)}%`"
              >
                <template #append>
                  <div class="d-flex align-center gap-1">
                    <VChip
                      v-if="r.casOutcome"
                      :color="casVerdictColor(r.casOutcome)"
                      size="x-small"
                    >
                      {{ r.casOutcome }}
                    </VChip>
                    <VChip
                      :color="r.passedQualityGate ? 'success' : 'warning'"
                      size="x-small"
                    >
                      {{ r.passedQualityGate ? 'gate ok' : 'review' }}
                    </VChip>
                  </div>
                </template>
              </VListItem>
            </VList>
          </div>
        </div>
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="close">
          {{ response ? 'Close' : 'Cancel' }}
        </VBtn>
        <VBtn
          v-if="!response"
          color="primary"
          :disabled="!canSubmit"
          :loading="loading"
          @click="submit"
        >
          Generate
        </VBtn>
        <VBtn
          v-else
          color="primary"
          variant="text"
          @click="response = null"
        >
          Generate more
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
