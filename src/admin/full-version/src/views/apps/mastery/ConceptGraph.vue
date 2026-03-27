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

const statusRadius = (status: string): number => {
  switch (status) {
    case 'mastered': return 18
    case 'in_progress': return 14
    case 'available': return 11
    default: return 8
  }
}

const buildGraph = async () => {
  loading.value = true
  error.value = null

  try {
    const data = await $api<{ nodes: GraphNode[]; edges: GraphEdge[] }>(
      `/admin/mastery/students/${props.studentId}/knowledge-map/graph`,
    )

    if (!svgRef.value || !data.nodes?.length) return

    const svg = d3.select(svgRef.value)
    svg.selectAll('*').remove()

    const width = svgRef.value.clientWidth || 900
    const height = 500

    svg.attr('viewBox', `0 0 ${width} ${height}`)

    // Defs: arrow markers
    svg.append('defs').append('marker')
      .attr('id', 'arrowhead')
      .attr('viewBox', '0 -5 10 10')
      .attr('refX', 20)
      .attr('refY', 0)
      .attr('markerWidth', 6)
      .attr('markerHeight', 6)
      .attr('orient', 'auto')
      .append('path')
      .attr('d', 'M0,-4L8,0L0,4')
      .attr('fill', 'rgba(255,255,255,0.2)')

    const g = svg.append('g')

    // Zoom
    const zoom = d3.zoom<SVGSVGElement, unknown>()
      .scaleExtent([0.3, 3])
      .on('zoom', (event) => {
        g.attr('transform', event.transform)
      })
    svg.call(zoom)

    // Force simulation
    const simulation = d3.forceSimulation<GraphNode>(data.nodes)
      .force('link', d3.forceLink<GraphNode, GraphEdge>(data.edges)
        .id((d: GraphNode) => d.id)
        .distance(80))
      .force('charge', d3.forceManyBody().strength(-300))
      .force('center', d3.forceCenter(width / 2, height / 2))
      .force('collision', d3.forceCollide().radius(25))
      .force('x', d3.forceX(width / 2).strength(0.05))
      .force('y', d3.forceY(height / 2).strength(0.05))

    // Edges
    const link = g.append('g')
      .selectAll('line')
      .data(data.edges)
      .join('line')
      .attr('stroke', 'rgba(255,255,255,0.12)')
      .attr('stroke-width', 1.5)
      .attr('marker-end', 'url(#arrowhead)')

    // Nodes
    const node = g.append('g')
      .selectAll('g')
      .data(data.nodes)
      .join('g')
      .call(d3.drag<SVGGElement, GraphNode>()
        .on('start', (event, d) => {
          if (!event.active) simulation.alphaTarget(0.3).restart()
          d.fx = d.x
          d.fy = d.y
        })
        .on('drag', (event, d) => {
          d.fx = event.x
          d.fy = event.y
        })
        .on('end', (event, d) => {
          if (!event.active) simulation.alphaTarget(0)
          d.fx = null
          d.fy = null
        }))

    // Circle
    node.append('circle')
      .attr('r', d => statusRadius(d.status))
      .attr('fill', d => {
        const base = clusterColors[d.cluster] ?? '#666'
        return d.status === 'locked' ? 'rgba(100,100,100,0.3)' : base
      })
      .attr('stroke', d => {
        if (d.status === 'mastered') return '#28C76F'
        if (d.status === 'in_progress') return '#FF9F43'
        return 'rgba(255,255,255,0.1)'
      })
      .attr('stroke-width', d => d.status === 'mastered' ? 3 : d.status === 'in_progress' ? 2 : 1)
      .attr('opacity', d => d.status === 'locked' ? 0.3 : 0.85 + d.mastery * 0.15)

    // Mastery fill ring (inner arc showing mastery %)
    node.filter(d => d.mastery > 0 && d.status !== 'locked')
      .append('circle')
      .attr('r', d => statusRadius(d.status) - 3)
      .attr('fill', 'none')
      .attr('stroke', '#28C76F')
      .attr('stroke-width', 2)
      .attr('stroke-dasharray', d => {
        const r = statusRadius(d.status) - 3
        const circumference = 2 * Math.PI * r
        return `${circumference * d.mastery} ${circumference * (1 - d.mastery)}`
      })
      .attr('stroke-dashoffset', d => {
        const r = statusRadius(d.status) - 3
        return 2 * Math.PI * r * 0.25 // start from top
      })

    // Labels
    node.append('text')
      .text(d => d.name.length > 16 ? `${d.name.slice(0, 14)}...` : d.name)
      .attr('dy', d => statusRadius(d.status) + 14)
      .attr('text-anchor', 'middle')
      .attr('font-size', '10px')
      .attr('fill', 'rgba(255,255,255,0.7)')
      .attr('pointer-events', 'none')

    // Mastery % label inside node
    node.filter(d => d.status !== 'locked' && statusRadius(d.status) >= 14)
      .append('text')
      .text(d => `${Math.round(d.mastery * 100)}`)
      .attr('dy', 4)
      .attr('text-anchor', 'middle')
      .attr('font-size', '9px')
      .attr('font-weight', '600')
      .attr('fill', 'white')
      .attr('pointer-events', 'none')

    // Tooltip
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
  catch (err: any) {
    error.value = err.message ?? 'Failed to load concept graph'
    console.error('Failed to build concept graph:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(buildGraph)
watch(() => props.studentId, buildGraph)

defineExpose({ refresh: buildGraph })
</script>

<template>
  <VCard>
    <VCardItem title="Concept Graph">
      <template #subtitle>
        Interactive prerequisite graph — node size = progress, color = topic cluster, green ring = mastery %
      </template>
      <template #append>
        <div class="d-flex gap-2 flex-wrap">
          <VChip
            v-for="(color, cluster) in clusterColors"
            :key="cluster"
            size="x-small"
            label
            :style="{ backgroundColor: color, color: '#fff' }"
          >
            {{ cluster }}
          </VChip>
        </div>
      </template>
    </VCardItem>

    <VDivider />

    <VCardText class="pa-0">
      <VProgressLinear
        v-if="loading"
        indeterminate
        color="primary"
      />

      <VAlert
        v-if="error"
        type="error"
        variant="tonal"
        class="ma-4"
      >
        {{ error }}
      </VAlert>

      <div
        class="concept-graph-container"
        style="min-height: 500px; background: rgb(var(--v-theme-surface));"
      >
        <svg
          ref="svgRef"
          width="100%"
          height="500"
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
