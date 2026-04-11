<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import ConceptDetailCard from '@/components/knowledge/ConceptDetailCard.vue'
import PrerequisiteChain from '@/components/knowledge/PrerequisiteChain.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import type { ConceptDetailDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.concept',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const conceptId = String(route.params.id)

const detailQuery = useApiQuery<ConceptDetailDto>(`/api/content/concepts/${conceptId}`)

function handleStartSession() {
  router.push('/session')
}
</script>

<template>
  <div
    class="concept-page pa-4"
    data-testid="concept-page"
  >
    <div
      v-if="detailQuery.loading.value && !detailQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="concept-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="detailQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="concept-error"
    >
      {{ detailQuery.error.value.message }}
    </VAlert>

    <template v-else-if="detailQuery.data.value">
      <ConceptDetailCard :concept="detailQuery.data.value" />

      <VRow class="mt-4">
        <VCol
          cols="12"
          md="6"
        >
          <PrerequisiteChain
            :title="t('knowledgeGraph.detail.prerequisitesTitle')"
            :concept-ids="detailQuery.data.value.prerequisites"
            icon="tabler-arrow-left"
            :empty-message="t('knowledgeGraph.detail.prerequisitesEmpty')"
          />
        </VCol>
        <VCol
          cols="12"
          md="6"
        >
          <PrerequisiteChain
            :title="t('knowledgeGraph.detail.dependenciesTitle')"
            :concept-ids="detailQuery.data.value.dependencies"
            icon="tabler-arrow-right"
            :empty-message="t('knowledgeGraph.detail.dependenciesEmpty')"
          />
        </VCol>
      </VRow>

      <div class="d-flex justify-center mt-6">
        <VBtn
          v-if="detailQuery.data.value.status !== 'locked'"
          color="primary"
          size="large"
          prepend-icon="tabler-player-play"
          data-testid="concept-start-session"
          @click="handleStartSession"
        >
          {{ t('knowledgeGraph.detail.startSession') }}
        </VBtn>
      </div>
    </template>
  </div>
</template>

<style scoped>
.concept-page {
  max-inline-size: 1100px;
  margin-inline: auto;
}
</style>
