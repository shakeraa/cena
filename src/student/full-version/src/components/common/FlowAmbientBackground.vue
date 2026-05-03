<script setup lang="ts">
import { computed } from 'vue'
import type { FlowState } from '@/plugins/vuetify/theme'
import { useStudentTheme } from '@/composables/useStudentTheme'
import { useReducedMotion } from '@/composables/useReducedMotion'

interface Props {
  flowState: FlowState
}

const props = defineProps<Props>()

const tokens = useStudentTheme()
const prefersReduced = useReducedMotion()

const backgroundColor = computed(() => tokens.value.flow[props.flowState])
const isTransparent = computed(() => props.flowState === 'fatigued')

const transitionDuration = computed(() => (prefersReduced.value ? '0ms' : '600ms'))
</script>

<template>
  <div
    class="flow-ambient-background"
    :data-flow-state="props.flowState"
    :data-transparent="isTransparent"
    :style="{
      backgroundColor: isTransparent ? 'transparent' : backgroundColor,
      transitionDuration,
    }"
    aria-hidden="true"
  />
</template>

<style scoped>
.flow-ambient-background {
  position: fixed;
  inset: 0;
  z-index: -1;
  pointer-events: none;
  transition-property: background-color;
  transition-timing-function: ease-in-out;
  opacity: 0.28;
}
</style>
