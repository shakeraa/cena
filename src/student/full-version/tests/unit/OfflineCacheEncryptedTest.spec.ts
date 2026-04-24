// =============================================================================
// Cena Student Web — Offline Cache Encrypted Architecture Test (prr-158)
//
// Two invariants, enforced by text-scan over src/composables/:
//
//   1. Encrypted offline cache composable exists and exposes:
//        - initEncryptedOfflineCache(uid, idToken)
//        - wipeEncryptedOfflineCache()
//        - AES-GCM via SubtleCrypto
//        - HKDF key derivation
//
//   2. No other composable/store/page opens the `cena-encrypted-offline-cache`
//      IndexedDB database name directly — only the encrypted helper may.
//      A regression that wrote plaintext to IndexedDB would bypass the
//      crypto-shred guarantee on logout.
//
// The auth store is also verified to call both init and wipe so wipe-on-logout
// remains wired; a regression that removes the wipe invocation would leave
// prior-user ciphertext (plus the in-memory key) reachable by the next user.
// =============================================================================

import { readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// Walk from this spec file up to the `src/student/full-version/` root that
// owns this vitest project. Path is deterministic per the repo layout.
const thisFile = fileURLToPath(import.meta.url)
const projectRoot = resolve(dirname(thisFile), '..', '..')
const composablesDir = join(projectRoot, 'src', 'composables')
const storesDir = join(projectRoot, 'src', 'stores')

function walkTsFiles(root: string): string[] {
  const out: string[] = []
  const stack: string[] = [root]

  while (stack.length) {
    const cur = stack.pop()!
    const st = statSync(cur)

    if (st.isDirectory()) {
      for (const child of readdirSync(cur)) stack.push(join(cur, child))
    }
    else if (st.isFile() && (cur.endsWith('.ts') || cur.endsWith('.vue'))) {
      out.push(cur)
    }
  }

  return out
}

function rel(path: string): string {
  return relative(projectRoot, path).replaceAll('\\', '/')
}

describe('OfflineCacheEncryptedTest (prr-158)', () => {
  const encryptedHelperPath = join(composablesDir, 'useEncryptedOfflineCache.ts')

  it('useEncryptedOfflineCache helper exists with AES-GCM + HKDF + SubtleCrypto', () => {
    const src = readFileSync(encryptedHelperPath, 'utf8')

    expect(src).toMatch(/\binitEncryptedOfflineCache\b/)
    expect(src).toMatch(/\bwipeEncryptedOfflineCache\b/)
    expect(src).toMatch(/AES-GCM/)
    expect(src).toMatch(/\bHKDF\b/)
    expect(src).toMatch(/crypto\.subtle/)
    expect(src).toMatch(/getRandomValues/) // per-write random IV
  })

  it('no composable/store/page opens the cena-encrypted-offline-cache DB outside the helper', () => {
    const dbName = 'cena-encrypted-offline-cache'
    const violations: string[] = []
    const roots = [
      join(projectRoot, 'src', 'composables'),
      join(projectRoot, 'src', 'stores'),
      join(projectRoot, 'src', 'pages'),
      join(projectRoot, 'src', 'components'),
    ]

    for (const root of roots) {
      let files: string[] = []

      try {
        files = walkTsFiles(root)
      }
      catch {
        continue // directory may not exist in every build
      }
      for (const file of files) {
        // The helper itself owns the DB.
        if (file === encryptedHelperPath) continue
        // Specs under tests/ are not scanned (this spec itself mentions the string).
        if (file.includes(`${'tests'}/`)) continue

        const content = readFileSync(file, 'utf8')

        if (content.includes(dbName) || /indexedDB\.open\s*\(\s*['"]cena-/.test(content)) {
          violations.push(rel(file))
        }
      }
    }

    if (violations.length > 0) {
      throw new Error(
        `prr-158 violation: ${violations.length} file(s) touch the encrypted offline-cache DB `
        + 'outside useEncryptedOfflineCache.ts. Route all access through the helper:\n  '
        + violations.join('\n  '),
      )
    }
  })

  it('authStore wires init on sign-in and wipe on sign-out (both real + mock paths)', () => {
    const authPath = join(storesDir, 'authStore.ts')
    const src = readFileSync(authPath, 'utf8')

    // Imports the helper.
    expect(src).toMatch(/initEncryptedOfflineCache/)
    expect(src).toMatch(/wipeEncryptedOfflineCache/)

    // Real Firebase sign-in invokes init. Anchor on the `function` keyword so
    // we match the function body, not the header-comment mention.
    const firebaseSignInBlock = src.match(/function\s+__firebaseSignIn[\s\S]{0,2000}?\n\s{0,4}\}/)

    expect(firebaseSignInBlock, 'function __firebaseSignIn not found in authStore').toBeTruthy()
    expect(firebaseSignInBlock?.[0]).toMatch(/initEncryptedOfflineCache\s*\(/)

    // Real Firebase sign-out invokes wipe.
    const firebaseSignOutBlock = src.match(/function\s+__firebaseSignOut[\s\S]{0,2000}?\n\s{0,4}\}/)

    expect(firebaseSignOutBlock, 'function __firebaseSignOut not found in authStore').toBeTruthy()
    expect(firebaseSignOutBlock?.[0]).toMatch(/wipeEncryptedOfflineCache\s*\(/)

    // Mock sign-out also wipes — shared dev boxes must be safe too.
    const mockSignOutBlock = src.match(/function\s+__signOut\b[\s\S]{0,2000}?\n\s{0,4}\}/)

    expect(mockSignOutBlock, 'function __signOut not found in authStore').toBeTruthy()
    expect(mockSignOutBlock?.[0]).toMatch(/wipeEncryptedOfflineCache\s*\(/)
  })
})
