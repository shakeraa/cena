<script setup lang="ts">
import { useApi } from '@/composables/useApi'

interface ActivityItem {
  id: string
  timestamp: string
  adminName: string
  action: string
  description: string
}

interface ActivityResponse {
  data: ActivityItem[]
}

const { data: rawData, isFetching, error } = useApi<ActivityResponse>(
  '/admin/dashboard/recent-activity?limit=20',
)

const activities = computed<ActivityItem[]>(() => {
  const items = rawData.value?.data ?? rawData.value
  if (!Array.isArray(items)) return []
  return items
})

const actionColor = (action: string): string => {
  const lower = action.toLowerCase()
  if (lower.includes('create') || lower.includes('add')) return 'success'
  if (lower.includes('delete') || lower.includes('remove') || lower.includes('reject')) return 'error'
  if (lower.includes('update') || lower.includes('edit') || lower.includes('modify')) return 'info'
  if (lower.includes('approve') || lower.includes('publish')) return 'primary'
  if (lower.includes('login') || lower.includes('auth')) return 'warning'
  if (lower.includes('review')) return 'secondary'
  return 'primary'
}

const actionIcon = (action: string): string => {
  const lower = action.toLowerCase()
  if (lower.includes('create') || lower.includes('add')) return 'tabler-plus'
  if (lower.includes('delete') || lower.includes('remove')) return 'tabler-trash'
  if (lower.includes('update') || lower.includes('edit')) return 'tabler-pencil'
  if (lower.includes('approve') || lower.includes('publish')) return 'tabler-check'
  if (lower.includes('reject')) return 'tabler-x'
  if (lower.includes('login') || lower.includes('auth')) return 'tabler-login'
  if (lower.includes('review')) return 'tabler-eye'
  return 'tabler-activity'
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
  if (diffDays < 7) return `${diffDays}d ago`

  return date.toLocaleDateString()
}
</script>

<template>
  <VCard>
    <VCardItem>
      <template #prepend>
        <VIcon
          icon="tabler-list-details"
          size="24"
          color="high-emphasis"
          class="me-1"
        />
      </template>
      <VCardTitle>Recent Activity</VCardTitle>
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
        Failed to load recent activity.
      </VAlert>

      <template v-else-if="activities.length > 0">
        <VTimeline
          side="end"
          align="start"
          line-inset="8"
          truncate-line="start"
          density="compact"
        >
          <VTimelineItem
            v-for="item in activities"
            :key="item.id"
            :dot-color="actionColor(item.action)"
            size="x-small"
          >
            <div class="d-flex justify-space-between align-center gap-2 flex-wrap mb-1">
              <span class="app-timeline-title d-flex align-center gap-1">
                <VIcon
                  :icon="actionIcon(item.action)"
                  size="16"
                />
                {{ item.action }}
              </span>
              <span class="app-timeline-meta">{{ formatTimestamp(item.timestamp) }}</span>
            </div>

            <div class="app-timeline-text mt-1">
              <span class="font-weight-medium">{{ item.adminName }}</span>
              <span class="text-medium-emphasis"> - {{ item.description }}</span>
            </div>
          </VTimelineItem>
        </VTimeline>
      </template>

      <div
        v-else
        class="d-flex align-center justify-center pa-6"
      >
        <div class="text-center text-disabled">
          <VIcon
            icon="tabler-list-details"
            size="48"
            class="mb-2"
          />
          <p>No recent activity</p>
        </div>
      </div>
    </VCardText>
  </VCard>
</template>
