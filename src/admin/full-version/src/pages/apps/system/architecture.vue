<script setup lang="ts">
import * as d3 from 'd3'

definePage({ meta: { action: 'read', subject: 'System' } })

const svgRef = ref<SVGSVGElement | null>(null)
const loading = ref(true)
const actorHostStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const adminApiStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const natsStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const pgStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const redisStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const frontendStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const activeActors = ref(0)

const statusColor = (s: string) => s === 'healthy' ? '#28C76F' : s === 'degraded' ? '#FF9F43' : '#EA5455'
const statusGlow = (s: string) => s === 'healthy' ? 'rgba(40,199,111,0.3)' : s === 'degraded' ? 'rgba(255,159,67,0.3)' : 'rgba(234,84,85,0.2)'

interface ServiceNode {
  id: string
  label: string
  sublabel: string
  x: number
  y: number
  status: 'healthy' | 'degraded' | 'offline'
  icon: string
  port?: string
  fx?: number | null
  fy?: number | null
}

interface ServiceEdge {
  from: string
  to: string
  label: string
  protocol: string
  active: boolean
}

// Persistent graph state — survives status refreshes
let nodes: ServiceNode[] = []
let edges: ServiceEdge[] = []
let nodeMap = new Map<string, ServiceNode>()
let simulation: d3.Simulation<any, any> | null = null
let diagramDrawn = false

// D3 element refs for in-place updates
let nodeElements: { node: ServiceNode; g: d3.Selection<SVGGElement, any, any, any> }[] = []
let edgeLines: { edge: ServiceEdge; line: d3.Selection<SVGLineElement, any, any, any>; labelText: d3.Selection<SVGTextElement, any, any, any>; protocolText: d3.Selection<SVGTextElement, any, any, any> }[] = []

const getNodeStatus = (id: string): 'healthy' | 'degraded' | 'offline' => {
  switch (id) {
    case 'frontend': return frontendStatus.value
    case 'admin-api': return adminApiStatus.value
    case 'actor-host': return actorHostStatus.value
    case 'nats': return natsStatus.value
    case 'postgres': return pgStatus.value
    case 'redis': return redisStatus.value
    case 'firebase': return 'healthy'
    case 'emulator': return activeActors.value > 0 ? 'healthy' : 'offline'
    default: return 'offline'
  }
}

const getEdgeActive = (edge: ServiceEdge): boolean => {
  switch (`${edge.from}-${edge.to}`) {
    case 'frontend-admin-api':
    case 'frontend-actor-host':
    case 'admin-api-firebase':
      return true
    case 'admin-api-nats':
    case 'actor-host-nats':
      return natsStatus.value === 'healthy'
    case 'emulator-nats':
      return activeActors.value > 0
    case 'admin-api-postgres':
    case 'actor-host-postgres':
      return pgStatus.value === 'healthy'
    case 'admin-api-redis':
    case 'actor-host-redis':
      return redisStatus.value === 'healthy'
    default:
      return false
  }
}

const checkServices = async () => {
  // Check Actor Host
  try {
    const data = await fetch('/api/actors/stats').then(r => r.json())
    actorHostStatus.value = 'healthy'
    activeActors.value = data.activeActorCount ?? 0
  }
  catch { actorHostStatus.value = 'offline' }

  // Check Admin API (health is now AllowAnonymous)
  try {
    const r = await fetch('/api/admin/system/health')
    adminApiStatus.value = r.ok ? 'healthy' : 'offline'
  }
  catch { adminApiStatus.value = 'offline' }

  // Frontend is healthy if we're here
  frontendStatus.value = 'healthy'

  // NATS — infer from actor host
  natsStatus.value = actorHostStatus.value === 'healthy' ? 'healthy' : 'offline'

  // PG/Redis — infer from admin API
  pgStatus.value = adminApiStatus.value === 'healthy' ? 'healthy' : 'offline'
  redisStatus.value = adminApiStatus.value === 'healthy' ? 'healthy' : 'offline'

  loading.value = false

  if (!diagramDrawn) {
    drawDiagram()
    diagramDrawn = true
  }
  else {
    updateStatuses()
  }
}

