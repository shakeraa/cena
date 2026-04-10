<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import type { FlowState } from '@/plugins/vuetify/theme'

definePage({
  meta: {
    layout: 'blank',
  },
})

const states: FlowState[] = ['warming', 'approaching', 'inFlow', 'disrupted', 'fatigued']
const current = ref<FlowState>('warming')
const auto = ref(true)

let timer: number | undefined

const tick = () => {
  const idx = states.indexOf(current.value)

  current.value = states[(idx + 1) % states.length]
}

onMounted(() => {
  if (auto.value)
    timer = window.setInterval(tick, 1000)
})

onBeforeUnmount(() => {
  if (timer)
    window.clearInterval(timer)
})

const stopAuto = () => {
  auto.value = false
  if (timer) {
    window.clearInterval(timer)
    timer = undefined
  }
}
</script>

<template>
  <FlowAmbientBackground :flow-state="current" />

  <div class="pa-8">
    <h1 class="text-h4 mb-2">
      Flow states
    </h1>
    <p class="text-body-2 text-medium-emphasis mb-4">
      Cycles through all five flow states on a 1-second interval. Click a state to jump directly.
    </p>
    <div class="d-flex flex-wrap gap-2 mb-3">
      <VBtn
        v-for="state in states"
        :key="state"
        :color="current === state ? 'primary' : undefined"
        :data-testid="`state-${state}`"
        variant="tonal"
        @click="stopAuto(); current = state"
      >
        {{ state }}
      </VBtn>
    </div>
    <VChip
      :color="current === 'fatigued' ? 'grey' : 'primary'"
      variant="flat"
      data-testid="current-state"
    >
      {{ current }}
    </VChip>
  </div>
</template>
