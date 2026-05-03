<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

interface Props {
  titleKey?: string
}

const props = defineProps<Props>()
const route = useRoute()
const { t, te } = useI18n()

const heading = computed(() => {
  const key = props.titleKey || (route.meta.title as string | undefined) || ''
  if (key && te(key))
    return t(key)

  return key || route.name?.toString() || 'Page'
})
</script>

<template>
  <div class="placeholder-page pa-6">
    <div class="text-h4 mb-2">
      {{ heading }}
    </div>
    <p class="text-body-2 text-medium-emphasis mb-4">
      Not implemented yet — placeholder page scaffolded by STU-W-02.
    </p>
    <VCard
      variant="outlined"
      class="pa-4"
    >
      <div class="text-caption text-medium-emphasis mb-1">
        Route metadata
      </div>
      <pre
        class="text-body-2"
        data-testid="placeholder-route-meta"
      >name: {{ route.name }}
path: {{ route.path }}
layout: {{ route.meta.layout ?? 'default' }}
requiresAuth: {{ route.meta.requiresAuth ?? 'false' }}</pre>
    </VCard>
  </div>
</template>

<style scoped>
.placeholder-page {
  max-inline-size: 960px;
  margin-inline: auto;
}

pre {
  white-space: pre-wrap;
  font-family: monospace;
}
</style>
