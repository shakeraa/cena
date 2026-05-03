<script setup lang="ts">
// =============================================================================
// Cena Platform — McOptionsGate.vue (EPIC-PRR-H PRR-260)
//
// Wraps the multi-choice options slot. When the session's attempt mode is
// `hidden_reveal` AND the student hasn't yet revealed THIS question, it
// renders a placeholder + reveal button in the options' bounding box
// (no layout shift on reveal — WCAG 4.1.3).
//
// Accessibility pattern (persona-a11y approved):
//   - Placeholder + `aria-live="polite"` on the options container.
//   - DOM-present pattern (not v-if'd off) — SR virtual cursor stays intact.
//   - Reveal announcement fires once, then aria-live goes silent.
//
// Accepts `forceVisible` for items where the options ARE the question
// (e.g. "choose which graph is correct"). Shipgate-compliant: no scarcity,
// no countdown, no timer. The button copy is plain "Show options".
// =============================================================================

import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSessionHideRevealStore } from '@/stores/sessionHideRevealStore'

interface Props {
  /** Stable per-question id.  */
  questionId: string
  /** Author flag: for items whose options ARE the question, skip the gate. */
  forceVisible?: boolean
}

const props = defineProps<Props>()
const { t } = useI18n()
const store = useSessionHideRevealStore()

const shouldReveal = computed(() =>
  store.shouldRevealOptions(props.questionId, !!props.forceVisible),
)

const justRevealed = ref(false)

const clickReveal = () => {
  store.markRevealed(props.questionId)
  justRevealed.value = true
}

// Reset the "just revealed" announcement flag when the question changes
// so the aria-live announcement doesn't keep firing.
watch(
  () => props.questionId,
  () => {
    justRevealed.value = false
  },
)
</script>

<template>
  <div
    class="mc-options-gate"
    :data-testid="`mc-options-gate-${questionId}`"
  >
    <!-- Revealed state (default visible + post-click): render the default slot as-is. -->
    <div
      v-if="shouldReveal"
      role="group"
      aria-live="polite"
      :aria-label="t('mcOptionsGate.optionsContainer')"
      :data-testid="`mc-options-gate-options-${questionId}`"
    >
      <span v-if="justRevealed" class="sr-only">
        {{ t('mcOptionsGate.revealedAnnouncement') }}
      </span>
      <slot />
    </div>

    <!-- Hidden placeholder: clicking reveals. -->
    <div
      v-else
      class="mc-options-gate__placeholder"
      :data-testid="`mc-options-gate-placeholder-${questionId}`"
    >
      <VCard
        variant="outlined"
        class="pa-4 d-flex flex-column align-center"
      >
        <VIcon
          icon="tabler-eye-off"
          color="medium-emphasis"
          size="32"
          class="mb-2"
        />
        <p class="text-body-2 text-medium-emphasis mb-3 text-center">
          {{ t('mcOptionsGate.placeholder') }}
        </p>
        <VBtn
          color="primary"
          variant="flat"
          :data-testid="`mc-options-gate-reveal-${questionId}`"
          @click="clickReveal"
        >
          <VIcon icon="tabler-eye" class="me-2" />
          {{ t('mcOptionsGate.reveal') }}
        </VBtn>
      </VCard>
    </div>
  </div>
</template>

<style scoped>
.mc-options-gate {
  /* Preserve the options' bounding box so revealing doesn't reflow the page. */
  min-block-size: 8rem;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
