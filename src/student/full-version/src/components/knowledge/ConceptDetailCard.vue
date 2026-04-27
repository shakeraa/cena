<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ConceptDetailDto } from '@/api/types/common'

interface Props {
  concept: ConceptDetailDto
}

const props = defineProps<Props>()
const { t } = useI18n()

const masteryPercent = computed(() => {
  if (props.concept.currentMastery === null)
    return 0

  return Math.round(props.concept.currentMastery * 100)
})

const statusColor = computed(() => {
  switch (props.concept.status) {
    case 'mastered': return 'success'
    case 'in-progress': return 'primary'
    case 'available': return 'info'
    case 'locked': return 'grey'
    default: return 'grey'
  }
})
</script>

<template>
  <VCard
    class="concept-detail-card pa-6"
    variant="outlined"
    data-testid="concept-detail-card"
  >
    <div class="d-flex align-start justify-space-between mb-4">
      <div class="flex-grow-1 min-w-0 me-4">
        <div class="d-flex align-center ga-2 mb-2">
          <VChip
            size="small"
            variant="tonal"
            :color="statusColor"
            data-testid="concept-status-chip"
          >
            {{ t(`knowledgeGraph.status.${concept.status}`) }}
          </VChip>
          <VChip
            size="small"
            variant="outlined"
          >
            {{ t(`knowledgeGraph.difficulty.${concept.difficulty}`) }}
          </VChip>
          <VChip
            size="small"
            variant="outlined"
            prepend-icon="tabler-clock"
          >
            {{ t('knowledgeGraph.detail.estimatedMinutes', { minutes: concept.estimatedMinutes }, { plural: concept.estimatedMinutes }) }}
          </VChip>
        </div>
        <h1
          class="text-h4 mb-2"
          data-testid="concept-name"
        >
          {{ concept.name }}
        </h1>
        <p class="text-body-1 text-medium-emphasis">
          {{ concept.description }}
        </p>
      </div>
    </div>

    <div
      v-if="concept.currentMastery !== null"
      class="mb-4"
      data-testid="concept-mastery"
    >
      <div class="d-flex align-center justify-space-between mb-2">
        <div class="text-subtitle-2">
          {{ t('knowledgeGraph.detail.masteryLabel') }}
        </div>
        <div class="text-subtitle-2 font-weight-bold">
          {{ masteryPercent }}%
        </div>
      </div>
      <VProgressLinear
        :model-value="masteryPercent"
        :color="statusColor"
        height="10"
        rounded
      />
    </div>

    <VDivider class="my-4" />

    <div class="d-flex flex-column flex-sm-row ga-4">
      <div
        class="flex-grow-1"
        data-testid="concept-question-count"
      >
        <div class="text-caption text-medium-emphasis">
          {{ t('knowledgeGraph.detail.questionCount') }}
        </div>
        <div class="text-h6">
          {{ concept.questionCount }}
        </div>
      </div>
      <div
        class="flex-grow-1"
        data-testid="concept-subject"
      >
        <div class="text-caption text-medium-emphasis">
          {{ t('knowledgeGraph.detail.subject') }}
        </div>
        <div class="text-h6">
          {{ t(`session.setup.subjects.${concept.subject}`, concept.subject) }}
        </div>
      </div>
    </div>
  </VCard>
</template>
