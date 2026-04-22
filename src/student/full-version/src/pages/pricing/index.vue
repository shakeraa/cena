<script setup lang="ts">
// =============================================================================
// Cena Platform — Pricing page (EPIC-PRR-I PRR-290, ADR-0057)
//
// Public retail pricing card. Anonymous access OK — parents evaluate before
// sign-up. Three retail tiers (Basic / Plus / Premium) with monthly/annual
// toggle, sibling-discount footer, 30-day money-back guarantee badge.
//
// Selection → POST /api/me/subscription/checkout-session when the user has
// a session cookie, otherwise redirect to /register with returnTo=/pricing.
// =============================================================================

import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { usePricingCatalog } from '@/composables/usePricingCatalog'
import type { RetailTierDto } from '@/composables/usePricingCatalog'
import TierCard from '@/components/pricing/TierCard.vue'
import GuaranteeBadge from '@/components/pricing/GuaranteeBadge.vue'
import SiblingDiscountNote from '@/components/pricing/SiblingDiscountNote.vue'

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
const { tiers, siblingDiscount, loading, error, formatPriceAgorot } = usePricingCatalog()

const annual = ref(false)

// Premium is the marketing-recommended target tier (per ADR-0057 §review).
const isRecommended = (tier: RetailTierDto) => tier.tierId === 'Premium'

const handleSelect = (tier: RetailTierDto) => {
  // Non-auth visitors are redirected to register with returnTo carrying the
  // selection. Authenticated parents would hit the checkout-session endpoint
  // directly; that path is owned by the account/checkout flow (PRR-292).
  const params = new URLSearchParams({
    tier: tier.tierId,
    cycle: annual.value ? 'Annual' : 'Monthly',
  })
  router.push(`/register?returnTo=/pricing%3F${params.toString()}`)
}
</script>

<template>
  <div class="pricing-page pa-4 pa-md-8" data-testid="pricing-page">
    <div class="text-center mb-8">
      <h1 class="text-h3 font-weight-bold mb-2">
        {{ t('pricing.page.title') }}
      </h1>
      <p class="text-body-1 text-medium-emphasis mb-4">
        {{ t('pricing.page.subtitle') }}
      </p>

      <!-- Monthly / Annual toggle -->
      <VBtnToggle
        v-model="annual"
        mandatory
        color="primary"
        divided
        variant="outlined"
        class="mb-4"
        data-testid="pricing-cycle-toggle"
      >
        <VBtn :value="false" data-testid="pricing-cycle-monthly">
          {{ t('pricing.cycle.monthly') }}
        </VBtn>
        <VBtn :value="true" data-testid="pricing-cycle-annual">
          {{ t('pricing.cycle.annual') }}
          <VChip size="x-small" color="success" class="ms-2">
            {{ t('pricing.cycle.annualSavings') }}
          </VChip>
        </VBtn>
      </VBtnToggle>

      <div>
        <GuaranteeBadge />
      </div>
    </div>

    <!-- Loading / error / content -->
    <div
      v-if="loading"
      class="d-flex justify-center align-center"
      style="min-height: 20rem"
      data-testid="pricing-loading"
    >
      <VProgressCircular indeterminate color="primary" />
    </div>

    <VAlert
      v-else-if="error"
      type="error"
      variant="tonal"
      class="mb-4"
      data-testid="pricing-error"
    >
      {{ t('pricing.page.loadError') }}
    </VAlert>

    <template v-else>
      <!-- 3-tier grid -->
      <VRow class="pricing-grid">
        <VCol
          v-for="tier in tiers"
          :key="tier.tierId"
          cols="12"
          md="4"
        >
          <TierCard
            :tier="tier"
            :recommended="isRecommended(tier)"
            :annual="annual"
            :format-price="formatPriceAgorot"
            @select="handleSelect"
          />
        </VCol>
      </VRow>

      <!-- Sibling discount footer -->
      <SiblingDiscountNote
        v-if="siblingDiscount"
        :sibling-discount="siblingDiscount"
        :format-price="formatPriceAgorot"
      />
    </template>
  </div>
</template>

<style scoped>
.pricing-page {
  max-inline-size: 80rem;
  margin-inline: auto;
}
</style>
