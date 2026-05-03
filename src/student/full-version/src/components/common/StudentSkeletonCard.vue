<script setup lang="ts">
import { computed } from 'vue'
import { useReducedMotion } from '@/composables/useReducedMotion'

interface Props {
  lines?: number
  height?: string
  showAvatar?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  lines: 3,
  height: '120px',
  showAvatar: false,
})

const prefersReduced = useReducedMotion()

const skeletonType = computed(() => {
  const lineList = Array.from({ length: props.lines }, () => 'text').join(', ')

  return props.showAvatar
    ? `list-item-avatar, ${lineList}`
    : `heading, ${lineList}`
})
</script>

<template>
  <VCard
    variant="flat"
    class="student-skeleton-card"
    :class="{ 'reduced-motion': prefersReduced }"
    :min-height="height"
    aria-busy="true"
    aria-live="polite"
  >
    <VSkeletonLoader
      :type="skeletonType"
      :boilerplate="prefersReduced"
    />
  </VCard>
</template>

<style scoped>
.student-skeleton-card.reduced-motion :deep(.v-skeleton-loader__bone::after) {
  animation: none !important;
}

/* RDY-030b: prefers-reduced-motion guard (WCAG 2.3.3).
   Component-local animations/transitions reduced to an imperceptible
   0.01ms so vestibular-sensitive users don't trigger motion-related
   symptoms. Complements the global reset in styles.scss. */
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
</style>
