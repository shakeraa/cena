// =============================================================================
// EPIC-E2E-L — Raw i18n key-leak detector across en/ar/he
//
// vue-i18n falls back to rendering the raw key (e.g. `pricing.tier.plus.title`)
// when a translation lookup fails — missing key, wrong namespace, plural-form
// mismatch, or a runtime-derived key that wasn't extracted to the locale
// JSON. The user sees the literal key on screen, which is a visible bug
// that escapes static i18n-extractor checks.
//
// This spec walks the canonical public pages on the student SPA in all three
// supported locales and asserts no DOM text matches the raw-key pattern.
//
// Pattern: a raw vue-i18n key looks like `something.something[.more]` —
//   - lowercase letter or digit start
//   - one or more dot-separated identifier segments
//   - identifier characters are [A-Za-z0-9_]
//
// Caveats handled inline:
//   - Filenames / URLs / version strings can match the pattern (e.g.
//     `app.js`, `v2.4.1`, `cena.test`). The detector skips elements whose
//     text contains `/`, common file extensions, semver-like patterns, or
//     URL-like substrings.
//   - Navigation testIds with dotted names. Skipped: only check user-visible
//     text content of leaf-text nodes, never attributes.
//   - Real product copy that legitimately contains a dotted phrase
//     (e.g. "version 2.0.1"). Heuristic: a raw key has NO whitespace
//     between segments and starts at the leaf-text boundary.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'

// vue-i18n raw-key shape. Dotted lowercase identifiers, optionally with
// camelCase / underscore segments. Anchored to the start of the matched
// text and consumes the whole token (no trailing whitespace allowed
// inside a single key).
const RAW_KEY_RE = /(?:^|\s)([a-z][a-zA-Z0-9_]*(?:\.[a-zA-Z][a-zA-Z0-9_]*){1,5})(?:\s|$|[.,!?:;])/

// Non-key dotted strings that legitimately appear in UI copy.
const ALLOWLIST_RE = [
  /\.(?:js|ts|vue|json|html|css|svg|png|jpg|jpeg|gif|webp|ico|woff2?)\b/i, // filenames
  /\b(?:v|version)?\s*\d+\.\d+(?:\.\d+)?\b/i,                              // version numbers
  /https?:\/\//,                                                          // URLs
  /\bwww\./,                                                              // bare www
  /@[\w-]+\./,                                                            // emails
  /\b\d+\.\d+/,                                                           // any decimal number
]

interface LocaleSpec { code: 'en' | 'ar' | 'he'; expectedDir: 'ltr' | 'rtl' }

const LOCALES: LocaleSpec[] = [
  { code: 'en', expectedDir: 'ltr' },
  { code: 'ar', expectedDir: 'rtl' },
  { code: 'he', expectedDir: 'rtl' },
]

// Pages that don't require sign-in. Exhaustively covering signed-in
// surfaces would mean a full auth bootstrap per locale per page — too
// expensive for a leak-detector. Public surfaces are where this kind
// of regression is most user-visible anyway (first impression).
const PUBLIC_PAGES = [
  '/login',
  '/register',
  '/forgot-password',
  '/pricing',
  '/privacy',
  '/terms',
  '/accessibility-statement',
] as const

function looksLikeRawKey(text: string): { match: string | null } {
  // Skip whitespace-only / very short text — vue-i18n keys have at
  // least one dot, so anything < 3 chars can't be a key.
  if (text.trim().length < 3)
    return { match: null }

  // Each allowlist hit means we should skip — that part of the text
  // is one of the known false-positive patterns.
  for (const rx of ALLOWLIST_RE) {
    if (rx.test(text))
      return { match: null }
  }

  const m = text.match(RAW_KEY_RE)
  if (!m)
    return { match: null }

  // Final guard: the matched key must look "key-shaped" — no spaces
  // and at least one dot.
  const candidate = m[1]
  if (!candidate || /\s/.test(candidate) || !candidate.includes('.'))
    return { match: null }

  // Skip purely numeric segments (version-like edge case).
  if (/^\d/.test(candidate))
    return { match: null }

  return { match: candidate }
}

