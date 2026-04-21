<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  currentStep: number
  totalSteps: number
}

const props = defineProps<Props>()
const { t } = useI18n()

const percent = computed(() => Math.round(((props.currentStep + 1) / props.totalSteps) * 100))
const stepLabel = computed(() =>
  t('onboarding.stepCounter', { current: props.currentStep + 1, total: props.totalSteps }))
</script>

<template>
  <div
    class="onboarding-stepper"
    data-testid="onboarding-stepper"
  >
    <div class="d-flex align-center justify-space-between mb-2">
      <span class="text-caption text-medium-emphasis">
        {{ stepLabel }}
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
      :aria-label="stepLabel"
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
