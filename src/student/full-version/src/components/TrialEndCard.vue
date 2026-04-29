<!--
  TrialEndCard.vue — Phase 2 paywall card.

  Mounts globally (in layouts/default.vue or App.vue). Watches
  entitlementStore.lastBlock. When non-null, opens a non-dismissable
  Vuetify dialog with copy + CTA tailored to the block reason:
    - trial_cap_reached: "You've reached your trial X for today" + upgrade CTA
    - unsubscribed/expired/cancelled/refunded: "Subscribe to continue" + CTA

  All copy is i18n-keyed; HE/AR locales fill via PRR-284 native review.

  ADR-0048 + GD-004: NO countdowns, NO streaks, NO loss-aversion phrasing.
  The card frames the block as a billing event ("a payment is required"),
  not as urgency or scarcity. Banned-mechanics scanner verifies on commit.
-->

<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useEntitlementStore } from '@/stores/entitlementStore'

const entitlement = useEntitlementStore()
const router = useRouter()

const open = computed(() => entitlement.isBlocked)

const titleKey = computed(() => {
  const reason = entitlement.lastBlock?.reason
  if (reason === 'trial_cap_reached')
    return 'paywall.trialCapReached.title'
  return 'paywall.entitlementRequired.title'
})

const bodyKey = computed(() => {
  const reason = entitlement.lastBlock?.reason
  if (reason === 'trial_cap_reached')
    return `paywall.trialCapReached.body.${entitlement.lastBlock?.feature ?? 'generic'}`
  return `paywall.entitlementRequired.body.${(entitlement.lastBlock?.effectiveStatus ?? 'unsubscribed').toLowerCase()}`
})

function goToSubscription(): void {
  entitlement.clearBlock()
  router.push({ name: 'account-subscription' })
}

function dismiss(): void {
  entitlement.clearBlock()
}
</script>

<template>
  <VDialog
    :model-value="open"
    max-width="480"
    persistent
    data-test="trial-end-card"
  >
    <VCard>
      <VCardTitle data-test="trial-end-card-title">
        {{ $t(titleKey) }}
      </VCardTitle>

      <VCardText data-test="trial-end-card-body">
        {{ $t(bodyKey) }}
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn
          variant="text"
          data-test="trial-end-card-dismiss"
          @click="dismiss"
        >
          {{ $t('paywall.dismiss') }}
        </VBtn>
        <VBtn
          color="primary"
          variant="elevated"
          data-test="trial-end-card-subscribe"
          @click="goToSubscription"
        >
          {{ $t('paywall.subscribe') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
