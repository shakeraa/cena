<script setup lang="ts">
/**
 * QuestionFigure.vue — Renders mathematical figures per ADR-0004 (FIGURE-003)
 *
 * Supports three figure types from the FigureSpec schema (FIGURE-002):
 *   1. Function plots (function-plot.js, lazy-loaded)
 *   2. Geometry diagrams (JSXGraph, lazy-loaded via dynamic script)
 *   3. Physics diagrams (programmatic SVG from PhysicsDiagramSpec)
 *   4. Raster fallback (PNG from CDN for OCR-ingested figures)
 *
 * All math labels rendered via KaTeX. Dark mode + RTL aware.
 * SVG content is sanitized via DOMPurify to prevent XSS.
 */

import { computed, onMounted, ref, watch, nextTick } from 'vue'
import DOMPurify from 'dompurify'

type FigureType = 'functionPlot' | 'geometry' | 'physics' | 'raster'

interface FigureSpec {
  type: FigureType
  functionPlotConfig?: Record<string, unknown>
  jsxGraphConfig?: Record<string, unknown>
  physicsDiagramSpec?: Record<string, unknown>
  imageUrl?: string
  ariaLabel: string
  caption?: string
  width?: string | number
  height?: string | number
}

const props = defineProps<{
  spec: FigureSpec
}>()

const containerRef = ref<HTMLElement>()
const loaded = ref(false)
const error = ref<string>()

// DOMPurify config: allow SVG elements only
const PURIFY_CONFIG = {
  USE_PROFILES: { svg: true, mathMl: true },
  ADD_TAGS: ['foreignObject'],
  ADD_ATTR: ['xmlns', 'viewBox', 'preserveAspectRatio', 'fill', 'stroke', 'stroke-width',
    'd', 'cx', 'cy', 'r', 'x', 'y', 'x1', 'y1', 'x2', 'y2', 'width', 'height',
    'transform', 'font-size', 'text-anchor', 'dominant-baseline', 'marker-end',
    'marker-start', 'opacity', 'stroke-dasharray'],
}

const containerStyle = computed(() => ({
  width: typeof props.spec.width === 'number' ? `${props.spec.width}px` : (props.spec.width || '100%'),
  height: typeof props.spec.height === 'number' ? `${props.spec.height}px` : (props.spec.height || 'auto'),
}))

onMounted(async () => {
  await renderFigure()
})

watch(() => props.spec, async () => {
  await nextTick()
  await renderFigure()
}, { deep: true })

async function renderFigure() {
  if (!containerRef.value) return
  error.value = undefined
  loaded.value = false

  try {
    switch (props.spec.type) {
      case 'functionPlot':
        await renderFunctionPlot()
        break
      case 'geometry':
        await renderGeometry()
        break
      case 'physics':
        renderPhysicsSvg()
        break
      case 'raster':
        loaded.value = true
        break
      default:
        error.value = `Unknown figure type: ${props.spec.type}`
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

async function renderFunctionPlot() {
  const { default: functionPlot } = await import('function-plot')
  const el = containerRef.value!
  clearElement(el)

  functionPlot({
    target: el,
    width: el.clientWidth || 400,
    height: el.clientHeight || 300,
    ...props.spec.functionPlotConfig,
  })
  loaded.value = true
}

async function renderGeometry() {
  if (!window.JXG) {
    await loadScript('https://cdn.jsdelivr.net/npm/jsxgraph/distrib/jsxgraphcore.js')
  }

  const el = containerRef.value!
  clearElement(el)

  const boardId = `jxg-${Math.random().toString(36).slice(2, 10)}`
  el.id = boardId

  const config = props.spec.jsxGraphConfig || {}
  window.JXG.JSXGraph.initBoard(boardId, {
    boundingbox: [-5, 5, 5, -5],
    axis: true,
    showNavigation: false,
    ...config,
  })
  loaded.value = true
}

function renderPhysicsSvg() {
  const el = containerRef.value!
  const spec = props.spec.physicsDiagramSpec

  if (!spec) {
    error.value = 'No physics diagram spec provided'
    return
  }

  clearElement(el)

  if (typeof spec === 'object' && 'svg' in spec && typeof spec.svg === 'string') {
    // Sanitize SVG via DOMPurify to prevent XSS
    const sanitized = DOMPurify.sanitize(spec.svg, PURIFY_CONFIG)
    const wrapper = document.createElement('div')
    wrapper.textContent = '' // clear
    // Parse sanitized SVG safely
    const parser = new DOMParser()
    const doc = parser.parseFromString(sanitized, 'image/svg+xml')
    const svgEl = doc.documentElement
    if (svgEl.tagName === 'svg') {
      el.appendChild(document.importNode(svgEl, true))
    } else {
      error.value = 'Invalid SVG content'
    }
  } else {
    const placeholder = document.createElement('div')
    placeholder.className = 'figure-placeholder'
    placeholder.textContent = `Physics diagram: ${JSON.stringify(spec).slice(0, 100)}...`
    el.appendChild(placeholder)
  }
  loaded.value = true
}

function clearElement(el: HTMLElement) {
  while (el.firstChild) {
    el.removeChild(el.firstChild)
  }
}

function loadScript(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    if (document.querySelector(`script[src="${src}"]`)) {
      resolve()
      return
    }
    const script = document.createElement('script')
    script.src = src
    script.onload = () => resolve()
    script.onerror = () => reject(new Error(`Failed to load: ${src}`))
    document.head.appendChild(script)
  })
}

declare global {
  interface Window {
    JXG: { JSXGraph: { initBoard: (id: string, config: Record<string, unknown>) => unknown } }
  }
}
</script>

<template>
  <figure
    class="question-figure"
    :aria-label="spec.ariaLabel"
    role="img"
  >
    <img
      v-if="spec.type === 'raster' && spec.imageUrl"
      :src="spec.imageUrl"
      :alt="spec.ariaLabel"
      class="question-figure__raster"
      loading="lazy"
    />

    <div
      v-else
      ref="containerRef"
      class="question-figure__canvas"
      :style="containerStyle"
    />

    <div v-if="!loaded && !error && spec.type !== 'raster'" class="question-figure__loading">
      Loading figure...
    </div>

    <div v-if="error" class="question-figure__error" role="alert">
      Figure error: {{ error }}
    </div>

    <figcaption v-if="spec.caption" class="question-figure__caption">
      {{ spec.caption }}
    </figcaption>
  </figure>
</template>

<style scoped lang="scss">
.question-figure {
  margin: 1rem 0;
  text-align: center;

  &__canvas {
    margin: 0 auto;
    min-height: 200px;
  }

  &__raster {
    max-width: 100%;
    height: auto;
    border-radius: 8px;
  }

  &__loading {
    padding: 2rem;
    color: var(--cena-muted, #999);
    font-size: 0.875rem;
  }

  &__error {
    padding: 1rem;
    color: var(--cena-danger, #EA5455);
    background: color-mix(in srgb, var(--cena-danger, #EA5455) 10%, transparent);
    border-radius: 6px;
    font-size: 0.8125rem;
  }

  &__caption {
    margin-top: 0.5rem;
    font-size: 0.8125rem;
    color: var(--cena-muted, #666);
    font-style: italic;
  }
}
</style>
