<script setup lang="ts">
// =============================================================================
// Cena Platform — TierCard.vue (EPIC-PRR-I PRR-290)
//
// One tier on the pricing card. Props-driven; no business logic. Parent page
// owns tier selection + checkout-session creation.
//
// Shipgate discipline:
//   - No scarcity / streak / countdown copy (enforced by CI scanner).
//   - Premium is highlighted via border + badge, NOT by exaggerated urgency.
//   - Price numerals wrapped in <bdi dir="ltr"> so they render LTR in RTL pages
//     (memory "Math always LTR" applies to prices too).
//   - Primary color from Vuetify theme (#7367F0 locked per memory).
// =============================================================================

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { RetailTierDto } from '@/composables/usePricingCatalog'

interface Props {
  tier: RetailTierDto
  /** True when this tier is the marketing-recommended "target" (Premium). */
  recommended?: boolean
  /** Whether the toggle is on "Annual" — controls which price to display. */
  annual?: boolean
  /** Format agorot to a display string (injected to keep formatting centralized). */
  formatPrice: (agorot: number) => string
}

const props = defineProps<Props>()
const emit = defineEmits<{
  (e: 'select', tier: RetailTierDto): void
}>()

const { t } = useI18n()

const displayedPriceAgorot = computed(() =>
  props.annual ? props.tier.annualPriceAgorot : props.tier.monthlyPriceAgorot,
)
const displayedPrice = computed(() => props.formatPrice(displayedPriceAgorot.value))

const cycleLabelKey = computed(() =>
  props.annual ? 'pricing.cycle.annual' : 'pricing.cycle.monthly',
)

const featureKeys = computed(() => {
  const t = props.tier
  const base = [
    'pricing.feature.coreExamPrep',
    'pricing.feature.cas',
    t.caps.photoDiagnosticsPerMonth !== null && t.caps.photoDiagnosticsPerMonth > 0
      ? (t.tierId === 'Premium'
        ? 'pricing.feature.photoDiagnosticPremium'
        : 'pricing.feature.photoDiagnosticPlus')
      : t.caps.photoDiagnosticsPerMonth === null
        ? 'pricing.feature.photoDiagnosticUnlimited'
        : null,
    t.features.parentDashboard ? 'pricing.feature.parentDashboard' : null,
    t.features.arabicDashboard ? 'pricing.feature.arabicDashboard' : null,
    t.features.tutorHandoffPdf ? 'pricing.feature.tutorHandoff' : null,
    t.features.prioritySupport ? 'pricing.feature.prioritySupport' : null,
  ]
  return base.filter((k): k is string => k !== null)
})
</script>

<template>
  <VCard
    class="tier-card pa-6 d-flex flex-column"
    :class="{ 'tier-card--recommended': recommended }"
    :variant="recommended ? 'flat' : 'outlined'"
    :data-testid="`tier-card-${tier.tierId.toLowerCase()}`"
    :aria-label="t(`pricing.tier.${tier.tierId.toLowerCase()}.name`)"
  >
    <!-- Recommended badge (positive framing only; shipgate compliant) -->
    <VChip
      v-if="recommended"
      color="primary"
      size="small"
      class="tier-card__recommended-chip mb-4 align-self-start"
      :data-testid="`tier-card-${tier.tierId.toLowerCase()}-recommended`"
    >
      {{ t('pricing.recommended') }}
    </VChip>

    <!-- Tier name + short pitch -->
    <h2 class="text-h5 font-weight-bold mb-1">
      {{ t(`pricing.tier.${tier.tierId.toLowerCase()}.name`) }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-4">
      {{ t(`pricing.tier.${tier.tierId.toLowerCase()}.pitch`) }}
    </p>

    <!-- Price (numerals forced LTR even inside RTL pages) -->
    <div class="tier-card__price mb-1">
      <bdi dir="ltr" class="text-h3 font-weight-bold">{{ displayedPrice }}</bdi>
      <span class="text-body-2 text-medium-emphasis ms-1">
        / {{ t(cycleLabelKey) }}
      </span>
    </div>

    <!-- VAT-inclusive disclosure -->
    <p class="text-caption text-medium-emphasis mb-4">
      {{ t('pricing.vatInclusive') }}
    </p>

    <!-- Feature bullets -->
    <ul class="tier-card__features ps-0 mb-6">
      <li
        v-for="key in featureKeys"
        :key="key"
        class="tier-card__feature d-flex align-start mb-2"
      >
        <VIcon
          icon="tabler-check"
          color="success"
          size="20"
          class="me-2 mt-1 flex-shrink-0"
        />
        <span class="text-body-2">{{ t(key) }}</span>
      </li>
    </ul>

    <!-- CTA -->
    <VBtn
      :color="recommended ? 'primary' : 'default'"
      :variant="recommended ? 'flat' : 'outlined'"
      size="large"
      block
      class="mt-auto"
      :data-testid="`tier-card-${tier.tierId.toLowerCase()}-cta`"
      @click="emit('select', tier)"
    >
      {{ t(`pricing.tier.${tier.tierId.toLowerCase()}.cta`) }}
    </VBtn>
  </VCard>
</template>

<style scoped>
.tier-card {
  height: 100%;
  min-height: 32rem;
}

.tier-card--recommended {
  border: 2px solid rgb(var(--v-theme-primary));
}

.tier-card__features {
  list-style: none;
}

.tier-card__price bdi {
  unicode-bidi: isolate;
}
</style>
