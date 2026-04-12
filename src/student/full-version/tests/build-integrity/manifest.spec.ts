/**
 * FIND-ux-029: Regression test — PWA manifest icons must resolve to real PNG files.
 *
 * The manifest.webmanifest declares icon entries that Vite serves from public/.
 * If the referenced files are missing, browsers receive the SPA HTML fallback
 * (Content-Type: text/html) instead of image/png, causing PWA install prompts
 * to break and DevTools to show "Download error or resource isn't a valid image".
 *
 * This test reads the manifest and verifies every icon entry has a corresponding
 * file in public/ that begins with a valid PNG signature.
 */
import { readFileSync, existsSync } from 'node:fs'
import { resolve, join } from 'node:path'
import { describe, expect, it } from 'vitest'

const STUDENT_ROOT = resolve(__dirname, '../..')
const PUBLIC_DIR = resolve(STUDENT_ROOT, 'public')
const MANIFEST_PATH = resolve(PUBLIC_DIR, 'manifest.webmanifest')

// PNG magic bytes: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])

interface ManifestIcon {
  src: string
  sizes: string
  type: string
  purpose?: string
}

interface WebManifest {
  icons?: ManifestIcon[]
}

describe('FIND-ux-029: PWA manifest icon integrity', () => {
  const manifestRaw = readFileSync(MANIFEST_PATH, 'utf-8')
  const manifest: WebManifest = JSON.parse(manifestRaw)

  it('manifest declares at least one icon', () => {
    expect(manifest.icons).toBeDefined()
    expect(manifest.icons!.length).toBeGreaterThanOrEqual(1)
  })

  it('manifest declares a 192x192 icon', () => {
    const has192 = manifest.icons!.some(i => i.sizes === '192x192')
    expect(has192).toBe(true)
  })

  it('manifest declares a 512x512 icon', () => {
    const has512 = manifest.icons!.some(i => i.sizes === '512x512')
    expect(has512).toBe(true)
  })

  // Deduplicate icon srcs (same file may appear with different purpose values)
  const uniqueSrcs = [...new Set(manifest.icons!.map(i => i.src))]

  for (const src of uniqueSrcs) {
    describe(`icon ${src}`, () => {
      // Strip leading / to resolve relative to public/
      const relativePath = src.startsWith('/') ? src.slice(1) : src
      const filePath = join(PUBLIC_DIR, relativePath)

      it('file exists in public/', () => {
        expect(existsSync(filePath)).toBe(true)
      })

      it('file has valid PNG signature', () => {
        const buf = readFileSync(filePath)
        const header = buf.subarray(0, 8)
        expect(Buffer.compare(header, PNG_SIGNATURE)).toBe(0)
      })

      it('file is at least 100 bytes (not a stub)', () => {
        const buf = readFileSync(filePath)
        expect(buf.length).toBeGreaterThan(100)
      })
    })
  }

  it('every icon entry declares type image/png', () => {
    for (const icon of manifest.icons!) {
      expect(icon.type).toBe('image/png')
    }
  })

  it('includes at least one maskable icon for Android home screen', () => {
    const hasMaskable = manifest.icons!.some(i => i.purpose === 'maskable')
    expect(hasMaskable).toBe(true)
  })
})
