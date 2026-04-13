<script setup lang="ts">
/**
 * FBD-001: FreeBodyDiagramConstruct.vue
 * Interactive free-body diagram construction mode for physics.
 *
 * In Construct mode: renders only the physical scene (plane, block, angle).
 * Student drags force arrows onto the body. CAS verifies force decomposition.
 * Market differentiator: nobody in Hebrew/Arabic market has interactive FBD.
 *
 * Touch: pinch-to-zoom + tap-to-place.
 * Mouse: click-to-place + drag-to-set-direction.
 */

import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

interface PlacedForce {
  id: string
  label: string
  magnitude: number
  angleDeg: number
  color: string
  x: number
  y: number
}

interface ForceTemplate {
  label: string
  color: string
  defaultMagnitude: number
}

const props = defineProps<{
  sceneSvg: string
  bodyCenter: { x: number; y: number }
  expectedForces: { label: string; magnitude: number; angleDeg: number }[]
  ariaLabel: string
}>()

const emit = defineEmits<{
  (e: 'submit', forces: PlacedForce[]): void
  (e: 'verified', correct: boolean, feedback: string): void
}>()

const { t } = useI18n()

const placedForces = ref<PlacedForce[]>([])
const selectedTemplate = ref<ForceTemplate | null>(null)
const isVerifying = ref(false)
const feedback = ref<{ correct: boolean; message: string } | null>(null)

const forceTemplates: ForceTemplate[] = [
  { label: 'mg', color: '#c0392b', defaultMagnitude: 5 },
  { label: 'N', color: '#27ae60', defaultMagnitude: 5 },
  { label: 'Ff', color: '#e67e22', defaultMagnitude: 3 },
  { label: 'F', color: '#2980b9', defaultMagnitude: 5 },
  { label: 'T', color: '#2980b9', defaultMagnitude: 5 },
]

const svgRef = ref<SVGSVGElement | null>(null)

function selectForce(template: ForceTemplate) {
  selectedTemplate.value = template
}

function handleSvgClick(event: MouseEvent | TouchEvent) {
  if (!selectedTemplate.value || !svgRef.value) return

  const svg = svgRef.value
  const pt = svg.createSVGPoint()

  const clientX = 'touches' in event ? event.touches[0].clientX : event.clientX
  const clientY = 'touches' in event ? event.touches[0].clientY : event.clientY

  pt.x = clientX
  pt.y = clientY
  const svgPt = pt.matrixTransform(svg.getScreenCTM()!.inverse())

  // Snap to body center
  const force: PlacedForce = {
    id: `force-${Date.now()}`,
    label: selectedTemplate.value.label,
    magnitude: selectedTemplate.value.defaultMagnitude,
    angleDeg: 270, // Default: straight down (gravity direction)
    color: selectedTemplate.value.color,
    x: props.bodyCenter.x,
    y: props.bodyCenter.y,
  }

  placedForces.value.push(force)
  selectedTemplate.value = null
}

function removeForce(id: string) {
  placedForces.value = placedForces.value.filter(f => f.id !== id)
  feedback.value = null
}

function updateForceAngle(id: string, angleDeg: number) {
  const force = placedForces.value.find(f => f.id === id)
  if (force) force.angleDeg = angleDeg
}

function updateForceMagnitude(id: string, magnitude: number) {
  const force = placedForces.value.find(f => f.id === id)
  if (force) force.magnitude = magnitude
}

async function submitForVerification() {
  if (isVerifying.value) return
  isVerifying.value = true

  try {
    const response = await fetch('/api/cas/verify-fbd', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        placedForces: placedForces.value.map(f => ({
          label: f.label,
          magnitude: f.magnitude,
          angleDeg: f.angleDeg,
        })),
        expectedForces: props.expectedForces,
      }),
    })

    const result = await response.json()
    feedback.value = {
      correct: result.isCorrect,
      message: result.isCorrect
        ? t('session.fbd.allForcesCorrect')
        : result.feedback || t('session.fbd.checkForces'),
    }

    emit('verified', result.isCorrect, result.feedback || '')
  } catch {
    feedback.value = {
      correct: false,
      message: t('session.fbd.verificationError'),
    }
  } finally {
    isVerifying.value = false
  }
}

