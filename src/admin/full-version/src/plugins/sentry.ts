import type { App } from 'vue'
import * as SentryVue from '@sentry/vue'
import { getSentryConfig } from './sentry.config'

/**
 * Sentry plugin — privacy-safe facade for error tracking.
 *
 * FIND-privacy-016 + ADR-0058: The real `@sentry/vue` SDK is initialised
 * here, but ONLY when all three gates pass (DSN present, observability
 * consent granted, SDK loads without error). The facade API surface
 * (`captureException`, `addBreadcrumb`, `setTag`, `setUser`) is
 * deliberately narrower than the raw SDK — call sites MUST go through
 * this shim so PII leaks (raw email / username / IP / free-text)
 * cannot accidentally reach Sentry.
 *
 * Until initialisation succeeds, every method is a no-op; this keeps
 * local dev + CI (and users who declined observability consent)
 * silent and quota-free.
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

/**
 * Create a thin facade over the real @sentry/vue SDK. Mirrors the
 * shim API so call sites never depend on the raw SDK type surface.
 * `setUser` is the only method that narrows the payload — it strips
 * anything that isn't `id_hash`, defence-in-depth against a caller
 * bypassing the TypeScript type.
 */
function createRealSentry(): SentryShim {
  return {
    captureException: (err, context) => {
      if (context)
        SentryVue.captureException(err, { extra: context })
      else
        SentryVue.captureException(err)
    },
    addBreadcrumb: breadcrumb => {
      SentryVue.addBreadcrumb(breadcrumb as Parameters<typeof SentryVue.addBreadcrumb>[0])
    },
    setTag: (key, value) => {
      SentryVue.setTag(key, value)
    },
    setUser: user => {
      if (user === null) {
        SentryVue.setUser(null)

        return
      }

      // Runtime narrow: only pass id_hash even if a caller bypassed TS.
      // Sentry's `User` type expects `id`; we use the hashed value there
      // so there is no path for raw studentId/email/IP to reach the wire.
      SentryVue.setUser({ id: user.id_hash })
    },
  }
}

// Mutable implementation pointer. `installSentry` flips this to the real
// SDK facade once init succeeds. The outer `Sentry` export delegates to
// `_impl`, so call sites keep a stable reference across the flip.
let _impl: SentryShim = createNoOpSentry()

/** The singleton Sentry facade. Always safe to call; no-op until initialized. */
export const Sentry: SentryShim = {
  captureException: (err, context) => _impl.captureException(err, context),
  addBreadcrumb: breadcrumb => _impl.addBreadcrumb(breadcrumb),
  setTag: (key, value) => _impl.setTag(key, value),
  setUser: user => _impl.setUser(user),
}

/**
 * Vue plugin install function.
 *
 * Initialization is gated on THREE conditions — ALL must be true:
 *   1. VITE_SENTRY_DSN env var is non-empty
 *   2. User has granted observability consent (consent.observability = true)
 *   3. The real @sentry/vue SDK loads without error
 *
 * Until all three are met, `Sentry` remains the no-op facade.
 */
export default function installSentry(app: App) {
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

  // ADR-0058 §3: release string flows from Vite at build time via the
  // `__SENTRY_RELEASE__` global (defined in vite.config.ts from
  // process.env.VITE_CENA_RELEASE at build time). Falls back to 'unknown'
  // when the build job didn't set it (local dev / non-CI builds).
  const release = ((globalThis as any).__SENTRY_RELEASE__ as string | undefined)
    ?? (import.meta as any).env?.VITE_CENA_RELEASE
    ?? 'unknown'

  try {
    const baseConfig = getSentryConfig(dsn)

    SentryVue.init({
      app,
      ...baseConfig,
      release,
    })

    _impl = createRealSentry()

    if ((import.meta as any).env?.DEV)
      console.info('[sentry] Initialised with release', release)
  }
  catch (err) {
    // Init failure must never break the app. Stay on the no-op facade.
    console.error('[sentry] Initialisation failed; staying on no-op facade.', err)
  }
}
