<script setup lang="ts">
import { useI18n } from 'vue-i18n'

interface Props {
  title: string
  conceptIds: string[]
  icon: string
  emptyMessage: string
}

defineProps<Props>()
const { t } = useI18n()
</script>

<template>
  <VCard
    class="prerequisite-chain pa-5"
    variant="outlined"
    :data-testid="`chain-${title.toLowerCase()}`"
  >
    <div class="d-flex align-center mb-3">
      <VIcon
        :icon="icon"
        size="20"
        class="me-2"
        aria-hidden="true"
      />
      <div class="text-h6">
        {{ title }}
      </div>
    </div>

    <div
      v-if="conceptIds.length === 0"
      class="text-body-2 text-medium-emphasis"
      data-testid="chain-empty"
    >
      {{ emptyMessage }}
    </div>
    <VList
      v-else
      density="compact"
      class="pa-0"
    >
      <VListItem
        v-for="id in conceptIds"
        :key="id"
        :to="`/knowledge-graph/concept/${id}`"
        :data-testid="`chain-link-${id}`"
      >
        <template #prepend>
          <VIcon
            icon="tabler-arrow-right"
            size="18"
            class="me-2 text-medium-emphasis"
            aria-hidden="true"
          />
        </template>
        <VListItemTitle>{{ id }}</VListItemTitle>
      </VListItem>
    </VList>
  </VCard>
</template>
