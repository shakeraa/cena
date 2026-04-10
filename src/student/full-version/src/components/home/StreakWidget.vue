<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  days: number
  isNewBest?: boolean
}

const props = withDefaults(defineProps<Props>(), { isNewBest: false })
const { t } = useI18n()

const label = computed(() =>
  props.days === 1 ? t('home.streak.singular') : t('home.streak.plural', { count: props.days }),
)
</script>

<template>
  <VCard
    class="streak-widget pa-5 text-center"
    variant="flat"
    data-testid="streak-widget"
  >
    <VIcon
      icon="tabler-flame"
      size="48"
      color="warning"
      class="mb-2"
      aria-hidden="true"
    />
    <div class="text-h3 font-weight-bold">
      {{ days }}
    </div>
    <div class="text-body-2 text-medium-emphasis">
      {{ label }}
    </div>
    <VChip
      v-if="isNewBest"
      size="small"
      color="warning"
      variant="tonal"
      class="mt-2"
    >
      {{ t('home.streak.newBest') }}
    </VChip>
  </VCard>
</template>
