<script setup lang="ts">
import { useTheme } from 'vuetify'
import { onAuthStateChanged } from 'firebase/auth'
import ScrollToTop from '@core/components/ScrollToTop.vue'
import initCore from '@core/initCore'
import { initConfigStore, useConfigStore } from '@core/stores/config'
import { hexToRgb } from '@core/utils/colorConverter'
import { firebaseAuth } from '@/plugins/firebase'
import { ability } from '@/plugins/casl/ability'

const { global } = useTheme()

// ℹ️ Sync current theme with initial loader theme
initCore()
initConfigStore()

const configStore = useConfigStore()

// Global auth state watcher — catches mid-session sign-outs
// (token revoked, account disabled, signed out in another tab)
const router = useRouter()
let authInitialized = false

onAuthStateChanged(firebaseAuth, user => {
  // Skip the initial callback (handled by router guard)
  if (!authInitialized) {
    authInitialized = true
    return
  }

  // User signed out mid-session → clear state and redirect to login
  if (!user) {
    ability.update([])
    useCookie('userData').value = null
    useCookie('accessToken').value = null
    useCookie('userAbilityRules').value = null

    const currentRoute = router.currentRoute.value
    if (currentRoute.name !== 'login' && currentRoute.name !== 'register') {
      router.push({
        name: 'login',
        query: currentRoute.path !== '/' ? { to: currentRoute.path } : undefined,
      })
    }
  }
})
</script>

<template>
  <VLocaleProvider :rtl="configStore.isAppRTL">
    <!-- ℹ️ This is required to set the background color of active nav link based on currently active global theme's primary -->
    <VApp :style="`--v-global-theme-primary: ${hexToRgb(global.current.value.colors.primary)}`">
      <RouterView />
      <ScrollToTop />
    </VApp>
  </VLocaleProvider>
</template>
