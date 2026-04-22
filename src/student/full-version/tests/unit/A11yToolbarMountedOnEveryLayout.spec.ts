// =============================================================================
// A11yToolbarMountedOnEveryLayoutTest — architecture ratchet.
//
// PRR-A11Y-PLAYWRIGHT-RATCHET §2: fails CI if any layout in src/layouts/
// renders primary content without mounting <A11yToolbar />. Authored as
// a Vitest describe.each over the layout files so the failure message
// names the offending layout.
//
// Rationale: the A11yToolbar is the only universal surface for IL
// 5758-1998 "independent control" — if a layout forgets to mount it, a
// student landing on that route has no way to change locale / contrast
// without navigating to an authenticated shell. Regression shield, not
// a functional test.
// =============================================================================
import { describe, expect, it } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'

const LAYOUTS_DIR = resolve(__dirname, '../../src/layouts')

function layoutFiles(): string[] {
  return readdirSync(LAYOUTS_DIR)
    .filter(name => name.endsWith('.vue'))
    .filter(name => {
      // Skip sub-layout components that are imported by real layouts but
      // are not themselves top-level layout targets.
      const full = join(LAYOUTS_DIR, name)

      return statSync(full).isFile()
    })
    .map(name => join(LAYOUTS_DIR, name))
}

describe('A11yToolbarMountedOnEveryLayoutTest', () => {
  const files = layoutFiles()

  it('finds at least one layout file to scan', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  it.each(files.map(f => [f]))('layout %s mounts <A11yToolbar />', (file) => {
    const source = readFileSync(file, 'utf8')

    // Accept either PascalCase or kebab-case Vue tag; require at least
    // one form somewhere in the template. The check is deliberately
    // permissive about whitespace / attributes / v-if guards so each
    // layout can wrap the toolbar with its own embed/unauth conditions.
    const hasPascal = /<A11yToolbar[\s/>]/i.test(source)
    const hasKebab = /<a11y-toolbar[\s/>]/i.test(source)

    expect(hasPascal || hasKebab, `Expected ${file} to render <A11yToolbar />`).toBe(true)
  })
})
