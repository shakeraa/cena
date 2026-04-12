<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ClassFeedItemCard from '@/components/social/ClassFeedItemCard.vue'
import ReportDialog from '@/components/social/ReportDialog.vue'
import BlockUserDialog from '@/components/social/BlockUserDialog.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import type { ClassFeedDto, ReportContentType } from '@/api/types/common'

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
const reactMutation = useApiMutation<{ ok: boolean; newCount: number }, { itemId: string; reactionType: string }>('/api/social/reactions', 'POST')

const reactionErrorVisible = ref(false)

// FIND-privacy-018: Report dialog state
const reportDialogOpen = ref(false)
const reportContentId = ref('')
const reportContentType = ref<ReportContentType>('feed-item')

// FIND-privacy-018: Block dialog state
const blockDialogOpen = ref(false)
const blockTargetId = ref('')
const blockTargetName = ref('')

const reportSuccessVisible = ref(false)
const blockSuccessVisible = ref(false)

function handleReport(contentId: string, contentType: ReportContentType) {
  reportContentId.value = contentId
  reportContentType.value = contentType
  reportDialogOpen.value = true
}

function handleBlock(studentId: string, displayName: string) {
  blockTargetId.value = studentId
  blockTargetName.value = displayName
  blockDialogOpen.value = true
}

function onReported() {
  reportSuccessVisible.value = true
}

function onBlocked() {
  blockSuccessVisible.value = true
  feedQuery.refresh()
}

async function handleReact(itemId: string) {
  try {
    await reactMutation.execute({ itemId, reactionType: 'heart' })
    feedQuery.refresh()
  }
  catch (err) {
    console.error('[FIND-ux-024] class-feed reaction failed', { itemId, error: err })
    reactionErrorVisible.value = true
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
        @report="handleReport"
        @block="handleBlock"
      />
    </div>

    <!-- FIND-privacy-018: Report & Block dialogs -->
    <ReportDialog
      v-model="reportDialogOpen"
      :content-type="reportContentType"
      :content-id="reportContentId"
      @reported="onReported"
    />
    <BlockUserDialog
      v-model="blockDialogOpen"
      :target-student-id="blockTargetId"
      :target-display-name="blockTargetName"
      @blocked="onBlocked"
    />

    <VSnackbar
      v-model="reactionErrorVisible"
      color="error"
      timeout="4000"
      data-testid="reaction-error-snackbar"
    >
      {{ reactMutation.error.value?.message ?? t('social.feed.reactionError') }}
    </VSnackbar>
    <VSnackbar
      v-model="reportSuccessVisible"
      color="success"
      timeout="4000"
      data-testid="report-success-snackbar"
    >
      {{ t('social.report.success') }}
    </VSnackbar>
    <VSnackbar
      v-model="blockSuccessVisible"
      color="success"
      timeout="4000"
      data-testid="block-success-snackbar"
    >
      {{ t('social.block.success') }}
    </VSnackbar>
  </div>
</template>

<style scoped>
.social-feed-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