async function collectVisibleText(page: Page): Promise<string[]> {
  return await page.evaluate(() => {
    const out: string[] = []
    const walker = document.createTreeWalker(
      document.body,
      NodeFilter.SHOW_TEXT,
      {
        acceptNode(node: Node): number {
          // Skip text inside <script>, <style>, hidden elements, or
          // nodes that are not user-visible.
          const parent = (node as Text).parentElement
          if (!parent)
            return NodeFilter.FILTER_REJECT
          const tag = parent.tagName
          if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT')
            return NodeFilter.FILTER_REJECT
          // Skip aria-hidden / display:none / visibility:hidden.
          const cs = window.getComputedStyle(parent)
          if (cs.display === 'none' || cs.visibility === 'hidden')
            return NodeFilter.FILTER_REJECT
          if (parent.getAttribute('aria-hidden') === 'true')
            return NodeFilter.FILTER_REJECT
          const text = (node as Text).data?.trim()
          if (!text || text.length < 3)
            return NodeFilter.FILTER_REJECT
          return NodeFilter.FILTER_ACCEPT
        },
      },
    )
    let n: Node | null
    while ((n = walker.nextNode()))
      out.push(((n as Text).data ?? '').trim())
    return out
  })
}

test.describe('EPIC_L_I18N_KEY_LEAK', () => {
  for (const loc of LOCALES) {
    test(`no raw i18n keys leak in DOM on public pages (locale=${loc.code}, dir=${loc.expectedDir}) @epic-l @i18n @key-leak`, async ({ page }, testInfo) => {
      test.setTimeout(120_000)

      // Seed locale BEFORE first navigation. The locale store handles
      // both legacy bare-string and the new versioned-object shape;
      // we use the versioned shape with locked=true so the first-run
      // chooser doesn't steal the route.
      await page.addInitScript((code: string) => {
        window.localStorage.setItem(
          'cena-student-locale',
          JSON.stringify({ code, locked: true, version: 1 }),
        )
      }, loc.code)

      const leaks: { route: string; key: string; sample: string }[] = []

      for (const route of PUBLIC_PAGES) {
        await page.goto(`${STUDENT_SPA_BASE_URL}${route}`, { waitUntil: 'domcontentloaded', timeout: 15_000 })
        // Give i18n + Vuetify a tick to render.
        await page.waitForTimeout(500)

        // Sanity: <html dir> should match the locale's expected direction.
        // If this fails, the locale didn't apply at all and the rest of
        // the test is meaningless.
        const dir = await page.evaluate(() => document.documentElement.dir)
        expect(dir, `<html dir> on ${route} must be ${loc.expectedDir} for locale=${loc.code} (else locale never hydrated)`)
          .toBe(loc.expectedDir)

        const texts = await collectVisibleText(page)
        for (const text of texts) {
          const { match } = looksLikeRawKey(text)
          if (match)
            leaks.push({ route, key: match, sample: text.slice(0, 160) })
        }
      }

      testInfo.attach(`i18n-key-leak-${loc.code}.json`, {
        body: JSON.stringify({ locale: loc.code, leaks }, null, 2),
        contentType: 'application/json',
      })

      // Group leaks by route + key so the failure message stays readable
      // even when many entries share the same key.
      const grouped = new Map<string, number>()
      for (const l of leaks) {
        const k = `${l.route} :: ${l.key}`
        grouped.set(k, (grouped.get(k) ?? 0) + 1)
      }
      const summary = [...grouped.entries()]
        .sort((a, b) => b[1] - a[1])
        .map(([k, n]) => `  ${k} (×${n})`)
        .join('\n')

      expect(leaks,
        `Found ${leaks.length} suspected raw i18n key(s) in locale=${loc.code}:\n${summary}\n\n` +
        `First 3 samples:\n${leaks.slice(0, 3).map(l => `  ${l.route}: "${l.sample}" (key=${l.key})`).join('\n')}`,
      ).toEqual([])
    })
  }
})
