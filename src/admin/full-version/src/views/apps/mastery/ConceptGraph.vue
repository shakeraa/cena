<script setup lang="ts">
import * as d3 from 'd3'
import { $api } from '@/utils/api'

interface Props {
  studentId: string
}

const props = defineProps<Props>()

interface GraphNode extends d3.SimulationNodeDatum {
  id: string
  name: string
  cluster: string
  mastery: number
  status: string
}

interface GraphEdge {
  source: string | GraphNode
  target: string | GraphNode
}

const svgRef = ref<SVGSVGElement | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const allNodes = ref<GraphNode[]>([])
const allEdges = ref<GraphEdge[]>([])
const activeFilters = ref<Set<string>>(new Set())
const searchQuery = ref('')

const clusterColors: Record<string, string> = {
  // Math clusters
  algebra: '#7367F0',
  functions: '#9B8AFB',
  geometry: '#B4A6FE',
  trigonometry: '#6554C0',
  calculus: '#5243AA',
  probability: '#8777D9',
  vectors: '#C0B6F2',
  // Other subjects
  physics: '#FF9F43',
  chemistry: '#28C76F',
  biology: '#00CFE8',
  cs: '#EA5455',
  english: '#FFD54F',
}

const toggleFilter = (cluster: string) => {
  if (activeFilters.value.has(cluster)) {
    activeFilters.value.delete(cluster)
  }
  else {
    activeFilters.value.add(cluster)
  }
  activeFilters.value = new Set(activeFilters.value) // trigger reactivity
  renderGraph()
}

const clearFilters = () => {
  activeFilters.value = new Set()
  searchQuery.value = ''
  renderGraph()
}

const availableClusters = computed(() => {
  const clusters = new Set(allNodes.value.map(n => n.cluster))
  return Object.entries(clusterColors).filter(([k]) => clusters.has(k))
})

const statusRadius = (status: string): number => {
  switch (status) {
    case 'mastered': return 18
    case 'in_progress': return 14
    case 'available': return 11
    default: return 8
  }
}

const fetchData = async () => {
  loading.value = true
  error.value = null
  try {
    const data = await $api<{ nodes: GraphNode[]; edges: GraphEdge[] }>(
      `/admin/mastery/students/${props.studentId}/knowledge-map/graph`,
    )
    allNodes.value = data.nodes ?? []
    allEdges.value = data.edges ?? []
    renderGraph()
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load concept graph'
  }
  finally {
    loading.value = false
  }
}

