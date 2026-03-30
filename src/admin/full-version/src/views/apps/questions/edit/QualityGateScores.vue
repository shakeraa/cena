<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  question: any
}

const props = defineProps<Props>()
const emit = defineEmits<{ refreshed: [] }>()

const isRunning = ref(false)
const runError = ref<string | null>(null)

const qualityGate = computed(() => props.question?.qualityGate)
const hasScores = computed(() => !!qualityGate.value)

const compositeScore = computed(() => qualityGate.value?.compositeScore ?? 0)
const compositeColor = computed(() => scoreColor(compositeScore.value))

const explanationDimensions = computed(() => {
  if (!qualityGate.value) return []
  return [
    { label: 'Factual Accuracy', score: qualityGate.value.factualAccuracy ?? 0 },
    { label: 'Linguistic Quality', score: qualityGate.value.languageQuality ?? 0 },
    { label: 'Pedagogical Value', score: qualityGate.value.pedagogicalQuality ?? 0 },
  ]
})

const allDimensions = computed(() => {
  if (!qualityGate.value) return []
  return [
    { label: 'Structural Validity', score: qualityGate.value.structuralValidity ?? 0 },
    { label: 'Stem Clarity', score: qualityGate.value.stemClarity ?? 0 },
    { label: 'Distractor Quality', score: qualityGate.value.distractorQuality ?? 0 },
    { label: "Bloom's Alignment", score: qualityGate.value.bloomAlignment ?? 0 },
    { label: 'Factual Accuracy', score: qualityGate.value.factualAccuracy ?? 0 },
    { label: 'Language Quality', score: qualityGate.value.languageQuality ?? 0 },
    { label: 'Pedagogical Quality', score: qualityGate.value.pedagogicalQuality ?? 0 },
    { label: 'Cultural Sensitivity', score: qualityGate.value.culturalSensitivity ?? 0 },
  ]
})

function scoreColor(score: number): string {
  if (score >= 80) return 'success'
  if (score >= 60) return 'warning'
  return 'error'
}

// Run quality check by re-submitting current question data (triggers re-evaluation in API)
const runQualityCheck = async () => {
  if (!props.question) return
  isRunning.value = true
  runError.value = null
  try {
    await $api(`/admin/questions/${props.question.id}`, {
      method: 'PUT',
      body: {
        stem: props.question.stem,
        difficulty: props.question.difficulty,
        options: (props.question.options ?? []).map((o: any) => ({
          id: o.id,
          text: o.text,
          isCorrect: o.isCorrect,
          distractorRationale: o.distractorRationale ?? '',
        })),
        conceptIds: props.question.conceptIds ?? [],
      },
    })
    emit('refreshed')
  }
  catch (err: any) {
    runError.value = err.data?.message ?? err.message ?? 'Quality check failed'
  }
  finally {
    isRunning.value = false
  }
}
</script>

<template>
  <VCard>
    <VCardItem>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon icon="tabler-shield-check" size="20" />
        Quality Gate Scores
      </VCardTitle>
      <template #append>
        <VBtn
          size="small"
          variant="tonal"
          color="primary"
          prepend-icon="tabler-refresh"
          :loading="isRunning"
          @click="runQualityCheck"
        >
          Run Quality Check
        </VBtn>
      </template>
    </VCardItem>

    <VCardText>
      <VAlert
        v-if="runError"
        type="error"
        variant="tonal"
        class="mb-4"
        closable
        @click:close="runError = null"
      >
        {{ runError }}
      </VAlert>

      <div v-if="!hasScores" class="text-center py-6 text-disabled">
        <VIcon icon="tabler-shield-off" size="40" class="mb-2 d-block mx-auto" />
        Quality gate not yet evaluated
      </div>

      <template v-else>
        <!-- Composite score + explanation tier scores -->
        <VRow class="mb-4">
          <VCol cols="12" sm="4" class="d-flex align-center gap-4">
            <VProgressCircular
              :model-value="compositeScore"
              :color="compositeColor"
              :size="72"
              :width="7"
            >
              <span class="text-h6 font-weight-bold">{{ Math.round(compositeScore) }}</span>
            </VProgressCircular>
            <div>
              <div class="text-body-1 font-weight-medium">Composite Score</div>
              <div class="text-caption text-disabled">/ 100</div>
              <VChip
                size="x-small"
                :color="compositeColor"
                label
                class="mt-1"
              >
                {{ qualityGate.gateDecision }}
              </VChip>
            </div>
          </VCol>

          <!-- Explanation-specific quality indicators -->
          <VCol cols="12" sm="8">
            <div class="text-caption text-medium-emphasis mb-2 font-weight-medium">
              Explanation Quality
            </div>
            <div class="d-flex flex-column gap-3">
              <div
                v-for="dim in explanationDimensions"
                :key="dim.label"
                class="d-flex align-center gap-3"
              >
                <span class="text-body-2 text-medium-emphasis" style="min-inline-size: 140px;">
                  {{ dim.label }}
                </span>
                <VProgressLinear
                  :model-value="dim.score"
                  :color="scoreColor(dim.score)"
                  height="8"
                  rounded
                  class="flex-grow-1"
                />
                <span
                  class="text-body-2 font-weight-medium"
                  style="min-inline-size: 32px; text-align: end;"
                >
                  {{ Math.round(dim.score) }}
                </span>
              </div>
            </div>
          </VCol>
        </VRow>

        <VDivider class="mb-4" />

        <!-- All dimension scores -->
        <div class="text-caption text-medium-emphasis mb-2 font-weight-medium">
          All Dimensions
        </div>
        <div class="d-flex flex-column gap-3">
          <div
            v-for="dim in allDimensions"
            :key="dim.label"
            class="d-flex align-center gap-3"
          >
            <span
              class="text-body-2 text-medium-emphasis"
              style="min-inline-size: 140px;"
            >
              {{ dim.label }}
            </span>
            <VProgressLinear
              :model-value="dim.score"
              :color="scoreColor(dim.score)"
              height="6"
              rounded
              class="flex-grow-1"
            />
            <span
              class="text-body-2 font-weight-medium"
              style="min-inline-size: 32px; text-align: end;"
            >
              {{ Math.round(dim.score) }}
            </span>
          </div>
        </div>

        <div
          v-if="qualityGate.violationCount > 0"
          class="mt-4"
        >
          <VChip size="small" color="error" prepend-icon="tabler-alert-circle">
            {{ qualityGate.violationCount }} violation{{ qualityGate.violationCount !== 1 ? 's' : '' }}
          </VChip>
        </div>
      </template>
    </VCardText>
  </VCard>
</template>
