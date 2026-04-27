<script setup lang="ts">
import { useTheme } from 'vuetify'
import ScrollToTop from '@core/components/ScrollToTop.vue'
import UpdateToast from '@/components/UpdateToast.vue'
import FirstRunLanguageChooser from '@/components/common/FirstRunLanguageChooser.vue'
import initCore from '@core/initCore'
import { initConfigStore, useConfigStore } from '@core/stores/config'
import { hexToRgb } from '@core/utils/colorConverter'
import { useLocaleStore } from '@/stores/localeStore'
import { useLocaleSideEffects } from '@/composables/useLocaleSideEffects'

const { global } = useTheme()

// ℹ️ Sync current theme with initial loader theme
initCore()
initConfigStore()

const configStore = useConfigStore()

// PRR-A11Y-FIRST-RUN-CHOOSER: mount the full-screen chooser when the locale
// store has not yet been locked. Legacy users (bare "en"/"ar"/"he" strings in
// cena-student-locale) are upcast to `{ code, locked: true }` by the store's
// loader, so they never see the chooser.
const localeStore = useLocaleStore()

// Apply the persisted locale on initial mount. The store reads code +
// locked from localStorage but does NOT call useLocaleSideEffects.apply()
// itself — that's the side-effect seam (vue-i18n + vuetify + html
// lang/dir). Without this call, a returning student with a locked ar/he
// locale gets the default LTR layout on every refresh until they
// interact with the language switcher. EPIC-L journey caught this.
const { apply: applyLocaleSideEffects } = useLocaleSideEffects()
applyLocaleSideEffects(localeStore.code)
</script>

<template>
  <VLocaleProvider :rtl="configStore.isAppRTL">
    <!-- RDY-015: Skip link — first focusable element for keyboard/screen-reader users -->
    <a
      href="#main-content"
      class="skip-link"
    >
      Skip to main content
    </a>
    <!-- PRR-A11Y-SEMANTICS-SHORTCUT: tertiary skip target — lands keyboard
         focus directly on the A11yToolbar handle (reachable on every layout). -->
    <a
      href="#a11y-toolbar-handle"
      class="skip-link skip-link--toolbar"
      data-testid="skip-to-a11y-toolbar"
    >
      Skip to accessibility toolbar
    </a>
    <!-- ℹ️ This is required to set the background color of active nav link based on currently active global theme's primary -->
    <VApp :style="`--v-global-theme-primary: ${hexToRgb(global.current.value.colors.primary)}`">
      <RouterView />
      <ScrollToTop />
      <UpdateToast />

      <!-- RDY-015: Global aria-live region for dynamic announcements -->
      <div
        id="cena-live-region"
        aria-live="polite"
        aria-atomic="true"
        class="sr-only"
      />

      <!-- PRR-A11Y-FIRST-RUN-CHOOSER: blocks route content on very first
           visit until the student picks a locale. Dismissal is intentional:
           Esc is disabled inside the chooser and it unmounts only after a
           tile click / Enter. -->
      <FirstRunLanguageChooser v-if="!localeStore.locked" />

      <!-- PRR-A11Y-EXPANDED-CONTROLS: SVG filter defs for the color-blind
           simulation modes. Keeping them in one always-mounted SVG lets the
           CSS `filter: url(#...)` rules on <main> work without each page
           needing to re-declare them. Hidden from the a11y tree. -->
      <svg
        class="sr-only"
        aria-hidden="true"
        focusable="false"
        width="0"
        height="0"
      >
        <defs>
          <filter id="cena-cb-protanopia">
            <feColorMatrix
              type="matrix"
              values="
                0.567 0.433 0.000 0 0
                0.558 0.442 0.000 0 0
                0.000 0.242 0.758 0 0
                0     0     0     1 0"
            />
          </filter>
          <filter id="cena-cb-deuteranopia">
            <feColorMatrix
              type="matrix"
              values="
                0.625 0.375 0.000 0 0
                0.700 0.300 0.000 0 0
                0.000 0.300 0.700 0 0
                0     0     0     1 0"
            />
          </filter>
          <filter id="cena-cb-tritanopia">
            <feColorMatrix
              type="matrix"
              values="
                0.950 0.050 0.000 0 0
                0.000 0.433 0.567 0 0
                0.000 0.475 0.525 0 0
                0     0     0     1 0"
            />
          </filter>
        </defs>
      </svg>
    </VApp>
  </VLocaleProvider>
</template>
