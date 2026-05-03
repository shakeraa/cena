<script setup lang="ts">
// =============================================================================
// Cena Platform — SiblingDiscountNote.vue (EPIC-PRR-I PRR-293)
//
// Post-purchase upsell info note shown on the pricing page footer. NOT a
// headline tier — households with multiple students add siblings AFTER
// activation on the account screen. This note just advertises the rate
// so parents with multiple students see it before buying.
// =============================================================================

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SiblingDiscountDto } from '@/composables/usePricingCatalog'

interface Props {
  siblingDiscount: SiblingDiscountDto
  formatPrice: (agorot: number) => string
}

const props = defineProps<Props>()
const { t } = useI18n()

const firstRate = computed(() =>
  props.formatPrice(props.siblingDiscount.firstSecondSiblingMonthlyAgorot),
)
const householdCapRate = computed(() =>
  props.formatPrice(props.siblingDiscount.thirdPlusSiblingMonthlyAgorot),
)
</script>

<template>
  <VAlert
    type="info"
    variant="tonal"
    class="sibling-discount-note mt-8"
    data-testid="sibling-discount-note"
  >
    <p class="mb-1 font-weight-medium">
      {{ t('pricing.sibling.heading') }}
    </p>
    <p class="text-body-2 mb-0">
      <i18n-t keypath="pricing.sibling.body" tag="span">
        <template #firstRate>
          <bdi dir="ltr">{{ firstRate }}</bdi>
        </template>
        <template #householdCapRate>
          <bdi dir="ltr">{{ householdCapRate }}</bdi>
        </template>
      </i18n-t>
    </p>
  </VAlert>
</template>
