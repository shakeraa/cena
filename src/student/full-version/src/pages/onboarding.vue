<script setup lang="ts">
definePage({
  meta: {
    layout: 'blank',
    requiresAuth: true,
    requiresOnboarded: false,
    public: false,
    title: 'nav.onboarding',
    hideSidebar: true,
    breadcrumbs: false,
  },
})
</script>

<template>
  <PlaceholderPage title-key="nav.onboarding" />
</template>
