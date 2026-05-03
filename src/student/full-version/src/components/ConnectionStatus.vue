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

/* RDY-030b: prefers-reduced-motion guard (WCAG 2.3.3).
   Component-local animations/transitions reduced to an imperceptible
   0.01ms so vestibular-sensitive users don't trigger motion-related
   symptoms. Complements the global reset in styles.scss. */
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
</style>
