import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/authStore'
import { __internal } from '@/api/$api'

// $api is an end-to-end fetch wrapper. Unit tests exercise the pure
// helpers exported via __internal (correlation ID generation, jitter,
// sleep, token resolution). Full retry/backoff behavior is covered by
// Playwright E2E against a msw-mocked backend in stuw03.spec.ts.

describe('$api internals', () => {
  beforeEach(() => {
    setActivePinia(createPinia())

    const auth = useAuthStore()

    auth.__mockSignIn({ uid: 'u-1', email: 'u1@example.com' })
  })

  it('generates a unique correlation ID per call', () => {
    const id1 = __internal.newCorrelationId()
    const id2 = __internal.newCorrelationId()

    expect(id1).not.toBe(id2)
    expect(id1.length).toBeGreaterThan(8)
  })

  it('MAX_RETRIES is capped at 3', () => {
    expect(__internal.MAX_RETRIES).toBe(3)
  })

  it('jitter adds between 0 and 500 ms', () => {
    const base = 1000
    for (let i = 0; i < 20; i++) {
      const out = __internal.jitter(base)

      expect(out).toBeGreaterThanOrEqual(base)
      expect(out).toBeLessThan(base + 500)
    }
  })

  it('sleep resolves after the specified delay', async () => {
    vi.useFakeTimers()

    const promise = __internal.sleep(100)

    vi.advanceTimersByTime(100)
    await promise
    vi.useRealTimers()

    // If we reach here the promise resolved.
    expect(true).toBe(true)
  })

  it('getCurrentToken returns auth store id token', async () => {
    const token = await __internal.getCurrentToken()

    expect(token).toBe('mock-token-u-1')
  })

  it('refreshToken returns the current auth store token', async () => {
    const token = await __internal.refreshToken()

    expect(token).toBe('mock-token-u-1')
  })
})
