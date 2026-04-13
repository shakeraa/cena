// =============================================================================
// PWA-005: Unit tests for useNetworkStatus composable
// =============================================================================

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { withSetup } from './test-utils'

// Mock indexedDB
const mockStore = new Map<string, unknown>()
const mockObjectStore = {
  put: vi.fn((entry: { id: string }) => {
    mockStore.set(entry.id, entry)
    return { onsuccess: null, onerror: null }
  }),
  count: vi.fn(() => {
    const req = { result: mockStore.size, onsuccess: null as any, onerror: null as any }
    setTimeout(() => req.onsuccess?.())
    return req
  }),
  getAll: vi.fn(() => {
    const req = { result: [...mockStore.values()], onsuccess: null as any, onerror: null as any }
    setTimeout(() => req.onsuccess?.())
    return req
  }),
  delete: vi.fn((id: string) => {
    mockStore.delete(id)
    return { onsuccess: null, onerror: null }
  }),
}

const mockTransaction = {
  objectStore: vi.fn(() => mockObjectStore),
  oncomplete: null as any,
  onerror: null as any,
}

// Auto-complete transactions
const origObjectStore = mockTransaction.objectStore
mockTransaction.objectStore = vi.fn((...args) => {
  const result = origObjectStore(...args)
  setTimeout(() => mockTransaction.oncomplete?.())
  return result
})

const mockDb = {
  transaction: vi.fn(() => mockTransaction),
  objectStoreNames: { contains: vi.fn(() => true) },
  createObjectStore: vi.fn(),
  close: vi.fn(),
}

vi.stubGlobal('indexedDB', {
  open: vi.fn(() => {
    const req = { result: mockDb, onsuccess: null as any, onerror: null as any, onupgradeneeded: null as any }
    setTimeout(() => req.onsuccess?.({}))
    return req
  }),
})

describe('useNetworkStatus', () => {
  let addSpy: ReturnType<typeof vi.spyOn>
  let removeSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    vi.useFakeTimers()
    mockStore.clear()

    // Default to online
    Object.defineProperty(navigator, 'onLine', {
      value: true,
      writable: true,
      configurable: true,
    })

    addSpy = vi.spyOn(window, 'addEventListener')
    removeSpy = vi.spyOn(window, 'removeEventListener')
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  async function setup() {
    // Dynamic import to get fresh module state after mocks
    const mod = await import('../../src/composables/useNetworkStatus')
    return withSetup(() => mod.useNetworkStatus())
  }

  it('returns isOnline=true when navigator.onLine is true', async () => {
    const [result] = await setup()
    expect(result.isOnline.value).toBe(true)
  })

  it('registers online/offline event listeners on mount', async () => {
    await setup()
    const registeredEvents = addSpy.mock.calls.map(c => c[0])
    expect(registeredEvents).toContain('online')
    expect(registeredEvents).toContain('offline')
  })

  it('updates isOnline when offline event fires', async () => {
    const [result] = await setup()
    expect(result.isOnline.value).toBe(true)

    window.dispatchEvent(new Event('offline'))
    await nextTick()
    expect(result.isOnline.value).toBe(false)
  })

  it('fires onReconnect callback after offline→online transition', async () => {
    const [result] = await setup()
    const cb = vi.fn()
    result.onReconnect(cb)

    // Go offline then back online
    window.dispatchEvent(new Event('offline'))
    window.dispatchEvent(new Event('online'))

    // Debounce timer
    vi.advanceTimersByTime(1100)
    expect(cb).toHaveBeenCalledOnce()
  })

  it('does not fire onReconnect without prior offline', async () => {
    const [result] = await setup()
    const cb = vi.fn()
    result.onReconnect(cb)

    // Online event without going offline first
    window.dispatchEvent(new Event('online'))
    vi.advanceTimersByTime(1100)

    // Should not fire because wasOffline was false
    expect(cb).not.toHaveBeenCalled()
  })

  it('unsubscribes onReconnect callback', async () => {
    const [result] = await setup()
    const cb = vi.fn()
    const unsub = result.onReconnect(cb)
    unsub()

    window.dispatchEvent(new Event('offline'))
    window.dispatchEvent(new Event('online'))
    vi.advanceTimersByTime(1100)

    expect(cb).not.toHaveBeenCalled()
  })
})
