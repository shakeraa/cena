<script setup lang="ts">
import * as d3 from 'd3'
import { $api } from '@/utils/api'

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
const natsEvents = ref(0)

const checkServices = async () => {
  // Check Actor Host
  try {
    const data = await fetch('/api/actors/stats').then(r => r.json())
    actorHostStatus.value = 'healthy'
    activeActors.value = data.activeActorCount ?? 0
  }
  catch { actorHostStatus.value = 'offline' }

  // Check Admin API
  try {
    await fetch('/api/admin/system/health').then(r => { if (!r.ok && r.status !== 401) throw new Error() })
    adminApiStatus.value = 'healthy'
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
  drawDiagram()
}

interface ServiceNode {
  id: string
  label: string
  sublabel: string
  x: number
  y: number
  status: 'healthy' | 'degraded' | 'offline'
  icon: string
  port?: string
}

interface ServiceEdge {
  from: string
  to: string
  label: string
  protocol: string
  active: boolean
}

const drawDiagram = () => {
  if (!svgRef.value) return

  const svg = d3.select(svgRef.value)
  svg.selectAll('*').remove()

  const width = svgRef.value.clientWidth || 1000
  const height = 700

  svg.attr('viewBox', `0 0 ${width} ${height}`)

  const statusColor = (s: string) => s === 'healthy' ? '#28C76F' : s === 'degraded' ? '#FF9F43' : '#EA5455'
  const statusGlow = (s: string) => s === 'healthy' ? 'rgba(40,199,111,0.3)' : s === 'degraded' ? 'rgba(255,159,67,0.3)' : 'rgba(234,84,85,0.2)'

  const nodes: ServiceNode[] = [
    { id: 'frontend', label: 'Admin Dashboard', sublabel: 'Vite + Vue 3 + Vuetify', x: width * 0.5, y: 60, status: frontendStatus.value, icon: 'V', port: '5174' },
    { id: 'admin-api', label: 'Admin API', sublabel: '.NET 9 REST + Firebase Auth', x: width * 0.3, y: 220, status: adminApiStatus.value, icon: 'A', port: '5000' },
    { id: 'actor-host', label: 'Actor Host', sublabel: `Proto.Actor Cluster (${activeActors.value} actors)`, x: width * 0.7, y: 220, status: actorHostStatus.value, icon: 'P', port: '5001' },
    { id: 'nats', label: 'NATS JetStream', sublabel: 'Message Bus', x: width * 0.5, y: 380, status: natsStatus.value, icon: 'N', port: '4222' },
    { id: 'postgres', label: 'PostgreSQL', sublabel: 'Marten Event Store', x: width * 0.2, y: 520, status: pgStatus.value, icon: 'PG', port: '5433' },
    { id: 'redis', label: 'Redis', sublabel: 'Cache + Sessions', x: width * 0.5, y: 520, status: redisStatus.value, icon: 'R', port: '6379' },
    { id: 'firebase', label: 'Firebase Auth', sublabel: 'Google OAuth + JWT', x: width * 0.15, y: 380, status: 'healthy', icon: 'F' },
    { id: 'emulator', label: 'Student Emulator', sublabel: `${activeActors.value > 0 ? '100 students' : 'Idle'}`, x: width * 0.85, y: 380, status: activeActors.value > 0 ? 'healthy' : 'offline', icon: 'E' },
  ]

  const edges: ServiceEdge[] = [
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

  const nodeMap = new Map(nodes.map(n => [n.id, n]))

  // Draw edges
  const edgeGroup = svg.append('g')
  edges.forEach(edge => {
    const from = nodeMap.get(edge.from)!
    const to = nodeMap.get(edge.to)!
    if (!from || !to) return

    const line = edgeGroup.append('line')
      .attr('x1', from.x)
      .attr('y1', from.y + 30)
      .attr('x2', to.x)
      .attr('y2', to.y - 30)
      .attr('stroke', edge.active ? 'rgba(115,103,240,0.4)' : 'rgba(255,255,255,0.08)')
      .attr('stroke-width', edge.active ? 2 : 1)
      .attr('stroke-dasharray', edge.active ? 'none' : '4,4')

    // Edge label
    const mx = (from.x + to.x) / 2
    const my = (from.y + to.y) / 2 + 5
    edgeGroup.append('text')
      .attr('x', mx)
      .attr('y', my)
      .attr('text-anchor', 'middle')
      .attr('font-size', '9px')
      .attr('fill', edge.active ? 'rgba(255,255,255,0.5)' : 'rgba(255,255,255,0.2)')
      .text(edge.label)

    // Protocol badge
    edgeGroup.append('text')
      .attr('x', mx)
      .attr('y', my + 12)
      .attr('text-anchor', 'middle')
      .attr('font-size', '8px')
      .attr('fill', 'rgba(115,103,240,0.6)')
      .text(edge.protocol)
  })

  // Draw nodes
  const nodeGroup = svg.append('g')
  nodes.forEach(node => {
    const g = nodeGroup.append('g')
      .attr('transform', `translate(${node.x}, ${node.y})`)
      .style('cursor', 'pointer')

    // Glow
    g.append('circle')
      .attr('r', 40)
      .attr('fill', statusGlow(node.status))
      .attr('opacity', 0.5)

    // Main circle
    g.append('circle')
      .attr('r', 32)
      .attr('fill', 'rgba(40,42,54,0.9)')
      .attr('stroke', statusColor(node.status))
      .attr('stroke-width', 2.5)

    // Icon
    g.append('text')
      .attr('text-anchor', 'middle')
      .attr('dy', 5)
      .attr('font-size', '14px')
      .attr('font-weight', '700')
      .attr('fill', statusColor(node.status))
      .text(node.icon)

    // Label
    g.append('text')
      .attr('y', 48)
      .attr('text-anchor', 'middle')
      .attr('font-size', '12px')
      .attr('font-weight', '600')
      .attr('fill', 'rgba(255,255,255,0.9)')
      .text(node.label)

    // Sublabel
    g.append('text')
      .attr('y', 62)
      .attr('text-anchor', 'middle')
      .attr('font-size', '9px')
      .attr('fill', 'rgba(255,255,255,0.5)')
      .text(node.sublabel)

    // Port badge
    if (node.port) {
      g.append('text')
        .attr('y', -38)
        .attr('text-anchor', 'middle')
        .attr('font-size', '9px')
        .attr('fill', 'rgba(115,103,240,0.7)')
        .text(`:${node.port}`)
    }

    // Status dot
    g.append('circle')
      .attr('cx', 24)
      .attr('cy', -24)
      .attr('r', 5)
      .attr('fill', statusColor(node.status))

    // Tooltip
    g.append('title')
      .text(`${node.label}\n${node.sublabel}\nStatus: ${node.status}${node.port ? `\nPort: ${node.port}` : ''}`)
  })
}

let pollInterval: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  checkServices()
  pollInterval = setInterval(checkServices, 5000)
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
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
