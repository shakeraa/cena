<script setup lang="ts">
import { computed } from 'vue'
import type { ConnectionStatus } from '@/composables/useSignalRConnection'

const props = defineProps<{
  status: ConnectionStatus
  isOnline: boolean
  reconnectAttempts: number
  pendingSubmissions: number
}>()

const statusConfig = computed(() => {
  if (!props.isOnline) {
    return {
      icon: 'tabler-wifi-off',
      text: 'Offline',
      color: 'error',
      show: true,
    }
  }
  switch (props.status) {
    case 'reconnecting':
      return {
        icon: 'tabler-refresh',
        text: `Reconnecting... (attempt ${props.reconnectAttempts})`,
        color: 'warning',
        show: true,
      }
    case 'disconnected':
      return {
        icon: 'tabler-plug-off',
        text: 'Disconnected',
        color: 'error',
        show: true,
      }
    default:
      return {
        icon: 'tabler-wifi',
        text: 'Connected',
        color: 'success',
        show: false,
      }
  }
})
</script>

<template>
  <Transition name="slide-y">
    <VChip
      v-if="statusConfig.show || pendingSubmissions > 0"
      :color="statusConfig.color"
      size="small"
      variant="tonal"
      class="connection-status"
    >
      <VIcon
        :icon="statusConfig.icon"
        start
        size="16"
      />
      {{ statusConfig.text }}
      <template v-if="pendingSubmissions > 0">
        &middot; {{ pendingSubmissions }} queued
      </template>
    </VChip>
  </Transition>
</template>

<style scoped>
.connection-status {
  position: fixed;
  top: calc(var(--safe-area-top, 0px) + 8px);
  left: 50%;
  transform: translateX(-50%);
  z-index: 9999;
}

.slide-y-enter-active,
.slide-y-leave-active {
  transition: all 0.3s ease;
}

.slide-y-enter-from,
.slide-y-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(-20px);
}
</style>
