// =============================================================================
// RDY-030: Math notation must render LTR inside RTL pages.
//
// The user rule "math always LTR" (feedback_math_always_ltr in memory):
// every KaTeX / math expression must be wrapped in <bdi dir="ltr"> so Arabic
// and Hebrew pages don't render equations right-to-left.
//
// This rule flags math expressions rendered without a bidi wrapper in
// components that are used on localized pages.
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

/**
 * Files that render math / KaTeX directly. Any file matching this test MUST
 * either already wrap math in <bdi dir="ltr"> or route through a helper that
 * does (e.g. QuestionFigure, RenderMath).
 */
function rendersMath(content: string): boolean {
  // Look for signals: katex import, v-katex directive, math template refs.
  return /\bkatex\b/i.test(content)
      || /v-katex/.test(content)
      || /renderKatex|renderMath/.test(content)
}

function hasBidiLtrWrapper(content: string): boolean {
  return /<bdi[^>]*\bdir\s*=\s*["']ltr["']/.test(content)
      || /dir\s*=\s*["']ltr["'][^>]*>[^<]*(?:katex|math)/i.test(content)
      // Delegation to a helper that applies the wrapper is OK too
      || /RenderMath|MathExpr|QuestionFigure/.test(content)
}

describe('Math LTR wrapper (RDY-030 — bidi)', () => {
  const mathFiles = getVueFiles(srcDir).filter(f => rendersMath(readFileSync(f, 'utf-8')))

  it('finds at least one math-rendering component', () => {
    // Math rendering is a real concern in this codebase — if no files match the
    // detector, the detector is broken (false negative).
    expect(mathFiles.length).toBeGreaterThan(0)
  })

  for (const file of mathFiles) {
    const rel = file.replace(srcDir, 'src')

    it(`${rel}: math rendering routed through bidi-safe wrapper`, () => {
      const content = readFileSync(file, 'utf-8')
      expect(
        hasBidiLtrWrapper(content),
        `${rel} renders math but does not wrap it in <bdi dir="ltr"> or a known safe helper.\n` +
        `Add <bdi dir="ltr">…</bdi> around the KaTeX / math output so RTL pages render equations correctly.`,
      ).toBe(true)
    })
  }
})