const arrowLength = 60
</script>

<template>
  <div class="fbd-construct" role="application" :aria-label="ariaLabel">
    <!-- Force palette -->
    <div class="fbd-palette" role="toolbar" :aria-label="t('session.fbd.forceToolbar')">
      <button
        v-for="tmpl in forceTemplates"
        :key="tmpl.label"
        class="fbd-force-btn"
        :class="{ 'fbd-force-btn--selected': selectedTemplate?.label === tmpl.label }"
        :style="{ borderColor: tmpl.color }"
        :aria-label="t('session.fbd.selectForce', { label: tmpl.label })"
        @click="selectForce(tmpl)"
      >
        <span class="fbd-force-icon" :style="{ color: tmpl.color }">→</span>
        {{ tmpl.label }}
      </button>
    </div>

    <!-- SVG canvas -->
    <svg
      ref="svgRef"
      class="fbd-canvas"
      viewBox="0 0 600 400"
      @click="handleSvgClick"
      @touchstart.prevent="handleSvgClick"
    >
      <!-- Scene background (from server-rendered SVG, minus forces) -->
      <g v-html="sceneSvg" />

      <!-- Placed force arrows -->
      <g v-for="force in placedForces" :key="force.id" class="fbd-placed-force">
        <line
          :x1="force.x"
          :y1="force.y"
          :x2="force.x + arrowLength * Math.cos(force.angleDeg * Math.PI / 180)"
          :y2="force.y - arrowLength * Math.sin(force.angleDeg * Math.PI / 180)"
          :stroke="force.color"
          stroke-width="3"
          marker-end="url(#arrowhead)"
        />
        <text
          :x="force.x + (arrowLength + 10) * Math.cos(force.angleDeg * Math.PI / 180)"
          :y="force.y - (arrowLength + 10) * Math.sin(force.angleDeg * Math.PI / 180)"
          :fill="force.color"
          font-size="14"
          text-anchor="middle"
        >
          {{ force.label }}
        </text>
      </g>

      <defs>
        <marker id="arrowhead" viewBox="0 0 10 10" refX="10" refY="5"
          markerWidth="8" markerHeight="8" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" fill="currentColor" />
        </marker>
      </defs>
    </svg>

    <!-- Placed forces list (for editing magnitude/angle) -->
    <div v-if="placedForces.length > 0" class="fbd-placed-list">
      <div v-for="force in placedForces" :key="force.id" class="fbd-placed-item">
        <span :style="{ color: force.color }">{{ force.label }}</span>
        <label>
          {{ t('session.fbd.magnitude') }}:
          <input type="number" :value="force.magnitude" min="0" step="0.5"
            @input="updateForceMagnitude(force.id, +($event.target as HTMLInputElement).value)" />
        </label>
        <label>
          {{ t('session.fbd.angle') }}:
          <input type="number" :value="force.angleDeg" min="0" max="360" step="5"
            @input="updateForceAngle(force.id, +($event.target as HTMLInputElement).value)" />°
        </label>
        <button @click="removeForce(force.id)" :aria-label="t('session.fbd.removeForce', { label: force.label })">✕</button>
      </div>
    </div>

    <!-- Submit + feedback -->
    <div class="fbd-actions">
      <button
        class="fbd-submit-btn"
        :disabled="placedForces.length === 0 || isVerifying"
        @click="submitForVerification"
      >
        {{ isVerifying ? t('session.fbd.checking') : t('session.fbd.checkDiagram') }}
      </button>
    </div>

    <div v-if="feedback" class="fbd-feedback"
      :class="feedback.correct ? 'fbd-feedback--correct' : 'fbd-feedback--incorrect'">
      {{ feedback.message }}
    </div>
  </div>
</template>
