// =============================================================================
// Cena — useAdminLiveStream (RDY-060 Phase 2)
//
// Singleton wrapper around @microsoft/signalr's HubConnection, scoped to
// the admin-hub at `/admin-hub/cena`. Every component that needs live
// events shares the SAME underlying WebSocket — so five dashboards open
// in five tabs open five sockets, but five panels on one page share one.
//
// Client contract matches CenaAdminHub: the server always pushes an
// `AdminHubEnvelope { subject, group, payloadJson, serverTimestamp }`
// on the `ReceiveEvent` method. Consumers filter by subject prefix.
//
// Design notes:
//   - HubConnectionBuilder().withAutomaticReconnect() handles transient
//     network drops. On permanent failure (ticket expired, server gone),
//     callers should fall back to their existing 30s safety poll.
//   - Firebase token is read via firebaseAuth.currentUser.getIdToken() at
//     connect-time AND on every reconnect (accessTokenFactory callback),
//     so a token that refreshes mid-session keeps the socket alive.
//   - Composable is Vue-agnostic on the inside (returns refs), so it can
//     be imported into any <script setup>.
// =============================================================================

import { onBeforeUnmount, ref, shallowRef } from 'vue'
import type { Ref, ShallowRef } from 'vue'
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { firebaseAuth } from '@/plugins/firebase'

export type AdminHubStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'failed'

export interface AdminHubEnvelope {
  subject: string
  group: string
  payloadJson: string
  serverTimestamp: string
}

export type EnvelopeHandler = (e: AdminHubEnvelope) => void

interface SingletonState {
  connection: HubConnection | null
  status: AdminHubStatus
  handlers: Set<EnvelopeHandler>
  joinedGroups: Set<string>
  refCount: number
}

// Module-level singleton — survives component unmount cycles so multiple
// panels share one socket.
const state: SingletonState = {
  connection: null,
  status: 'idle',
  handlers: new Set(),
  joinedGroups: new Set(),
  refCount: 0,
}

function hubUrl(): string {
  // Go through the Vite proxy in dev; direct in prod.
  // `/admin-hub/cena` is mounted by MapCenaAdminHub on the admin API.
  return '/admin-hub/cena'
}

async function getToken(): Promise<string> {
  const user = firebaseAuth.currentUser
  if (!user)
    throw new Error('admin-hub: no Firebase user; cannot connect')
  return user.getIdToken()
}

async function ensureConnection(): Promise<HubConnection> {
  if (state.connection && state.connection.state === HubConnectionState.Connected)
    return state.connection

  if (state.connection && state.connection.state === HubConnectionState.Connecting)
    return state.connection

  // Build a fresh connection. accessTokenFactory is called on every
  // handshake + reconnect, so a refreshed Firebase token is picked up
  // transparently.
  const hub = new HubConnectionBuilder()
    .withUrl(hubUrl(), {
      accessTokenFactory: () => getToken(),
      // Keep WebSocket only — admin net policies sometimes block
      // long-polling, and we want a clean failure mode, not a silent
      // downgrade that masks broken transport.
    })
    .withAutomaticReconnect([0, 2000, 5000, 10_000, 30_000])
    .configureLogging(LogLevel.Warning)
    .build()

  hub.on('ReceiveEvent', (env: AdminHubEnvelope) => {
    for (const h of state.handlers) {
      try {
        h(env)
      }
      catch (err) {
        // Never let one consumer's crash take down the shared bus.
        console.warn('[admin-hub] handler threw:', err)
      }
    }
  })

  hub.onreconnecting(() => { state.status = 'reconnecting' })
  hub.onreconnected(async () => {
    state.status = 'connected'
    // Re-join groups server-side (they were lost on reconnect).
    for (const g of state.joinedGroups)
      await invokeJoin(hub, g)
  })
  hub.onclose((err) => {
    state.status = err ? 'failed' : 'disconnected'
  })

  state.connection = hub
  state.status = 'connecting'

  await hub.start()
  state.status = 'connected'
  return hub
}

