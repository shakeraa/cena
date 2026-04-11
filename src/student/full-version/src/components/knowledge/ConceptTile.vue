<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ConceptSummary } from '@/api/types/common'

interface Props {
  concept: ConceptSummary
}

const props = defineProps<Props>()
const { t } = useI18n()

const statusColor = computed(() => {
  switch (props.concept.status) {
    case 'mastered': return 'success'
    case 'in-progress': return 'primary'
    case 'available': return 'info'
    case 'locked': return 'grey'
    default: return 'grey'
  }
})

const statusIcon = computed(() => {
  switch (props.concept.status) {
    case 'mastered': return 'tabler-circle-check-filled'
    case 'in-progress': return 'tabler-progress'
    case 'available': return 'tabler-circle-dashed'
    case 'locked': return 'tabler-lock'
    default: return 'tabler-help'
  }
})

const isLocked = computed(() => props.concept.status === 'locked')
</script>

<template>
  <VCard
    :variant="isLocked ? 'outlined' : 'outlined'"
    class="concept-tile pa-4 cursor-pointer"
    :class="{ 'concept-tile--locked': isLocked, 'concept-tile--mastered': concept.status === 'mastered' }"
    :to="isLocked ? undefined : `/knowledge-graph/concept/${concept.conceptId}`"
    :data-testid="`concept-${concept.conceptId}`"
    :data-status="concept.status"
  >
    <div class="d-flex align-center mb-3">
      <VAvatar
        :color="statusColor"
        size="40"
        class="me-3"
      >
        <VIcon
          :icon="statusIcon"
          size="22"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="text-subtitle-1 font-weight-medium text-truncate">
          {{ concept.name }}
        </div>
        <div class="text-caption text-medium-emphasis">
          {{ concept.topic || concept.subject }}
        </div>
      </div>
    </div>

    <div class="d-flex align-center flex-wrap ga-2">
      <VChip
        size="x-small"
        variant="tonal"
        :color="statusColor"
      >
        {{ t(`knowledgeGraph.status.${concept.status}`) }}
      </VChip>
      <VChip
        size="x-small"
        variant="outlined"
      >
        {{ t(`knowledgeGraph.difficulty.${concept.difficulty}`) }}
      </VChip>
    </div>
  </VCard>
</template>

<style scoped>
.concept-tile {
  transition: transform 0.15s ease-out;
}

.concept-tile:hover {
  transform: translateY(-2px);
}

.concept-tile--locked {
  opacity: 0.6;
  cursor: not-allowed !important;
}

.concept-tile--mastered {
  border-color: rgb(var(--v-theme-success));
}
</style>
