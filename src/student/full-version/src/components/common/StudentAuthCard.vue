<script setup lang="ts">
interface Props {
  title: string
  subtitle?: string
}

defineProps<Props>()
</script>

<template>
  <VCard
    class="student-auth-card pa-6"
    elevation="8"
    max-width="440"
  >
    <div class="text-h4 mb-1">
      {{ title }}
    </div>
    <div
      v-if="subtitle"
      class="text-body-2 text-medium-emphasis mb-6"
    >
      {{ subtitle }}
    </div>
    <slot />
    <div
      v-if="$slots.footer"
      class="student-auth-card__footer mt-6 text-center text-body-2"
    >
      <slot name="footer" />
    </div>
  </VCard>
</template>

<style scoped>
.student-auth-card {
  inline-size: 100%;
  max-inline-size: 440px;
  margin-inline: auto;
}

.student-auth-card__footer {
  border-block-start: 1px solid rgb(var(--v-theme-on-surface) / 0.08);
  padding-block-start: 1rem;
}
</style>