async function invokeJoin(hub: HubConnection, group: string): Promise<boolean> {
  // Group keys follow AdminGroupNames on the server — we pass the
  // server-facing name, not the raw input:
  //   'system'          → JoinSystemMonitor()
  //   'school:<id>'     → JoinSchool(id)
  //   'student:<id>'    → JoinStudentInsights(id)
  //   'ingestion'       → JoinIngestionPipeline()
  if (group === 'system') return await hub.invoke<boolean>('JoinSystemMonitor')
  if (group === 'ingestion') return await hub.invoke<boolean>('JoinIngestionPipeline')
  if (group.startsWith('school:')) return await hub.invoke<boolean>('JoinSchool', group.slice('school:'.length))
  if (group.startsWith('student:')) return await hub.invoke<boolean>('JoinStudentInsights', group.slice('student:'.length))
  console.warn('[admin-hub] unknown group key:', group)
  return false
}

async function invokeLeave(hub: HubConnection, group: string): Promise<boolean> {
  if (group === 'system') return await hub.invoke<boolean>('LeaveSystemMonitor')
  if (group === 'ingestion') return await hub.invoke<boolean>('LeaveIngestionPipeline')
  if (group.startsWith('school:')) return await hub.invoke<boolean>('LeaveSchool', group.slice('school:'.length))
  if (group.startsWith('student:')) return await hub.invoke<boolean>('LeaveStudentInsights', group.slice('student:'.length))
  return false
}

export interface UseAdminLiveStream {
  status: Ref<AdminHubStatus>
  connection: ShallowRef<HubConnection | null>
  /** Register an envelope handler. Returns an unsubscribe fn. */
  on: (handler: EnvelopeHandler) => () => void
  /** Join a server-side group. Safe to call before connect; deferred. */
  join: (group: string) => Promise<boolean>
  /** Leave a server-side group. */
  leave: (group: string) => Promise<boolean>
  /** Start connecting (idempotent). */
  connect: () => Promise<void>
  /** Disconnect (called automatically on unmount when refCount hits 0). */
  disconnect: () => Promise<void>
}

/**
 * Shared-singleton composable for the admin SignalR hub.
 *
 * ```ts
 * const stream = useAdminLiveStream()
 * const unsub = stream.on((e) => {
 *   if (e.subject.startsWith('cena.events.session.')) { ... }
 * })
 * await stream.connect()
 * await stream.join('system')
 * onBeforeUnmount(() => unsub())
 * ```
 */
export function useAdminLiveStream(): UseAdminLiveStream {
  const status = ref<AdminHubStatus>(state.status)
  const connection = shallowRef<HubConnection | null>(state.connection)

  // Track this caller for refCount-based shutdown. We don't actually
  // close the socket on first unmount — only when refCount hits 0 AND
  // no groups remain — so multi-page navigation inside one session
  // doesn't churn the WebSocket.
  state.refCount++

  function syncStatus() {
    status.value = state.status
    connection.value = state.connection
  }

  function on(handler: EnvelopeHandler): () => void {
    state.handlers.add(handler)
    return () => { state.handlers.delete(handler) }
  }

  async function connect(): Promise<void> {
    await ensureConnection()
    syncStatus()
  }

  async function join(group: string): Promise<boolean> {
    const hub = await ensureConnection()
    syncStatus()
    const ok = await invokeJoin(hub, group)
    if (ok) state.joinedGroups.add(group)
    return ok
  }

  async function leave(group: string): Promise<boolean> {
    if (!state.connection || state.connection.state !== HubConnectionState.Connected)
      return false
    const ok = await invokeLeave(state.connection, group)
    if (ok) state.joinedGroups.delete(group)
    return ok
  }

  async function disconnect(): Promise<void> {
    if (!state.connection) return
    try {
      await state.connection.stop()
    }
    catch (err) {
      console.warn('[admin-hub] stop threw:', err)
    }
    state.connection = null
    state.status = 'disconnected'
    state.joinedGroups.clear()
    syncStatus()
  }

  // Auto-release one reference on unmount. Only tear down the shared
  // socket when every consumer has released AND no group memberships
  // remain (consumers should call leave() explicitly before unmount).
  onBeforeUnmount(() => {
    state.refCount = Math.max(0, state.refCount - 1)
    if (state.refCount === 0 && state.joinedGroups.size === 0) {
      // Fire-and-forget; disconnect is async.
      void disconnect()
    }
  })

  return { status, connection, on, join, leave, connect, disconnect }
}
