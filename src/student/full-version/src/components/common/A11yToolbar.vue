<script setup lang="ts">
// A11yToolbar — IL 5758-1998 + Reg 5773-2013 compliance. Offers text-size,
// high-contrast, dyslexia-font, reduced-motion, numerals, language, plus
// line-height / color-blind sim / underline-all-links (added 2026-04-21 via
// PRR-A11Y-EXPANDED-CONTROLS). Alt+A opens it from any layout and every
// preference change emits an aria-live announce (PRR-A11Y-SEMANTICS-SHORTCUT).
// Language writes route through useLocaleStore + useLocaleSideEffects so the
// toolbar, LanguageSwitcher, and FirstRunLanguageChooser never drift.
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useA11yStore, LINE_HEIGHT_STEPS, type A11yColorBlind, type A11yLineHeight } from '@/stores/a11yStore'
import { useOnboardingStore, type NumeralsPreference } from '@/stores/onboardingStore'
import { useAvailableLocales, type LocaleDescriptor } from '@/composables/useAvailableLocales'
import { useLocaleSideEffects } from '@/composables/useLocaleSideEffects'
import { useLocaleStore } from '@/stores/localeStore'
import A11yColorBlindRadio from '@/components/common/A11yColorBlindRadio.vue'

const { t, locale: i18nLocale } = useI18n()
const { apply: applyLocaleSideEffects } = useLocaleSideEffects()
const localeStore = useLocaleStore()
const a11y = useA11yStore()
const onboarding = useOnboardingStore()
const { locales: availableLocales, hebrewEnabled } = useAvailableLocales()

const open = ref(false)

const sizeMarks = computed(() => [
  { value: 0, label: t('a11y.size.xsmall') },
  { value: 1, label: t('a11y.size.small') },
  { value: 2, label: t('a11y.size.medium') },
  { value: 3, label: t('a11y.size.large') },
  { value: 4, label: t('a11y.size.xlarge') },
  { value: 5, label: t('a11y.size.xxlarge') },
])

// PRR-232: numerals preference. `null` in the store means "auto from locale".
type NumeralsChoice = 'auto' | NumeralsPreference
const numeralsChoice = computed<NumeralsChoice>(() =>
  onboarding.numeralsPreference === null ? 'auto' : onboarding.numeralsPreference,
)
function setNumerals(choice: NumeralsChoice) {
  onboarding.setNumeralsPreference(choice === 'auto' ? null : choice)
  const label
    = choice === 'auto' ? t('a11y.numerals.auto')
    : choice === 'eastern' ? t('a11y.numerals.eastern')
    : t('a11y.numerals.western')
  announce(t('a11y.announce.numerals', { value: label }))
}

// Language — single-seam writer via useLocaleSideEffects + useLocaleStore.
const currentLocale = computed<LocaleDescriptor['code']>(() => {
  const code = i18nLocale.value
  if (code === 'en' || code === 'ar' || code === 'he')
    return code as LocaleDescriptor['code']
  return 'en'
})
function setLocale(code: LocaleDescriptor['code']) {
  if (code === 'he' && !hebrewEnabled)
    return
  const applied = applyLocaleSideEffects(code)
  if (!applied)
    return
  localeStore.setLocale(code, { lock: true })
  onboarding.setLocale(code)
  announce(t('a11y.announce.language', { value: applied.label }))
}

// ───── aria-live announcer ─────
// PRR-A11Y-SEMANTICS-SHORTCUT: every preference change emits a polite live
// message. The global #cena-live-region element (mounted in App.vue) is the
// target; if it is not in the DOM we fall back to a local element. Debounced
// so slider drag only announces the final resting value.
let pendingAnnounce: ReturnType<typeof setTimeout> | null = null
function announce(message: string, delay = 250) {
  if (pendingAnnounce) {
    clearTimeout(pendingAnnounce)
    pendingAnnounce = null
  }
  pendingAnnounce = setTimeout(() => {
    pendingAnnounce = null
    if (typeof document === 'undefined')
      return
    const region = document.getElementById('cena-live-region')
    if (!region)
      return
    // Flip the content twice so screen readers re-announce even when the
    // same message fires in quick succession (AT implementation quirk).
    region.textContent = ''
    requestAnimationFrame(() => { region.textContent = message })
  }, delay)
}

