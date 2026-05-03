<script setup lang="ts">
// =============================================================================
// Cena Platform — GuaranteeBadge.vue (EPIC-PRR-I PRR-294)
//
// 30-day money-back guarantee badge. Always positive framing (no "limited
// time" scarcity copy). Visible on pricing page + checkout confirmation.
// =============================================================================

import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <div
    class="guarantee-badge d-inline-flex align-center"
    data-testid="guarantee-badge"
  >
    <VIcon
      icon="tabler-shield-check"
      color="success"
      size="24"
      class="me-2"
    />
    <span class="text-body-2 font-weight-medium">
      {{ t('pricing.guarantee.thirtyDay') }}
    </span>
  </div>
</template>
