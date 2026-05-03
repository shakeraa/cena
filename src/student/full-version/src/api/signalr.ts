import type { HubConnection } from '@microsoft/signalr'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { Ref } from 'vue'
import { readonly, ref } from 'vue'
import type { BusEnvelope, HubEventMap, HubEventName } from './types/hub'
import { useAuthStore } from '@/stores/authStore'

/**
 * SignalR client for the Cena student web. Wraps `@microsoft/signalr`
 * with:
 *   - Firebase ID token attached via `accessTokenFactory`
 *   - Auto-reconnect with staggered delays
 *   - Pending-send replay after reconnect
 *   - Typed `subscribe<K>(eventName)` returning a reactive Ref<payload | null>
 *   - Testable seam: inject a `connectionFactory` for unit tests
 */

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

export interface SignalrClient {
  state: Readonly<Ref<ConnectionState>>
  connect: () => Promise<void>
  disconnect: () => Promise<void>
  subscribe: <K extends HubEventName>(event: K) => Ref<HubEventMap[K] | null>
  send: <T>(methodName: string, payload: T) => Promise<void>
  __pendingSendCount: () => number
  __injectEvent: <K extends HubEventName>(event: K, payload: HubEventMap[K]) => void
}

export interface CreateSignalrClientOptions {
  hubUrl: string

  /**
   * Allows tests to inject a stub HubConnection. The default builds a
   * real `@microsoft/signalr` connection.
   */
  connectionFactory?: (hubUrl: string, tokenFactory: () => Promise<string>) => HubConnection
}

function defaultConnectionFactory(hubUrl: string, tokenFactory: () => Promise<string>): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: tokenFactory,
    })
    .withAutomaticReconnect([0, 2000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()
}

export function createSignalrClient(options: CreateSignalrClientOptions): SignalrClient {
  const state = ref<ConnectionState>('disconnected')
  const pending: Array<{ method: string; payload: unknown }> = []
  const listeners = new Map<string, Set<(payload: unknown) => void>>()

  let connection: HubConnection | null = null

  const tokenFactory = async (): Promise<string> => {
    const auth = useAuthStore()

    return auth.idToken ?? ''
  }

  function registerHubHandler(conn: HubConnection) {
    // Single generic handler per known event name — SignalR client binds
    // listeners on a per-method basis, so we register one `on()` per
    // HubEventName whose handler dispatches to the Set<listener>.
    const dispatch = (eventName: string) => (envelope: BusEnvelope<unknown> | unknown) => {
      // Accept either a wrapped `BusEnvelope` or a raw payload — the hub
      // publisher wraps by default, but tests may inject raw payloads.
      const payload = (envelope as BusEnvelope<unknown>)?.payload ?? envelope
      const set = listeners.get(eventName)
      if (set) {
        for (const fn of set)
          fn(payload)
      }
    }

    const events: HubEventName[] = [
      'SessionStarted',
      'SessionEnded',
      'AnswerEvaluated',
      'MasteryUpdated',
      'HintDelivered',
      'XpAwarded',
      'StreakUpdated',
      'BadgeEarned',
      'TutorMessage',
      'Error',
      'CommandAck',
    ]

    for (const evt of events)
      conn.on(evt, dispatch(evt))

    conn.onreconnecting(() => {
      state.value = 'reconnecting'
    })
    conn.onreconnected(() => {
      state.value = 'connected'

      // Replay any sends that were queued while we were disconnected.
      const toReplay = pending.splice(0, pending.length)
      for (const { method, payload } of toReplay)
        conn.invoke(method, payload).catch(() => { /* best effort */ })
    })
    conn.onclose(() => {
      state.value = 'disconnected'
    })
  }

  async function connect() {
    if (connection && connection.state === HubConnectionState.Connected)
      return
    state.value = 'connecting'

    const factory = options.connectionFactory ?? defaultConnectionFactory

    connection = factory(options.hubUrl, tokenFactory)
    registerHubHandler(connection)
    await connection.start()
    state.value = 'connected'
  }

  async function disconnect() {
    if (connection) {
      await connection.stop()
      connection = null
    }
    state.value = 'disconnected'
  }

  function subscribe<K extends HubEventName>(event: K): Ref<HubEventMap[K] | null> {
    const payload = ref<HubEventMap[K] | null>(null) as Ref<HubEventMap[K] | null>
    if (!listeners.has(event))
      listeners.set(event, new Set())
    listeners.get(event)!.add(p => {
      payload.value = p as HubEventMap[K]
    })

    return payload
  }

  async function send<T>(methodName: string, payload: T): Promise<void> {
    if (!connection || connection.state !== HubConnectionState.Connected) {
      pending.push({ method: methodName, payload })

      return
    }
    await connection.invoke(methodName, payload)
  }

  // Test helper — lets tests inject a synthetic hub envelope without
  // standing up a real connection. Does NOT go through `connection.on`.
  function __injectEvent<K extends HubEventName>(event: K, payload: HubEventMap[K]) {
    const set = listeners.get(event)
    if (set) {
      for (const fn of set)
        fn(payload as unknown)
    }
  }

  return {
    state: readonly(state),
    connect,
    disconnect,
    subscribe,
    send,
    __pendingSendCount: () => pending.length,
    __injectEvent,
  }
}

// Module-level singleton wired to the env var. Feature tasks import this
// directly; tests construct their own via `createSignalrClient`.
export const signalrClient: SignalrClient = createSignalrClient({
  hubUrl: (import.meta as any).env?.VITE_HUB_URL || '/hub/cena',
})
