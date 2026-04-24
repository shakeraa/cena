<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import SessionSummaryCard from '@/components/session/SessionSummaryCard.vue'
import { $api } from '@/api/$api'
import type { SessionCompletedDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.sessionSummary',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const sessionId = String(route.params.sessionId)

const summary = ref<SessionCompletedDto | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    summary.value = await $api<SessionCompletedDto>(
      `/api/sessions/${sessionId}/complete`,
      { method: 'POST' as any, body: {} as any },
    )
  }
  catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
  finally {
    loading.value = false
  }
})

function handleStartAnother() {
  router.push('/session')
}

function handleHome() {
  router.push('/home')
}
</script>

<template>
  <div
    class="session-summary-page pa-4"
    data-testid="session-summary-page"
  >
    <div
      v-if="loading"
      class="d-flex justify-center py-12"
      data-testid="summary-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="error"
      type="error"
      variant="tonal"
      data-testid="summary-error"
    >
      {{ error }}
    </VAlert>

    <template v-else-if="summary">
      <SessionSummaryCard :summary="summary" />

      <div class="d-flex flex-column flex-sm-row justify-center ga-3 mt-6">
        <VBtn
          color="primary"
          size="large"
          prepend-icon="tabler-player-play"
          data-testid="summary-start-another"
          @click="handleStartAnother"
        >
          {{ t('session.summary.startAnother') }}
        </VBtn>
        <VBtn
          variant="text"
          size="large"
          prepend-icon="tabler-home"
          data-testid="summary-home"
          @click="handleHome"
        >
          {{ t('session.summary.backToHome') }}
        </VBtn>
      </div>
    </template>
  </div>
</template>

<style scoped>
.session-summary-page {
  max-inline-size: 900px;
  margin-inline: auto;
}
</style>