const updateStatuses = () => {
  // Update node colors/glows in-place without redrawing
  nodeElements.forEach(({ node, g }) => {
    const status = getNodeStatus(node.id)
    node.status = status

    const sublabel = node.id === 'actor-host'
      ? `Proto.Actor Cluster (${activeActors.value} actors)`
      : node.id === 'emulator'
        ? (activeActors.value > 0 ? '100 students' : 'Idle')
        : node.sublabel

    node.sublabel = sublabel

    g.select('circle:nth-child(1)').attr('fill', statusGlow(status))
    g.select('circle:nth-child(2)').attr('stroke', statusColor(status))
    g.select('text:nth-child(3)').attr('fill', statusColor(status))
    g.select('text:nth-child(6)').text(sublabel)

    // Status dot — last circle in the group
    const circles = g.selectAll('circle')
    circles.filter((_d: any, i: number) => i === 2).attr('fill', statusColor(status))

    g.select('title').text(`${node.label}\n${sublabel}\nStatus: ${status}${node.port ? `\nPort: ${node.port}` : ''}`)
  })

  // Update edge styles in-place
  edgeLines.forEach(({ edge, line, labelText }) => {
    const active = getEdgeActive(edge)
    edge.active = active
    line
      .attr('stroke', active ? 'rgba(115,103,240,0.4)' : 'rgba(255,255,255,0.08)')
      .attr('stroke-width', active ? 2 : 1)
      .attr('stroke-dasharray', active ? 'none' : '4,4')
    labelText.attr('fill', active ? 'rgba(255,255,255,0.5)' : 'rgba(255,255,255,0.2)')
  })
}

