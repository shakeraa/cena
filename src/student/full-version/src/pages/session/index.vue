<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import SessionSetupForm from '@/components/session/SessionSetupForm.vue'
import { useApiMutation } from '@/composables/useApiMutation'
import type { SessionStartRequest, SessionStartResponse } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.startSession',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

const error = ref<string | null>(null)

const { execute: startSession, loading: starting } = useApiMutation<
  SessionStartResponse,
  SessionStartRequest
>('/api/sessions/start', 'POST')

async function handleSubmit(payload: SessionStartRequest) {
  error.value = null
  try {
    const res = await startSession(payload)

    await router.push(`/session/${res.sessionId}`)
  }
  catch (err) {
    error.value = (err as Error).message || t('error.serverError')
  }
}
</script>

<template>
  <div
    class="session-setup-page pa-4"
    data-testid="session-setup-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('session.setup.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('session.setup.subtitle') }}
    </p>

    <VCard
      variant="outlined"
      class="pa-6"
    >
      <VAlert
        v-if="error"
        type="error"
        variant="tonal"
        class="mb-4"
        data-testid="setup-error"
      >
        {{ error }}
      </VAlert>

      <SessionSetupForm
        :loading="starting"
        @submit="handleSubmit"
      />
    </VCard>
  </div>
</template>

<style scoped>
.session-setup-page {
  max-inline-size: 720px;
  margin-inline: auto;
}
</style>
