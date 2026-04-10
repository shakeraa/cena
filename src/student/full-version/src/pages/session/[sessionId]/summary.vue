<script setup lang="ts">
definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.sessionSummary',
    hideSidebar: false,
    breadcrumbs: true,
  },
})
</script>

<template>
  <PlaceholderPage title-key="nav.sessionSummary" />
</template>
