<script setup lang="ts">
// =============================================================================
// PWA-005: Offline banner — shows a persistent snackbar when the user loses
// connectivity, auto-replays queued submissions on reconnect.
// =============================================================================
import { useNetworkStatus } from '@/composables/useNetworkStatus'

const { isOnline, pendingCount, onReconnect, drainQueue } = useNetworkStatus()

const showReconnected = ref(false)
const drainResult = ref<{ sent: number; failed: number } | null>(null)

onReconnect(async () => {
  if (pendingCount.value > 0)
    drainResult.value = await drainQueue()

  showReconnected.value = true
  setTimeout(() => { showReconnected.value = false }, 4000)
})

const { t } = useI18n()
</script>

<template>
  <!-- Offline banner -->
  <VSnackbar
    v-model="!isOnline"
    :timeout="-1"
    location="bottom"
    color="warning"
    multi-line
    class="offline-banner"
  >
    <div class="d-flex align-center gap-3">
      <VIcon
        icon="ri-wifi-off-line"
        size="24"
      />
      <div>
        <div class="text-subtitle-1 font-weight-medium">
          {{ t('pwa.connection.offline') }}
        </div>
        <div
          v-if="pendingCount > 0"
          class="text-body-2"
        >
          {{ t('pwa.connection.queued', { count: pendingCount }) }}
        </div>
      </div>
    </div>
  </VSnackbar>

  <!-- Reconnected toast -->
  <VSnackbar
    v-model="showReconnected"
    :timeout="4000"
    location="bottom"
    color="success"
    class="reconnected-toast"
  >
    <div class="d-flex align-center gap-2">
      <VIcon
        icon="ri-wifi-line"
        size="20"
      />
      <span>{{ t('pwa.connection.connected') }}</span>
      <span
        v-if="drainResult && drainResult.sent > 0"
        class="text-body-2 ms-1"
      >
        ({{ drainResult.sent }} synced)
      </span>
    </div>
  </VSnackbar>
</template>

<style scoped>
.offline-banner :deep(.v-snackbar__wrapper) {
  /* Safe area inset for notched devices */
  padding-block-end: env(safe-area-inset-bottom, 0px);
}
</style>
