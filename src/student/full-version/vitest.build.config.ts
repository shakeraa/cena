/**
 * FIND-ux-029: Lightweight vitest config for build-integrity tests.
 * These tests validate static assets (manifest, icons, etc.) and do
 * not need Vue, Vuetify, or JSDOM.
 */
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'node',
    globals: true,
    include: ['tests/build-integrity/**/*.spec.ts'],
  },
})
