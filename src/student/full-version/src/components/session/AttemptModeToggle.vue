<script setup lang="ts">
// =============================================================================
// Cena Platform — AttemptModeToggle.vue (EPIC-PRR-H PRR-260)
//
// Student-facing toggle between "visible" (default) and "hidden_reveal"
// (opt-in). Appears in the session settings drawer. Not a default feature —
// students discover it when they choose to.
//
// Copy is deliberately plain; shipgate-compliant — no pressure language, no
// "try harder", no "challenge yourself" streak-framing. The button simply
// names the choice.
// =============================================================================

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSessionHideRevealStore } from '@/stores/sessionHideRevealStore'

const { t } = useI18n()
const store = useSessionHideRevealStore()

const enabled = computed({
  get: () => store.attemptMode === 'hidden_reveal',
  set: (v: boolean) => store.setAttemptMode(v ? 'hidden_reveal' : 'visible'),
})
</script>

<template>
  <div
    class="attempt-mode-toggle d-flex align-start ga-3 pa-3"
    data-testid="attempt-mode-toggle"
  >
    <VIcon
      icon="tabler-eye-off"
      color="medium-emphasis"
      size="24"
      class="mt-1"
    />
    <div class="flex-grow-1">
      <label class="text-body-1 font-weight-medium d-block mb-1">
        {{ t('attemptModeToggle.label') }}
      </label>
      <p class="text-caption text-medium-emphasis mb-0">
        {{ t('attemptModeToggle.description') }}
      </p>
    </div>
    <VSwitch
      v-model="enabled"
      color="primary"
      density="comfortable"
      hide-details
      data-testid="attempt-mode-toggle-switch"
    />
  </div>
</template>
