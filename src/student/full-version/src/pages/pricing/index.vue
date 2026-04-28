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

import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { usePricingCatalog } from '@/composables/usePricingCatalog'
import type { RetailTierDto } from '@/composables/usePricingCatalog'
import { useCheckoutSession } from '@/composables/useCheckoutSession'
import { $api } from '@/utils/api'
import TierCard from '@/components/pricing/TierCard.vue'
import GuaranteeBadge from '@/components/pricing/GuaranteeBadge.vue'
import SiblingDiscountNote from '@/components/pricing/SiblingDiscountNote.vue'

interface ApplicableDiscount {
  assignmentId: string
  discountKind: 'PercentOff' | 'AmountOff'
  discountValue: number
  durationMonths: number
  status: string
}

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
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const meStore = useMeStore()
const { tiers, siblingDiscount, loading, error, formatPriceAgorot } = usePricingCatalog()
const { startCheckout, submitting: checkoutSubmitting, error: checkoutError } = useCheckoutSession()

const annual = ref(false)
const redirectingToStripe = ref(false)

// Per-user discount-codes feature: fetched on mount when the caller is
// authenticated. Anonymous visitors see no banner.
const applicableDiscount = ref<ApplicableDiscount | null>(null)

const discountBannerText = computed(() => {
  const d = applicableDiscount.value
  if (!d) return ''
  const months = d.durationMonths
  if (d.discountKind === 'PercentOff') {
    // basis points → percent
    const pct = (d.discountValue / 100).toString().replace(/\.0+$/, '')
    return t('pricing.discount.bannerPercent', {
      percent: pct,
      months,
    })
  }
  // agorot → shekels
  const ils = (d.discountValue / 100).toFixed(2).replace(/\.00$/, '')
  return t('pricing.discount.bannerAmount', {
    amount: ils,
    months,
  })
})

/**
 * Apply the active discount to a displayed monthly/annual price (in agorot).
 * Used by the banner subtext to show "your effective price" — the actual
 * billed amount is computed by Stripe from the attached coupon.
 */
function applyDiscountToPriceAgorot(priceAgorot: number): number {
  const d = applicableDiscount.value
  if (!d) return priceAgorot
  if (d.discountKind === 'PercentOff') {
    const factor = 1 - (d.discountValue / 10_000)
    return Math.max(0, Math.round(priceAgorot * factor))
  }
  // AmountOff in agorot
  return Math.max(0, priceAgorot - d.discountValue)
}

// Premium is the marketing-recommended target tier (per ADR-0057 §review).
const isRecommended = (tier: RetailTierDto) => tier.tierId === 'Premium'

/**
 * Drive checkout for an authenticated user: POST to checkout-session endpoint
 * then redirect to Stripe. Error surfaces via checkoutError ref.
 */
const launchCheckout = async (tier: RetailTierDto, cycle: 'Monthly' | 'Annual') => {
  const studentId = meStore.studentId ?? authStore.uid
  if (!studentId) {
    // Defensive: shouldn't happen if isSignedIn is true. Fall back to register.
    router.push(buildRegisterReturnTo(tier.tierId, cycle))
    return
  }
  redirectingToStripe.value = true
  try {
    await startCheckout({
      primaryStudentId: studentId,
      tier: tier.tierId,
      billingCycle: cycle,
    })
  } catch {
    // useCheckoutSession already captured the error on its own ref.
    redirectingToStripe.value = false
  }
}

/**
 * Build the register URL with a returnTo that replays the current selection
 * on /pricing after successful sign-up/in.
 */
const buildRegisterReturnTo = (
  tierId: 'Basic' | 'Plus' | 'Premium',
  cycle: 'Monthly' | 'Annual',
): string => {
  const params = new URLSearchParams({ tier: tierId, cycle })
  return `/register?returnTo=${encodeURIComponent(`/pricing?${params.toString()}`)}`
}

const handleSelect = (tier: RetailTierDto) => {
  const cycle: 'Monthly' | 'Annual' = annual.value ? 'Annual' : 'Monthly'
  if (authStore.isSignedIn) {
    launchCheckout(tier, cycle)
  } else {
    router.push(buildRegisterReturnTo(tier.tierId, cycle))
  }
}

/**
 * Fetch the active personal discount, if any, for the authenticated user.
 * 404 (the common case) is silently absorbed.
 */
async function fetchApplicableDiscount() {
  if (!authStore.isSignedIn) return
  try {
    const resp = await $api<ApplicableDiscount>('/me/applicable-discount')
    applicableDiscount.value = resp ?? null
  }
  catch {
    // 404 == no applicable discount; any other error == ignore + render
    // page without banner. Pricing visibility must not depend on this.
    applicableDiscount.value = null
  }
}

/**
 * Auto-resume flow: if the page loads with ?tier=&cycle= query params AND
 * the user is authenticated (just finished register), trigger the Stripe
 * redirect automatically. Anonymous visitors keep the card visible.
 */
onMounted(async () => {
  // Discount lookup runs concurrently with the auto-resume flow; banner
  // can render late without breaking checkout.
  fetchApplicableDiscount()

  const queryTier = typeof route.query.tier === 'string' ? route.query.tier : null
  const queryCycle = typeof route.query.cycle === 'string' ? route.query.cycle : null
  if (!queryTier || !queryCycle) return
  if (!authStore.isSignedIn) return
  const matchedTier = tiers.value.find(t => t.tierId === queryTier)
  if (!matchedTier) return
  const matchedCycle = queryCycle === 'Annual' ? 'Annual' : 'Monthly'
  annual.value = matchedCycle === 'Annual'
  await launchCheckout(matchedTier, matchedCycle)
})
</script>

<template>
  <div class="pricing-page pa-4 pa-md-8" data-testid="pricing-page">
    <!-- Full-page overlay while redirecting to Stripe -->
    <VOverlay
      v-model="redirectingToStripe"
      contained
      persistent
      class="align-center justify-center"
      data-testid="pricing-redirecting-overlay"
    >
      <div class="d-flex flex-column align-center">
        <VProgressCircular indeterminate color="primary" size="48" />
        <p class="text-body-1 mt-4">
          {{ t('pricing.redirecting') }}
        </p>
      </div>
    </VOverlay>

    <VAlert
      v-if="checkoutError"
      type="error"
      variant="tonal"
      closable
      class="mb-4"
      data-testid="pricing-checkout-error"
    >
      {{ t('pricing.checkoutError') }}
    </VAlert>

    <!-- Per-user discount banner: visible only when an active personal
         discount applies to the authenticated caller's email. Date-statement
         framing only — never time-pressure copy. -->
    <VAlert
      v-if="applicableDiscount"
      type="success"
      variant="tonal"
      class="mb-4"
      data-testid="pricing-discount-banner"
    >
      <div class="font-weight-medium">
        {{ discountBannerText }}
      </div>
      <div class="text-body-2 mt-1">
        {{ t('pricing.discount.autoApplied') }}
      </div>
    </VAlert>

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
