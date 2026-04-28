<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import NotificationListItem from '@/components/notifications/NotificationListItem.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { $api } from '@/api/$api'
import type { NotificationListDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.notifications',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const listQuery = useApiQuery<NotificationListDto>('/api/notifications')

const unreadCount = computed(() => listQuery.data.value?.unreadCount ?? 0)

async function handleMarkRead(id: string) {
  try {
    await $api(`/api/notifications/${id}/read`, { method: 'POST' as any, body: {} as any })
    listQuery.refresh()
  }
  catch {
    // swallow
  }
}

async function handleMarkAllRead() {
  try {
    await $api('/api/notifications/mark-all-read', { method: 'POST' as any, body: {} as any })
    listQuery.refresh()
  }
  catch {
    // swallow
  }
}
</script>

<template>
  <div
    class="notifications-page pa-4"
    data-testid="notifications-page"
  >
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 mb-1">
          {{ t('notifications.title') }}
        </h1>
        <p class="text-body-1 text-medium-emphasis">
          {{ t('notifications.subtitle') }}
        </p>
      </div>
      <VBtn
        v-if="unreadCount > 0"
        variant="outlined"
        prepend-icon="tabler-checks"
        data-testid="mark-all-read"
        @click="handleMarkAllRead"
      >
        {{ t('notifications.markAllRead') }}
      </VBtn>
    </div>

    <div
      v-if="unreadCount > 0"
      class="mb-4 d-flex align-center ga-2"
      data-testid="unread-banner"
    >
      <VChip
        color="primary"
        variant="flat"
        prepend-icon="tabler-bell"
      >
        {{ t('notifications.unreadCount', { count: unreadCount }, { plural: unreadCount }) }}
      </VChip>
    </div>

    <div
      v-if="listQuery.loading.value && !listQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="notifications-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="listQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="notifications-error"
    >
      {{ t(listQuery.error.value.i18nKey ?? 'notifications.unavailable') }}
    </VAlert>

    <div
      v-else-if="listQuery.data.value && listQuery.data.value.items.length > 0"
      data-testid="notifications-list"
    >
      <NotificationListItem
        v-for="n in listQuery.data.value.items"
        :key="n.notificationId"
        :notification="n"
        @mark-read="handleMarkRead"
      />
    </div>

    <div
      v-else
      class="text-center py-12"
      data-testid="notifications-empty"
    >
      <VIcon
        icon="tabler-bell-off"
        size="56"
        class="text-medium-emphasis mb-4"
        aria-hidden="true"
      />
      <div class="text-h6 mb-2">
        {{ t('notifications.emptyTitle') }}
      </div>
      <div class="text-body-2 text-medium-emphasis">
        {{ t('notifications.emptySubtitle') }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.notifications-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
