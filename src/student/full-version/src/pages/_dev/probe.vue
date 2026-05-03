<script setup lang="ts">
import { onMounted, ref } from 'vue'
import katex from 'katex'
import 'katex/dist/katex.min.css'
import { HubConnectionBuilder } from '@microsoft/signalr'

// STU-W-00 dev probe: proves KaTeX + SignalR deps resolve AND that
// katex.render actually runs inside our RTL-capable layout. The math
// output lives inside <bdi dir="ltr"> per feedback_math_always_ltr —
// even dev pages follow the invariant so the RDY-030 math-ltr-wrapper
// rule stays simple (no per-path exclusion list).
const katexAvailable = typeof katex.render === 'function'
const signalrAvailable = typeof HubConnectionBuilder === 'function'

const mathRef = ref<HTMLElement | null>(null)

onMounted(() => {
  if (katexAvailable && mathRef.value) {
    katex.render('\\int_0^\\infty e^{-x^2} \\, dx = \\frac{\\sqrt{\\pi}}{2}',
      mathRef.value,
      { throwOnError: false, output: 'html' })
  }
})
</script>

<template>
  <div class="pa-8">
    <h1>STU-W-00 Dev Probe</h1>
    <ul>
      <li>KaTeX loaded: {{ katexAvailable }}</li>
      <li>SignalR loaded: {{ signalrAvailable }}</li>
    </ul>
    <p class="mt-4">
      Live KaTeX render (confirms `katex.render` works end-to-end):
    </p>
    <!-- RDY-030b: math rendered inside <bdi dir="ltr"> even on dev pages
         so RTL locales render equations LTR consistently (memory pointer
         feedback_math_always_ltr). Same invariant as QuestionCard. -->
    <bdi
      ref="mathRef"
      dir="ltr"
      class="probe-math"
    />
  </div>
</template>

<style scoped>
.probe-math {
  display: inline-block;
  margin-block-start: 0.5rem;
}
</style>
