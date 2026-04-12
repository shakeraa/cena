<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import ClassFeedItemCard from '@/components/social/ClassFeedItemCard.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import type { ClassFeedDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.classFeed',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const feedQuery = useApiQuery<ClassFeedDto>('/api/social/class-feed')
const reactMutation = useApiMutation<{ ok: boolean }, { itemId: string; reactionType: string }>('/api/social/reactions', 'POST')

async function handleReact(itemId: string) {
  try {
    await reactMutation.execute({ itemId, reactionType: 'heart' })
    feedQuery.refresh()
  }
  catch {
    // error surfaced via reactMutation.error
  }
}
</script>

<template>
  <div
    class="social-feed-page pa-4"
    data-testid="social-feed-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('social.feed.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('social.feed.subtitle') }}
    </p>

    <div
      v-if="feedQuery.loading.value && !feedQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="feed-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="feedQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="feed-error"
    >
      {{ t(feedQuery.error.value.i18nKey ?? 'social.feedUnavailable') }}
    </VAlert>

    <div
      v-else-if="feedQuery.data.value"
      data-testid="feed-list"
    >
      <ClassFeedItemCard
        v-for="item in feedQuery.data.value.items"
        :key="item.itemId"
        :item="item"
        @react="handleReact"
      />
    </div>
  </div>
</template>

<style scoped>
.social-feed-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
