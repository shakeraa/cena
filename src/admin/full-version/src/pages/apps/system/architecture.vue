<script setup lang="ts">
import * as d3 from 'd3'
import { firebaseAuth } from '@/plugins/firebase'

definePage({ meta: { action: 'read', subject: 'System' } })

const svgRef = ref<SVGSVGElement | null>(null)
const loading = ref(true)
const actorHostStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const adminApiStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const studentApiStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const natsStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const pgStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const redisStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const firebaseStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const sympyStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const emulatorStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const frontendStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const studentSpaStatus = ref<'healthy' | 'degraded' | 'offline'>('offline')
const activeActors = ref(0)
const commandsRouted = ref(0)
const emulatorSublabel = ref('Idle')

const statusColor = (s: string) => s === 'healthy' ? '#28C76F' : s === 'degraded' ? '#FF9F43' : '#EA5455'
const statusGlow = (s: string) => s === 'healthy' ? 'rgba(40,199,111,0.4)' : s === 'degraded' ? 'rgba(255,159,67,0.4)' : 'rgba(234,84,85,0.25)'

const protocolColor: Record<string, string> = {
  HTTP: '#7367F0',
  HTTPS: '#FF9F43',
  NATS: '#00BCD4',
  TCP: '#28C76F',
}

// Category colors for actor types
const categoryColor: Record<string, string> = {
  core: '#7367F0',
  graph: '#00BCD4',
  ai: '#FF9F43',
  outreach: '#28C76F',
  infra: '#EA5455',
}

