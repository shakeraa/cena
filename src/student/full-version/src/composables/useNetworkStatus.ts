// =============================================================================
// PWA-005: Reactive network status composable
// Wraps navigator.onLine + online/offline events with debounced reconnect
// detection and an offline submission queue backed by IndexedDB.
// =============================================================================

import { computed, onMounted, onUnmounted, ref, shallowRef } from 'vue'

// ---------------------------------------------------------------------------
// Reactive online / offline state
// ---------------------------------------------------------------------------

const isOnline = ref(navigator.onLine)
const wasOffline = ref(false)
const lastOnlineAt = ref<Date | null>(null)
const lastOfflineAt = ref<Date | null>(null)

/** Fires once per offline→online transition (debounced 1 s). */
const reconnectCallbacks: Array<() => void> = []

let listenerCount = 0
let reconnectTimer: ReturnType<typeof setTimeout> | undefined

function onOnline() {
  isOnline.value = true
  lastOnlineAt.value = new Date()

  // Debounce: browsers sometimes fire online/offline in rapid succession
  clearTimeout(reconnectTimer)
  reconnectTimer = setTimeout(() => {
    if (wasOffline.value) {
      wasOffline.value = false
      reconnectCallbacks.forEach(cb => cb())
    }
  }, 1000)
}

function onOffline() {
  isOnline.value = false
  wasOffline.value = true
  lastOfflineAt.value = new Date()
}

function attachListeners() {
  if (listenerCount++ === 0) {
    window.addEventListener('online', onOnline)
    window.addEventListener('offline', onOffline)
  }
}

function detachListeners() {
  if (--listenerCount === 0) {
    window.removeEventListener('online', onOnline)
    window.removeEventListener('offline', onOffline)
    clearTimeout(reconnectTimer)
  }
}

// ---------------------------------------------------------------------------
// IndexedDB-backed offline submission queue
// ---------------------------------------------------------------------------

const DB_NAME = 'cena-offline'
const DB_VERSION = 1
const STORE_NAME = 'submissions'

interface QueuedSubmission {
  id: string
  url: string
  method: string
  body: string
  headers: Record<string, string>
  createdAt: number
  retries: number
}

const pendingCount = ref(0)

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION)
    req.onupgradeneeded = () => {
      const db = req.result
      if (!db.objectStoreNames.contains(STORE_NAME))
        db.createObjectStore(STORE_NAME, { keyPath: 'id' })
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

async function enqueueSubmission(
  url: string,
  method: string,
  body: unknown,
  headers: Record<string, string> = {},
): Promise<string> {
  const id = `sub_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`
  const entry: QueuedSubmission = {
    id,
    url,
    method,
    body: JSON.stringify(body),
    headers: { 'Content-Type': 'application/json', ...headers },
    createdAt: Date.now(),
    retries: 0,
  }

  const db = await openDb()
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readwrite')
    tx.objectStore(STORE_NAME).put(entry)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
  db.close()

  pendingCount.value++
  return id
}

async function countPending(): Promise<number> {
  try {
    const db = await openDb()
    const count = await new Promise<number>((resolve, reject) => {
      const tx = db.transaction(STORE_NAME, 'readonly')
      const req = tx.objectStore(STORE_NAME).count()
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    db.close()
    return count
  }
  catch {
    return 0
  }
}

async function drainQueue(): Promise<{ sent: number; failed: number }> {
  const db = await openDb()
  const entries = await new Promise<QueuedSubmission[]>((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readonly')
    const req = tx.objectStore(STORE_NAME).getAll()
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })

  let sent = 0
  let failed = 0

  for (const entry of entries) {
    try {
      const res = await fetch(entry.url, {
        method: entry.method,
        headers: entry.headers,
        body: entry.body,
      })

      if (res.ok || res.status === 409 /* idempotent duplicate */) {
        // Remove from queue
        const tx = db.transaction(STORE_NAME, 'readwrite')
        tx.objectStore(STORE_NAME).delete(entry.id)
        await new Promise<void>((resolve, reject) => {
          tx.oncomplete = () => resolve()
          tx.onerror = () => reject(tx.error)
        })
        sent++
      }
      else {
        failed++
      }
    }
    catch {
      failed++
    }
  }

  db.close()
  pendingCount.value = await countPending()
  return { sent, failed }
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useNetworkStatus() {
  onMounted(() => {
    attachListeners()
    countPending().then(c => { pendingCount.value = c })
  })

  onUnmounted(() => {
    detachListeners()
  })

  function onReconnect(cb: () => void) {
    reconnectCallbacks.push(cb)
    // Return unsubscribe
    return () => {
      const idx = reconnectCallbacks.indexOf(cb)
      if (idx >= 0)
        reconnectCallbacks.splice(idx, 1)
    }
  }

  return {
    /** True when navigator.onLine === true */
    isOnline: computed(() => isOnline.value),

    /** Number of queued offline submissions */
    pendingCount: computed(() => pendingCount.value),

    /** When the browser last detected an online transition */
    lastOnlineAt: computed(() => lastOnlineAt.value),

    /** When the browser last went offline */
    lastOfflineAt: computed(() => lastOfflineAt.value),

    /** Register a callback that fires once after each offline→online transition */
    onReconnect,

    /** Queue a fetch request for replay when back online */
    enqueueSubmission,

    /** Replay all queued submissions (call on reconnect) */
    drainQueue,

    /** Re-count pending items (e.g. after external manipulation) */
    refreshPendingCount: async () => { pendingCount.value = await countPending() },
  }
}