// ───── announce wrappers for boolean toggles ─────
function onContrastToggle() {
  a11y.toggleContrast()
  announce(t('a11y.announce.contrast', {
    value: a11y.prefs.contrast === 'high' ? t('a11y.announce.enabled') : t('a11y.announce.disabled'),
  }))
}
function onMotionToggle() {
  a11y.toggleMotion()
  announce(t('a11y.announce.motion', {
    value: a11y.prefs.motion === 'reduced' ? t('a11y.announce.enabled') : t('a11y.announce.disabled'),
  }))
}
function onDyslexiaToggle() {
  a11y.toggleDyslexiaFont()
  announce(t('a11y.announce.dyslexia', {
    value: a11y.prefs.dyslexiaFont === 'on' ? t('a11y.announce.enabled') : t('a11y.announce.disabled'),
  }))
}
function onUnderlineToggle() {
  a11y.toggleUnderlineLinks()
  announce(t('a11y.announce.underlineLinks', {
    value: a11y.prefs.underlineLinks === 'on' ? t('a11y.announce.enabled') : t('a11y.announce.disabled'),
  }))
}
function onSetTextSize(v: number) {
  a11y.setTextSize(v as 0 | 1 | 2 | 3 | 4 | 5)
  announce(t('a11y.announce.textSize', { value: sizeMarks.value[a11y.prefs.textSize].label }))
}
function onSetLineHeight(v: number) {
  const step = Math.max(0, Math.min(4, Math.round(v))) as A11yLineHeight
  a11y.setLineHeight(step)
  announce(t('a11y.announce.lineHeight', { value: `${LINE_HEIGHT_STEPS[step].toFixed(1)}×` }))
}
function onSetColorBlind(mode: A11yColorBlind) {
  a11y.setColorBlind(mode)
  const labelKey
    = mode === 'protanopia' ? 'a11y.cbProtanopia'
    : mode === 'deuteranopia' ? 'a11y.cbDeuteranopia'
    : mode === 'tritanopia' ? 'a11y.cbTritanopia'
    : 'a11y.cbNone'
  announce(t('a11y.announce.colorBlind', { value: t(labelKey) }))
}
function onReset() {
  a11y.resetToDefaults()
  announce(t('a11y.announce.reset'))
}

function closeOnEsc(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}

// ───── Alt+A global shortcut ─────
// We attach our own listener rather than go through `useShortcut` because
// the A11yToolbar is mounted in EVERY layout (including auth/blank where
// ShellShortcuts is not present). Alt+A is used sparingly by OS screen
// readers — NVDA uses Insert+, JAWS uses Insert+, VoiceOver uses Ctrl+
// Opt+ — so Alt+A is safe.
function onGlobalKeydown(e: KeyboardEvent) {
  if (e.altKey && !e.metaKey && !e.ctrlKey && e.key.toLowerCase() === 'a') {
    e.preventDefault()
    open.value = true
  }
}
onMounted(() => {
  if (typeof window !== 'undefined')
    window.addEventListener('keydown', onGlobalKeydown)
})
onBeforeUnmount(() => {
  if (typeof window !== 'undefined')
    window.removeEventListener('keydown', onGlobalKeydown)
  if (pendingAnnounce)
    clearTimeout(pendingAnnounce)
})

// Keep the reactive store in sync when mounting — ensures announce() text
// picks up the correct locale on first keystroke after locale switch.
watch(i18nLocale, () => {
  // no-op today; placeholder for future "language changed" polite announce.
})
</script>