interface ClusterChild {
  id: string
  label: string
  icon: string
  category: string
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
  children?: ClusterChild[]
  expanded?: boolean
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
let diagramDrawn = false

// D3 element refs for in-place updates
let nodeElements: { node: ServiceNode; g: d3.Selection<SVGGElement, any, any, any> }[] = []
let edgeLines: { edge: ServiceEdge; line: d3.Selection<SVGLineElement, any, any, any>; labelText: d3.Selection<SVGTextElement, any, any, any>; protocolText: d3.Selection<SVGTextElement, any, any, any>; labelBg?: d3.Selection<SVGRectElement, any, any, any> }[] = []

// Cluster expand state — persists across redraws
const expandedClusters = ref<Set<string>>(new Set())

const actorChildren: ClusterChild[] = [
  { id: 'student-actor', label: 'Student', icon: 'S', category: 'core' },
  { id: 'session-actor', label: 'Session', icon: 'LS', category: 'core' },
  { id: 'curriculum-actor', label: 'Curriculum', icon: 'CG', category: 'graph' },
  { id: 'mcm-actor', label: 'MCM Graph', icon: 'M', category: 'graph' },
  { id: 'tutor-actor', label: 'Tutor', icon: 'TA', category: 'ai' },
  { id: 'llm-gateway', label: 'LLM Gateway', icon: 'LG', category: 'ai' },
  { id: 'circuit-breaker', label: 'Circuit Breaker', icon: 'CB', category: 'ai' },
  { id: 'outreach-actor', label: 'Outreach', icon: 'OR', category: 'outreach' },
  { id: 'stagnation-actor', label: 'Stagnation', icon: 'SD', category: 'outreach' },
  { id: 'health-actor', label: 'Health Agg', icon: 'HA', category: 'infra' },
  { id: 'feature-flag', label: 'Feature Flag', icon: 'FF', category: 'infra' },
  { id: 'conversation-actor', label: 'Messaging', icon: 'CT', category: 'infra' },
  { id: 'analytics-actor', label: 'Analytics', icon: 'AN', category: 'infra' },
]

const getNodeStatus = (id: string): 'healthy' | 'degraded' | 'offline' => {
  switch (id) {
    case 'admin-spa': return frontendStatus.value
    case 'student-spa': return studentSpaStatus.value
    case 'admin-api': return adminApiStatus.value
    case 'student-api': return studentApiStatus.value
    case 'actor-host': return actorHostStatus.value
    case 'nats': return natsStatus.value
    case 'postgres': return pgStatus.value
    case 'redis': return redisStatus.value
    case 'firebase': return firebaseStatus.value
    case 'sympy': return sympyStatus.value
    case 'emulator': return emulatorStatus.value
    default: return 'offline'
  }
}

const getEdgeActive = (edge: ServiceEdge): boolean => {
  switch (`${edge.from}-${edge.to}`) {
    case 'admin-spa-admin-api':
    case 'admin-spa-actor-host':
    case 'student-spa-student-api':
    case 'admin-spa-firebase':
    case 'student-spa-firebase':
    case 'admin-api-firebase':
    case 'student-api-firebase':
    case 'actor-host-firebase':
      return true
    case 'admin-api-nats':
    case 'student-api-nats':
    case 'actor-host-nats':
      return natsStatus.value === 'healthy'
    case 'emulator-nats':
      return emulatorStatus.value === 'healthy'
    case 'admin-api-sympy':
    case 'actor-host-sympy':
      return sympyStatus.value === 'healthy'
    case 'admin-api-postgres':
    case 'student-api-postgres':
    case 'actor-host-postgres':
      return pgStatus.value === 'healthy'
    case 'admin-api-redis':
    case 'student-api-redis':
    case 'actor-host-redis':
      return redisStatus.value === 'healthy'
    default:
      return false
  }
}

// RDY-056 §5: attach the Firebase bearer so admin-api + actor-host accept
// the probe calls. Plain fetch() was firing unauthenticated, producing a
// 401 cascade that painted every node red even when every backend was up.
async function authedFetch(url: string): Promise<Response> {
  const user = firebaseAuth.currentUser
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (user) {
    const token = await user.getIdToken()
    headers.Authorization = `Bearer ${token}`
  }
  return fetch(url, { headers })
}

// Health status is also sourced from admin-api's per-service probe —
// the dashboard's pg/redis/nats cards should reflect what the server
// reports, not just "same as admin-api".
function mapProbeStatus(s: string): 'healthy' | 'degraded' | 'offline' {
  if (s === 'up' || s === 'healthy') return 'healthy'
  if (s === 'degraded') return 'degraded'
  return 'offline'
}

const checkServices = async () => {
  // Actor host — stats + live-actor count
  try {
    const r = await authedFetch('/api/actors/stats')
    if (r.ok) {
      const data = await r.json()
      actorHostStatus.value = 'healthy'
      activeActors.value = data.activeActorCount ?? 0
      commandsRouted.value = data.commandsRouted ?? 0
    }
    else {
      actorHostStatus.value = 'offline'
    }
  }
  catch { actorHostStatus.value = 'offline' }

  // Admin API + per-service fanout (NATS/PG/Redis read off its probe)
  try {
    const r = await authedFetch('/api/admin/system/health')
    if (r.ok) {
      adminApiStatus.value = 'healthy'
      const data = await r.json() as { services: { name: string; status: string }[] }
      for (const s of data.services ?? []) {
        const st = mapProbeStatus(s.status)
        if (s.name === 'NATS') natsStatus.value = st
        else if (s.name === 'PostgreSQL') pgStatus.value = st
        else if (s.name === 'Redis') redisStatus.value = st
        else if (s.name === 'Actor Host' && actorHostStatus.value !== 'healthy') actorHostStatus.value = st
      }
    }
    else {
      adminApiStatus.value = 'offline'
    }
  }
  catch { adminApiStatus.value = 'offline' }

  // Student API — /health/live is unauthenticated; reach it via the SPA
  // origin so the browser's CORS rules apply rather than a cross-origin fetch.
  // The student-api port is exposed on the host; probe by origin fetch.
  try {
    const studentApiOrigin = import.meta.env.VITE_STUDENT_API_BASE_URL ?? 'http://localhost:5050'
    // no-cors returns an opaque response; reaching it without throwing is
    // our liveness signal. Avoids needing CORS on /health/live.
    await fetch(`${studentApiOrigin}/health/live`, { method: 'GET', mode: 'no-cors' })
    studentApiStatus.value = 'healthy'
  }
  catch { studentApiStatus.value = 'offline' }

  // Firebase emulator liveness — also use no-cors to avoid CORS preflight.
  try {
    const emuHost = import.meta.env.VITE_FIREBASE_AUTH_EMULATOR_HOST ?? 'http://localhost:9099'
    await fetch(`${emuHost}/`, { method: 'GET', mode: 'no-cors' })
    firebaseStatus.value = 'healthy'
  }
  catch { firebaseStatus.value = 'offline' }

  // SymPy sidecar — exposed through the admin-api as part of the CAS
  // gate's own startup probe (CAS-STARTUP-OK gauge). Derive from
  // natsStatus: sidecar lives on NATS, so if NATS is healthy + the
  // actor-host reports healthy we assume sympy is reachable. A direct
  // probe would need a new admin endpoint.
  sympyStatus.value = (natsStatus.value === 'healthy' && actorHostStatus.value === 'healthy')
    ? 'healthy' : 'degraded'

  // Student Emulator — NATS traffic volume is the liveness signal.
  // commandsRouted > 0 means the actor-host is servicing attempts.
  if (commandsRouted.value > 0) {
    emulatorStatus.value = 'healthy'
    emulatorSublabel.value = `${commandsRouted.value} commands routed`
  }
  else {
    emulatorStatus.value = 'offline'
    emulatorSublabel.value = 'Idle'
  }

  // Frontends are trivially healthy — if we're rendering, admin SPA is up.
  frontendStatus.value = 'healthy'
  // Student SPA: probe via the public port so we don't hide a broken SPA.
  try {
    const studentSpaOrigin = import.meta.env.VITE_STUDENT_SPA_URL ?? 'http://localhost:5175'
    const r = await fetch(`${studentSpaOrigin}/`, { method: 'GET', mode: 'no-cors' })
    // no-cors means opaque response; reaching it without throwing is "up"
    studentSpaStatus.value = 'healthy'
    void r
  }
  catch { studentSpaStatus.value = 'offline' }

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
  nodeElements.forEach(({ node, g }) => {
    const status = getNodeStatus(node.id)
    node.status = status

    const sublabel = node.id === 'actor-host'
      ? `Proto.Actor Cluster (${activeActors.value} actors)`
      : node.id === 'emulator'
        ? (activeActors.value > 0 ? '100 students' : 'Idle')
        : node.sublabel

    node.sublabel = sublabel

    // Update colors on the collapsed node elements
    if (!node.expanded) {
      g.select('.node-glow').attr('fill', statusGlow(status))
      g.select('.node-circle').attr('stroke', statusColor(status))
      g.select('.node-icon').attr('fill', statusColor(status))
      g.select('.node-sublabel').text(sublabel)
      g.select('.node-status-dot').attr('fill', statusColor(status))
    }
    else {
      // Update expanded cluster border
      g.select('.cluster-border').attr('stroke', statusColor(status))
      g.select('.cluster-title').attr('fill', statusColor(status))
      g.select('.cluster-sublabel').text(sublabel)
    }

    g.select('title').text(`${node.label}\n${sublabel}\nStatus: ${status}${node.port ? `\nPort: ${node.port}` : ''}`)
  })

  edgeLines.forEach(({ edge, line, labelText }) => {
    const active = getEdgeActive(edge)
    edge.active = active
    const color = protocolColor[edge.protocol] ?? '#7367F0'
    line
      .attr('stroke', active ? color : 'rgba(200,200,220,0.35)')
      .attr('stroke-width', active ? 2.5 : 2)
      .attr('stroke-dasharray', active ? 'none' : '8,5')
      .attr('opacity', active ? 0.9 : 0.7)
    labelText.attr('fill', active ? 'rgba(255,255,255,0.95)' : 'rgba(220,220,230,0.7)')
  })
}

const toggleCluster = (node: ServiceNode) => {
  if (!node.children?.length) return
  if (expandedClusters.value.has(node.id)) {
    expandedClusters.value.delete(node.id)
  }
  else {
    expandedClusters.value.add(node.id)
  }
  // Full redraw to recalculate layout
  diagramDrawn = false
  drawDiagram()
  diagramDrawn = true
}

const drawDiagram = () => {
  if (!svgRef.value) return

  const svg = d3.select(svgRef.value)
  svg.selectAll('*').remove()

  const width = Math.max(svgRef.value.clientWidth, 900)
  const height = 750

  svg.attr('viewBox', `0 0 ${width} ${height}`)

  const px = 100
  const iw = width - px * 2

  // RDY-056 §5: diagram labels are driven by Vite env vars so prod / dev /
  // compose layouts all render the same source without code edits.
  // Defaults line up with the dockerised dev stack.
  const portFrontend = import.meta.env.VITE_PORT_FRONTEND ?? '5174'
  const portStudentSpa = import.meta.env.VITE_PORT_STUDENT_SPA ?? '5175'
  const portAdminApi = import.meta.env.VITE_PORT_ADMIN_API ?? '5052'
  const portStudentApi = import.meta.env.VITE_PORT_STUDENT_API ?? '5050'
  const portActorHost = import.meta.env.VITE_PORT_ACTOR_HOST ?? '5050'
  const portNats = import.meta.env.VITE_PORT_NATS ?? '4222'
  const portPostgres = import.meta.env.VITE_PORT_POSTGRES ?? '5433'
  const portRedis = import.meta.env.VITE_PORT_REDIS ?? '6379'
  const portFirebaseEmu = import.meta.env.VITE_PORT_FIREBASE_EMU ?? '9099'

  // Layout: 3 rows × {frontend, middle-tier, infra}. Widen the middle row
  // so the new nodes (Student SPA, Student API, SymPy) fit without
  // overlapping. Firebase Emulator + CAS sidecar read status from
  // admin-api's per-service probe (see mapProbeStatus above); emulator
  // status comes from NATS traffic (non-zero commandsRouted = alive).
  nodes = [
    // ── Row 0: frontends ──────────────────────────────────────────────
    { id: 'admin-spa', label: 'Admin Dashboard', sublabel: 'Vite + Vue 3 + Vuetify', x: px + iw * 0.30, y: 70, status: frontendStatus.value, icon: 'V', port: portFrontend },
    { id: 'student-spa', label: 'Student PWA', sublabel: 'Vite + Vue 3 + Vuetify', x: px + iw * 0.70, y: 70, status: studentSpaStatus.value, icon: 'S', port: portStudentSpa },

    // ── Row 1: APIs + actor host ─────────────────────────────────────
    { id: 'admin-api', label: 'Admin API', sublabel: '.NET 9 REST + Firebase Auth', x: px + iw * 0.15, y: 250, status: adminApiStatus.value, icon: 'A', port: portAdminApi },
    { id: 'student-api', label: 'Student API', sublabel: '.NET 9 REST + SignalR', x: px + iw * 0.50, y: 250, status: studentApiStatus.value, icon: 'St', port: portStudentApi },
    { id: 'actor-host', label: 'Actor Host', sublabel: `Proto.Actor Cluster (${activeActors.value} actors)`, x: px + iw * 0.85, y: 250, status: actorHostStatus.value, icon: 'P', port: portActorHost, children: actorChildren, expanded: expandedClusters.value.has('actor-host') },

    // ── Row 2: message bus, auth emulator, CAS sidecar ───────────────
    { id: 'firebase', label: 'Firebase Emulator', sublabel: 'Auth (dev)', x: px + iw * 0.00, y: 430, status: firebaseStatus.value, icon: 'F', port: portFirebaseEmu },
    { id: 'nats', label: 'NATS JetStream', sublabel: 'Message Bus', x: px + iw * 0.35, y: 430, status: natsStatus.value, icon: 'N', port: portNats },
    { id: 'sympy', label: 'SymPy Sidecar', sublabel: 'CAS Tier 2 (NATS)', x: px + iw * 0.65, y: 430, status: sympyStatus.value, icon: 'Σ' },
    { id: 'emulator', label: 'Student Emulator', sublabel: emulatorSublabel.value, x: px + iw * 1.00, y: 430, status: emulatorStatus.value, icon: 'E' },

    // ── Row 3: datastores ────────────────────────────────────────────
    { id: 'postgres', label: 'PostgreSQL', sublabel: 'Marten Event Store', x: px + iw * 0.20, y: 620, status: pgStatus.value, icon: 'PG', port: portPostgres },
    { id: 'redis', label: 'Redis', sublabel: 'Cache + Sessions', x: px + iw * 0.60, y: 620, status: redisStatus.value, icon: 'R', port: portRedis },
  ]

  edges = [
    // Frontends → APIs
    { from: 'admin-spa', to: 'admin-api', label: 'REST /api/*', protocol: 'HTTP', active: true },
    { from: 'admin-spa', to: 'actor-host', label: '/api/actors/*', protocol: 'HTTP', active: true },
    { from: 'student-spa', to: 'student-api', label: 'REST /api/*', protocol: 'HTTP', active: true },
    { from: 'student-spa', to: 'firebase', label: 'Sign-in', protocol: 'HTTPS', active: true },
    { from: 'admin-spa', to: 'firebase', label: 'Sign-in', protocol: 'HTTPS', active: true },

    // APIs/host ↔ NATS
    { from: 'admin-api', to: 'nats', label: 'Events pub/sub', protocol: 'NATS', active: natsStatus.value === 'healthy' },
    { from: 'student-api', to: 'nats', label: 'Commands', protocol: 'NATS', active: natsStatus.value === 'healthy' },
    { from: 'actor-host', to: 'nats', label: 'Pub/Sub', protocol: 'NATS', active: natsStatus.value === 'healthy' },
    { from: 'emulator', to: 'nats', label: 'Publish attempts', protocol: 'NATS', active: emulatorStatus.value === 'healthy' },

    // CAS cascade: admin-api + actor-host → SymPy via NATS
    { from: 'admin-api', to: 'sympy', label: 'CAS verify', protocol: 'NATS', active: sympyStatus.value === 'healthy' },
    { from: 'actor-host', to: 'sympy', label: 'CAS verify', protocol: 'NATS', active: sympyStatus.value === 'healthy' },

    // Auth verification (server-side JWT check)
    { from: 'admin-api', to: 'firebase', label: 'Token verify', protocol: 'HTTPS', active: true },
    { from: 'student-api', to: 'firebase', label: 'Token verify', protocol: 'HTTPS', active: true },
    { from: 'actor-host', to: 'firebase', label: 'Token verify', protocol: 'HTTPS', active: true },

    // Datastore reads/writes
    { from: 'admin-api', to: 'postgres', label: 'Marten queries', protocol: 'TCP', active: pgStatus.value === 'healthy' },
    { from: 'student-api', to: 'postgres', label: 'Marten queries', protocol: 'TCP', active: pgStatus.value === 'healthy' },
    { from: 'actor-host', to: 'postgres', label: 'Event sourcing', protocol: 'TCP', active: pgStatus.value === 'healthy' },
    { from: 'admin-api', to: 'redis', label: 'Cache', protocol: 'TCP', active: redisStatus.value === 'healthy' },
    { from: 'student-api', to: 'redis', label: 'Cache', protocol: 'TCP', active: redisStatus.value === 'healthy' },
    { from: 'actor-host', to: 'redis', label: 'State cache', protocol: 'TCP', active: redisStatus.value === 'healthy' },
  ]

  nodeMap = new Map(nodes.map(n => [n.id, n]))

  // ── Arrow markers per protocol ──
  const defs = svg.append('defs')
  for (const [proto, color] of Object.entries(protocolColor)) {
    defs.append('marker')
      .attr('id', `arrow-${proto}`)
      .attr('viewBox', '0 0 10 6')
      .attr('refX', 10)
      .attr('refY', 3)
      .attr('markerWidth', 8)
      .attr('markerHeight', 6)
      .attr('orient', 'auto')
      .append('path')
      .attr('d', 'M0,0 L10,3 L0,6 Z')
      .attr('fill', color)
      .attr('opacity', 0.9)
  }

  // ── Edge elements ──
  const edgeGroup = svg.append('g')
  edgeLines = edges.map(edge => {
    const color = protocolColor[edge.protocol] ?? '#7367F0'
    const line = edgeGroup.append('line')
      .attr('stroke', edge.active ? color : 'rgba(200,200,220,0.35)')
      .attr('stroke-width', edge.active ? 2.5 : 2)
      .attr('stroke-dasharray', edge.active ? 'none' : '8,5')
      .attr('opacity', edge.active ? 0.9 : 0.7)
      .attr('marker-end', `url(#arrow-${edge.protocol})`)

    const labelBg = edgeGroup.append('rect')
      .attr('rx', 6).attr('ry', 6)
      .attr('fill', 'rgba(22,24,36,0.92)')
      .attr('stroke', color)
      .attr('stroke-width', 1)
      .attr('opacity', 1)

    const labelText = edgeGroup.append('text')
      .attr('text-anchor', 'middle')
      .attr('font-size', '11px')
      .attr('font-weight', '600')
      .attr('fill', edge.active ? 'rgba(255,255,255,0.95)' : 'rgba(220,220,230,0.7)')
      .text(edge.label)

    const protocolText = edgeGroup.append('text')
      .attr('text-anchor', 'middle')
      .attr('font-size', '10px')
      .attr('font-weight', '700')
      .attr('fill', color)
      .attr('opacity', 0.95)
      .text(edge.protocol)

    return { edge, line, labelText, protocolText, labelBg }
  })

  // ── Node elements ──
  const nodeGroup = svg.append('g')
  nodeElements = nodes.map(node => {
    const g = nodeGroup.append('g')
      .style('cursor', node.children?.length ? 'pointer' : 'grab')
      .datum(node)

    if (node.expanded && node.children?.length) {
      drawExpandedCluster(g, node)
    }
    else {
      drawCollapsedNode(g, node)
    }

    g.append('title').text(`${node.label}\n${node.sublabel}\nStatus: ${node.status}${node.port ? `\nPort: ${node.port}` : ''}${node.children?.length ? '\nClick to expand cluster' : ''}`)

    return { node, g }
  })

  // ── Click handler for clusters ──
  nodeElements.forEach(({ node, g }) => {
    if (node.children?.length) {
      g.on('click', (event) => {
        event.stopPropagation()
        toggleCluster(node)
      })
    }
  })

  // ── Update positions ──
  const updatePositions = () => {
    nodeElements.forEach(({ node, g }) => {
      g.attr('transform', `translate(${node.x}, ${node.y})`)
    })

    edgeLines.forEach(({ edge, line, labelText, protocolText, labelBg }) => {
      const from = nodeMap.get(edge.from)!
      const to = nodeMap.get(edge.to)!
      const dx = to.x - from.x
      const dy = to.y - from.y
      const dist = Math.sqrt(dx * dx + dy * dy) || 1
      // Use larger offset for expanded clusters
      const fromOffset = from.expanded ? 90 : 40
      const toOffset = to.expanded ? 90 : 40
      line
        .attr('x1', from.x + (dx / dist) * fromOffset)
        .attr('y1', from.y + (dy / dist) * fromOffset)
        .attr('x2', to.x - (dx / dist) * toOffset)
        .attr('y2', to.y - (dy / dist) * toOffset)
      const mx = (from.x + to.x) / 2
      const my = (from.y + to.y) / 2
      labelText.attr('x', mx).attr('y', my - 5)
      protocolText.attr('x', mx).attr('y', my + 9)
      if (labelBg) {
        const textLen = Math.max((edge.label.length * 6) + 12, 50)
        labelBg.attr('x', mx - textLen / 2).attr('y', my - 18).attr('width', textLen).attr('height', 30)
      }
    })
  }

  // ── Drag behavior ──
  const drag = d3.drag<SVGGElement, any>()
    .on('start', (event, _d) => {
      d3.select(event.sourceEvent.target.closest('g')).style('cursor', 'grabbing')
    })
    .on('drag', (event, d) => {
      d.x = event.x
      d.y = event.y
      updatePositions()
    })
    .on('end', (event, d) => {
      const el = d3.select(event.sourceEvent.target.closest('g'))
      el.style('cursor', d.children?.length ? 'pointer' : 'grab')
    })

  nodeElements.forEach(({ g }) => {
    g.call(drag as any)
  })

  updatePositions()
}

// ── Draw a collapsed (single circle) node ──
const drawCollapsedNode = (g: d3.Selection<SVGGElement, any, any, any>, node: ServiceNode) => {
  g.append('circle').attr('class', 'node-glow').attr('r', 44).attr('fill', statusGlow(node.status)).attr('opacity', 0.6)
  g.append('circle').attr('class', 'node-circle').attr('r', 36).attr('fill', 'rgba(30,32,44,0.95)').attr('stroke', statusColor(node.status)).attr('stroke-width', 3)
  g.append('text').attr('class', 'node-icon').attr('text-anchor', 'middle').attr('dy', 6).attr('font-size', '16px').attr('font-weight', '800').attr('fill', statusColor(node.status)).text(node.icon)
  g.append('text').attr('class', 'node-label').attr('y', 52).attr('text-anchor', 'middle').attr('font-size', '13px').attr('font-weight', '600').attr('fill', 'rgba(255,255,255,0.95)').text(node.label)
  g.append('text').attr('class', 'node-sublabel').attr('y', 68).attr('text-anchor', 'middle').attr('font-size', '10px').attr('fill', 'rgba(255,255,255,0.6)').text(node.sublabel)

  if (node.port) {
    g.append('rect').attr('x', -16).attr('y', -52).attr('width', 32).attr('height', 16).attr('rx', 8).attr('fill', 'rgba(115,103,240,0.15)').attr('stroke', 'rgba(115,103,240,0.4)').attr('stroke-width', 1)
    g.append('text').attr('y', -40).attr('text-anchor', 'middle').attr('font-size', '9px').attr('font-weight', '600').attr('fill', 'rgba(115,103,240,0.9)').text(`:${node.port}`)
  }

  g.append('circle').attr('class', 'node-status-dot').attr('cx', 28).attr('cy', -28).attr('r', 6).attr('fill', statusColor(node.status)).attr('stroke', 'rgba(30,32,44,0.95)').attr('stroke-width', 2)

  // Expand indicator for cluster nodes
  if (node.children?.length) {
    g.append('circle').attr('cx', -28).attr('cy', -28).attr('r', 10).attr('fill', 'rgba(115,103,240,0.2)').attr('stroke', 'rgba(115,103,240,0.5)').attr('stroke-width', 1)
    g.append('text').attr('x', -28).attr('y', -24).attr('text-anchor', 'middle').attr('font-size', '11px').attr('font-weight', '700').attr('fill', 'rgba(115,103,240,0.9)').text('+')
  }
}

// ── Draw an expanded cluster with child nodes ──
const drawExpandedCluster = (g: d3.Selection<SVGGElement, any, any, any>, node: ServiceNode) => {
  const children = node.children!
  const cols = 4
  const rows = Math.ceil(children.length / cols)
  const cellW = 80
  const cellH = 60
  const padX = 20
  const padTop = 50
  const padBottom = 30
  const clusterW = cols * cellW + padX * 2
  const clusterH = rows * cellH + padTop + padBottom

  // Cluster background
  g.append('rect')
    .attr('class', 'cluster-bg')
    .attr('x', -clusterW / 2)
    .attr('y', -clusterH / 2)
    .attr('width', clusterW)
    .attr('height', clusterH)
    .attr('rx', 16)
    .attr('fill', 'rgba(22,24,36,0.95)')
    .attr('stroke', 'rgba(115,103,240,0.15)')
    .attr('stroke-width', 1)

  // Cluster border with status color
  g.append('rect')
    .attr('class', 'cluster-border')
    .attr('x', -clusterW / 2)
    .attr('y', -clusterH / 2)
    .attr('width', clusterW)
    .attr('height', clusterH)
    .attr('rx', 16)
    .attr('fill', 'none')
    .attr('stroke', statusColor(node.status))
    .attr('stroke-width', 2)
    .attr('opacity', 0.6)

  // Title
  g.append('text')
    .attr('class', 'cluster-title')
    .attr('x', 0)
    .attr('y', -clusterH / 2 + 22)
    .attr('text-anchor', 'middle')
    .attr('font-size', '14px')
    .attr('font-weight', '700')
    .attr('fill', statusColor(node.status))
    .text(node.label)

  // Sublabel
  g.append('text')
    .attr('class', 'cluster-sublabel')
    .attr('x', 0)
    .attr('y', -clusterH / 2 + 38)
    .attr('text-anchor', 'middle')
    .attr('font-size', '10px')
    .attr('fill', 'rgba(255,255,255,0.6)')
    .text(node.sublabel)

  // Port badge
  if (node.port) {
    g.append('rect').attr('x', clusterW / 2 - 36).attr('y', -clusterH / 2 + 6).attr('width', 32).attr('height', 16).attr('rx', 8).attr('fill', 'rgba(115,103,240,0.15)').attr('stroke', 'rgba(115,103,240,0.4)').attr('stroke-width', 1)
    g.append('text').attr('x', clusterW / 2 - 20).attr('y', -clusterH / 2 + 18).attr('text-anchor', 'middle').attr('font-size', '9px').attr('font-weight', '600').attr('fill', 'rgba(115,103,240,0.9)').text(`:${node.port}`)
  }

  // Collapse button (top-left)
  g.append('circle').attr('cx', -clusterW / 2 + 16).attr('cy', -clusterH / 2 + 16).attr('r', 10).attr('fill', 'rgba(234,84,85,0.2)').attr('stroke', 'rgba(234,84,85,0.5)').attr('stroke-width', 1)
  g.append('text').attr('x', -clusterW / 2 + 16).attr('y', -clusterH / 2 + 20).attr('text-anchor', 'middle').attr('font-size', '12px').attr('font-weight', '700').attr('fill', 'rgba(234,84,85,0.9)').text('−')

  // Child nodes grid
  const gridStartX = -clusterW / 2 + padX + cellW / 2
  const gridStartY = -clusterH / 2 + padTop + cellH / 2

  children.forEach((child, i) => {
    const col = i % cols
    const row = Math.floor(i / cols)
    const cx = gridStartX + col * cellW
    const cy = gridStartY + row * cellH
    const catColor = categoryColor[child.category] ?? '#7367F0'

    // Child glow
    g.append('circle').attr('cx', cx).attr('cy', cy).attr('r', 22).attr('fill', catColor).attr('opacity', 0.1)
    // Child circle
    g.append('circle').attr('cx', cx).attr('cy', cy).attr('r', 18).attr('fill', 'rgba(30,32,44,0.95)').attr('stroke', catColor).attr('stroke-width', 1.5)
    // Child icon
    g.append('text').attr('x', cx).attr('y', cy + 4).attr('text-anchor', 'middle').attr('font-size', '10px').attr('font-weight', '700').attr('fill', catColor).text(child.icon)
    // Child label
    g.append('text').attr('x', cx).attr('y', cy + 32).attr('text-anchor', 'middle').attr('font-size', '8px').attr('font-weight', '500').attr('fill', 'rgba(255,255,255,0.7)').text(child.label)
  })

  // Category legend inside cluster (bottom)
  const categories = [...new Set(children.map(c => c.category))]
  const legendY = clusterH / 2 - 14
  const legendStartX = -(categories.length * 70) / 2

  categories.forEach((cat, i) => {
    const lx = legendStartX + i * 70
    const color = categoryColor[cat] ?? '#7367F0'
    g.append('circle').attr('cx', lx).attr('cy', legendY).attr('r', 4).attr('fill', color)
    g.append('text').attr('x', lx + 8).attr('y', legendY + 3).attr('font-size', '8px').attr('fill', 'rgba(255,255,255,0.5)').text(cat)
  })
}

let pollInterval: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  checkServices()
  // arch-test-allow: setInterval-fast  (RDY-060 Phase 5b pending — architecture.vue migration to cena.system.* stream)
  pollInterval = setInterval(checkServices, 5000)
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
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
          style="min-height: 750px; background: rgb(var(--v-theme-surface));"
        >
          <svg
            ref="svgRef"
            width="100%"
            height="750"
            style="display: block;"
          />
        </div>
      </VCardText>
    </VCard>

