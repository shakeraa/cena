<script setup lang="ts">
definePage({
  meta: {
    layout: 'auth',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'nav.login',
    hideSidebar: false,
    breadcrumbs: false,
  },
})
</script>

<template>
  <PlaceholderPage title-key="nav.login" />
</template>
