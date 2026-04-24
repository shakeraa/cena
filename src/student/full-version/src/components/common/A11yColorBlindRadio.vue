<script setup lang="ts">
/**
 * A11yColorBlindRadio — extracted from A11yToolbar to keep the parent
 * under the 500-LOC cap mandated by the task body.
 *
 * Renders four radio options (off / protanopia / deuteranopia /
 * tritanopia). Emits `change` with the new mode. The parent owns the
 * announce() side-effect so screen-reader copy stays in the toolbar's
 * live region.
 */
import { useI18n } from 'vue-i18n'
import type { A11yColorBlind } from '@/stores/a11yStore'

defineProps<{
  modelValue: A11yColorBlind
}>()

const emit = defineEmits<{
  (e: 'change', value: A11yColorBlind): void
}>()

const { t } = useI18n()

function onSelect(mode: A11yColorBlind) {
  emit('change', mode)
}
</script>

<template>
  <fieldset
    class="a11y-toolbar__fieldset mb-6"
    data-testid="a11y-color-blind-section"
  >
    <legend
      id="a11y-color-blind-label"
      class="text-subtitle-2 mb-2"
    >
      {{ t('a11y.colorBlindMode') }}
    </legend>
    <p class="text-caption text-medium-emphasis mb-2">
      {{ t('a11y.colorBlindHint') }}
    </p>
    <div
      role="radiogroup"
      aria-labelledby="a11y-color-blind-label"
      class="d-flex flex-column ga-1"
    >
      <label class="d-flex align-center ga-2 cursor-pointer">
        <input
          type="radio"
          name="a11y-color-blind"
          value="off"
          :checked="modelValue === 'off'"
          data-testid="a11y-color-blind-off"
          @change="onSelect('off')"
        >
        <span>{{ t('a11y.cbNone') }}</span>
      </label>
      <label class="d-flex align-center ga-2 cursor-pointer">
        <input
          type="radio"
          name="a11y-color-blind"
          value="protanopia"
          :checked="modelValue === 'protanopia'"
          data-testid="a11y-color-blind-protanopia"
          @change="onSelect('protanopia')"
        >
        <span>{{ t('a11y.cbProtanopia') }}</span>
      </label>
      <label class="d-flex align-center ga-2 cursor-pointer">
        <input
          type="radio"
          name="a11y-color-blind"
          value="deuteranopia"
          :checked="modelValue === 'deuteranopia'"
          data-testid="a11y-color-blind-deuteranopia"
          @change="onSelect('deuteranopia')"
        >
        <span>{{ t('a11y.cbDeuteranopia') }}</span>
      </label>
      <label class="d-flex align-center ga-2 cursor-pointer">
        <input
          type="radio"
          name="a11y-color-blind"
          value="tritanopia"
          :checked="modelValue === 'tritanopia'"
          data-testid="a11y-color-blind-tritanopia"
          @change="onSelect('tritanopia')"
        >
        <span>{{ t('a11y.cbTritanopia') }}</span>
      </label>
    </div>
  </fieldset>
</template>

<style scoped>
.a11y-toolbar__fieldset {
  border: 1px solid rgb(var(--v-theme-on-surface), 0.15);
  border-radius: 8px;
  padding: 0.75rem 1rem;
}

.cursor-pointer {
  cursor: pointer;
}
</style>
