<script lang="ts" setup>
const { injectSkinClasses } = useSkins()

injectSkinClasses()

const isFallbackStateActive = ref(false)
const refLoadingIndicator = ref<any>(null)

watch([isFallbackStateActive, refLoadingIndicator], () => {
  if (isFallbackStateActive.value && refLoadingIndicator.value)
    refLoadingIndicator.value.fallbackHandle()

  if (!isFallbackStateActive.value && refLoadingIndicator.value)
    refLoadingIndicator.value.resolveHandle()
}, { immediate: true })
</script>

<template>
  <AppLoadingIndicator ref="refLoadingIndicator" />

  <div
    class="layout-wrapper layout-auth"
    data-allow-mismatch
  >
    <div
      class="layout-auth-hero"
      aria-hidden="true"
    />
    <div class="layout-auth-card-wrap">
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
  </div>

  <!-- A11yToolbar on auth + onboarding screens too (IL 5758-1998 requires
       the toolbar be reachable before login). -->
  <A11yToolbar />
</template>

<style scoped lang="scss">
.layout-wrapper.layout-auth {
  display: grid;
  min-block-size: 100vh;
  grid-template-columns: 1fr;
  background-color: rgb(var(--v-theme-background));

  @media (min-width: 960px) {
    grid-template-columns: minmax(0, 1fr) minmax(420px, 560px);
  }
}

.layout-auth-hero {
  display: none;
  background:
    radial-gradient(ellipse at top left, rgb(var(--v-theme-primary) / 0.18), transparent 60%),
    radial-gradient(ellipse at bottom right, rgb(var(--v-theme-flow-in-flow) / 0.22), transparent 55%),
    rgb(var(--v-theme-surface));

  @media (min-width: 960px) {
    display: block;
  }
}

.layout-auth-card-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding-block: 48px;
  padding-inline: 24px;
}
</style>
