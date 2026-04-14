<script setup lang="ts">
import { useTheme } from 'vuetify'
import ScrollToTop from '@core/components/ScrollToTop.vue'
import UpdateToast from '@/components/UpdateToast.vue'
import initCore from '@core/initCore'
import { initConfigStore, useConfigStore } from '@core/stores/config'
import { hexToRgb } from '@core/utils/colorConverter'

const { global } = useTheme()

// ℹ️ Sync current theme with initial loader theme
initCore()
initConfigStore()

const configStore = useConfigStore()
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
    </VApp>
  </VLocaleProvider>
</template>