const renderGraph = () => {
  if (!svgRef.value || !allNodes.value.length) return

  // Filter nodes
  let nodes = [...allNodes.value]
  let edges = [...allEdges.value]

  if (activeFilters.value.size > 0) {
    const filterSet = activeFilters.value
    nodes = nodes.filter(n => filterSet.has(n.cluster))
    const nodeIds = new Set(nodes.map(n => n.id))
    edges = edges.filter(e => {
      const srcId = typeof e.source === 'string' ? e.source : e.source.id
      const tgtId = typeof e.target === 'string' ? e.target : e.target.id
      return nodeIds.has(srcId) && nodeIds.has(tgtId)
    })
  }

  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase()
    const matchIds = new Set(nodes.filter(n => n.name.toLowerCase().includes(q)).map(n => n.id))
    // Keep matching nodes + their direct neighbors
    const neighborIds = new Set(matchIds)
    edges.forEach(e => {
      const srcId = typeof e.source === 'string' ? e.source : e.source.id
      const tgtId = typeof e.target === 'string' ? e.target : e.target.id
      if (matchIds.has(srcId)) neighborIds.add(tgtId)
      if (matchIds.has(tgtId)) neighborIds.add(srcId)
    })
    nodes = nodes.filter(n => neighborIds.has(n.id))
    const nodeIds = new Set(nodes.map(n => n.id))
    edges = edges.filter(e => {
      const srcId = typeof e.source === 'string' ? e.source : e.source.id
      const tgtId = typeof e.target === 'string' ? e.target : e.target.id
      return nodeIds.has(srcId) && nodeIds.has(tgtId)
    })
  }

  // Reset simulation positions
  nodes.forEach(n => { n.x = undefined; n.y = undefined; n.fx = undefined; n.fy = undefined })

  const svg = d3.select(svgRef.value)
  svg.selectAll('*').remove()

  const width = svgRef.value.clientWidth || 1000
  const height = 560

  svg.attr('viewBox', `0 0 ${width} ${height}`)

  // Defs
  const defs = svg.append('defs')
  defs.append('marker')
    .attr('id', 'arrowhead')
    .attr('viewBox', '0 -5 10 10')
    .attr('refX', 22)
    .attr('refY', 0)
    .attr('markerWidth', 5)
    .attr('markerHeight', 5)
    .attr('orient', 'auto')
    .append('path')
    .attr('d', 'M0,-3L7,0L0,3')
    .attr('fill', 'rgba(255,255,255,0.15)')

  // Drop shadow for nodes
  const filter = defs.append('filter').attr('id', 'node-shadow')
  filter.append('feDropShadow')
    .attr('dx', 0).attr('dy', 1)
    .attr('stdDeviation', 3)
    .attr('flood-opacity', 0.3)

  const g = svg.append('g')

  // Zoom
  const zoom = d3.zoom<SVGSVGElement, unknown>()
    .scaleExtent([0.2, 4])
    .on('zoom', event => g.attr('transform', event.transform))
  svg.call(zoom)

  // Force simulation — wider spacing, stronger repulsion
  const simulation = d3.forceSimulation<GraphNode>(nodes)
    .force('link', d3.forceLink<GraphNode, GraphEdge>(edges)
      .id((d: GraphNode) => d.id)
      .distance(120))
    .force('charge', d3.forceManyBody().strength(-400))
    .force('center', d3.forceCenter(width / 2, height / 2))
    .force('collision', d3.forceCollide().radius(40))
    .force('x', d3.forceX(width / 2).strength(0.04))
    .force('y', d3.forceY(height / 2).strength(0.04))

  // Edges
  const link = g.append('g')
    .selectAll('line')
    .data(edges)
    .join('line')
    .attr('stroke', 'rgba(255,255,255,0.1)')
    .attr('stroke-width', 1)
    .attr('marker-end', 'url(#arrowhead)')

  // Nodes
  const node = g.append('g')
    .selectAll('g')
    .data(nodes)
    .join('g')
    .style('cursor', 'pointer')
    .call((d3.drag() as any)
      .on('start', (event: any, d: any) => {
        if (!event.active) simulation.alphaTarget(0.3).restart()
        d.fx = d.x; d.fy = d.y
      })
      .on('drag', (event: any, d: any) => { d.fx = event.x; d.fy = event.y })
      .on('end', (event: any, d: any) => {
        if (!event.active) simulation.alphaTarget(0)
        d.fx = null; d.fy = null
      }))

  // Background circle (glow for mastered)
  node.filter(d => d.status === 'mastered')
    .append('circle')
    .attr('r', d => statusRadius(d.status) + 5)
    .attr('fill', 'none')
    .attr('stroke', '#28C76F')
    .attr('stroke-width', 1)
    .attr('opacity', 0.3)

  // Main circle
  node.append('circle')
    .attr('r', d => statusRadius(d.status))
    .attr('fill', d => {
      const base = clusterColors[d.cluster] ?? '#666'
      return d.status === 'locked' ? 'rgba(100,100,100,0.3)' : base
    })
    .attr('stroke', d => {
      if (d.status === 'mastered') return '#28C76F'
      if (d.status === 'in_progress') return '#FF9F43'
      return 'rgba(255,255,255,0.15)'
    })
    .attr('stroke-width', d => d.status === 'mastered' ? 3 : d.status === 'in_progress' ? 2 : 1)
    .attr('opacity', d => d.status === 'locked' ? 0.25 : 0.9)
    .attr('filter', 'url(#node-shadow)')

  // Mastery ring
  node.filter(d => d.mastery > 0 && d.status !== 'locked')
    .append('circle')
    .attr('r', d => statusRadius(d.status) - 3)
    .attr('fill', 'none')
    .attr('stroke', '#28C76F')
    .attr('stroke-width', 2)
    .attr('stroke-dasharray', d => {
      const r = statusRadius(d.status) - 3
      const c = 2 * Math.PI * r
      return `${c * d.mastery} ${c * (1 - d.mastery)}`
    })
    .attr('stroke-dashoffset', d => 2 * Math.PI * (statusRadius(d.status) - 3) * 0.25)

  // Mastery % inside node
  node.filter(d => d.status !== 'locked' && statusRadius(d.status) >= 14)
    .append('text')
    .text(d => `${Math.round(d.mastery * 100)}`)
    .attr('dy', 4)
    .attr('text-anchor', 'middle')
    .attr('font-size', '10px')
    .attr('font-weight', '700')
    .attr('fill', 'white')
    .attr('pointer-events', 'none')

  // Labels — full names, wrapping long text
  node.each(function (d) {
    const el = d3.select(this)
    const name = d.name
    const yOffset = statusRadius(d.status) + 14

    if (name.length <= 20) {
      el.append('text')
        .text(name)
        .attr('dy', yOffset)
        .attr('text-anchor', 'middle')
        .attr('font-size', '11px')
        .attr('fill', 'rgba(255,255,255,0.8)')
        .attr('pointer-events', 'none')
    }
    else {
      // Split into two lines at the nearest space
      const mid = name.lastIndexOf(' ', 20)
      const split = mid > 5 ? mid : 20
      el.append('text')
        .text(name.slice(0, split))
        .attr('dy', yOffset)
        .attr('text-anchor', 'middle')
        .attr('font-size', '10px')
        .attr('fill', 'rgba(255,255,255,0.8)')
        .attr('pointer-events', 'none')
      el.append('text')
        .text(name.slice(split).trim())
        .attr('dy', yOffset + 12)
        .attr('text-anchor', 'middle')
        .attr('font-size', '10px')
        .attr('fill', 'rgba(255,255,255,0.6)')
        .attr('pointer-events', 'none')
    }
  })

  // Rich tooltip
  node.append('title')
    .text(d => `${d.name}\nCluster: ${d.cluster}\nMastery: ${(d.mastery * 100).toFixed(0)}%\nStatus: ${d.status}`)

  // Tick
  simulation.on('tick', () => {
    link
      .attr('x1', (d: any) => d.source.x)
      .attr('y1', (d: any) => d.source.y)
      .attr('x2', (d: any) => d.target.x)
      .attr('y2', (d: any) => d.target.y)
    node.attr('transform', (d: GraphNode) => `translate(${d.x},${d.y})`)
  })
}

