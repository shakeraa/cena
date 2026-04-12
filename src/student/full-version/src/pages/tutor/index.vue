<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import TutorThreadListItem from '@/components/tutor/TutorThreadListItem.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import type { CreateThreadResponse, TutorThreadListDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.tutor',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

const threadsQuery = useApiQuery<TutorThreadListDto>('/api/tutor/threads')

const createMutation = useApiMutation<CreateThreadResponse, { initialMessage?: string }>('/api/tutor/threads', 'POST')

const creating = ref(false)

async function createNewThread() {
  creating.value = true
  try {
    const res = await createMutation.execute({ initialMessage: undefined })

    await router.push(`/tutor/${res.threadId}`)
  }
  catch {
    // error surfaced via createMutation.error
  }
  finally {
    creating.value = false
  }
}
</script>

<template>
  <div
    class="tutor-page pa-4"
    data-testid="tutor-page"
  >
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h1 class="text-h4 mb-1">
          {{ t('tutor.listPage.title') }}
        </h1>
        <p class="text-body-1 text-medium-emphasis">
          {{ t('tutor.listPage.subtitle') }}
        </p>
      </div>
      <VBtn
        color="primary"
        prepend-icon="tabler-plus"
        :loading="creating"
        data-testid="tutor-new-thread"
        @click="createNewThread"
      >
        {{ t('tutor.listPage.newThread') }}
      </VBtn>
    </div>

    <div
      v-if="threadsQuery.loading.value && !threadsQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="tutor-list-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="threadsQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="tutor-list-error"
    >
      {{ t(threadsQuery.error.value.i18nKey ?? 'tutor.threadsUnavailable') }}
    </VAlert>

    <div
      v-else-if="threadsQuery.data.value && threadsQuery.data.value.items.length === 0"
      class="text-center py-12"
      data-testid="tutor-list-empty"
    >
      <VIcon
        icon="tabler-message-off"
        size="56"
        class="text-medium-emphasis mb-4"
        aria-hidden="true"
      />
      <div class="text-h6 mb-2">
        {{ t('tutor.listPage.emptyTitle') }}
      </div>
      <div class="text-body-2 text-medium-emphasis">
        {{ t('tutor.listPage.emptySubtitle') }}
      </div>
    </div>

    <div
      v-else-if="threadsQuery.data.value"
      class="tutor-page__list"
      data-testid="tutor-list"
    >
      <TutorThreadListItem
        v-for="thread in threadsQuery.data.value.items"
        :key="thread.threadId"
        :thread="thread"
      />
    </div>
  </div>
</template>

<style scoped>
.tutor-page {
  max-inline-size: 800px;
  margin-inline: auto;
}

.tutor-page__list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
</style>
