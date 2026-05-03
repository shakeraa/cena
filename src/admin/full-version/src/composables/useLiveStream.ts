// =============================================================================
// Cena Platform -- Live Stream SSE Composable
// ADM-026.3: SSE client with auto-reconnect and Last-Event-ID support
// =============================================================================

import { firebaseAuth } from '@/plugins/firebase'

export interface LiveEvent {
  id: string
  event: string
  studentId: string
  timestamp: string
  payload: Record<string, unknown>
}

export interface UseLiveStreamReturn {
  events: Ref<LiveEvent[]>
  connected: Ref<boolean>
  error: Ref<string | null>
  disconnect: () => void
}

const MAX_BUFFERED_EVENTS = 200
const BASE_RECONNECT_DELAY_MS = 1000
const MAX_RECONNECT_DELAY_MS = 30000

export function useLiveStream(url: string): UseLiveStreamReturn {
  const events    = ref<LiveEvent[]>([])
  const connected = ref(false)
  const error     = ref<string | null>(null)

  let es: EventSource | null = null
  let lastEventId             = ''
  let reconnectDelay          = BASE_RECONNECT_DELAY_MS
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null
  let stopped                 = false

  async function buildUrl(): Promise<string> {
    const base = import.meta.env.VITE_API_BASE_URL || '/api'
    const full = url.startsWith('http') ? url : `${base}${url}`

    const user = firebaseAuth.currentUser
    if (!user)
      return full

    const token = await user.getIdToken()
    const sep   = full.includes('?') ? '&' : '?'

    return `${full}${sep}access_token=${encodeURIComponent(token)}`
  }

  function pushEvent(ev: LiveEvent) {
    events.value = [...events.value.slice(-(MAX_BUFFERED_EVENTS - 1)), ev]
  }

  async function connect() {
    if (stopped)
      return

    try {
      const fullUrl = await buildUrl()
      const connectUrl = lastEventId
        ? fullUrl + (fullUrl.includes('?') ? '&' : '?') + `lastEventId=${encodeURIComponent(lastEventId)}`
        : fullUrl

      es = new EventSource(connectUrl)

      es.onopen = () => {
        connected.value = true
        error.value = null
        reconnectDelay = BASE_RECONNECT_DELAY_MS
      }

      // Handle named events defined in the SSE stream
      const eventTypes = [
        'session.snapshot',
        'session.started',
        'session.ended',
        'question.attempted',
        'mastery.updated',
        'stagnation.detected',
        'methodology.switched',
        'tutoring.started',
        'tutoring.message',
        'tutoring.ended',
      ]

      for (const eventType of eventTypes) {
        es.addEventListener(eventType, (msgEvent: MessageEvent) => {
          if (msgEvent.lastEventId)
            lastEventId = msgEvent.lastEventId

          try {
            const data = JSON.parse(msgEvent.data)

            pushEvent({
              id:        msgEvent.lastEventId || Date.now().toString(),
              event:     eventType,
              studentId: data.studentId ?? '',
              timestamp: data.timestamp ?? new Date().toISOString(),
              payload:   data.payload ?? data,
            })
          }
          catch {
            // Ignore malformed JSON
          }
        })
      }

      es.onerror = () => {
        connected.value = false

        if (!stopped) {
          error.value = 'Connection lost — reconnecting...'
          scheduleReconnect()
        }

        es?.close()
        es = null
      }
    }
    catch (err: unknown) {
      error.value = err instanceof Error ? err.message : 'Failed to connect'
      if (!stopped)
        scheduleReconnect()
    }
  }

  function scheduleReconnect() {
    if (reconnectTimer)
      clearTimeout(reconnectTimer)

    reconnectTimer = setTimeout(() => {
      reconnectDelay = Math.min(reconnectDelay * 2, MAX_RECONNECT_DELAY_MS)
      connect()
    }, reconnectDelay)
  }

  function disconnect() {
    stopped = true
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
    es?.close()
    es = null
    connected.value = false
  }

  onMounted(connect)
  onUnmounted(disconnect)

  return { events, connected, error, disconnect }
}
