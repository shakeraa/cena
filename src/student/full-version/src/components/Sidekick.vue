<script setup lang="ts">
// prr-207 — Sidekick.vue
// Session-scoped tutor drawer. True dialog (focus-trap, restore-focus,
// Esc-to-close); no variable-ratio animations, no streak/badge counters,
// no "you've asked 3 times today" framing.
// Accessibility: root is role="dialog" + aria-modal="true" +
// aria-labelledby bound to the heading. Focus-trap is handled by
// Vuetify's drawer portal; we additionally wire the open/close manual
// focus restore so keyboard users land back on the trigger.
// Math content wrapped in <bdi dir="ltr"> per the math-always-LTR
// invariant.

import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSidekick } from '@/composables/useSidekick'
import type { SidekickIntent } from '@/api/types/common'
import SidekickIntentBar from '@/components/session/SidekickIntentBar.vue'
import SidekickMessageStream from '@/components/session/SidekickMessageStream.vue'

interface Props {
  sessionId: string

  /** Locale direction ('ltr' | 'rtl'); drawer docks right/left accordingly. */
  direction?: 'ltr' | 'rtl'

  /** External control — parent can force-close on route-leave. */
  modelValue?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  direction: 'ltr',
  modelValue: false,
})

const emit = defineEmits<{
  (e: 'update:modelValue', open: boolean): void
  (e: 'fallbackToLadder'): void
}>()

const { t } = useI18n()
const sidekick = useSidekick({ sessionId: props.sessionId })

const headingId = `sidekick-heading-${Math.random().toString(36).slice(2, 8)}`
const freeFormInput = ref('')
const currentIntent = ref<SidekickIntent | null>(null)
const drawerEl = ref<HTMLElement | null>(null)
let triggerEl: HTMLElement | null = null

// --- External control sync (parent's v-model drives our open state) ---
watch(() => props.modelValue, async open => {
  if (open && !sidekick.isOpen.value) {
    // Remember the element that had focus before we opened the drawer —
    // we restore focus here on close (WCAG 2.4.3 Focus Order).
    triggerEl = (document.activeElement as HTMLElement | null) ?? null
    await sidekick.open()
    await nextTick()

    // Move focus into the drawer heading so keyboard users land inside.
    drawerEl.value?.querySelector<HTMLElement>(`#${headingId}`)?.focus()
  }
  else if (!open && sidekick.isOpen.value) {
    sidekick.close()
    triggerEl?.focus?.()
  }
}, { immediate: true })

// --- Parent reflection — keep v-model in sync with internal close() ---
watch(() => sidekick.isOpen.value, open => {
  emit('update:modelValue', open)
})

// --- Keyboard shortcut: Ctrl/Cmd+K toggle (only while mounted) ---
function onKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    sidekick.toggle()
  }
  else if (e.key === 'Escape' && sidekick.isOpen.value) {
    // Let the native drawer's Esc handling also fire; we just make
    // sure focus restores to the trigger.
    sidekick.close()
    triggerEl?.focus?.()
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
  sidekick.teardown()
})

// --- Intent handlers ---
function handleIntent(intent: SidekickIntent) {
  currentIntent.value = intent
  if (intent === 'explain_question')
    sidekick.sendTurn({ intent, userMessage: t('sidekick.intent.explainQuestionPrompt') })

  else if (intent === 'explain_step')
    sidekick.sendTurn({ intent, userMessage: t('sidekick.intent.explainStepPrompt') })

  else if (intent === 'explain_concept')
    sidekick.sendTurn({ intent, userMessage: t('sidekick.intent.explainConceptPrompt') })

  // free_form: waits for the student to type + submit.
}

function submitFreeForm() {
  const text = freeFormInput.value.trim()
  if (!text)
    return
  sidekick.sendTurn({ intent: 'free_form', userMessage: text })
  freeFormInput.value = ''
}

function closeDrawer() {
  sidekick.close()
  triggerEl?.focus?.()
}

// Expose teardown + debounce hooks for the parent runner to call on
// wrong-step or route-leave events.
defineExpose({
  teardown: sidekick.teardown,
  noteWrongStep: sidekick.noteWrongStep,
  refreshContext: sidekick.refreshContext,
  open: sidekick.open,
  close: sidekick.close,
  toggle: sidekick.toggle,
})
</script>

