<script setup lang="ts">
export interface ServiceStatus {
  name: string
  status: 'healthy' | 'degraded' | 'down'
  uptimePercent: number
  lastCheckAt: string
}

const props = defineProps<{
  service: ServiceStatus
}>()

const statusColor = computed(() => {
  const map: Record<string, string> = {
    healthy: 'success',
    degraded: 'warning',
    down: 'error',
  }

  return map[props.service.status] ?? 'secondary'
})

const statusIcon = computed(() => {
  const map: Record<string, string> = {
    healthy: 'tabler-circle-check',
    degraded: 'tabler-alert-triangle',
    down: 'tabler-circle-x',
  }

  return map[props.service.status] ?? 'tabler-help'
})

const statusLabel = computed(() => {
  const map: Record<string, string> = {
    healthy: 'Healthy',
    degraded: 'Degraded',
    down: 'Down',
  }

  return map[props.service.status] ?? props.service.status
})

const formattedLastCheck = computed(() => {
  if (!props.service.lastCheckAt)
    return 'Never'

  return new Date(props.service.lastCheckAt).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
})
</script>

<template>
  <VCard
    :class="`border-s-4 border-${statusColor}`"
    variant="outlined"
  >
    <VCardText class="d-flex align-center gap-x-4">
      <VAvatar
        :color="statusColor"
        variant="tonal"
        rounded
        size="44"
      >
        <VIcon
          :icon="statusIcon"
          size="26"
        />
      </VAvatar>

      <div class="flex-grow-1">
        <div class="d-flex justify-space-between align-center mb-1">
          <h6 class="text-h6">
            {{ service.name }}
          </h6>
          <VChip
            :color="statusColor"
            label
            size="small"
          >
            {{ statusLabel }}
          </VChip>
        </div>

        <div class="d-flex justify-space-between align-center">
          <div class="text-body-2 text-medium-emphasis">
            Uptime: {{ service.uptimePercent.toFixed(2) }}%
          </div>
          <div class="text-body-2 text-disabled">
            Last check: {{ formattedLastCheck }}
          </div>
        </div>
      </div>
    </VCardText>
  </VCard>
</template>
