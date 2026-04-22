<script setup lang="ts">
import { useTheme } from 'vuetify'
import ScrollToTop from '@core/components/ScrollToTop.vue'
import UpdateToast from '@/components/UpdateToast.vue'
import FirstRunLanguageChooser from '@/components/common/FirstRunLanguageChooser.vue'
import initCore from '@core/initCore'
import { initConfigStore, useConfigStore } from '@core/stores/config'
import { hexToRgb } from '@core/utils/colorConverter'
import { useLocaleStore } from '@/stores/localeStore'

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
    </VApp>
  </VLocaleProvider>
</template>
