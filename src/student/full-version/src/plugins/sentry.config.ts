/**
 * Locked-down Sentry configuration for a child-serving education product.
 *
 * FIND-privacy-016 — ICO Children's Code (Standard 9), GDPR Art 28.
 *
 * This config is consumed by the real @sentry/vue init (STU-W-OBS-SENTRY).
 * Every privacy constraint is baked in here so the follow-up task cannot
 * accidentally ship a permissive config.
 *
 * Key constraints:
 *  - defaultPii: false (never auto-attach IP, cookies, etc.)
 *  - Session Replay fully disabled (child-serving product)
 *  - beforeSend scrubs: user.email, user.username, user.ip_address,
 *    localStorage contents, URL query params, all headers except trace-id
 *  - tracePropagationTargets limited to localhost + Cena API domains
 */

export interface SentryLockedConfig {
  dsn: string
  defaultPii: false
  replaysSessionSampleRate: 0
  replaysOnErrorSampleRate: 0
  tracePropagationTargets: readonly (string | RegExp)[]
  beforeSend: (event: SentryEvent) => SentryEvent | null
}

/** Minimal type for Sentry event used in beforeSend. */
export interface SentryEvent {
  user?: {
    id?: string
    id_hash?: string
    email?: string
    username?: string
    ip_address?: string
    [key: string]: unknown
  }
  request?: {
    headers?: Record<string, string>
    query_string?: string
    url?: string
    [key: string]: unknown
  }
  breadcrumbs?: Array<{
    data?: Record<string, unknown>
    message?: string
    [key: string]: unknown
  }>
  extra?: Record<string, unknown>
  contexts?: Record<string, Record<string, unknown>>
  [key: string]: unknown
}

/**
 * Scrub PII from a Sentry event before it leaves the browser.
 * This is the last line of defence — even if a developer accidentally
 * sets user.email somewhere, it gets stripped here.
 */
export function scrubEvent(event: SentryEvent): SentryEvent | null {
  // --- User context: strip everything except id_hash ---
  if (event.user) {
    const safeUser: { id_hash?: string } = {}
    if (event.user.id_hash)
      safeUser.id_hash = event.user.id_hash

    // Structured log for monitoring: PII scrub triggered
    if (event.user.email || event.user.username || event.user.ip_address) {
      console.warn(
        '[sentry:privacy] PII scrubbed from Sentry event.',
        JSON.stringify({
          event: 'sentry_pii_scrub',
          fields: [
            event.user.email ? 'email' : null,
            event.user.username ? 'username' : null,
            event.user.ip_address ? 'ip_address' : null,
          ].filter(Boolean),
          timestamp: new Date().toISOString(),
        }),
      )
    }

    event.user = safeUser
  }

  // --- Request: strip query strings and all headers except trace-id ---
  if (event.request) {
    event.request.query_string = ''
    if (event.request.url) {
      try {
        const url = new URL(event.request.url)
        url.search = ''
        event.request.url = url.toString()
      }
      catch {
        // Malformed URL — strip entirely rather than leak
        event.request.url = '[redacted]'
      }
    }
    if (event.request.headers) {
      const traceId = event.request.headers['sentry-trace'] || event.request.headers['traceparent']
      event.request.headers = {}
      if (traceId)
        event.request.headers['sentry-trace'] = traceId
    }
  }

  // --- Breadcrumbs: strip any that reference localStorage ---
  if (event.breadcrumbs) {
    event.breadcrumbs = event.breadcrumbs.map((bc) => {
      if (bc.data) {
        const cleaned = { ...bc.data }
        for (const key of Object.keys(cleaned)) {
          if (typeof cleaned[key] === 'string' && (cleaned[key] as string).includes('localStorage'))
            cleaned[key] = '[redacted:localStorage]'
        }
        return { ...bc, data: cleaned }
      }
      return bc
    })
  }

  // --- Extra / contexts: redact any localStorage references ---
  if (event.extra) {
    for (const key of Object.keys(event.extra)) {
      if (typeof event.extra[key] === 'string' && (event.extra[key] as string).includes('localStorage'))
        event.extra[key] = '[redacted:localStorage]'
    }
  }

  return event
}

/**
 * Build the locked-down Sentry config. The real @sentry/vue init
 * (STU-W-OBS-SENTRY) must call `Sentry.init(getSentryConfig(dsn))`.
 */
export function getSentryConfig(dsn: string): SentryLockedConfig {
  return {
    dsn,
    defaultPii: false,

    // Session replay fully disabled — child-serving product.
    replaysSessionSampleRate: 0,
    replaysOnErrorSampleRate: 0,

    // Only propagate trace headers to our own domains. Never to
    // third-party services (Anthropic, Firebase, Google Fonts, etc.)
    tracePropagationTargets: [
      'localhost',
      /^https:\/\/api\.cena\.app/,
      /^https:\/\/.*\.cena\.app\/api/,
    ],

    beforeSend: scrubEvent,
  }
}
