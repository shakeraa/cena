/**
 * FIND-arch-017: Regression test — MSW must not leak into production builds.
 *
 * This test reads the fake-api/index.ts source and verifies the structural
 * guards that prevent MSW from running in production:
 *
 *   1. All MSW imports (setupWorker, handler modules) are inside a
 *      `import.meta.env.DEV` block as dynamic imports, not top-level statics.
 *   2. The exported default function is a no-op in production mode.
 *
 * A companion build-time check (scripts/check-no-msw-in-dist.mjs) verifies
 * the actual dist/ output post-build. This unit test catches regressions at
 * the source level so developers get fast feedback before running a full build.
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const STUDENT_ROOT = resolve(__dirname, '../..')
const FAKE_API_INDEX = resolve(STUDENT_ROOT, 'src/plugins/fake-api/index.ts')
const PLUGINS_UTIL = resolve(STUDENT_ROOT, 'src/@core/utils/plugins.ts')

function readSource(path: string): string {
  return readFileSync(path, 'utf-8')
}

describe('FIND-arch-017: MSW production gate', () => {
  describe('fake-api/index.ts', () => {
    const src = readSource(FAKE_API_INDEX)

    it('does not have top-level static import of msw/browser', () => {
      // Top-level imports appear at the start of lines (possibly with whitespace).
      // Dynamic imports inside if-blocks are fine (they are tree-shaken).
      const topLevelMswImport = /^import\s+.*from\s+['"]msw\/browser['"]/m
      expect(src).not.toMatch(topLevelMswImport)
    })

    it('does not have top-level static imports of @db/ handlers', () => {
      const topLevelDbImport = /^import\s+.*from\s+['"]@db\//m
      expect(src).not.toMatch(topLevelDbImport)
    })

    it('gates MSW setup behind import.meta.env.DEV', () => {
      expect(src).toContain('if (import.meta.env.DEV)')
    })

    it('has a production no-op as default export', () => {
      // The module should define startFakeApi as a no-op initially,
      // then reassign it inside the DEV block.
      expect(src).toContain('export default startFakeApi')
    })

    it('uses dynamic imports for msw/browser inside DEV block', () => {
      expect(src).toContain("await import('msw/browser')")
    })
  })

  describe('@core/utils/plugins.ts', () => {
    const src = readSource(PLUGINS_UTIL)

    it('skips fake-api plugin in production', () => {
      // The registerPlugins function should contain a guard that checks
      // for fake-api in the path and skips it when not in DEV mode.
      expect(src).toContain('fake-api')
      expect(src).toContain('import.meta.env.DEV')
    })
  })
})
