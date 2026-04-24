<script setup lang="ts">
/**
 * FigureEditor.vue — Admin figure spec editor (FIGURE-006, Phase 1)
 *
 * JSON spec editor with live preview + validation + templates.
 * Used in question authoring to attach figures to QuestionDocuments.
 */

import { computed, ref, watch } from 'vue'

interface FigureSpec {
  type: 'functionPlot' | 'geometry' | 'physics' | 'raster'
  ariaLabel: string
  caption?: string
  [key: string]: unknown
}

const props = defineProps<{
  modelValue: FigureSpec | null
}>()

const emit = defineEmits<{
  'update:modelValue': [value: FigureSpec | null]
}>()

const jsonInput = ref(props.modelValue ? JSON.stringify(props.modelValue, null, 2) : '')
const parseError = ref<string>()
const selectedTemplate = ref<string>('')

const templates: Record<string, FigureSpec> = {
  'function-plot-linear': {
    type: 'functionPlot',
    ariaLabel: 'Graph of linear function y = 2x + 1',
    caption: 'Linear function',
    functionPlotConfig: {
      data: [{ fn: '2*x + 1', color: '#7367F0' }],
      grid: true,
      xAxis: { domain: [-5, 5] },
      yAxis: { domain: [-5, 10] },
    },
  },
  'function-plot-quadratic': {
    type: 'functionPlot',
    ariaLabel: 'Graph of quadratic function y = x^2 - 4',
    caption: 'Quadratic function',
    functionPlotConfig: {
      data: [{ fn: 'x^2 - 4', color: '#7367F0' }],
      grid: true,
      xAxis: { domain: [-5, 5] },
      yAxis: { domain: [-5, 10] },
    },
  },
  'function-plot-trig': {
    type: 'functionPlot',
    ariaLabel: 'Graph of sine function y = sin(x)',
    caption: 'Trigonometric function',
    functionPlotConfig: {
      data: [
        { fn: 'sin(x)', color: '#7367F0' },
        { fn: 'cos(x)', color: '#28C76F' },
      ],
      grid: true,
      xAxis: { domain: [-2 * Math.PI, 2 * Math.PI] },
      yAxis: { domain: [-1.5, 1.5] },
    },
  },
  'physics-inclined-plane': {
    type: 'physics',
    ariaLabel: 'Inclined plane with friction force, normal force, and weight vectors',
    caption: 'Free-body diagram: inclined plane',
    physicsDiagramSpec: {
      diagramType: 'inclinedPlane',
      angle: 30,
      mass: 5,
      hasFriction: true,
      forces: ['weight', 'normal', 'friction', 'applied'],
    },
  },
  'physics-free-body': {
    type: 'physics',
    ariaLabel: 'Free-body diagram showing force vectors on an object',
    caption: 'Free-body diagram',
    physicsDiagramSpec: {
      diagramType: 'freeBody',
      forces: [
        { name: 'F_g', magnitude: 49, direction: 270 },
        { name: 'F_N', magnitude: 49, direction: 90 },
        { name: 'F_app', magnitude: 20, direction: 0 },
        { name: 'F_f', magnitude: 10, direction: 180 },
      ],
    },
  },
  'raster-bagrut': {
    type: 'raster',
    ariaLabel: 'Diagram from Bagrut exam question',
    caption: 'Bagrut exam figure',
    imageUrl: '/images/questions/placeholder.png',
  },
}

const templateOptions = computed(() => [
  { title: 'Select a template...', value: '' },
  { title: 'Linear function plot', value: 'function-plot-linear' },
  { title: 'Quadratic function plot', value: 'function-plot-quadratic' },
  { title: 'Trigonometric functions', value: 'function-plot-trig' },
  { title: 'Inclined plane (physics)', value: 'physics-inclined-plane' },
  { title: 'Free-body diagram (physics)', value: 'physics-free-body' },
  { title: 'Raster (Bagrut OCR)', value: 'raster-bagrut' },
])

const parsedSpec = computed<FigureSpec | null>(() => {
  if (!jsonInput.value.trim()) return null
  try {
    const parsed = JSON.parse(jsonInput.value)
    parseError.value = undefined
    return parsed as FigureSpec
  } catch (e) {
    parseError.value = e instanceof Error ? e.message : String(e)
    return null
  }
})

const validationErrors = computed<string[]>(() => {
  const errors: string[] = []
  const spec = parsedSpec.value
  if (!spec) return errors

  if (!spec.type) errors.push('Missing required field: type')
  if (!['functionPlot', 'geometry', 'physics', 'raster'].includes(spec.type))
    errors.push(`Invalid type: ${spec.type}. Must be one of: functionPlot, geometry, physics, raster`)
  if (!spec.ariaLabel) errors.push('Missing required field: ariaLabel (accessibility)')

  if (spec.type === 'functionPlot' && !spec.functionPlotConfig)
    errors.push('functionPlot type requires functionPlotConfig')
  if (spec.type === 'physics' && !spec.physicsDiagramSpec)
    errors.push('physics type requires physicsDiagramSpec')
  if (spec.type === 'raster' && !spec.imageUrl)
    errors.push('raster type requires imageUrl')

  return errors
})

const isValid = computed(() => parsedSpec.value !== null && validationErrors.value.length === 0)

watch(selectedTemplate, (templateKey) => {
  if (templateKey && templates[templateKey]) {
    jsonInput.value = JSON.stringify(templates[templateKey], null, 2)
  }
})

watch(parsedSpec, (spec) => {
  if (spec && isValid.value) {
    emit('update:modelValue', spec)
  }
})

function clearFigure() {
  jsonInput.value = ''
  selectedTemplate.value = ''
  emit('update:modelValue', null)
}
</script>

<template>
  <VCard variant="outlined" class="figure-editor pa-4">
    <div class="d-flex align-center justify-space-between mb-4">
      <h3 class="text-subtitle-1 font-weight-bold">Figure Editor</h3>
      <VBtn
        v-if="jsonInput.trim()"
        size="small"
        variant="text"
        color="error"
        @click="clearFigure"
      >
        Remove figure
      </VBtn>
    </div>

    <VSelect
      v-model="selectedTemplate"
      :items="templateOptions"
      label="Load from template"
      variant="outlined"
      density="compact"
      class="mb-4"
    />

    <VTextarea
      v-model="jsonInput"
      label="Figure spec (JSON)"
      variant="outlined"
      rows="12"
      auto-grow
      monospace
      :error="!!parseError"
      :error-messages="parseError ? [parseError] : []"
      class="mb-2"
    />

    <VAlert
      v-if="validationErrors.length > 0"
      type="warning"
      variant="tonal"
      class="mb-4"
    >
      <ul class="pl-4">
        <li v-for="err in validationErrors" :key="err">{{ err }}</li>
      </ul>
    </VAlert>

    <VAlert
      v-if="isValid"
      type="success"
      variant="tonal"
      class="mb-4"
    >
      Figure spec is valid ({{ parsedSpec?.type }})
    </VAlert>
  </VCard>
</template>
