<script setup lang="ts">
definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'nav.register',
    hideSidebar: false,
    breadcrumbs: false,
  },
})
</script>

<template>
  <PlaceholderPage title-key="nav.register" />
</template>
