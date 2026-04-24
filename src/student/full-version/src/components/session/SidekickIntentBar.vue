<script setup lang="ts">
// prr-207 — SidekickIntentBar.vue
// Chip row that pre-seeds the Sidekick's intent. Four chips:
//   - explain-question / explain-step / explain-concept / free-form
// explain-step is disabled during the 15s productive-failure debounce
// per useSidekick.noteWrongStep(). aria-label on each chip; aria-disabled
// reflects the debounce state.

import { useI18n } from 'vue-i18n'
import type { SidekickIntent } from '@/api/types/common'

interface Props {
  explainStepEnabled: boolean
  active?: SidekickIntent | null
}

const props = withDefaults(defineProps<Props>(), {
  active: null,
})

const emit = defineEmits<{
  (e: 'select', intent: SidekickIntent): void
}>()

const { t } = useI18n()

interface ChipDef {
  id: SidekickIntent
  labelKey: string
  icon: string
}

const chips: ChipDef[] = [
  { id: 'explain_question', labelKey: 'sidekick.intent.explainQuestion', icon: 'tabler-help' },
  { id: 'explain_step', labelKey: 'sidekick.intent.explainStep', icon: 'tabler-stairs' },
  { id: 'explain_concept', labelKey: 'sidekick.intent.explainConcept', icon: 'tabler-book' },
  { id: 'free_form', labelKey: 'sidekick.intent.freeForm', icon: 'tabler-message' },
]

function isDisabled(intent: SidekickIntent): boolean {
  if (intent === 'explain_step')
    return !props.explainStepEnabled

  return false
}

function handleSelect(intent: SidekickIntent) {
  if (isDisabled(intent))
    return
  emit('select', intent)
}
</script>

<template>
  <div
    class="sidekick-intent-bar"
    role="toolbar"
    :aria-label="t('sidekick.intent.toolbar')"
    data-testid="sidekick-intent-bar"
  >
    <VChip
      v-for="chip in chips"
      :key="chip.id"
      :prepend-icon="chip.icon"
      :variant="active === chip.id ? 'flat' : 'tonal'"
      :color="active === chip.id ? 'primary' : undefined"
      class="sidekick-intent-bar__chip"
      :data-testid="`sidekick-intent-${chip.id}`"
      :aria-label="t(chip.labelKey)"
      :aria-disabled="isDisabled(chip.id) ? 'true' : 'false'"
      :disabled="isDisabled(chip.id)"
      tabindex="0"
      @click="handleSelect(chip.id)"
      @keydown.enter.prevent="handleSelect(chip.id)"
      @keydown.space.prevent="handleSelect(chip.id)"
    >
      {{ t(chip.labelKey) }}
    </VChip>
  </div>
</template>

<style scoped>
.sidekick-intent-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  padding-block: 0.5rem;
}

.sidekick-intent-bar__chip:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}
</style>
