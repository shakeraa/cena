import type { App } from 'vue'

/**
 * Sentry plugin stub. STU-W-03 reserves the slot so feature tasks can
 * assume `Sentry.captureException()` is available; the real `@sentry/vue`
 * SDK initialization lands in a follow-up (STU-W-OBS-SENTRY) once the
 * DSN is provisioned.
 *
 * Until then, the export is a no-op facade with the same API surface
 * `$api.ts` and feature code can call without a runtime error.
 */

interface SentryShim {
  captureException: (err: unknown, context?: Record<string, unknown>) => void
  addBreadcrumb: (breadcrumb: Record<string, unknown>) => void
  setTag: (key: string, value: string) => void
  setUser: (user: { id: string; email?: string } | null) => void
}

export const Sentry: SentryShim = {
  captureException: (err, context) => {
    if ((import.meta as any).env?.DEV)
      console.error('[sentry stub]', err, context)
  },
  addBreadcrumb: () => {},
  setTag: () => {},
  setUser: () => {},
}

export default function (__: App) {
  const dsn = (import.meta as any).env?.VITE_SENTRY_DSN

  // Stub mode when dsn is missing — `Sentry` above is a no-op facade.
  // Real init (import @sentry/vue and initialize with app + dsn + tracing)
  // will replace this in STU-W-OBS-SENTRY once the DSN is provisioned.
  if (dsn) {
    // Placeholder — real Sentry init lives in STU-W-OBS-SENTRY follow-up.
    void dsn
  }
}