const drawDiagram = () => {
  if (!svgRef.value) return

  const svg = d3.select(svgRef.value)
  svg.selectAll('*').remove()

  const width = svgRef.value.clientWidth || 1000
  const height = 700

  svg.attr('viewBox', `0 0 ${width} ${height}`)

  nodes = [
    { id: 'frontend', label: 'Admin Dashboard', sublabel: 'Vite + Vue 3 + Vuetify', x: width * 0.5, y: 60, status: frontendStatus.value, icon: 'V', port: '5174' },
    { id: 'admin-api', label: 'Admin API', sublabel: '.NET 9 REST + Firebase Auth', x: width * 0.3, y: 220, status: adminApiStatus.value, icon: 'A', port: '5000' },
    { id: 'actor-host', label: 'Actor Host', sublabel: `Proto.Actor Cluster (${activeActors.value} actors)`, x: width * 0.7, y: 220, status: actorHostStatus.value, icon: 'P', port: '5001' },
    { id: 'nats', label: 'NATS JetStream', sublabel: 'Message Bus', x: width * 0.5, y: 380, status: natsStatus.value, icon: 'N', port: '4222' },
    { id: 'postgres', label: 'PostgreSQL', sublabel: 'Marten Event Store', x: width * 0.2, y: 520, status: pgStatus.value, icon: 'PG', port: '5433' },
    { id: 'redis', label: 'Redis', sublabel: 'Cache + Sessions', x: width * 0.5, y: 520, status: redisStatus.value, icon: 'R', port: '6379' },
    { id: 'firebase', label: 'Firebase Auth', sublabel: 'Google OAuth + JWT', x: width * 0.15, y: 380, status: 'healthy', icon: 'F' },
    { id: 'emulator', label: 'Student Emulator', sublabel: `${activeActors.value > 0 ? '100 students' : 'Idle'}`, x: width * 0.85, y: 380, status: activeActors.value > 0 ? 'healthy' : 'offline', icon: 'E' },
  ]

  edges = [
    { from: 'frontend', to: 'admin-api', label: 'REST /api/*', protocol: 'HTTP', active: true },
    { from: 'frontend', to: 'actor-host', label: '/api/actors/*', protocol: 'HTTP', active: true },
    { from: 'admin-api', to: 'nats', label: 'Subscribe events', protocol: 'NATS', active: natsStatus.value === 'healthy' },
    { from: 'actor-host', to: 'nats', label: 'Pub/Sub commands', protocol: 'NATS', active: natsStatus.value === 'healthy' },
    { from: 'emulator', to: 'nats', label: 'Publish attempts', protocol: 'NATS', active: activeActors.value > 0 },
    { from: 'admin-api', to: 'postgres', label: 'Marten queries', protocol: 'TCP', active: pgStatus.value === 'healthy' },
    { from: 'actor-host', to: 'postgres', label: 'Event sourcing', protocol: 'TCP', active: pgStatus.value === 'healthy' },
    { from: 'admin-api', to: 'redis', label: 'Cache', protocol: 'TCP', active: redisStatus.value === 'healthy' },
    { from: 'actor-host', to: 'redis', label: 'State cache', protocol: 'TCP', active: redisStatus.value === 'healthy' },
    { from: 'admin-api', to: 'firebase', label: 'Token verify', protocol: 'HTTPS', active: true },
  ]

  nodeMap = new Map(nodes.map(n => [n.id, n]))

  // ── Edge elements (drawn first, below nodes) ──
  const edgeGroup = svg.append('g')
  edgeLines = edges.map(edge => {
    const line = edgeGroup.append('line')
      .attr('stroke', edge.active ? 'rgba(115,103,240,0.4)' : 'rgba(255,255,255,0.08)')
      .attr('stroke-width', edge.active ? 2 : 1)
      .attr('stroke-dasharray', edge.active ? 'none' : '4,4')

    const labelText = edgeGroup.append('text')
      .attr('text-anchor', 'middle')
      .attr('font-size', '9px')
      .attr('fill', edge.active ? 'rgba(255,255,255,0.5)' : 'rgba(255,255,255,0.2)')
      .text(edge.label)

    const protocolText = edgeGroup.append('text')
      .attr('text-anchor', 'middle')
      .attr('font-size', '8px')
      .attr('fill', 'rgba(115,103,240,0.6)')
      .text(edge.protocol)

    return { edge, line, labelText, protocolText }
  })

  // ── Node elements (draggable) ──
  const nodeGroup = svg.append('g')
  nodeElements = nodes.map(node => {
    const g = nodeGroup.append('g')
      .style('cursor', 'grab')
      .datum(node)

    g.append('circle').attr('r', 40).attr('fill', statusGlow(node.status)).attr('opacity', 0.5)
    g.append('circle').attr('r', 32).attr('fill', 'rgba(40,42,54,0.9)').attr('stroke', statusColor(node.status)).attr('stroke-width', 2.5)
    g.append('text').attr('text-anchor', 'middle').attr('dy', 5).attr('font-size', '14px').attr('font-weight', '700').attr('fill', statusColor(node.status)).text(node.icon)
    g.append('text').attr('y', 48).attr('text-anchor', 'middle').attr('font-size', '12px').attr('font-weight', '600').attr('fill', 'rgba(255,255,255,0.9)').text(node.label)
    g.append('text').attr('y', 62).attr('text-anchor', 'middle').attr('font-size', '9px').attr('fill', 'rgba(255,255,255,0.5)').text(node.sublabel)
    if (node.port) g.append('text').attr('y', -38).attr('text-anchor', 'middle').attr('font-size', '9px').attr('fill', 'rgba(115,103,240,0.7)').text(`:${node.port}`)
    g.append('circle').attr('cx', 24).attr('cy', -24).attr('r', 5).attr('fill', statusColor(node.status))
    g.append('title').text(`${node.label}\n${node.sublabel}\nStatus: ${node.status}${node.port ? `\nPort: ${node.port}` : ''}`)

    return { node, g }
  })

  // ── Update positions (called on every tick and drag) ──
  const updatePositions = () => {
    nodeElements.forEach(({ node, g }) => {
      g.attr('transform', `translate(${node.x}, ${node.y})`)
    })

    edgeLines.forEach(({ edge, line, labelText, protocolText }) => {
      const from = nodeMap.get(edge.from)!
      const to = nodeMap.get(edge.to)!
      line.attr('x1', from.x).attr('y1', from.y).attr('x2', to.x).attr('y2', to.y)
      const mx = (from.x + to.x) / 2
      const my = (from.y + to.y) / 2
      labelText.attr('x', mx).attr('y', my - 4)
      protocolText.attr('x', mx).attr('y', my + 8)
    })
  }

  // ── D3 Force Simulation (gentle — keeps nodes near initial positions) ──
  simulation = d3.forceSimulation(nodes as any)
    .force('charge', d3.forceManyBody().strength(-300))
    .force('center', d3.forceCenter(width / 2, height / 2).strength(0.02))
    .force('collision', d3.forceCollide(70))
    .force('link', d3.forceLink(edges.map(e => ({
      source: e.from,
      target: e.to,
    }))).id((d: any) => d.id).distance(180).strength(0.3))
    .alphaDecay(0.05)
    .on('tick', updatePositions)

  // Let simulation settle quickly, then stop (nodes stay draggable)
  simulation.alpha(0.3).restart()

  // ── Drag behavior ──
  const drag = d3.drag<SVGGElement, any>()
    .on('start', (event, d) => {
      if (!event.active && simulation) simulation.alphaTarget(0.1).restart()
      d.fx = d.x
      d.fy = d.y
      d3.select(event.sourceEvent.target.closest('g')).style('cursor', 'grabbing')
    })
    .on('drag', (event, d) => {
      d.fx = event.x
      d.fy = event.y
    })
    .on('end', (event, d) => {
      if (!event.active && simulation) simulation.alphaTarget(0)
      d.fx = event.x
      d.fy = event.y
      d3.select(event.sourceEvent.target.closest('g')).style('cursor', 'grab')
    })

  nodeElements.forEach(({ g }) => {
    g.call(drag as any)
  })

  updatePositions()
}

