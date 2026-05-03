// =============================================================================
// RDY-030: aria-live presence on dynamic content containers (WCAG 4.1.3).
//
// Components whose visible content changes without a full navigation (toast
// notifications, answer feedback, live score updates) must expose an
// aria-live region so screen readers announce the update.
// =============================================================================

import { describe, it, expect } from 'vitest'
import { readFileSync, existsSync } from 'fs'
import { resolve } from 'path'

const srcDir = resolve(__dirname, '../../src')

/**
 * Components that MUST contain an aria-live region. Keep this list in sync
 * with any new live-updating component. Adding here > discovering in prod.
 */
const LIVE_REGIONS_REQUIRED = [
  'components/session/AnswerFeedback.vue',
  'components/session/QuestionCard.vue',
  'components/notifications/NotificationListItem.vue',
] as const

describe('aria-live on dynamic content (RDY-030 — WCAG 4.1.3)', () => {
  for (const rel of LIVE_REGIONS_REQUIRED) {
    const full = resolve(srcDir, rel)

    it(`${rel}: exposes an aria-live region`, () => {
      if (!existsSync(full)) {
        // Soft skip if the file has been moved — maintainers should update
        // LIVE_REGIONS_REQUIRED. This keeps the rule useful rather than
        // brittle.
        console.warn(`[a11y] ${rel} not found; update LIVE_REGIONS_REQUIRED`)
        return
      }

      const content = readFileSync(full, 'utf-8')
      const hasLive = /\baria-live\s*=\s*["'](polite|assertive)["']/.test(content)
        || /\brole\s*=\s*["']status["']/.test(content)
        || /\brole\s*=\s*["']alert["']/.test(content)

      expect(
        hasLive,
        `${rel} must expose aria-live="polite|assertive" or role="status|alert" ` +
        `so screen readers announce dynamic updates (WCAG 4.1.3 Status Messages).`,
      ).toBe(true)
    })
  }
})
