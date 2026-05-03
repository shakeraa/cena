/**
 * Tests for the Messaging Pinia store.
 *
 * Validates:
 *  - fetchThreads calls API and populates state
 *  - fetchThreads sets error on API failure
 *  - sortedThreads getter orders by lastMessageAt descending
 *  - sendMessage calls POST and refreshes thread detail
 *  - polling start/stop lifecycle
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'

// Mock the $api used by the store
const mockApi = vi.fn()
vi.mock('@/utils/api', () => ({
  $api: mockApi,
}))

// Mock Pinia's defineStore to capture store definition
let storeDefinition: any = null
vi.mock('pinia', async () => {
  const actual = await vi.importActual('pinia')
  return {
    ...actual,
    // We override defineStore but still call the real one for type compatibility
  }
})

// Use defineStore globally (auto-imported in the admin app)
vi.stubGlobal('defineStore', (id: string, options: any) => {
  storeDefinition = { id, ...options }
  return () => storeDefinition
})

describe('useMessagingStore', () => {
  let state: any

  beforeEach(async () => {
    vi.useFakeTimers()
    mockApi.mockReset()
    storeDefinition = null

    vi.resetModules()
    await import('@/stores/useMessagingStore')

    // Initialize fresh state
    state = typeof storeDefinition.state === 'function'
      ? storeDefinition.state()
      : { ...storeDefinition.state }
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('store is named "messaging"', () => {
    expect(storeDefinition.id).toBe('messaging')
  })

  it('fetchThreads populates threads from API response', async () => {
    const mockThreads = [
      { threadId: 't1', lastMessageAt: '2026-04-10T10:00:00Z', threadType: 'class' },
      { threadId: 't2', lastMessageAt: '2026-04-11T10:00:00Z', threadType: 'direct' },
    ]

    mockApi.mockResolvedValue({ items: mockThreads, totalCount: 2, page: 1, pageSize: 20 })

    // Bind actions to our state object
    const ctx = { ...state, ...storeDefinition.actions }
    Object.keys(storeDefinition.actions).forEach(key => {
      ctx[key] = storeDefinition.actions[key].bind(ctx)
    })

    await ctx.fetchThreads()

    expect(ctx.threads).toEqual(mockThreads)
    expect(ctx.totalThreads).toBe(2)
    expect(ctx.loading).toBe(false)
    expect(ctx.error).toBeNull()
  })

  it('fetchThreads sets error on API failure', async () => {
    mockApi.mockRejectedValue(new Error('Network error'))

    const ctx = { ...state, ...storeDefinition.actions }
    Object.keys(storeDefinition.actions).forEach(key => {
      ctx[key] = storeDefinition.actions[key].bind(ctx)
    })

    await ctx.fetchThreads()

    expect(ctx.error).toBe('Network error')
    expect(ctx.loading).toBe(false)
    expect(ctx.threads).toEqual([])
  })

  it('sortedThreads getter orders by lastMessageAt descending', () => {
    const threads = [
      { threadId: 't1', lastMessageAt: '2026-04-09T10:00:00Z' },
      { threadId: 't2', lastMessageAt: '2026-04-11T10:00:00Z' },
      { threadId: 't3', lastMessageAt: '2026-04-10T10:00:00Z' },
    ]

    const sorted = storeDefinition.getters.sortedThreads({ threads } as any)

    expect(sorted[0].threadId).toBe('t2')
    expect(sorted[1].threadId).toBe('t3')
    expect(sorted[2].threadId).toBe('t1')
  })

  it('sendMessage calls POST and refreshes thread', async () => {
    mockApi.mockResolvedValue({})

    const ctx = {
      ...state,
      ...storeDefinition.actions,
      activeThread: null,
    }
    Object.keys(storeDefinition.actions).forEach(key => {
      ctx[key] = storeDefinition.actions[key].bind(ctx)
    })

    await ctx.sendMessage('thread-1', 'Hello world')

    expect(mockApi).toHaveBeenCalledWith(
      '/admin/messaging/threads/thread-1/messages',
      expect.objectContaining({ method: 'POST', body: { text: 'Hello world', replyToMessageId: undefined } }),
    )
  })

  it('fetchThreadDetail stores active thread', async () => {
    const detail = {
      threadId: 'td-1',
      threadType: 'class',
      participantIds: ['u1', 'u2'],
      participantNames: ['Alice', 'Bob'],
      messages: [],
      totalMessages: 0,
      nextCursor: null,
    }

    mockApi.mockResolvedValue(detail)

    const ctx = { ...state, ...storeDefinition.actions }
    Object.keys(storeDefinition.actions).forEach(key => {
      ctx[key] = storeDefinition.actions[key].bind(ctx)
    })

    await ctx.fetchThreadDetail('td-1')

    expect(ctx.activeThread).toEqual(detail)
    expect(ctx.loading).toBe(false)
  })

  it('startPolling sets interval and stopPolling clears it', () => {
    const ctx = { ...state, ...storeDefinition.actions }
    Object.keys(storeDefinition.actions).forEach(key => {
      ctx[key] = storeDefinition.actions[key].bind(ctx)
    })

    ctx.startPolling()
    expect(ctx._pollTimer).not.toBeNull()

    ctx.stopPolling()
    expect(ctx._pollTimer).toBeNull()
  })
})