    <!-- Legend -->
    <VCard class="mt-4">
      <VCardText class="d-flex flex-wrap gap-6 align-center">
        <span class="text-caption font-weight-bold text-medium-emphasis">STATUS</span>
        <div class="d-flex align-center gap-2">
          <div style="width: 12px; height: 12px; border-radius: 50%; background: #28C76F;" />
          <span class="text-body-2">Healthy</span>
        </div>
        <div class="d-flex align-center gap-2">
          <div style="width: 12px; height: 12px; border-radius: 50%; background: #FF9F43;" />
          <span class="text-body-2">Degraded</span>
        </div>
        <div class="d-flex align-center gap-2">
          <div style="width: 12px; height: 12px; border-radius: 50%; background: #EA5455;" />
          <span class="text-body-2">Offline</span>
        </div>
        <VDivider vertical />
        <span class="text-caption font-weight-bold text-medium-emphasis">PROTOCOLS</span>
        <div class="d-flex align-center gap-2">
          <div style="width: 20px; height: 3px; background: #7367F0; border-radius: 2px;" />
          <span class="text-body-2">HTTP</span>
        </div>
        <div class="d-flex align-center gap-2">
          <div style="width: 20px; height: 3px; background: #FF9F43; border-radius: 2px;" />
          <span class="text-body-2">HTTPS</span>
        </div>
        <div class="d-flex align-center gap-2">
          <div style="width: 20px; height: 3px; background: #00BCD4; border-radius: 2px;" />
          <span class="text-body-2">NATS</span>
        </div>
        <div class="d-flex align-center gap-2">
          <div style="width: 20px; height: 3px; background: #28C76F; border-radius: 2px;" />
          <span class="text-body-2">TCP</span>
        </div>
        <VDivider vertical />
        <span class="text-body-2 text-medium-emphasis">
          Auto-refreshes every 5s. Click cluster nodes (+) to expand.
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
