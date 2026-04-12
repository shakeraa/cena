<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PeerSolutionCard from '@/components/social/PeerSolutionCard.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { $api } from '@/api/$api'
import type { PeerSolutionListDto } from '@/api/types/common'

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
      />
    </div>

    <VSnackbar
      v-model="voteErrorVisible"
      color="error"
      timeout="4000"
      data-testid="vote-error-snackbar"
    >
      {{ voteError ?? t('social.peers.voteError') }}
    </VSnackbar>
  </div>
</template>

<style scoped>
.peers-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
