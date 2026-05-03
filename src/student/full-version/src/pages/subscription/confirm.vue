<script setup lang="ts">
// =============================================================================
// Cena Platform — Subscription confirm page (EPIC-PRR-I PRR-292)
//
// Stripe redirects here after successful checkout. The actual SubscriptionActivated_V1
// event is written by the webhook handler (which may land before OR after the
// user sees this page — webhook is fast, usually already done). We poll
// GET /api/me/subscription up to ~10s so the displayed state is accurate.
// =============================================================================

import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApi } from '@/composables/useApi'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: false,
    public: false,
    title: 'nav.subscription',
    hideSidebar: false,
    breadcrumbs: false,
  },
})

const { t } = useI18n()
const router = useRouter()

const status = ref<'polling' | 'active' | 'pending' | 'error'>('polling')
const tier = ref<string | null>(null)
const renewsAt = ref<string | null>(null)

/** Poll /api/me/subscription a few times — webhook may arrive with a small delay. */
async function pollForActivation() {
  const maxAttempts = 10
  for (let i = 0; i < maxAttempts; i++) {
    const { data } = await useApi('/me/subscription').get().json<{
      status: string
      currentTier: string | null
      renewsAt: string | null
    }>()
    if (data.value?.status === 'Active') {
      status.value = 'active'
      tier.value = data.value.currentTier
      renewsAt.value = data.value.renewsAt
      return
    }
    await new Promise(r => setTimeout(r, 1000))
  }
  status.value = 'pending'
}

const goHome = () => router.push('/home')

onMounted(() => {
  pollForActivation().catch(() => {
    status.value = 'error'
  })
})
</script>

<template>
  <div class="subscription-confirm pa-4 pa-md-8" data-testid="subscription-confirm-page">
    <VCard class="confirm-card pa-8 mx-auto" max-width="560">
      <!-- Polling -->
      <div
        v-if="status === 'polling'"
        class="text-center"
        data-testid="subscription-confirm-polling"
      >
        <VProgressCircular indeterminate color="primary" size="48" class="mb-4" />
        <h1 class="text-h5 mb-2">
          {{ t('subscriptionConfirm.polling.title') }}
        </h1>
        <p class="text-body-2 text-medium-emphasis">
          {{ t('subscriptionConfirm.polling.subtitle') }}
        </p>
      </div>

      <!-- Active (success) -->
      <div
        v-else-if="status === 'active'"
        class="text-center"
        data-testid="subscription-confirm-active"
      >
        <VIcon
          icon="tabler-circle-check"
          color="success"
          size="64"
          class="mb-4"
        />
        <h1 class="text-h4 font-weight-bold mb-2">
          {{ t('subscriptionConfirm.active.title') }}
        </h1>
        <p class="text-body-1 mb-4">
          {{ t('subscriptionConfirm.active.subtitle', { tier: tier ?? '' }) }}
        </p>
        <p
          v-if="renewsAt"
          class="text-body-2 text-medium-emphasis mb-6"
        >
          {{ t('subscriptionConfirm.active.renewsAt', { date: new Date(renewsAt).toLocaleDateString() }) }}
        </p>
        <VBtn color="primary" size="large" @click="goHome">
          {{ t('subscriptionConfirm.active.cta') }}
        </VBtn>
      </div>

      <!-- Pending (polling exhausted, webhook probably delayed) -->
      <div
        v-else-if="status === 'pending'"
        class="text-center"
        data-testid="subscription-confirm-pending"
      >
        <VIcon
          icon="tabler-clock"
          color="warning"
          size="48"
          class="mb-4"
        />
        <h1 class="text-h5 mb-2">
          {{ t('subscriptionConfirm.pending.title') }}
        </h1>
        <p class="text-body-2 mb-4">
          {{ t('subscriptionConfirm.pending.subtitle') }}
        </p>
        <VBtn variant="outlined" @click="goHome">
          {{ t('subscriptionConfirm.pending.cta') }}
        </VBtn>
      </div>

      <!-- Error -->
      <VAlert
        v-else
        type="error"
        variant="tonal"
        data-testid="subscription-confirm-error"
      >
        {{ t('subscriptionConfirm.error') }}
      </VAlert>
    </VCard>
  </div>
</template>

<style scoped>
.confirm-card {
  margin-block-start: 4rem;
}
</style>
