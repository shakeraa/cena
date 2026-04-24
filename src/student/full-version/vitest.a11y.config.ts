import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

/**
 * Lightweight vitest config for a11y tests.
 * Does NOT load the full Vuetify component tree (no setup.ts, no jsdom).
 * These tests check theme-level color contrast math — pure TypeScript.
 */
export default defineConfig({
  test: {
    environment: 'node',
    globals: true,
    include: ['tests/a11y/**/*.spec.ts'],
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@themeConfig': fileURLToPath(new URL('./themeConfig.ts', import.meta.url)),
      '@core': fileURLToPath(new URL('./src/@core', import.meta.url)),
      '@layouts': fileURLToPath(new URL('./src/@layouts', import.meta.url)),
    },
  },
})
