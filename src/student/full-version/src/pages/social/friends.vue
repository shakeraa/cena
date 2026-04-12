<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import FriendRow from '@/components/social/FriendRow.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { $api } from '@/api/$api'
import type { FriendsListDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.friends',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const friendsQuery = useApiQuery<FriendsListDto>('/api/social/friends')

async function handleAccept(requestId: string) {
  try {
    await $api(`/api/social/friends/${requestId}/accept`, { method: 'POST' as any, body: {} as any })
    friendsQuery.refresh()
  }
  catch {
    // swallow
  }
}
</script>

<template>
  <div
    class="friends-page pa-4"
    data-testid="friends-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('social.friends.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('social.friends.subtitle') }}
    </p>

    <div
      v-if="friendsQuery.loading.value && !friendsQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="friends-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="friendsQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="friends-error"
    >
      {{ t(friendsQuery.error.value.i18nKey ?? 'social.friendsUnavailable') }}
    </VAlert>

    <template v-else-if="friendsQuery.data.value">
      <section
        v-if="friendsQuery.data.value.pendingRequests.length > 0"
        class="mb-6"
        data-testid="pending-requests-section"
      >
        <h2 class="text-h6 mb-3">
          {{ t('social.friends.pendingTitle') }}
        </h2>
        <VCard
          v-for="req in friendsQuery.data.value.pendingRequests"
          :key="req.requestId"
          variant="outlined"
          class="pa-3 mb-2"
          :data-testid="`request-${req.requestId}`"
        >
          <div class="d-flex align-center">
            <VAvatar
              color="warning"
              size="40"
              class="me-3"
            >
              <VIcon
                icon="tabler-user-plus"
                size="20"
                color="white"
                aria-hidden="true"
              />
            </VAvatar>
            <div class="flex-grow-1 min-w-0">
              <div class="text-subtitle-1 font-weight-medium text-truncate">
                {{ req.fromDisplayName }}
              </div>
              <div class="text-caption text-medium-emphasis">
                {{ t('social.friends.wantsToConnect') }}
              </div>
            </div>
            <VBtn
              color="primary"
              size="small"
              :data-testid="`accept-${req.requestId}`"
              @click="handleAccept(req.requestId)"
            >
              {{ t('social.friends.accept') }}
            </VBtn>
          </div>
        </VCard>
      </section>

      <section data-testid="friends-list-section">
        <h2 class="text-h6 mb-3">
          {{ t('social.friends.listTitle', { count: friendsQuery.data.value.friends.length }) }}
        </h2>
        <FriendRow
          v-for="f in friendsQuery.data.value.friends"
          :key="f.studentId"
          :friend="f"
        />
      </section>
    </template>
  </div>
</template>

<style scoped>
.friends-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
