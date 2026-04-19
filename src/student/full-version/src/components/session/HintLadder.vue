<script setup lang="ts">
// =============================================================================
// Cena Platform — Standalone Hint Ladder component (RDY-065b)
//
// Phase 1B follow-up to RDY-065 phase 1 (which landed minimal inline hint
// UI inside QuestionCard.vue). This component is a dedicated, reusable
// progressive-disclosure ladder that:
//
//   * Shows each received hint rung as a separate tonal alert (never
//     warning — hints are learning aids, not penalties).
//   * Uses qualitative rung labels from i18n (session.runner.hintStep.N)
//     — NEVER numeric ordinals like "Hint 1 of 5".
//   * Emits 'request-next-rung' when the student asks for more; parent
//     owns the fetch and passes back the next hint via the `hints` prop.
//   * Respects expertise-reversal: when `hintsRemaining === 0` or
//     `maxRungs` has been reached, the request-more button disappears
//     (never a disabled-button shame state, never a counter).
//   * All math runs wrap in <bdi dir="ltr"> so mixed RTL math renders
//     correctly in Hebrew / Arabic locales (ADR-locked: math-always-LTR
//     per memory:feedback_math_always_ltr).
//
// Shipgate guard (RDY-065 phase 1): the rendered DOM MUST NEVER contain
// banned patterns like "Hint 1", "(N remaining)", comparative percentiles,
// or visible BKT credit penalties. The regression suite
// tests/unit/HintLadder.anxietySafe.spec.ts asserts this across en/ar/he.
// =============================================================================

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * Shape of a single received hint rung. Matches the shape the backend
 * returns from POST /api/sessions/{sid}/question/{qid}/hint. Kept loose
 * so callers don't pull in the full SessionHintResponseDto just to
 * render the ladder.
 */
interface HintRung {
  hintLevel: number
  hintText: string
  hintsRemaining?: number
}

interface Props {
  /** Hints received so far, in delivery order (rung 1 first). */
  hints: HintRung[]
  /** Hints still available to fetch. 0 hides the request-more button. */
  hintsRemaining?: number
  /** Max rungs the ladder supports (defaults to 5 per phase 1B design). */
  maxRungs?: number
  /** Disable all interaction (e.g. question locked after submit). */
  locked?: boolean
  /** Loading spinner on the request-more button while the parent fetches. */
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  hintsRemaining: undefined,
  maxRungs: 5,
  locked: false,
  loading: false,
})

const emit = defineEmits<{
  (e: 'request-next-rung'): void
}>()

const { t } = useI18n()

// The "More help" button is visible only when every guard clears.
// Intentionally NOT displaying hintsRemaining anywhere in the DOM — that
// field exists for the backend's expertise-reversal logic, never for
// student eyes (RDY-065 shipgate lock).
const canRequestMore = computed(() => {
  if (props.locked || props.loading) return false
  if (props.hints.length >= props.maxRungs) return false
  if (props.hintsRemaining !== undefined && props.hintsRemaining <= 0) return false
  return true
})

// Label for rung `level` (1..maxRungs). Falls back to "More help" if a
// rung index arrives that isn't in the i18n table — keeps the component
// resilient to future backend extensions without shipping a crash.
function rungLabel(level: number): string {
  const key = `session.runner.hintStep.${level}`
  const translated = t(key)
  // vue-i18n returns the key string itself on missing keys.
  if (translated === key) return t('session.runner.hintLadder.moreHint')
  return translated
}

function handleRequestMore() {
  if (!canRequestMore.value) return
  emit('request-next-rung')
}
</script>

<template>
  <div
    class="hint-ladder"
    data-testid="hint-ladder"
    role="region"
    :aria-label="t('session.runner.hintLadder.previousRungs')"
  >
    <!--
      Received rungs: render each as a separate tonal info alert. Order is
      delivery-order (oldest first) so the student scrolls down through a
      progressive-disclosure trail.
    -->
    <VAlert
      v-for="rung in hints"
      :key="rung.hintLevel"
      type="info"
      variant="tonal"
      icon="tabler-bulb"
      class="hint-ladder__rung mb-3"
      :data-testid="`hint-rung-${rung.hintLevel}`"
    >
      <div class="text-caption text-medium-emphasis mb-1">
        {{ rungLabel(rung.hintLevel) }}
      </div>
      <div class="text-body-2">
        <!--
          Math expressions in Arabic / Hebrew must render LTR. The <bdi>
          wrapper keeps numerals, operators, and variable letters in
          left-to-right order inside an RTL page direction.
        -->
        <bdi
          dir="ltr"
          class="hint-ladder__math-run"
        >
          {{ rung.hintText }}
        </bdi>
      </div>
    </VAlert>

    <!--
      Request-next-rung button. Tonal + info color (never warning / error —
      hints are learning aids, not penalties). Label is neutral "More help"
      in the student's locale. NO counter, NO remaining-of-N.
    -->
    <VBtn
      v-if="canRequestMore"
      variant="tonal"
      color="info"
      size="small"
      prepend-icon="tabler-bulb"
      :loading="loading"
      class="hint-ladder__request"
      data-testid="hint-ladder-request-more"
      @click="handleRequestMore"
    >
      {{ t('session.runner.hintLadder.moreHint') }}
    </VBtn>
  </div>
</template>

<style scoped>
.hint-ladder {
  display: flex;
  flex-direction: column;
  align-items: stretch;
}

.hint-ladder__rung {
  transition: opacity 150ms ease-out;
}

.hint-ladder__request {
  align-self: flex-start;
}

.hint-ladder__math-run {
  font-variant-numeric: lining-nums tabular-nums;
}
</style>
