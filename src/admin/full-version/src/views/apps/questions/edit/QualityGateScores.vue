<script setup lang="ts">
interface Props {
  question: any
}

const props = defineProps<Props>()

const qualityGate = computed(() => props.question?.qualityGate)
const hasScores = computed(() => !!qualityGate.value)

const dimensions = computed(() => {
  if (!qualityGate.value) return []
  return [
    { label: 'Structural Validity', score: qualityGate.value.structuralValidity ?? 0, color: scoreColor(qualityGate.value.structuralValidity ?? 0) },
    { label: 'Stem Clarity', score: qualityGate.value.stemClarity ?? 0, color: scoreColor(qualityGate.value.stemClarity ?? 0) },
    { label: 'Distractor Quality', score: qualityGate.value.distractorQuality ?? 0, color: scoreColor(qualityGate.value.distractorQuality ?? 0) },
    { label: "Bloom's Alignment", score: qualityGate.value.bloomAlignment ?? 0, color: scoreColor(qualityGate.value.bloomAlignment ?? 0) },
    { label: 'Factual Accuracy', score: qualityGate.value.factualAccuracy ?? 0, color: scoreColor(qualityGate.value.factualAccuracy ?? 0) },
    { label: 'Language Quality', score: qualityGate.value.languageQuality ?? 0, color: scoreColor(qualityGate.value.languageQuality ?? 0) },
  ]
})

const compositeScore = computed(() => {
  if (!qualityGate.value) return 0
  return qualityGate.value.compositeScore ?? 0
})

const compositeColor = computed(() => scoreColor(compositeScore.value))

function scoreColor(score: number): string {
  if (score >= 80) return 'success'
  if (score >= 60) return 'warning'
  return 'error'
}
</script>

<template>
  <VCard>
    <VCardItem>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon
          icon="tabler-shield-check"
          size="20"
        />
        Quality Gate Scores
      </VCardTitle>
    </VCardItem>

    <VCardText>
      <div
        v-if="!hasScores"
        class="text-center py-6 text-disabled"
      >
        Quality gate not yet evaluated
      </div>

      <template v-else>
        <!-- Composite score -->
        <div class="d-flex align-center gap-6 mb-6">
          <VProgressCircular
            :model-value="compositeScore"
            :color="compositeColor"
            :size="80"
            :width="8"
          >
            <span class="text-h5 font-weight-bold">{{ Math.round(compositeScore) }}</span>
          </VProgressCircular>
          <div>
            <div class="text-body-1 font-weight-medium">
              Composite Score
            </div>
            <div class="text-body-2 text-disabled">
              Overall quality assessment
            </div>
          </div>
        </div>

        <!-- Dimension breakdowns -->
        <div class="d-flex flex-column gap-4">
          <div
            v-for="dim in dimensions"
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
              :color="dim.color"
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
      </template>
    </VCardText>
  </VCard>
</template>
