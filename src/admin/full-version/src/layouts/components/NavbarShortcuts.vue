<script setup lang="ts">
const shortcuts = [
  {
    icon: 'tabler-chart-dots-3',
    title: 'Mastery',
    subtitle: 'Student Progress',
    to: { name: 'apps-mastery-dashboard' },
  },
  {
    icon: 'tabler-help-hexagon',
    title: 'Questions',
    subtitle: 'Question Bank',
    to: { name: 'apps-questions-list' },
  },
  {
    icon: 'tabler-user',
    title: 'Users',
    subtitle: 'Manage Users',
    to: { name: 'apps-user-list' },
  },
  {
    icon: 'tabler-shield-lock',
    title: 'Permissions',
    subtitle: 'Access Control',
    to: { name: 'apps-permissions' },
  },
  {
    icon: 'tabler-device-desktop-analytics',
    title: 'Dashboard',
    subtitle: 'Admin Overview',
    to: { name: 'dashboards-admin' },
  },
  {
    icon: 'tabler-settings',
    title: 'Settings',
    subtitle: 'System Settings',
    to: { name: 'apps-system-settings' },
  },
]
</script>

<template>
  <Shortcuts :shortcuts="shortcuts" />
</template>
