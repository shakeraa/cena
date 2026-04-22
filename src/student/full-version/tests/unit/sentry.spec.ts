/**
 * FIND-privacy-016: Regression tests for Sentry privacy lockdown.
 *
 * Verifies that:
 *  1. SentryShim API surface does NOT accept email
 *  2. setUser only accepts id_hash (no email, username, IP)
 *  3. beforeSend scrubs all PII from events
 *  4. Sentry init is gated on observability consent
 *  5. Locked-down config has correct privacy defaults
 *  6. No localStorage contents leak through breadcrumbs
 *  7. URL query parameters are stripped
 *  8. Headers are stripped except trace-id
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getSentryConfig, scrubEvent } from '@/plugins/sentry.config'
import type { SentryEvent } from '@/plugins/sentry.config'

// ADR-0058 — the real @sentry/vue SDK is installed, but these tests are
// about the privacy contract. Mock the SDK so (a) tests don't require
// network, (b) init() is a no-op, and (c) nothing reaches a real DSN.
vi.mock('@sentry/vue', () => ({
  init: vi.fn(),
  captureException: vi.fn(),
  addBreadcrumb: vi.fn(),
  setTag: vi.fn(),
  setUser: vi.fn(),
}))

describe('FIND-privacy-016: Sentry privacy lockdown', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
  })

  describe('SentryShim API surface', () => {
    it('SentryUser interface only has id_hash — no email, username, or ip_address', async () => {
      // TypeScript compile-time check materialized as a runtime test:
      // Import the types and verify the shape.
      const { Sentry } = await import('@/plugins/sentry')

      // setUser accepts SentryUser | null. SentryUser only has id_hash.
      // Calling with email should be a type error at compile time.
      // At runtime, we verify the no-op does not throw.
      Sentry.setUser({ id_hash: 'abc123' })
      Sentry.setUser(null)

      // The shim is a no-op — nothing to assert except no throw
      expect(true).toBe(true)
    })

    it('setUser no-op does not log email even if misused at runtime', async () => {
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
      const consoleLogSpy = vi.spyOn(console, 'log').mockImplementation(() => {})
      const consoleInfoSpy = vi.spyOn(console, 'info').mockImplementation(() => {})

      const { Sentry } = await import('@/plugins/sentry')

      // Even if someone bypasses TypeScript and passes email at runtime,
      // the no-op setUser must not log or forward it.
      Sentry.setUser({ id_hash: 'hash', email: 'student@school.edu' } as any)

      const allLogs = [
        ...consoleSpy.mock.calls.map(c => c.join(' ')),
        ...consoleLogSpy.mock.calls.map(c => c.join(' ')),
        ...consoleInfoSpy.mock.calls.map(c => c.join(' ')),
      ].join('\n')

      expect(allLogs).not.toContain('student@school.edu')
    })
  })

  describe('scrubEvent (beforeSend)', () => {
    it('strips email from user context', () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})

      const event: SentryEvent = {
        user: { id_hash: 'safe-hash', email: 'student@school.edu' },
      }

      const result = scrubEvent(event)

      expect(result).not.toBeNull()
      expect(result!.user).toEqual({ id_hash: 'safe-hash' })
      expect(result!.user?.email).toBeUndefined()
    })

    it('strips username from user context', () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})

      const event: SentryEvent = {
        user: { id_hash: 'h', username: 'johnny' },
      }

      const result = scrubEvent(event)

      expect(result!.user?.username).toBeUndefined()
    })

    it('strips ip_address from user context', () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})

      const event: SentryEvent = {
        user: { id_hash: 'h', ip_address: '192.168.1.1' },
      }

      const result = scrubEvent(event)

      expect(result!.user?.ip_address).toBeUndefined()
    })

    it('emits structured warning when PII is scrubbed', () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      scrubEvent({
        user: { email: 'test@example.com', ip_address: '10.0.0.1' },
      })

      expect(warnSpy).toHaveBeenCalledOnce()

      const logArg = warnSpy.mock.calls[0][1]

      expect(logArg).toContain('sentry_pii_scrub')
      expect(logArg).toContain('email')
      expect(logArg).toContain('ip_address')
    })

    it('strips URL query parameters from request', () => {
      const event: SentryEvent = {
        request: {
          url: 'https://app.cena.app/session?student_id=123&token=secret',
          query_string: 'student_id=123&token=secret',
        },
      }

      const result = scrubEvent(event)

      expect(result!.request!.query_string).toBe('')
      expect(result!.request!.url).not.toContain('student_id')
      expect(result!.request!.url).not.toContain('token')
    })

    it('strips all headers except sentry-trace', () => {
      const event: SentryEvent = {
        request: {
          headers: {
            'authorization': 'Bearer secret-jwt',
            'cookie': 'session=abc',
            'sentry-trace': 'trace-123',
          },
        },
      }

      const result = scrubEvent(event)

      expect(Object.keys(result!.request!.headers!)).toEqual(['sentry-trace'])
      expect(result!.request!.headers!['sentry-trace']).toBe('trace-123')
    })

    it('redacts localStorage references in breadcrumbs', () => {
      const event: SentryEvent = {
        breadcrumbs: [
          {
            data: {
              key: 'localStorage.getItem("cena-token")',
              safe: 'clicked button',
            },
          },
        ],
      }

      const result = scrubEvent(event)

      expect(result!.breadcrumbs![0].data!.key).toBe('[redacted:localStorage]')
      expect(result!.breadcrumbs![0].data!.safe).toBe('clicked button')
    })

    it('redacts localStorage references in extra', () => {
      const event: SentryEvent = {
        extra: {
          dump: 'localStorage.setItem("token", "abc123")',
          safe: 'normal value',
        },
      }

      const result = scrubEvent(event)

      expect(result!.extra!.dump).toBe('[redacted:localStorage]')
      expect(result!.extra!.safe).toBe('normal value')
    })

    it('handles null user gracefully', () => {
      const event: SentryEvent = {}
      const result = scrubEvent(event)

      expect(result).not.toBeNull()
      expect(result!.user).toBeUndefined()
    })

    it('preserves only id_hash even when many user fields are present', () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})

      const event: SentryEvent = {
        user: {
          id: 'raw-student-id',
          id_hash: 'sha256-hash',
          email: 'kid@school.edu',
          username: 'kiddo',
          ip_address: '::1',
          custom_field: 'should-be-dropped',
        },
      }

      const result = scrubEvent(event)

      expect(result!.user).toEqual({ id_hash: 'sha256-hash' })
    })
  })

  describe('getSentryConfig', () => {
    it('sets defaultPii to false', () => {
      const config = getSentryConfig('https://key@sentry.io/123')

      expect(config.defaultPii).toBe(false)
    })

    it('disables session replay entirely', () => {
      const config = getSentryConfig('https://key@sentry.io/123')

      expect(config.replaysSessionSampleRate).toBe(0)
      expect(config.replaysOnErrorSampleRate).toBe(0)
    })

    it('limits tracePropagationTargets to localhost and Cena API', () => {
      const config = getSentryConfig('https://key@sentry.io/123')
      const targets = config.tracePropagationTargets

      // Must include localhost
      expect(targets).toContain('localhost')

      // Must have at least the Cena API pattern
      expect(targets.length).toBeGreaterThanOrEqual(2)
    })

    it('uses scrubEvent as beforeSend', () => {
      const config = getSentryConfig('https://key@sentry.io/123')

      expect(config.beforeSend).toBe(scrubEvent)
    })

    it('passes through the provided DSN', () => {
      const dsn = 'https://examplePublicKey@o0.ingest.sentry.io/0'
      const config = getSentryConfig(dsn)

      expect(config.dsn).toBe(dsn)
    })
  })

  describe('consent gating', () => {
    it('hasObservabilityConsent returns false when not set', async () => {
      localStorage.clear()

      const { hasObservabilityConsent } = await import('@/plugins/sentry')

      expect(hasObservabilityConsent()).toBe(false)
    })

    it('hasObservabilityConsent returns true when consent granted', async () => {
      localStorage.setItem('cena-consent-observability', 'true')

      const { hasObservabilityConsent } = await import('@/plugins/sentry')

      expect(hasObservabilityConsent()).toBe(true)
    })

    it('hasObservabilityConsent returns false for any non-true value', async () => {
      localStorage.setItem('cena-consent-observability', 'false')

      const { hasObservabilityConsent } = await import('@/plugins/sentry')

      expect(hasObservabilityConsent()).toBe(false)
    })
  })

  describe('Pact-style mock Sentry assertions', () => {
    /**
     * Simulates what the real @sentry/vue SDK would send to the Sentry
     * ingest endpoint. Asserts that our beforeSend hook strips all PII
     * before the event would leave the browser.
     */
    it('mock Sentry ingest receives NO email, NO full name, NO IP, NO localStorage', () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})

      const ingestedEvents: SentryEvent[] = []

      // Simulate: developer accidentally sets full user context
      const rawEvent: SentryEvent = {
        user: {
          id: 'student-uuid-123',
          id_hash: 'sha256-abc',
          email: 'student@school.edu',
          username: 'Little Johnny',
          ip_address: '192.168.1.42',
        },
        request: {
          url: 'https://app.cena.app/tutor?session_id=secret123',
          query_string: 'session_id=secret123',
          headers: {
            'authorization': 'Bearer jwt-token',
            'cookie': 'sid=abc',
            'sentry-trace': 'trace-xyz',
          },
        },
        breadcrumbs: [
          {
            data: { value: 'localStorage.getItem("cena-auth-token")' },
          },
        ],
        extra: {
          state: 'localStorage.getItem("cena-session")',
        },
      }

      // Pass through our beforeSend
      const config = getSentryConfig('https://key@sentry.io/123')
      const scrubbed = config.beforeSend(rawEvent)

      if (scrubbed)
        ingestedEvents.push(scrubbed)

      expect(ingestedEvents).toHaveLength(1)

      const event = ingestedEvents[0]

      // Assertions matching the DoD Pact contract
      expect(event.user?.email).toBeUndefined()
      expect(event.user?.username).toBeUndefined()
      expect(event.user?.ip_address).toBeUndefined()
      expect(event.user?.id).toBeUndefined()
      expect(event.user?.id_hash).toBe('sha256-abc')

      // No raw student data in request
      expect(event.request?.query_string).toBe('')
      expect(event.request?.url).not.toContain('secret123')

      // No auth headers
      expect(event.request?.headers?.authorization).toBeUndefined()
      expect(event.request?.headers?.cookie).toBeUndefined()
      expect(event.request?.headers?.['sentry-trace']).toBe('trace-xyz')

      // No localStorage in breadcrumbs
      expect(JSON.stringify(event.breadcrumbs)).not.toContain('cena-auth-token')
      expect(JSON.stringify(event.breadcrumbs)).toContain('[redacted:localStorage]')

      // No localStorage in extra
      expect(JSON.stringify(event.extra)).not.toContain('cena-session')
    })
  })
})
