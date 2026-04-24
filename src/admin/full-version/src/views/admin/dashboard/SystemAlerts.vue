<script setup lang="ts">
import { useApi } from '@/composables/useApi'

interface SystemAlert {
  id: string
  severity: 'error' | 'warning' | 'info' | 'success'
  title: string
  message: string
  timestamp: string
}

interface AlertsResponse {
  data: SystemAlert[]
}

const { data: rawData, isFetching, error } = useApi<AlertsResponse>(
  '/admin/dashboard/alerts',
)

const alerts = computed<SystemAlert[]>(() => {
  const items = rawData.value?.data ?? rawData.value
  if (!Array.isArray(items)) return []
  return items
})

const severityIcon = (severity: string): string => {
  switch (severity) {
    case 'error': return 'tabler-alert-circle'
    case 'warning': return 'tabler-alert-triangle'
    case 'info': return 'tabler-info-circle'
    case 'success': return 'tabler-circle-check'
    default: return 'tabler-info-circle'
  }
}

const formatTimestamp = (ts: string): string => {
  const date = new Date(ts)
  if (isNaN(date.getTime())) return ts
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMin = Math.floor(diffMs / 60000)

  if (diffMin < 1) return 'Just now'
  if (diffMin < 60) return `${diffMin}m ago`

  const diffHr = Math.floor(diffMin / 60)
  if (diffHr < 24) return `${diffHr}h ago`

  const diffDays = Math.floor(diffHr / 24)
  return `${diffDays}d ago`
}
</script>

<template>
  <VCard>
    <VCardItem>
      <template #prepend>
        <VIcon
          icon="tabler-alert-triangle"
          size="24"
          color="high-emphasis"
          class="me-1"
        />
      </template>
      <VCardTitle>System Alerts</VCardTitle>
    </VCardItem>

    <VCardText>
      <VProgressLinear
        v-if="isFetching"
        indeterminate
        color="primary"
        class="mb-4"
      />

      <VAlert
        v-else-if="error"
        type="error"
        variant="tonal"
        class="mb-2"
      >
        Failed to load system alerts.
      </VAlert>

      <template v-else-if="alerts.length > 0">
        <VAlert
          v-for="alert in alerts"
          :key="alert.id"
          :type="alert.severity"
          variant="tonal"
          class="mb-3"
          density="compact"
        >
          <template #prepend>
            <VIcon :icon="severityIcon(alert.severity)" />
          </template>
          <div class="d-flex justify-space-between align-start">
            <div>
              <div class="font-weight-medium">
                {{ alert.title }}
              </div>
              <div class="text-sm mt-1">
                {{ alert.message }}
              </div>
            </div>
            <span class="text-caption text-no-wrap ms-2">
              {{ formatTimestamp(alert.timestamp) }}
            </span>
          </div>
        </VAlert>
      </template>

      <div
        v-else
        class="d-flex align-center justify-center pa-6"
      >
        <div class="text-center">
          <VAvatar
            color="success"
            variant="tonal"
            size="48"
            class="mb-3"
          >
            <VIcon
              icon="tabler-circle-check"
              size="28"
            />
          </VAvatar>
          <p class="text-body-1 mb-0">
            All systems healthy
          </p>
          <p class="text-sm text-disabled mb-0">
            No active alerts
          </p>
        </div>
      </div>
    </VCardText>
  </VCard>
</template>
