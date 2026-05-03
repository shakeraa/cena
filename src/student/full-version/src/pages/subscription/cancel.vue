<script setup lang="ts">
// =============================================================================
// Cena Platform — Subscription cancel page (EPIC-PRR-I PRR-292)
//
// Stripe redirects here when the user bails on the hosted checkout. No state
// was written on our side — user can try again from /pricing. Positive
// framing, no scarcity.
// =============================================================================

import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: false,
    requiresOnboarded: false,
    public: true,
    title: 'nav.pricing',
    hideSidebar: false,
    breadcrumbs: false,
  },
})

const { t } = useI18n()
const router = useRouter()

const backToPricing = () => router.push('/pricing')
const goHome = () => router.push('/home')
</script>

<template>
  <div class="subscription-cancel pa-4 pa-md-8" data-testid="subscription-cancel-page">
    <VCard class="cancel-card pa-8 mx-auto" max-width="520">
      <div class="text-center">
        <VIcon
          icon="tabler-arrow-back-up"
          color="primary"
          size="48"
          class="mb-4"
        />
        <h1 class="text-h5 mb-2">
          {{ t('subscriptionCancel.title') }}
        </h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
          {{ t('subscriptionCancel.subtitle') }}
        </p>
        <div class="d-flex justify-center ga-2 flex-wrap">
          <VBtn color="primary" variant="flat" @click="backToPricing">
            {{ t('subscriptionCancel.backToPricing') }}
          </VBtn>
          <VBtn variant="outlined" @click="goHome">
            {{ t('subscriptionCancel.goHome') }}
          </VBtn>
        </div>
      </div>
    </VCard>
  </div>
</template>

<style scoped>
.cancel-card {
  margin-block-start: 4rem;
}
</style>
