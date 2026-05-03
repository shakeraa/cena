import { onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import type { Ref, ShallowRef } from 'vue'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'

export type ConnectionStatus = 'connected' | 'connecting' | 'reconnecting' | 'disconnected'

export interface UseSignalRConnectionReturn {

  /** Current connection status */
  status: Ref<ConnectionStatus>

  /** Whether currently online (navigator.onLine) */
  isOnline: Ref<boolean>

  /** The raw HubConnection (for sending messages) */
  connection: ShallowRef<HubConnection | null>

  /** Start the connection */
  connect: (url: string) => Promise<void>

  /** Gracefully disconnect */
  disconnect: () => Promise<void>

  /** Number of reconnect attempts so far */
  reconnectAttempts: Ref<number>
}

export function useSignalRConnection(): UseSignalRConnectionReturn {
  const status = ref<ConnectionStatus>('disconnected')
  const isOnline = ref(typeof navigator !== 'undefined' ? navigator.onLine : true)
  const connection = shallowRef<HubConnection | null>(null)
  const reconnectAttempts = ref(0)

  function handleOnline() {
    isOnline.value = true
  }

  function handleOffline() {
    isOnline.value = false
  }

  async function connect(url: string) {
    // Tear down any existing connection first
    if (connection.value) {
      try {
        await connection.value.stop()
      }
      catch {
        // Ignore stop errors
      }
    }

    status.value = 'connecting'

    const conn = new HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect({
        // Retry: 0s, 2s, 5s, 10s, 30s, then every 30s up to 10 attempts
        nextRetryDelayInMilliseconds(retryContext) {
          reconnectAttempts.value = retryContext.previousRetryCount + 1
          if (retryContext.previousRetryCount >= 10)
            return null // give up
          const delays = [0, 2000, 5000, 10000, 30000]

          return delays[Math.min(retryContext.previousRetryCount, delays.length - 1)]
        },
      })
      .configureLogging(LogLevel.Warning)
      .build()

    conn.onreconnecting(() => {
      status.value = 'reconnecting'
    })

    conn.onreconnected(() => {
      status.value = 'connected'
      reconnectAttempts.value = 0
    })

    conn.onclose(() => {
      status.value = 'disconnected'
    })

    try {
      await conn.start()
      connection.value = conn
      status.value = 'connected'
      reconnectAttempts.value = 0
    }
    catch (err) {
      status.value = 'disconnected'
      console.error('[SignalR] Connection failed:', err)
    }
  }

  async function disconnect() {
    if (connection.value) {
      try {
        await connection.value.stop()
      }
      catch {
        // Ignore stop errors during teardown
      }
      connection.value = null
    }
    status.value = 'disconnected'
  }

  onMounted(() => {
    window.addEventListener('online', handleOnline)
    window.addEventListener('offline', handleOffline)
  })

  onBeforeUnmount(() => {
    window.removeEventListener('online', handleOnline)
    window.removeEventListener('offline', handleOffline)
    disconnect()
  })

  return { status, isOnline, connection, connect, disconnect, reconnectAttempts }
}
