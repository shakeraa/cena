// =============================================================================
// Shared test utilities for Vue composable unit tests.
// =============================================================================

import { type App, createApp } from 'vue'

/**
 * Wraps a composable call inside a mini Vue app so lifecycle hooks fire.
 * Returns [result, app] — call app.unmount() to trigger onUnmounted hooks.
 */
export function withSetup<T>(composable: () => T): [T, App] {
  let result!: T

  const app = createApp({
    setup() {
      result = composable()

      return () => null
    },
  })

  app.mount(document.createElement('div'))

  return [result, app]
}
