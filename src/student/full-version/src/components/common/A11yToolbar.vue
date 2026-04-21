<script setup lang="ts">
// =============================================================================
// A11yToolbar — Israeli Equal-Rights for Persons with Disabilities Law
// (5758-1998) compliance. Offers text-size slider, high-contrast toggle,
// dyslexia-font toggle, reduced-motion toggle, and a reset button.
//
// Placement: slide-in sheet from the trailing edge (inline-end). Trigger is
// a persistent "handle" button anchored inline-end, center-vertical.
// The sheet is a true dialog (focus-trap, role=dialog, Esc closes).
// =============================================================================
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useA11yStore } from '@/stores/a11yStore'

const { t } = useI18n()
const a11y = useA11yStore()
const open = ref(false)

const sizeMarks = computed(() => [
  { value: 0, label: t('a11y.size.xsmall') },
  { value: 1, label: t('a11y.size.small') },
  { value: 2, label: t('a11y.size.medium') },
  { value: 3, label: t('a11y.size.large') },
  { value: 4, label: t('a11y.size.xlarge') },
  { value: 5, label: t('a11y.size.xxlarge') },
])

function closeOnEsc(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}
</script>

<template>
  <!-- Persistent handle — fixed inline-end center. Always reachable even
       when the sidebar is collapsed. Tab-accessible. -->
  <VBtn
    class="a11y-toolbar__handle"
    color="primary"
    icon
    :aria-label="t('a11y.openToolbar')"
    :aria-expanded="open"
    aria-haspopup="dialog"
    data-testid="a11y-toolbar-handle"
    @click="open = true"
    @keydown="closeOnEsc"
  >
    <VIcon
      icon="tabler-accessible"
      size="24"
    />
  </VBtn>

  <VNavigationDrawer
    v-model="open"
    location="end"
    temporary
    width="320"
    role="dialog"
    :aria-label="t('a11y.toolbarTitle')"
    data-testid="a11y-toolbar-drawer"
    @keydown="closeOnEsc"
  >
    <div class="pa-4">
      <div class="d-flex align-center justify-space-between mb-4">
        <h2 class="text-h6 mb-0">
          {{ t('a11y.toolbarTitle') }}
        </h2>
        <VBtn
          icon
          variant="text"
          size="small"
          :aria-label="t('a11y.closeToolbar')"
          @click="open = false"
        >
          <VIcon icon="tabler-x" />
        </VBtn>
      </div>

      <!-- Text size -->
      <div class="mb-6">
        <label
          id="a11y-size-label"
          class="text-subtitle-2 mb-2 d-block"
        >
          {{ t('a11y.textSize') }}
        </label>
        <div class="d-flex align-center ga-2 mb-2">
          <VBtn
            icon
            variant="outlined"
            size="small"
            :disabled="a11y.prefs.textSize <= 0"
            :aria-label="t('a11y.decreaseTextSize')"
            data-testid="a11y-text-smaller"
            @click="a11y.decreaseTextSize"
          >
            <VIcon icon="tabler-letter-a-small" />
          </VBtn>
          <VSlider
            :model-value="a11y.prefs.textSize"
            :min="0"
            :max="5"
            :step="1"
            hide-details
            aria-labelledby="a11y-size-label"
            data-testid="a11y-text-size-slider"
            class="flex-grow-1"
            @update:model-value="(v) => a11y.setTextSize(v as 0 | 1 | 2 | 3 | 4 | 5)"
          />
          <VBtn
            icon
            variant="outlined"
            size="small"
            :disabled="a11y.prefs.textSize >= 5"
            :aria-label="t('a11y.increaseTextSize')"
            data-testid="a11y-text-larger"
            @click="a11y.increaseTextSize"
          >
            <VIcon icon="tabler-letter-a" />
          </VBtn>
        </div>
        <p class="text-caption text-medium-emphasis mb-0">
          {{ sizeMarks[a11y.prefs.textSize].label }}
        </p>
      </div>

      <!-- High contrast -->
      <VSwitch
        :model-value="a11y.prefs.contrast === 'high'"
        :label="t('a11y.highContrast')"
        color="primary"
        hide-details
        class="mb-2"
        data-testid="a11y-contrast-toggle"
        @update:model-value="a11y.toggleContrast"
      />

      <!-- Reduced motion -->
      <VSwitch
        :model-value="a11y.prefs.motion === 'reduced'"
        :label="t('a11y.reducedMotion')"
        color="primary"
        hide-details
        class="mb-2"
        data-testid="a11y-motion-toggle"
        @update:model-value="a11y.toggleMotion"
      />

      <!-- Dyslexia font -->
      <VSwitch
        :model-value="a11y.prefs.dyslexiaFont === 'on'"
        :label="t('a11y.dyslexiaFont')"
        color="primary"
        hide-details
        class="mb-4"
        data-testid="a11y-dyslexia-toggle"
        @update:model-value="a11y.toggleDyslexiaFont"
      />

      <VDivider class="mb-4" />

      <VBtn
        variant="outlined"
        block
        :aria-label="t('a11y.resetDefaults')"
        data-testid="a11y-reset"
        @click="a11y.resetToDefaults"
      >
        <VIcon
          icon="tabler-rotate"
          start
        />
        {{ t('a11y.resetDefaults') }}
      </VBtn>

      <p class="text-caption text-medium-emphasis mt-4 mb-0">
        {{ t('a11y.legalNote') }}
      </p>
    </div>
  </VNavigationDrawer>
</template>

<style scoped>
.a11y-toolbar__handle {
  position: fixed;
  inset-inline-end: 1rem;
  inset-block-start: 50%;
  transform: translateY(-50%);
  z-index: 1000;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

@media (prefers-reduced-motion: reduce) {
  .a11y-toolbar__handle {
    transition: none;
  }
}
</style>
