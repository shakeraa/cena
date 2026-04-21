<script setup lang="ts">
// =============================================================================
// A11yToolbar — Israeli Equal-Rights for Persons with Disabilities Law
// (5758-1998) compliance. Offers text-size slider, high-contrast toggle,
// dyslexia-font toggle, reduced-motion toggle, numerals preference
// (PRR-232), a language switcher (IL 5758-1998 "independent control"
// principle — Arabic/Hebrew readers must be able to switch BEFORE login),
// and a reset button.
//
// Placement: slide-in sheet from the trailing edge (inline-end). Trigger is
// a persistent "handle" button anchored inline-end, center-vertical.
// The sheet is a true dialog (focus-trap, role=dialog, Esc closes).
//
// WHY language switcher lives here (added 2026-04-21 via
// claude-subagent-wave6b): the first A11yToolbar pass deliberately scoped
// only WCAG-oriented controls and delegated locale to the onboarding /
// settings flows. User review surfaced that students landing on the wrong
// locale (shared device, public kiosk, wrong OS default) could not reach
// the LanguagePicker without first completing onboarding — an access
// barrier for first-language Arabic/Hebrew readers and arguably a
// violation of the IL 5758-1998 "independent control" principle. The
// follow-up collapses i18n into the a11y surface so language is a
// first-class a11y control on every layout (default / auth / blank).
//
// Deferred to separate follow-up tasks (see
// TASK-PRR-A11Y-TOOLBAR-ENRICH-FOLLOWUPS):
//   - First-run full-screen language chooser (prr-a11y-first-run-chooser)
//   - Color-blind simulation filters (prr-a11y-color-blind)
//   - Cursor-tracking reading guide (prr-a11y-reading-guide)
//   - Line-height slider (prr-a11y-line-height)
//   - Alt+A keyboard shortcut (prr-a11y-keyboard-shortcut)
// =============================================================================
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useLocale } from 'vuetify'
import { useA11yStore } from '@/stores/a11yStore'
import { useOnboardingStore, type NumeralsPreference } from '@/stores/onboardingStore'
import { useAvailableLocales, type LocaleDescriptor } from '@/composables/useAvailableLocales'

const { t, locale: i18nLocale } = useI18n()
const vuetifyLocale = useLocale()
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

// PRR-232: numerals preference. `null` in the store means "auto from
// locale"; the radio group surfaces the auto default as a non-modifying
// option so the student can opt back into locale-driven behavior.
type NumeralsChoice = 'auto' | NumeralsPreference
const numeralsChoice = computed<NumeralsChoice>(() =>
  onboarding.numeralsPreference === null ? 'auto' : onboarding.numeralsPreference,
)
function setNumerals(choice: NumeralsChoice) {
  onboarding.setNumeralsPreference(choice === 'auto' ? null : choice)
}

// Language switcher — mirrors LanguageSwitcher.vue's persistence contract
// (same storage key) so the two stay in lockstep when both are visible.
const LOCALE_STORAGE_KEY = 'cena-student-locale'
const currentLocale = computed<LocaleDescriptor['code']>(() => {
  const code = i18nLocale.value
  if (code === 'en' || code === 'ar' || code === 'he')
    return code as LocaleDescriptor['code']
  return 'en'
})
function setLocale(code: LocaleDescriptor['code']) {
  const found = availableLocales.value.find(l => l.code === code)
  if (!found)
    return

  // Defence in depth: if Hebrew is gated off at build time, the filtered
  // list already excluded it, but a stray radio dispatch shouldn't crash.
  if (code === 'he' && !hebrewEnabled)
    return

  i18nLocale.value = code
  vuetifyLocale.current.value = code
  if (typeof document !== 'undefined') {
    document.documentElement.lang = code
    document.documentElement.dir = found.dir
  }
  if (typeof localStorage !== 'undefined')
    localStorage.setItem(LOCALE_STORAGE_KEY, code)

  // Keep the onboarding store in sync so store-driven flows (settings
  // page, useMathRenderer's numerals inference) see the new locale
  // immediately without waiting for a route change.
  onboarding.setLocale(code)
}

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

      <!-- Language — listed first so first-language Arabic/Hebrew readers
           reach it without scrolling past the WCAG toggles. -->
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
            <!-- Native-script label — never transliterated. Per-locale
                 fonts (Noto Sans Hebrew / Noto Kufi Arabic) are already
                 loaded by commits cdfc0a24 / 418aec7a. -->
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

      <!-- PRR-232: Numerals preference. Samples are forced LTR so the
           western vs eastern digits render in numeric order even when the
           toolbar sits on an RTL page. -->
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
            <bdi
              dir="ltr"
              class="text-caption text-medium-emphasis"
            >(0123)</bdi>
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
            <bdi
              dir="ltr"
              class="text-caption text-medium-emphasis"
            >(٠١٢٣)</bdi>
          </label>
        </div>
      </fieldset>

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

      <p class="text-caption text-medium-emphasis mt-4 mb-2">
        {{ t('a11y.legalNote') }}
      </p>

      <!-- Accessibility statement stub. Route target is authored in
           follow-up task A11Y-STATEMENT-ROUTE; for now we render the link
           pointing at /accessibility-statement so the footprint is in
           place and screen-reader users can confirm the contact channel
           (required by IL Reg 5773-2013 §35(b)(4)). -->
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