let pollInterval: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  checkServices()
  pollInterval = setInterval(checkServices, 5000)
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
  if (simulation) simulation.stop()
  diagramDrawn = false
})
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          System Architecture
        </h4>
        <p class="text-body-1 text-medium-emphasis mb-0">
          Live service connectivity diagram — green = healthy, orange = degraded, red = offline
        </p>
      </div>
      <VBtn
        icon="tabler-refresh"
        variant="text"
        :loading="loading"
        @click="checkServices"
      />
    </div>

    <VCard>
      <VCardText class="pa-0">
        <div
          class="architecture-container"
          style="min-height: 700px; background: rgb(var(--v-theme-surface));"
        >
          <svg
            ref="svgRef"
            width="100%"
            height="700"
            style="display: block;"
          />
        </div>
      </VCardText>
    </VCard>

    <!-- Legend -->
    <VCard class="mt-4">
      <VCardText class="d-flex flex-wrap gap-6 align-center">
        <div class="d-flex align-center gap-2">
          <div
            style="width: 12px; height: 12px; border-radius: 50%; background: #28C76F;"
          />
          <span class="text-body-2">Healthy</span>
        </div>
        <div class="d-flex align-center gap-2">
          <div
            style="width: 12px; height: 12px; border-radius: 50%; background: #FF9F43;"
          />
          <span class="text-body-2">Degraded</span>
        </div>
        <div class="d-flex align-center gap-2">
          <div
            style="width: 12px; height: 12px; border-radius: 50%; background: #EA5455;"
          />
          <span class="text-body-2">Offline</span>
        </div>
        <VDivider vertical />
        <span class="text-body-2 text-medium-emphasis">
          Auto-refreshes every 5 seconds. Solid lines = active connection, dashed = disconnected.
        </span>
      </VCardText>
    </VCard>
  </div>
</template>

<style scoped>
.architecture-container {
  overflow: hidden;
  border-radius: 8px;
}
</style>
