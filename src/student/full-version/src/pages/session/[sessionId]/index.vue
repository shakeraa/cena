<script setup lang="ts">
definePage({
  meta: {
    layout: 'blank',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.session',
    hideSidebar: true,
    breadcrumbs: false,
  },
})
</script>

<template>
  <PlaceholderPage title-key="nav.session" />
</template>
