<script lang="ts" setup>
const { injectSkinClasses } = useSkins()

// ℹ️ This will inject classes in body tag for accurate styling
injectSkinClasses()

// SECTION: Loading Indicator
const isFallbackStateActive = ref(false)
const refLoadingIndicator = ref<any>(null)

// watching if the fallback state is active and the refLoadingIndicator component is available
watch([isFallbackStateActive, refLoadingIndicator], () => {
  if (isFallbackStateActive.value && refLoadingIndicator.value)
    refLoadingIndicator.value.fallbackHandle()

  if (!isFallbackStateActive.value && refLoadingIndicator.value)
    refLoadingIndicator.value.resolveHandle()
}, { immediate: true })
// !SECTION
</script>

<template>
  <AppLoadingIndicator ref="refLoadingIndicator" />

  <div
    class="layout-wrapper layout-blank"
    data-allow-mismatch
  >
    <RouterView #="{Component}">
      <Suspense
        :timeout="0"
        @fallback="isFallbackStateActive = true"
        @resolve="isFallbackStateActive = false"
      >
        <Component :is="Component" />
      </Suspense>
    </RouterView>
  </div>

  <!-- A11yToolbar on the blank layout too — onboarding (layout: 'blank')
       is the first surface a new student sees. IL 5758-1998 requires the
       a11y toolbar to be reachable on every web surface, including
       pre-onboarded flows. -->
  <A11yToolbar />
</template>

<style>
.layout-wrapper.layout-blank {
  flex-direction: column;
}
</style>
