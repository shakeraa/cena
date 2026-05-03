<script setup lang="ts">
interface Props {
  userId: string
}

const props = defineProps<Props>()

interface ActivityItem {
  id: string
  timestamp: string
  actionType: string
  description: string
}

const activities = ref<ActivityItem[]>([])
const isLoading = ref(true)
const loadError = ref('')

const fetchActivity = async () => {
  isLoading.value = true
  loadError.value = ''

  try {
    const data = await $api(`/admin/users/${props.userId}/activity`)

    activities.value = data.activities ?? []
  }
  catch (e: any) {
    loadError.value = e?.data?.message ?? 'Failed to load activity log'
    console.error('Failed to fetch activity', e)
  }
  finally {
    isLoading.value = false
  }
}

fetchActivity()

const resolveActionColor = (actionType: string) => {
  const map: Record<string, string> = {
    login: 'success',
    logout: 'secondary',
    password_change: 'warning',
    profile_update: 'info',
    role_change: 'error',
    session_created: 'primary',
    content_submitted: 'info',
    assessment_completed: 'success',
    suspended: 'error',
    activated: 'success',
  }

  return map[actionType] ?? 'primary'
}

const resolveActionIcon = (actionType: string) => {
  const map: Record<string, string> = {
    login: 'tabler-login',
    logout: 'tabler-logout',
    password_change: 'tabler-key',
    profile_update: 'tabler-user-edit',
    role_change: 'tabler-shield',
    session_created: 'tabler-device-desktop',
    content_submitted: 'tabler-file-upload',
    assessment_completed: 'tabler-clipboard-check',
    suspended: 'tabler-ban',
    activated: 'tabler-check',
  }

  return map[actionType] ?? 'tabler-activity'
}

const formatActionLabel = (actionType: string) => {
  return actionType.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase())
}

const formatTimestamp = (timestamp: string) => {
  if (!timestamp) return '--'

  const date = new Date(timestamp)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  const diffHours = Math.floor(diffMs / 3600000)
  const diffDays = Math.floor(diffMs / 86400000)

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins} min ago`
  if (diffHours < 24) return `${diffHours} hours ago`
  if (diffDays < 7) return `${diffDays} days ago`

  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard title="Activity Log">
        <VCardText>
          <div
            v-if="isLoading"
            class="d-flex justify-center pa-8"
          >
            <VProgressCircular indeterminate />
          </div>

          <VAlert
            v-else-if="loadError"
            type="error"
            variant="tonal"
            :text="loadError"
          />

          <div
            v-else-if="activities.length === 0"
            class="text-center pa-8 text-medium-emphasis"
          >
            No activity recorded for this user.
          </div>

          <VTimeline
            v-else
            side="end"
            align="start"
            line-inset="8"
            truncate-line="start"
            density="compact"
          >
            <VTimelineItem
              v-for="activity in activities"
              :key="activity.id"
              :dot-color="resolveActionColor(activity.actionType)"
              size="x-small"
            >
              <div class="d-flex justify-space-between align-center gap-2 flex-wrap mb-1">
                <span class="app-timeline-title font-weight-medium">
                  {{ formatActionLabel(activity.actionType) }}
                </span>
                <span class="app-timeline-meta text-sm text-medium-emphasis">
                  {{ formatTimestamp(activity.timestamp) }}
                </span>
              </div>

              <div class="app-timeline-text text-body-2 mt-1">
                {{ activity.description }}
              </div>
            </VTimelineItem>
          </VTimeline>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>
