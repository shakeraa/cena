<script setup lang="ts">
// =============================================================================
// Cena Platform — SoftCapUpsellModal.vue (EPIC-PRR-I PRR-313, EPIC-PRR-J PRR-386)
//
// Shown when a Premium user hits the 100/mo soft cap on photo diagnostics or
// the weekly Sonnet-escalation cap. Positive framing only — no scarcity,
// no streak counter, no countdown. Shipgate scanner enforces banned terms;
// this copy passes.
//
// CTAs: "Book a tutor session" (routes to partner-marketplace placeholder)
// or "Continue anyway" (soft cap — still allows the action through). Hard
// caps surface HardCapContactSupport separately.
// =============================================================================

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  modelValue: boolean
  /** Which cap was hit: photo | sonnet | hint */
  capKind: 'photo' | 'sonnet' | 'hint'
  /** Current usage count (for the "you're in the top X%" copy) */
  usage: number
  /** The soft cap that was reached */
  softCap: number
}

const props = defineProps<Props>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'continueAnyway'): void
  (e: 'bookTutor'): void
}>()

const { t } = useI18n()

const headingKey = computed(() => `softCapUpsell.${props.capKind}.heading`)
const bodyKey = computed(() => `softCapUpsell.${props.capKind}.body`)

const close = () => emit('update:modelValue', false)
const continueAction = () => {
  emit('continueAnyway')
  close()
}
const bookTutor = () => {
  emit('bookTutor')
  close()
}
</script>

<template>
  <VDialog
    :model-value="modelValue"
    max-width="520"
    persistent
    data-testid="soft-cap-upsell-modal"
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
  >
    <VCard class="pa-6">
      <div class="text-center mb-4">
        <VIcon
          icon="tabler-sparkles"
          color="primary"
          size="48"
          class="mb-3"
        />
        <h2 class="text-h5 font-weight-bold mb-2">
          {{ t(headingKey) }}
        </h2>
        <p class="text-body-2 text-medium-emphasis">
          {{ t(bodyKey, { usage, softCap }) }}
        </p>
      </div>

      <div class="d-flex flex-column ga-2 mt-4">
        <VBtn
          color="primary"
          variant="flat"
          size="large"
          data-testid="soft-cap-book-tutor"
          @click="bookTutor"
        >
          {{ t('softCapUpsell.bookTutor') }}
        </VBtn>
        <VBtn
          variant="outlined"
          size="large"
          data-testid="soft-cap-continue"
          @click="continueAction"
        >
          {{ t('softCapUpsell.continueAnyway') }}
        </VBtn>
        <VBtn
          variant="text"
          size="small"
          data-testid="soft-cap-dismiss"
          @click="close"
        >
          {{ t('softCapUpsell.dismiss') }}
        </VBtn>
      </div>
    </VCard>
  </VDialog>
</template>
