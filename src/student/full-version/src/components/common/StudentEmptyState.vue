<script setup lang="ts">
interface Props {
  icon?: string
  title: string
  subtitle?: string
}

withDefaults(defineProps<Props>(), {
  icon: 'tabler-mood-empty',
  subtitle: '',
})
</script>

<template>
  <VCard
    class="student-empty-state pa-8 text-center"
    variant="flat"
    role="status"
    aria-live="polite"
  >
    <VIcon
      :icon="icon"
      size="64"
      color="primary"
      class="mb-4"
      aria-hidden="true"
    />
    <div class="text-h5 mb-2">
      {{ title }}
    </div>
    <div
      v-if="subtitle"
      class="text-body-1 text-medium-emphasis mb-6"
    >
      {{ subtitle }}
    </div>
    <div class="d-flex justify-center gap-3 flex-wrap">
      <slot name="actions" />
    </div>
  </VCard>
</template>

<style scoped>
.student-empty-state {
  max-inline-size: 480px;
  margin-inline: auto;
}
</style>
