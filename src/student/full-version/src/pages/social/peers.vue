<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PeerSolutionCard from '@/components/social/PeerSolutionCard.vue'
import ReportDialog from '@/components/social/ReportDialog.vue'
import BlockUserDialog from '@/components/social/BlockUserDialog.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { $api } from '@/api/$api'
import type { PeerSolutionListDto, ReportContentType } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.peerSolutions',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const solutionsQuery = useApiQuery<PeerSolutionListDto>('/api/social/peers/solutions')

const voteError = ref<string | null>(null)
const voteErrorVisible = ref(false)

// FIND-privacy-018: Report dialog state
const reportDialogOpen = ref(false)
const reportContentId = ref('')
const reportContentType = ref<ReportContentType>('peer-solution')

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
  solutionsQuery.refresh()
}

async function handleVote(solutionId: string, direction: 'up' | 'down') {
  try {
    await $api(`/api/social/peers/solutions/${solutionId}/vote`, {
      method: 'POST' as any,
      body: { direction } as any,
    })
    solutionsQuery.refresh()
  }
  catch (err) {
    console.error('[FIND-ux-024] peer-vote failed', { solutionId, direction, error: err })
    voteError.value = err instanceof Error ? err.message : t('social.peers.voteError')
    voteErrorVisible.value = true
  }
}
</script>

<template>
  <div
    class="peers-page pa-4"
    data-testid="peers-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('social.peers.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('social.peers.subtitle') }}
    </p>

    <div
      v-if="solutionsQuery.loading.value && !solutionsQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="peers-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="solutionsQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="peers-error"
    >
      {{ t(solutionsQuery.error.value.i18nKey ?? 'common.errorGeneric') }}
    </VAlert>

    <div
      v-else-if="solutionsQuery.data.value"
      data-testid="peers-list"
    >
      <PeerSolutionCard
        v-for="sol in solutionsQuery.data.value.solutions"
        :key="sol.solutionId"
        :solution="sol"
        @vote="handleVote"
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
      v-model="voteErrorVisible"
      color="error"
      timeout="4000"
      data-testid="vote-error-snackbar"
    >
      {{ voteError ?? t('social.peers.voteError') }}
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
.peers-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
