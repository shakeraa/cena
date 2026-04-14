// =============================================================================
// RDY-030: prefers-reduced-motion coverage (WCAG 2.3.3 Animation from
// Interactions).
//
// Any component that declares a CSS animation or transition must also declare
// a @media (prefers-reduced-motion: reduce) override that disables or reduces
// it. Vestibular-disorder users rely on this.
// =============================================================================

import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync } from 'fs'
import { resolve, join } from 'path'

const srcDir = resolve(__dirname, '../../src')

function getVueFiles(dir: string): string[] {
  const files: string[] = []
  try {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = join(dir, entry.name)
      if (entry.isDirectory()) files.push(...getVueFiles(full))
      else if (entry.name.endsWith('.vue')) files.push(full)
    }
  } catch { /* ignore */ }
  return files
}

function hasAnimation(css: string): boolean {
  // Genuine declarations, not just property names in a comment.
  return /^[^*/]*\banimation\s*:/m.test(css)
      || /@keyframes\s+[A-Za-z_-][A-Za-z0-9_-]*/.test(css)
      || /^[^*/]*\btransition\s*:\s*(?!none\b)/m.test(css)
}

function hasReducedMotionGuard(css: string): boolean {
  return /@media\s*\([^)]*prefers-reduced-motion\s*:\s*reduce\)/.test(css)
}

describe('prefers-reduced-motion coverage (RDY-030 — WCAG 2.3.3)', () => {
  const offending: string[] = []

  for (const file of getVueFiles(srcDir)) {
    const content = readFileSync(file, 'utf-8')

    // Only inspect component-local <style> blocks. Global resets already
    // handle most motion; we're guarding against new component-local animations.
    const styleBlocks = content.match(/<style[^>]*>([\s\S]*?)<\/style>/g)
    if (!styleBlocks) continue

    const css = styleBlocks.join('\n')

    if (hasAnimation(css) && !hasReducedMotionGuard(css)) {
      offending.push(file.replace(srcDir, 'src'))
    }
  }

  it('every component with animations declares prefers-reduced-motion guard', () => {
    expect(
      offending.length,
      `Components with CSS animations but no @media (prefers-reduced-motion: reduce) override:\n  ` +
      offending.join('\n  ') +
      `\n\nAdd a @media (prefers-reduced-motion: reduce) block that sets ` +
      `animation: none / transition: none for vestibular-sensitive users.`,
    ).toBe(0)
  })
})
