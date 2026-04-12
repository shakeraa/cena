import type { App } from 'vue'
import { getSentryConfig } from './sentry.config'

/**
 * Sentry plugin — privacy-safe facade for error tracking.
 *
 * FIND-privacy-016: The shim API deliberately excludes email, username,
 * IP address, and all other directly-identifiable PII. When the real
 * @sentry/vue SDK lands (STU-W-OBS-SENTRY), it MUST use the locked-down
 * config from `sentry.config.ts` which enforces `defaultPii: false`,
 * disables session replay, and scrubs PII in `beforeSend`.
 *
 * The `setUser` method only accepts a SHA-256 hash of the student ID
 * combined with a per-tenant pepper. Callers MUST NOT pass raw student
 * IDs or any other PII.
 */

/** Hashed-only user identifier. No email, no username, no IP. */
export interface SentryUser {
  /** SHA-256(tenantPepper + studentId). Never a raw ID or email. */
  id_hash: string
}

export interface SentryShim {
  captureException: (err: unknown, context?: Record<string, unknown>) => void
  addBreadcrumb: (breadcrumb: Record<string, unknown>) => void
  setTag: (key: string, value: string) => void
  /**
   * Set the current user context using ONLY a hashed identifier.
   * Pass `null` to clear. Email/username/IP are NOT accepted.
   */
  setUser: (user: SentryUser | null) => void
}

/**
 * Check whether the user has granted observability consent.
 * Reads from localStorage where the consent manager persists choices.
 * Returns false if consent is missing, revoked, or unreadable.
 */
export function hasObservabilityConsent(): boolean {
  try {
    const raw = globalThis.localStorage?.getItem('cena-consent-observability')
    return raw === 'true'
  }
  catch {
    // localStorage unavailable (SSR, sandboxed iframe, etc.)
    return false
  }
}

/**
 * Create a no-op Sentry facade. Used when DSN is absent, consent is
 * not granted, or the real SDK has not been initialized.
 */
function createNoOpSentry(): SentryShim {
  return {
    captureException: (err, context) => {
      if ((import.meta as any).env?.DEV)
        console.error('[sentry:noop] captureException:', err, context)
    },
    addBreadcrumb: () => {},
    setTag: () => {},
    setUser: () => {},
  }
}

/** The singleton Sentry facade. Always safe to call; no-op until initialized. */
export let Sentry: SentryShim = createNoOpSentry()

/**
 * Vue plugin install function.
 *
 * Initialization is gated on THREE conditions — ALL must be true:
 *   1. VITE_SENTRY_DSN env var is non-empty
 *   2. User has granted observability consent (consent.observability = true)
 *   3. The real @sentry/vue SDK is available (STU-W-OBS-SENTRY follow-up)
 *
 * Until all three are met, `Sentry` remains the no-op facade.
 */
export default function installSentry(_app: App) {
  const dsn = (import.meta as any).env?.VITE_SENTRY_DSN

  if (!dsn) {
    // No DSN provisioned — stay no-op.
    if ((import.meta as any).env?.DEV)
      console.info('[sentry] No DSN configured. Sentry disabled.')
    return
  }

  if (!hasObservabilityConsent()) {
    // User has not consented to observability tracking.
    console.info('[sentry] Observability consent not granted. Sentry disabled.')
    return
  }

  // DSN present and consent granted, but real SDK init is deferred to
  // STU-W-OBS-SENTRY. When that task lands, it will:
  //   1. Import @sentry/vue
  //   2. Call Sentry.init() with getSentryConfig(dsn)
  //   3. Replace the `Sentry` export with the real SDK instance
  //
  // The config from sentry.config.ts enforces all privacy constraints.
  const _config = getSentryConfig(dsn)
  void _config // Will be consumed by the real init in STU-W-OBS-SENTRY

  if ((import.meta as any).env?.DEV)
    console.info('[sentry] DSN + consent OK. Awaiting real SDK init (STU-W-OBS-SENTRY).')
}
