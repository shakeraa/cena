<!-- =============================================================================
     Cena Platform — NavBar Ingestion Jobs trigger

     Sits in the admin navbar next to NavBarNotifications. Shows a badge
     with the active (queued+running) ingestion job count; clicking
     opens the IngestionJobsDrawer. Mounted globally via DefaultLayout
     so the drawer is reachable from any admin page.
============================================================================= -->
<script setup lang="ts">
import { onMounted, onBeforeUnmount } from 'vue'
import { useIngestionJobs } from '@/composables/useIngestionJobs'

const { activeCount, runningCount, openDrawer, fetchJobs, startPolling, stopPolling } = useIngestionJobs()

onMounted(() => {
  fetchJobs()
  startPolling()
})

onBeforeUnmount(() => {
  stopPolling()
})
</script>

<template>
  <IconBtn @click="openDrawer">
    <VBadge
      :model-value="activeCount > 0"
      :content="activeCount"
      :color="runningCount > 0 ? 'info' : 'secondary'"
      offset-x="2"
      offset-y="2"
    >
      <VIcon icon="tabler-list-details" size="22" />
    </VBadge>
    <VTooltip
      activator="parent"
      location="bottom"
    >
      Ingestion Jobs
    </VTooltip>
  </IconBtn>
</template>
