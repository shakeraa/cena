<script setup lang="ts">
interface Props {
  conceptId: string
  name: string
  mastery: number
  status: 'mastered' | 'learning' | 'not-started'
}

const props = defineProps<Props>()

const progressColor = computed(() => {
  if (props.mastery >= 0.8) return 'success'
  if (props.mastery >= 0.4) return 'warning'
  if (props.mastery > 0) return 'error'
  return 'secondary'
})

const statusChipConfig = computed(() => {
  switch (props.status) {
    case 'mastered':
      return { color: 'success', label: 'Mastered', icon: 'tabler-circle-check' }
    case 'learning':
      return { color: 'warning', label: 'Learning', icon: 'tabler-book' }
    case 'not-started':
      return { color: 'secondary', label: 'Not Started', icon: 'tabler-clock' }
    default:
      return { color: 'secondary', label: props.status, icon: 'tabler-help' }
  }
})

const masteryPercent = computed(() => Math.round(props.mastery * 100))
</script>

<template>
  <VCard
    variant="outlined"
    class="concept-card"
  >
    <VCardText class="d-flex flex-column align-center text-center pa-4">
      <VProgressCircular
        :model-value="masteryPercent"
        :color="progressColor"
        :size="72"
        :width="6"
        class="mb-3"
      >
        <span class="text-body-1 font-weight-medium">{{ masteryPercent }}%</span>
      </VProgressCircular>

      <h6 class="text-subtitle-1 font-weight-medium mb-2">
        {{ props.name }}
      </h6>

      <VChip
        :color="statusChipConfig.color"
        :prepend-icon="statusChipConfig.icon"
        label
        size="small"
      >
        {{ statusChipConfig.label }}
      </VChip>
    </VCardText>
  </VCard>
</template>

<style lang="scss" scoped>
.concept-card {
  transition: border-color 0.15s ease;

  &:hover {
    border-color: rgb(var(--v-theme-primary));
  }
}
</style>
