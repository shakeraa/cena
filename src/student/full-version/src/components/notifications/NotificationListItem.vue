<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { NotificationItem } from '@/api/types/common'

interface Props {
  notification: NotificationItem
}

const props = defineProps<Props>()
const emit = defineEmits<{
  markRead: [id: string]
}>()

const { t } = useI18n()

const kindColor = computed(() => {
  switch (props.notification.kind) {
    case 'badge': return 'warning'
    case 'xp': return 'primary'
    case 'streak': return 'error'
    case 'friend-request': return 'info'
    case 'review-due': return 'success'
    case 'system': return 'grey'
    default: return 'grey'
  }
})

const defaultIcon = computed(() => props.notification.iconName || 'tabler-bell')

const relativeTime = computed(() => {
  const posted = new Date(props.notification.createdAt).getTime()
  const diffMin = Math.floor((Date.now() - posted) / 60_000)
  const diffHour = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHour / 24)

  if (diffMin < 1) return t('notifications.justNow')
  if (diffMin < 60) return t('notifications.minutesAgo', { count: diffMin })
  if (diffHour < 24) return t('notifications.hoursAgo', { count: diffHour })

  return t('notifications.daysAgo', { count: diffDay })
})

function handleClick() {
  if (!props.notification.read)
    emit('markRead', props.notification.notificationId)
}
</script>

<template>
  <VCard
    :variant="notification.read ? 'outlined' : 'flat'"
    :color="notification.read ? undefined : 'surface-variant'"
    class="notification-item pa-4 mb-2 cursor-pointer"
    :class="{ 'notification-item--unread': !notification.read }"
    :data-testid="`notification-${notification.notificationId}`"
    :data-read="notification.read"
    @click="handleClick"
  >
    <div class="d-flex align-start">
      <VAvatar
        :color="kindColor"
        size="40"
        class="me-3"
      >
        <VIcon
          :icon="defaultIcon"
          size="20"
          color="white"
          aria-hidden="true"
        />
      </VAvatar>
      <div class="flex-grow-1 min-w-0">
        <div class="d-flex align-center ga-2 mb-1">
          <span class="text-subtitle-1 font-weight-medium">
            {{ notification.title }}
          </span>
          <VBadge
            v-if="!notification.read"
            color="primary"
            dot
            inline
          />
        </div>
        <div class="text-body-2 text-medium-emphasis">
          {{ notification.body }}
        </div>
        <div class="text-caption text-medium-emphasis mt-2">
          {{ relativeTime }}
        </div>
      </div>
    </div>
  </VCard>
</template>

<style scoped>
.notification-item--unread {
  border-inline-start: 3px solid rgb(var(--v-theme-primary));
}
</style>
