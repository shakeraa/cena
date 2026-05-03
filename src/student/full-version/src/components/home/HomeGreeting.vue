<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMeStore } from '@/stores/meStore'

const { t } = useI18n()
const meStore = useMeStore()

const hour = new Date().getHours()

const greetingKey = computed(() => {
  if (hour < 5)
    return 'home.greeting.night'
  if (hour < 12)
    return 'home.greeting.morning'
  if (hour < 18)
    return 'home.greeting.afternoon'
  if (hour < 22)
    return 'home.greeting.evening'

  return 'home.greeting.night'
})

const displayName = computed(() => meStore.profile?.displayName || t('home.greeting.fallback'))
</script>

<template>
  <header class="home-greeting">
    <div class="text-h4 font-weight-bold mb-1">
      {{ t(greetingKey, { name: displayName }) }}
    </div>
    <div class="text-body-2 text-medium-emphasis">
      {{ t('home.greeting.subtitle') }}
    </div>
  </header>
</template>

<style scoped>
.home-greeting {
  margin-block-end: 1.5rem;
}
</style>