<template>
  <!-- Persistent handle — fixed inline-end center. Always reachable even
       when the sidebar is collapsed. Tab-accessible. -->
  <VBtn
    id="a11y-toolbar-handle"
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

      <p class="text-caption text-medium-emphasis mb-4">
        {{ t('a11y.shortcutHint') }}
      </p>

      <!-- Language -->
      <fieldset
        class="mb-6 a11y-toolbar__fieldset"
        data-testid="a11y-language-section"
      >
        <legend
          id="a11y-language-label"
          class="text-subtitle-2 mb-2"
        >
          {{ t('a11y.language') }}
        </legend>
        <div
          role="radiogroup"
          aria-labelledby="a11y-language-label"
          class="d-flex flex-column ga-1"
        >
          <label
            v-for="option in availableLocales"
            :key="option.code"
            class="d-flex align-center ga-2 cursor-pointer"
            :data-testid="`a11y-language-${option.code}`"
          >
            <input
              type="radio"
              name="a11y-language"
              :value="option.code"
              :checked="currentLocale === option.code"
              @change="setLocale(option.code)"
            >
            <bdi :dir="option.dir">{{ option.label }}</bdi>
          </label>
        </div>
      </fieldset>

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
            :aria-label="t('a11y.textSize')"
            :aria-valuetext="sizeMarks[a11y.prefs.textSize].label"
            data-testid="a11y-text-size-slider"
            class="flex-grow-1"
            @end="onSetTextSize($event as number)"
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

      <!-- Line-height (PRR-A11Y-EXPANDED-CONTROLS) -->
      <div class="mb-6">
        <label
          id="a11y-line-height-label"
          class="text-subtitle-2 mb-2 d-block"
        >
          {{ t('a11y.lineHeight') }}
        </label>
        <VSlider
          :model-value="a11y.prefs.lineHeight"
          :min="0"
          :max="4"
          :step="1"
          hide-details
          aria-labelledby="a11y-line-height-label"
          :aria-label="t('a11y.lineHeight')"
          :aria-valuetext="`${LINE_HEIGHT_STEPS[a11y.prefs.lineHeight].toFixed(1)}×`"
          data-testid="a11y-line-height-slider"
          @end="onSetLineHeight($event as number)"
          @update:model-value="(v) => a11y.setLineHeight(Math.max(0, Math.min(4, Math.round(v as number))) as 0 | 1 | 2 | 3 | 4)"
        />
        <p class="text-caption text-medium-emphasis mb-0">
          <bdi dir="ltr">{{ LINE_HEIGHT_STEPS[a11y.prefs.lineHeight].toFixed(1) }}×</bdi>
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
        @update:model-value="onContrastToggle"
      />

      <!-- Reduced motion -->
      <VSwitch
        :model-value="a11y.prefs.motion === 'reduced'"
        :label="t('a11y.reducedMotion')"
        color="primary"
        hide-details
        class="mb-2"
        data-testid="a11y-motion-toggle"
        @update:model-value="onMotionToggle"
      />

      <!-- Dyslexia font -->
      <VSwitch
        :model-value="a11y.prefs.dyslexiaFont === 'on'"
        :label="t('a11y.dyslexiaFont')"
        color="primary"
        hide-details
        class="mb-2"
        data-testid="a11y-dyslexia-toggle"
        @update:model-value="onDyslexiaToggle"
      />

      <!-- Underline all links (PRR-A11Y-EXPANDED-CONTROLS) -->
      <VSwitch
        :model-value="a11y.prefs.underlineLinks === 'on'"
        :label="t('a11y.underlineLinks')"
        color="primary"
        hide-details
        class="mb-4"
        data-testid="a11y-underline-links-toggle"
        @update:model-value="onUnderlineToggle"
      />

      <!-- Color-blind filter (PRR-A11Y-EXPANDED-CONTROLS) — extracted so
           the parent stays under the 500-LOC cap. -->
      <A11yColorBlindRadio
        :model-value="a11y.prefs.colorBlind"
        @change="onSetColorBlind"
      />

      <!-- PRR-232: Numerals preference. -->
      <fieldset
        class="mb-6 a11y-toolbar__fieldset"
        data-testid="a11y-numerals-section"
      >
        <legend
          id="a11y-numerals-label"
          class="text-subtitle-2 mb-2"
        >
          {{ t('a11y.numerals.label') }}
        </legend>
        <div
          role="radiogroup"
          aria-labelledby="a11y-numerals-label"
          class="d-flex flex-column ga-1"
        >
          <label class="d-flex align-center ga-2 cursor-pointer">
            <input
              type="radio"
              name="a11y-numerals"
              value="auto"
              :checked="numeralsChoice === 'auto'"
              data-testid="a11y-numerals-auto"
              @change="setNumerals('auto')"
            >
            <span>{{ t('a11y.numerals.auto') }}</span>
          </label>
          <label class="d-flex align-center ga-2 cursor-pointer">
            <input
              type="radio"
              name="a11y-numerals"
              value="western"
              :checked="numeralsChoice === 'western'"
              data-testid="a11y-numerals-western"
              @change="setNumerals('western')"
            >
            <span>{{ t('a11y.numerals.western') }}</span>
            <bdi dir="ltr" class="text-caption text-medium-emphasis">(0123)</bdi>
          </label>
          <label class="d-flex align-center ga-2 cursor-pointer">
            <input
              type="radio"
              name="a11y-numerals"
              value="eastern"
              :checked="numeralsChoice === 'eastern'"
              data-testid="a11y-numerals-eastern"
              @change="setNumerals('eastern')"
            >
            <span>{{ t('a11y.numerals.eastern') }}</span>
            <bdi dir="ltr" class="text-caption text-medium-emphasis">(٠١٢٣)</bdi>
          </label>
        </div>
      </fieldset>

      <VDivider class="mb-4" />

      <VBtn
        variant="outlined"
        block
        :aria-label="t('a11y.resetDefaults')"
        data-testid="a11y-reset"
        @click="onReset"
      >
        <VIcon icon="tabler-rotate" start />
        {{ t('a11y.resetDefaults') }}
      </VBtn>

      <p class="text-caption text-medium-emphasis mt-4 mb-2">
        {{ t('a11y.legalNote') }}
      </p>

      <p class="text-caption mb-0">
        <a
          href="/accessibility-statement"
          data-testid="a11y-statement-link"
        >
          {{ t('a11y.accessibilityStatement') }}
        </a>
        <span class="text-medium-emphasis"> · </span>
        <a
          href="mailto:accessibility@cena.app"
          data-testid="a11y-contact-link"
        >
          accessibility@cena.app
        </a>
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

.a11y-toolbar__fieldset {
  border: 1px solid rgb(var(--v-theme-on-surface), 0.15);
  border-radius: 8px;
  padding: 0.75rem 1rem;
}

.cursor-pointer {
  cursor: pointer;
}

@media (prefers-reduced-motion: reduce) {
  .a11y-toolbar__handle {
    transition: none;
  }
}
</style>
