import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import type { HubConnection, IRetryPolicy } from '@microsoft/signalr'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useSignalRConnection } from '@/composables/useSignalRConnection'

// ---- Mock @microsoft/signalr ------------------------------------------------

type EventHandler = (...args: any[]) => void

const mockHandlers: Record<string, EventHandler> = {}
let startMock = vi.fn<() => Promise<void>>().mockResolvedValue(undefined)
let stopMock = vi.fn<() => Promise<void>>().mockResolvedValue(undefined)

const mockConnection = {
  start: () => startMock(),
  stop: () => stopMock(),
  onreconnecting(cb: EventHandler) { mockHandlers.reconnecting = cb },
  onreconnected(cb: EventHandler) { mockHandlers.reconnected = cb },
  onclose(cb: EventHandler) { mockHandlers.close = cb },
  state: HubConnectionState.Disconnected,
} as unknown as HubConnection

let capturedRetryPolicy: IRetryPolicy | undefined

const mockBuilder = {
  withUrl: vi.fn().mockReturnThis(),
  withAutomaticReconnect: vi.fn((policy: IRetryPolicy) => {
    capturedRetryPolicy = policy

    return mockBuilder
  }),
  configureLogging: vi.fn().mockReturnThis(),
  build: vi.fn(() => mockConnection),
}

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(() => mockBuilder),
  HubConnectionState: { Disconnected: 0, Connecting: 1, Connected: 2, Reconnecting: 3 },
  LogLevel: { Warning: 3 },
}))

// ---- Helpers ----------------------------------------------------------------

function makeHost() {
  return defineComponent({
    setup() {
      return useSignalRConnection()
    },
    template: '<div />',
  })
}

describe('useSignalRConnection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.keys(mockHandlers).forEach(k => delete mockHandlers[k])
    startMock = vi.fn<() => Promise<void>>().mockResolvedValue(undefined)
    stopMock = vi.fn<() => Promise<void>>().mockResolvedValue(undefined)
    capturedRetryPolicy = undefined
  })

  it('starts with disconnected status', () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    expect(vm.status).toBe('disconnected')
    expect(vm.connection).toBeNull()
    expect(vm.reconnectAttempts).toBe(0)
  })

  it('transitions to connected on successful connect', async () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    await vm.connect('https://hub.example.com/session')
    await nextTick()

    expect(vm.status).toBe('connected')
    expect(vm.connection).not.toBeNull()
    expect(mockBuilder.withUrl).toHaveBeenCalledWith('https://hub.example.com/session')
    expect(mockBuilder.configureLogging).toHaveBeenCalledWith(LogLevel.Warning)
  })

  it('transitions to disconnected on connection failure', async () => {
    startMock.mockRejectedValueOnce(new Error('network error'))

    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    await vm.connect('https://hub.example.com/session')
    await nextTick()

    expect(vm.status).toBe('disconnected')
    expect(consoleSpy).toHaveBeenCalledWith(
      '[SignalR] Connection failed:',
      expect.any(Error),
    )
    consoleSpy.mockRestore()
  })

  it('reports reconnecting when the connection is reconnecting', async () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    await vm.connect('https://hub.example.com/session')
    expect(vm.status).toBe('connected')

    mockHandlers.reconnecting?.()
    await nextTick()

    expect(vm.status).toBe('reconnecting')
  })

  it('reports connected after successful reconnect', async () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    await vm.connect('https://hub.example.com/session')
    mockHandlers.reconnecting?.()
    await nextTick()

    expect(vm.status).toBe('reconnecting')

    mockHandlers.reconnected?.('new-connection-id')
    await nextTick()

    expect(vm.status).toBe('connected')
    expect(vm.reconnectAttempts).toBe(0)
  })

  it('reports disconnected when the connection is closed', async () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    await vm.connect('https://hub.example.com/session')
    mockHandlers.close?.()
    await nextTick()

    expect(vm.status).toBe('disconnected')
  })

  it('disconnect stops the connection gracefully', async () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    await vm.connect('https://hub.example.com/session')
    await vm.disconnect()

    expect(stopMock).toHaveBeenCalled()
    expect(vm.status).toBe('disconnected')
    expect(vm.connection).toBeNull()
  })

  it('tracks online/offline via window events', async () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    expect(vm.isOnline).toBe(navigator.onLine)

    window.dispatchEvent(new Event('offline'))
    await nextTick()

    expect(vm.isOnline).toBe(false)

    window.dispatchEvent(new Event('online'))
    await nextTick()

    expect(vm.isOnline).toBe(true)
  })

  it('configures retry policy with correct delays', async () => {
    const wrapper = mount(makeHost())
    const vm = wrapper.vm as any

    await vm.connect('https://hub.example.com/session')

    expect(capturedRetryPolicy).toBeDefined()

    const policy = capturedRetryPolicy!

    // Attempt 0 → 0ms
    expect(policy.nextRetryDelayInMilliseconds({
      previousRetryCount: 0,
      elapsedMilliseconds: 0,
      retryReason: new Error(),
    })).toBe(0)

    // Attempt 1 → 2000ms
    expect(policy.nextRetryDelayInMilliseconds({
      previousRetryCount: 1,
      elapsedMilliseconds: 0,
      retryReason: new Error(),
    })).toBe(2000)

    // Attempt 4 → 30000ms
    expect(policy.nextRetryDelayInMilliseconds({
      previousRetryCount: 4,
      elapsedMilliseconds: 0,
      retryReason: new Error(),
    })).toBe(30000)

    // Attempt 7 → 30000ms (capped)
    expect(policy.nextRetryDelayInMilliseconds({
      previousRetryCount: 7,
      elapsedMilliseconds: 0,
      retryReason: new Error(),
    })).toBe(30000)

    // Attempt 10 → null (give up)
    expect(policy.nextRetryDelayInMilliseconds({
      previousRetryCount: 10,
      elapsedMilliseconds: 0,
      retryReason: new Error(),
    })).toBeNull()
  })

  it('cleans up event listeners on unmount', async () => {
    const removeSpy = vi.spyOn(window, 'removeEventListener')
    const wrapper = mount(makeHost())

    await (wrapper.vm as any).connect('https://hub.example.com/session')
    wrapper.unmount()

    const removedEvents = removeSpy.mock.calls.map(c => c[0])

    expect(removedEvents).toContain('online')
    expect(removedEvents).toContain('offline')
    removeSpy.mockRestore()
  })
})
