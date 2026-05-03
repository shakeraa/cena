<script setup lang="ts">
definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.diagram',
    hideSidebar: false,
    breadcrumbs: true,
  },
})
</script>

<template>
  <PlaceholderPage title-key="nav.diagram" />
</template>
