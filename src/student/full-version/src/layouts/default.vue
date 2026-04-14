<script lang="ts" setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useConfigStore } from '@core/stores/config'
import { AppContentLayoutNav } from '@layouts/enums'
import { switchToVerticalNavOnLtOverlayNavBreakpoint } from '@layouts/utils'

const DefaultLayoutWithHorizontalNav = defineAsyncComponent(() => import('./components/DefaultLayoutWithHorizontalNav.vue'))
const DefaultLayoutWithVerticalNav = defineAsyncComponent(() => import('./components/DefaultLayoutWithVerticalNav.vue'))

const configStore = useConfigStore()
const route = useRoute()

switchToVerticalNavOnLtOverlayNavBreakpoint()

const { layoutAttrs, injectSkinClasses } = useSkins()

injectSkinClasses()

const isEmbedMode = computed(() => route.query.embed === '1')
const isHideSidebar = computed(() => route.meta.hideSidebar === true)

// Manage `<meta http-equiv="Content-Security-Policy">` lifecycle — only
// present while the user is on an `?embed=1` page. Removed on navigation
// away so the normal app isn't running with iframe-embed CSP rules.
const CSP_ID = 'cena-embed-csp'

function installCspMeta() {
  if (typeof document === 'undefined')
    return
  if (document.getElementById(CSP_ID))
    return
  const meta = document.createElement('meta')

  meta.id = CSP_ID
  meta.setAttribute('http-equiv', 'Content-Security-Policy')
  meta.setAttribute('content', 'frame-ancestors \'self\' https:')
  document.head.appendChild(meta)
}

function removeCspMeta() {
  if (typeof document === 'undefined')
    return
  const existing = document.getElementById(CSP_ID)
  if (existing)
    existing.remove()
}

watch(isEmbedMode, embed => {
  if (embed)
    installCspMeta()
  else
    removeCspMeta()
}, { immediate: true })

onMounted(() => {
  if (isEmbedMode.value)
    installCspMeta()
})

onUnmounted(() => {
  removeCspMeta()
})

// SECTION: Loading Indicator
const isFallbackStateActive = ref(false)
const refLoadingIndicator = ref<any>(null)

watch([isFallbackStateActive, refLoadingIndicator], () => {
  if (isFallbackStateActive.value && refLoadingIndicator.value)
    refLoadingIndicator.value.fallbackHandle()

  if (!isFallbackStateActive.value && refLoadingIndicator.value)
    refLoadingIndicator.value.resolveHandle()
}, { immediate: true })
// !SECTION
</script>

<template>
  <!-- Embed mode or hideSidebar: render a bare RouterView with zero chrome. -->
  <template v-if="isEmbedMode || isHideSidebar">
    <div
      class="layout-wrapper layout-embed"
      data-testid="layout-embed"
      data-allow-mismatch
    >
      <AppLoadingIndicator ref="refLoadingIndicator" />
      <RouterView v-slot="{ Component }">
        <Suspense
          :timeout="0"
          @fallback="isFallbackStateActive = true"
          @resolve="isFallbackStateActive = false"
        >
          <Component :is="Component" />
        </Suspense>
      </RouterView>
    </div>
  </template>

  <!-- Normal default layout: Vuexy vertical/horizontal nav + breadcrumbs + bottom nav. -->
  <template v-else>
    <Component
      v-bind="layoutAttrs"
      :is="configStore.appContentLayoutNav === AppContentLayoutNav.Vertical ? DefaultLayoutWithVerticalNav : DefaultLayoutWithHorizontalNav"
    >
      <AppLoadingIndicator ref="refLoadingIndicator" />

      <StudentBreadcrumbs />

      <!-- RDY-015: Skip link target -->
      <main id="main-content">
        <RouterView v-slot="{ Component }">
          <Suspense
            :timeout="0"
            @fallback="isFallbackStateActive = true"
            @resolve="isFallbackStateActive = false"
          >
            <Component :is="Component" />
          </Suspense>
        </RouterView>
      </main>
    </Component>

    <StudentBottomNav />
  </template>

  <!--
    Global command palette + cheatsheet + keyboard shortcuts. Mounted
    across both embed and normal layouts (STU-W-15 Phase A).
  -->
  <ShellShortcuts v-if="!isEmbedMode" />

  <!--
    PWA-002: Install prompt shown after 2nd visit, respects dismiss cooldown.
    Handles Chrome/Edge (beforeinstallprompt) and iOS Safari (manual guide).
  -->
  <InstallPrompt v-if="!isEmbedMode" />

  <!-- PWA-005: Offline banner + reconnect toast with auto-replay of queued submissions. -->
  <OfflineBanner />
</template>

<style lang="scss">
@use "@layouts/styles/default-layout";
</style>

<style scoped>
.layout-wrapper.layout-embed {
  min-block-size: 100vh;
}
</style>
