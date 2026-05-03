import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { HubConnection } from '@microsoft/signalr'
import { HubConnectionState } from '@microsoft/signalr'
import { createSignalrClient } from '@/api/signalr'

type FakeConnection = HubConnection & { __triggerReconnecting: () => void; __triggerReconnected: () => void; __triggerClose: () => void }

/**
 * Fake HubConnection — just enough to exercise subscribe / send / reconnect.
 */
function makeFakeConnection(): FakeConnection {
  let reconnectingCb: (() => void) | null = null
  let reconnectedCb: (() => void) | null = null
  let closeCb: (() => void) | null = null

  const fake = {
    state: HubConnectionState.Disconnected,
    start: vi.fn(async () => {
      fake.state = HubConnectionState.Connected
    }),
    stop: vi.fn(async () => {
      fake.state = HubConnectionState.Disconnected
    }),
    on: vi.fn(),
    invoke: vi.fn(async () => undefined),
    onreconnecting: vi.fn((cb: () => void) => { reconnectingCb = cb }),
    onreconnected: vi.fn((cb: () => void) => { reconnectedCb = cb }),
    onclose: vi.fn((cb: () => void) => { closeCb = cb }),
    __triggerReconnecting: () => {
      fake.state = HubConnectionState.Reconnecting
      reconnectingCb?.()
    },
    __triggerReconnected: () => {
      fake.state = HubConnectionState.Connected
      reconnectedCb?.()
    },
    __triggerClose: () => {
      fake.state = HubConnectionState.Disconnected
      closeCb?.()
    },
  } as any

  return fake
}

describe('createSignalrClient', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts disconnected and transitions to connected on connect()', async () => {
    const fake = makeFakeConnection()

    const client = createSignalrClient({
      hubUrl: '/hub/test',
      connectionFactory: () => fake,
    })

    expect(client.state.value).toBe('disconnected')
    await client.connect()
    expect(fake.start).toHaveBeenCalledTimes(1)
    expect(client.state.value).toBe('connected')
  })

  it('subscribe returns a reactive ref that updates on injected events', async () => {
    const fake = makeFakeConnection()

    const client = createSignalrClient({
      hubUrl: '/hub/test',
      connectionFactory: () => fake,
    })

    await client.connect()

    const xpRef = client.subscribe('XpAwarded')

    expect(xpRef.value).toBeNull()

    client.__injectEvent('XpAwarded', {
      studentId: 'u-1',
      reason: 'test',
      amount: 10,
      newTotal: 150,
    })

    expect(xpRef.value).toEqual({
      studentId: 'u-1',
      reason: 'test',
      amount: 10,
      newTotal: 150,
    })
  })

  it('send queues the call when disconnected and replays on reconnect', async () => {
    const fake = makeFakeConnection()

    const client = createSignalrClient({
      hubUrl: '/hub/test',
      connectionFactory: () => fake,
    })

    await client.connect()

    // Simulate a drop.
    fake.__triggerReconnecting()
    expect(client.state.value).toBe('reconnecting')
    expect(fake.state).toBe(HubConnectionState.Reconnecting)

    // Queue two sends while disconnected.
    await client.send('Ping', { id: 1 })
    await client.send('Ping', { id: 2 })
    expect(client.__pendingSendCount()).toBe(2)

    // `invoke` is NOT called while reconnecting.
    expect(fake.invoke).not.toHaveBeenCalled()

    // Simulate successful reconnect.
    fake.__triggerReconnected()
    expect(client.state.value).toBe('connected')

    // Both queued sends were replayed.
    expect(fake.invoke).toHaveBeenCalledTimes(2)
    expect(fake.invoke).toHaveBeenNthCalledWith(1, 'Ping', { id: 1 })
    expect(fake.invoke).toHaveBeenNthCalledWith(2, 'Ping', { id: 2 })
    expect(client.__pendingSendCount()).toBe(0)
  })

  it('send invokes directly when connected', async () => {
    const fake = makeFakeConnection()

    const client = createSignalrClient({
      hubUrl: '/hub/test',
      connectionFactory: () => fake,
    })

    await client.connect()
    await client.send('Ping', { id: 99 })
    expect(fake.invoke).toHaveBeenCalledWith('Ping', { id: 99 })
    expect(client.__pendingSendCount()).toBe(0)
  })

  it('onclose transitions state to disconnected', async () => {
    const fake = makeFakeConnection()

    const client = createSignalrClient({
      hubUrl: '/hub/test',
      connectionFactory: () => fake,
    })

    await client.connect()
    fake.__triggerClose()
    expect(client.state.value).toBe('disconnected')
  })

  it('disconnect stops the connection and resets state', async () => {
    const fake = makeFakeConnection()

    const client = createSignalrClient({
      hubUrl: '/hub/test',
      connectionFactory: () => fake,
    })

    await client.connect()
    await client.disconnect()
    expect(fake.stop).toHaveBeenCalled()
    expect(client.state.value).toBe('disconnected')
  })

  it('multiple subscribers to the same event all receive injected payloads', async () => {
    const fake = makeFakeConnection()

    const client = createSignalrClient({
      hubUrl: '/hub/test',
      connectionFactory: () => fake,
    })

    await client.connect()

    const ref1 = client.subscribe('BadgeEarned')
    const ref2 = client.subscribe('BadgeEarned')

    client.__injectEvent('BadgeEarned', {
      studentId: 'u-1',
      badgeId: 'b-1',
      badgeName: 'First Steps',
      earnedAt: '2026-04-10T00:00:00Z',
    })

    expect(ref1.value?.badgeId).toBe('b-1')
    expect(ref2.value?.badgeId).toBe('b-1')
  })
})
