// =============================================================================
// Cena Platform — E2E flow-test config (multi-worker, parallel, isolated tenants)
//
// Separate from the SPA's default playwright.config.ts (which runs
// tests/e2e/* serially with workers=1). This config is for *full-stack*
// workflow tests that exercise SPA → student-api → Postgres → NATS →
// Stripe test-mode in a single spec.
//
// Isolation model (see tasks/e2e-flow/README.md):
//   * Shared docker stack (long-running, not torn down between tests)
//   * Per-worker tenant_id via fixtures/tenant.ts
//   * Per-spec fresh Firebase user (emulator)
//   * Per-spec Stripe checkout metadata tag for webhook routing
//
// Run:
//   npm run test:e2e:flow                 # 4 workers, all specs
//   npm run test:e2e:flow -- --workers=1  # serial debug
//   npm run test:e2e:flow -- --grep happy # filter by title
// =============================================================================

import { defineConfig, devices } from '@playwright/test'

// PRR-436: keep the dev probe token in sync with docker-compose.app.yml's
// student-api env (`CENA_TEST_PROBE_TOKEN`). Tests fall back to this default
// when the env isn't explicitly set so a developer can run the suite without
// extra env juggling. CI overrides via repository secret.
if (!process.env.CENA_TEST_PROBE_TOKEN) {
  process.env.CENA_TEST_PROBE_TOKEN = 'dev-only-test-probe-token-do-not-ship'
}

export default defineConfig({
  testDir: './tests/e2e-flow/workflows',
  timeout: 60_000, // full-stack flows need headroom
  expect: { timeout: 10_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 2 : 4,
  reporter: [
    ['list'],
    ['html', { outputFolder: './test-results/e2e-flow/report', open: 'never' }],
  ],
  outputDir: './test-results/e2e-flow/artifacts',
  use: {
    baseURL: process.env.E2E_FLOW_BASE_URL ?? 'http://localhost:5175',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    // Tests carry their own session cookies; don't share browser state.
    storageState: undefined,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // Unlike tests/e2e, we do NOT boot a webServer here. The full-stack suite
  // requires the dev docker stack to be up already (docker-compose up -d).
  // Booting a separate `npm run dev` would conflict with the compose SPA.
})
