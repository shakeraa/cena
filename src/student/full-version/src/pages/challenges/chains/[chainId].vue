<script setup lang="ts">
definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.cardChain',
    hideSidebar: false,
    breadcrumbs: true,
  },
})
</script>

<template>
  <PlaceholderPage title-key="nav.cardChain" />
</template>