onMounted(fetchData)
watch(() => props.studentId, fetchData)

defineExpose({ refresh: fetchData })
</script>

<template>
  <VCard>
    <VCardItem>
      <VCardTitle>Concept Graph</VCardTitle>
      <VCardSubtitle>
        Node size = progress, color = cluster, green ring = mastery %. Click clusters to filter.
      </VCardSubtitle>
    </VCardItem>

    <!-- Filter bar -->
    <VCardText class="pb-0 pt-2">
      <div class="d-flex align-center gap-3 flex-wrap">
        <AppTextField
          v-model="searchQuery"
          placeholder="Search concepts..."
          density="compact"
          prepend-inner-icon="tabler-search"
          style="max-inline-size: 220px;"
          clearable
          @update:model-value="renderGraph"
        />
        <div class="d-flex gap-1 flex-wrap">
          <VChip
            v-for="[cluster, color] in availableClusters"
            :key="cluster"
            size="small"
            label
            :variant="activeFilters.has(cluster) ? 'elevated' : 'tonal'"
            :style="activeFilters.has(cluster)
              ? { backgroundColor: color, color: '#fff' }
              : { borderColor: color, color: color }"
            class="cursor-pointer text-capitalize"
            @click="toggleFilter(cluster)"
          >
            {{ cluster }}
          </VChip>
        </div>
        <VBtn
          v-if="activeFilters.size > 0 || searchQuery"
          variant="text"
          size="small"
          color="secondary"
          @click="clearFilters"
        >
          Clear filters
        </VBtn>
        <VSpacer />
        <span class="text-caption text-disabled">
          {{ allNodes.length }} concepts{{ activeFilters.size > 0 || searchQuery ? ` (filtered)` : '' }}
        </span>
      </div>
    </VCardText>

    <VDivider class="mt-2" />

    <VCardText class="pa-0">
      <VProgressLinear v-if="loading" indeterminate color="primary" />

      <VAlert v-if="error" type="error" variant="tonal" class="ma-4">
        {{ error }}
      </VAlert>

      <div
        class="concept-graph-container"
        style="min-height: 560px; background: rgb(var(--v-theme-surface));"
      >
        <svg
          ref="svgRef"
          width="100%"
          height="560"
          style="display: block;"
        />
      </div>
    </VCardText>
  </VCard>
</template>

<style scoped>
.concept-graph-container {
  overflow: hidden;
  border-radius: 0 0 8px 8px;
}

.concept-graph-container svg {
  cursor: grab;
}

.concept-graph-container svg:active {
  cursor: grabbing;
}
</style>