<template>
  <VNavigationDrawer
    v-model="sidekick.isOpen.value"
    temporary
    :location="direction === 'rtl' ? 'start' : 'end'"
    width="420"
    class="sidekick-drawer"
    data-testid="sidekick-drawer"
  >
    <div
      ref="drawerEl"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="headingId"
      class="sidekick-drawer__content"
    >
      <header class="sidekick-drawer__header">
        <h2
          :id="headingId"
          class="sidekick-drawer__heading"
          tabindex="-1"
        >
          {{ t('sidekick.title') }}
        </h2>
        <VBtn
          icon="tabler-x"
          variant="text"
          size="small"
          :aria-label="t('sidekick.closeAria')"
          data-testid="sidekick-close"
          @click="closeDrawer"
        />
      </header>

      <!-- Context summary (prr-204 seed) -->
      <section
        v-if="sidekick.context.value"
        class="sidekick-drawer__context"
        data-testid="sidekick-context"
      >
        <div class="sidekick-drawer__context-row">
          <span class="sidekick-drawer__context-label">
            {{ t('sidekick.context.rung') }}
          </span>
          <span class="sidekick-drawer__context-value">
            {{ sidekick.context.value.currentRung }}
          </span>
        </div>
        <div
          v-if="sidekick.context.value.lastMisconceptionTag"
          class="sidekick-drawer__context-row"
        >
          <span class="sidekick-drawer__context-label">
            {{ t('sidekick.context.lastFocus') }}
          </span>
          <bdi
            dir="ltr"
            class="sidekick-drawer__context-value"
          >
            {{ sidekick.context.value.lastMisconceptionTag }}
          </bdi>
        </div>
        <div class="sidekick-drawer__context-row">
          <span class="sidekick-drawer__context-label">
            {{ t('sidekick.context.minutesLeft') }}
          </span>
          <span class="sidekick-drawer__context-value">
            {{ sidekick.context.value.dailyMinutesRemaining }}
          </span>
        </div>
      </section>

      <!-- Intent bar -->
      <SidekickIntentBar
        :explain-step-enabled="sidekick.explainStepEnabled.value"
        :active="currentIntent"
        @select="handleIntent"
      />

      <!-- Circuit-breaker fallback -->
      <div
        v-if="sidekick.circuitBroken.value"
        class="sidekick-drawer__circuit"
        data-testid="sidekick-circuit-broken"
      >
        <p class="sidekick-drawer__circuit-copy">
          {{ t('sidekick.circuit.resting') }}
        </p>
        <VBtn
          variant="tonal"
          color="info"
          prepend-icon="tabler-bulb"
          data-testid="sidekick-fallback-to-ladder"
          @click="emit('fallbackToLadder'); closeDrawer()"
        >
          {{ t('sidekick.circuit.openLadder') }}
        </VBtn>
      </div>

      <!-- Stream -->
      <SidekickMessageStream
        v-else
        :messages="sidekick.messages.value"
        :loading="sidekick.streaming.value"
      />

      <!-- Free-form composer (only when the free_form intent is active) -->
      <form
        v-if="currentIntent === 'free_form'"
        class="sidekick-drawer__composer"
        @submit.prevent="submitFreeForm"
      >
        <VTextField
          v-model="freeFormInput"
          :placeholder="t('sidekick.composer.placeholder')"
          :aria-label="t('sidekick.composer.ariaLabel')"
          variant="outlined"
          density="compact"
          :disabled="sidekick.streaming.value"
          data-testid="sidekick-composer-input"
        />
        <VBtn
          type="submit"
          color="primary"
          :disabled="!freeFormInput.trim() || sidekick.streaming.value"
          data-testid="sidekick-composer-submit"
        >
          {{ t('sidekick.composer.send') }}
        </VBtn>
      </form>
    </div>
  </VNavigationDrawer>
</template>

<style scoped>
.sidekick-drawer__content {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  min-block-size: 100%;
}

.sidekick-drawer__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.sidekick-drawer__heading {
  font-size: 1.1rem;
  font-weight: 600;
  outline: none;
}

.sidekick-drawer__heading:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
  border-radius: 0.25rem;
}

.sidekick-drawer__context {
  background: rgba(var(--v-theme-on-surface), 0.04);
  padding: 0.75rem 1rem;
  border-radius: 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.9rem;
}

.sidekick-drawer__context-row {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
}

.sidekick-drawer__context-label {
  color: rgba(var(--v-theme-on-surface), 0.72);
}

.sidekick-drawer__circuit {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  background: rgba(var(--v-theme-info), 0.08);
  border-radius: 0.5rem;
}

.sidekick-drawer__circuit-copy {
  font-size: 0.95rem;
  line-height: 1.4;
}

.sidekick-drawer__composer {
  display: flex;
  gap: 0.5rem;
  align-items: flex-start;
  padding-block-start: 0.5rem;
  border-block-start: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}

/* Reduced-motion guard (WCAG 2.3.3) */
@media (prefers-reduced-motion: reduce) {
  .sidekick-drawer * {
    animation-duration: 0.01ms !important;
    transition-duration: 0.01ms !important;
  }
}
</style>
