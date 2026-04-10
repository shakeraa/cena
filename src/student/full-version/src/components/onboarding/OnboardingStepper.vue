<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  currentStep: number
  totalSteps: number
}

const props = defineProps<Props>()

const percent = computed(() => Math.round(((props.currentStep + 1) / props.totalSteps) * 100))
</script>

<template>
  <div
    class="onboarding-stepper"
    data-testid="onboarding-stepper"
  >
    <div class="d-flex align-center justify-space-between mb-2">
      <span class="text-caption text-medium-emphasis">
        Step {{ currentStep + 1 }} of {{ totalSteps }}
      </span>
      <span class="text-caption text-medium-emphasis">
        {{ percent }}%
      </span>
    </div>
    <VProgressLinear
      :model-value="percent"
      color="primary"
      rounded
      height="6"
      :aria-label="`Onboarding progress: step ${currentStep + 1} of ${totalSteps}`"
    />
  </div>
</template>

<style scoped>
.onboarding-stepper {
  max-inline-size: 480px;
  margin-inline: auto;
  margin-block-end: 2rem;
}
</style>
