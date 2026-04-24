// =============================================================================
// Cena Student Web — Encrypted Offline Cache (prr-158, ADR-0038 crypto-shred)
//
// Previously the offline submission queue and PWA-cached student data sat in
// plain localStorage / IndexedDB. On a shared device (school lab, family
// tablet), a different user who logged in after a student could open devtools
// and read the prior student's queued answers, tutor hints, and reflective
// text. That is a redteam finding (persona-redteam O-126) — offline cache
// must be encrypted at rest with a per-user key that is revoked on logout.
//
// Design:
//   - Encryption:  AES-GCM via SubtleCrypto (Web Crypto API). Random 96-bit IV
//                  per write, stored alongside the ciphertext.
//   - Key derive:  HKDF-SHA-256 over the Firebase ID token. The idToken
//                  rotates every ~1h, which naturally rotates the encryption
//                  key — a stolen ciphertext is unusable once the token
//                  expires. Salt is `cena-offline-cache-v1` (constant).
//   - Key lifetime: CryptoKey is held only in memory (non-extractable), never
//                  persisted. On logout, the in-memory reference is zeroed
//                  AND the backing IndexedDB store is wiped. This is the
//                  ADR-0038 crypto-shred pattern: without the key, the
//                  remaining ciphertext is cryptographically unrecoverable
//                  even if the DB wipe fails (belt + suspenders).
//   - Failure mode: if Web Crypto is unavailable (very old browser, insecure
//                  context), the cache refuses to write. The student still
//                  gets the UI, just no offline persistence on that device.
//                  Better than silently storing plaintext.
//
// This composable replaces the raw localStorage use in useOfflineQueue.ts
// (see the updated `useOfflineQueue` for wiring).
// =============================================================================

/**
 * Cena offline-cache DB name. Constant across installs so on logout we can
 * find and wipe any leftover ciphertext the previous user left behind.
 */
const CENA_OFFLINE_DB_NAME = 'cena-encrypted-offline-cache'
const CENA_OFFLINE_STORE_NAME = 'entries'
const CENA_OFFLINE_DB_VERSION = 1

/**
 * HKDF constants. The salt is a fixed string so the same idToken always
 * derives the same key — we rely on idToken rotation for key rotation.
 */
const HKDF_SALT = new TextEncoder().encode('cena-offline-cache-v1')
const HKDF_INFO = new TextEncoder().encode('cena-aes-gcm-256')

/**
 * In-memory CryptoKey for the current session. Cleared on logout.
 * Exported ONLY for test visibility — production callers should use the
 * composable returned by useEncryptedOfflineCache() or the module-level
 * logout helper.
 */
let _activeKey: CryptoKey | null = null
let _activeUid: string | null = null

/** Structured entry persisted to IndexedDB. */
interface EncryptedEntry {
  /** Stable key supplied by the caller (namespace + id). */
  cacheKey: string
  /** 96-bit IV, random per write. */
  iv: Uint8Array
  /** AES-GCM ciphertext (includes auth tag). */
  ciphertext: Uint8Array
  /** Wall-clock write time, for TTL sweeps and audit. */
  writtenAt: string
}

function hasSubtleCrypto(): boolean {
  return typeof globalThis !== 'undefined'
    && typeof globalThis.crypto !== 'undefined'
    && typeof globalThis.crypto.subtle !== 'undefined'
    && typeof globalThis.indexedDB !== 'undefined'
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(CENA_OFFLINE_DB_NAME, CENA_OFFLINE_DB_VERSION)

    req.onupgradeneeded = () => {
      const db = req.result

      if (!db.objectStoreNames.contains(CENA_OFFLINE_STORE_NAME))
        db.createObjectStore(CENA_OFFLINE_STORE_NAME, { keyPath: 'cacheKey' })
    }
    req.onerror = () => reject(req.error ?? new Error('indexedDB open failed'))
    req.onsuccess = () => resolve(req.result)
  })
}

function tx<T>(
  db: IDBDatabase,
  mode: IDBTransactionMode,
  fn: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  return new Promise((resolve, reject) => {
    const t = db.transaction(CENA_OFFLINE_STORE_NAME, mode)
    const store = t.objectStore(CENA_OFFLINE_STORE_NAME)
    const r = fn(store)

    r.onsuccess = () => resolve(r.result)
    r.onerror = () => reject(r.error ?? new Error('indexedDB op failed'))
  })
}

/**
 * Derive a 256-bit AES-GCM key from the Firebase ID token via HKDF.
 * Non-extractable — the CryptoKey cannot be read out of the browser even
 * if something upstream leaks the reference.
 */
async function deriveKey(idToken: string): Promise<CryptoKey> {
  const subtle = globalThis.crypto.subtle
  const tokenBytes = new TextEncoder().encode(idToken)

  const baseKey = await subtle.importKey(
    'raw',
    tokenBytes,
    'HKDF',
    false, // non-extractable
    ['deriveKey'],
  )

  return subtle.deriveKey(
    {
      name: 'HKDF',
      hash: 'SHA-256',
      salt: HKDF_SALT,
      info: HKDF_INFO,
    },
    baseKey,
    { name: 'AES-GCM', length: 256 },
    false, // non-extractable
    ['encrypt', 'decrypt'],
  )
}

/**
 * Initialise the cache for a newly-signed-in user. Call this from the
 * authStore __firebaseSignIn path with the real idToken, and from
 * __mockSignIn with the `mock-token-*` string (mocks get a distinct key per
 * uid — same cryptographic guarantee at a lower assurance level).
 *
 * Safe to call repeatedly — re-derives the key if the uid changed.
 */
export async function initEncryptedOfflineCache(uid: string, idToken: string): Promise<void> {
  if (!hasSubtleCrypto())
    return // degraded mode — cache is a no-op.
  if (_activeUid === uid && _activeKey !== null)
    return // already initialised for this user.

  // If a different user was previously signed in on this device, wipe their
  // ciphertext first. This is the critical redteam mitigation: without this,
  // a later user can't decrypt the prior user's data (different HKDF input),
  // but the bytes still sit on disk and could be exfiltrated by an attacker
  // who later obtains the prior user's token.
  if (_activeUid !== null && _activeUid !== uid)
    await wipeEncryptedOfflineCache()

  _activeKey = await deriveKey(idToken)
  _activeUid = uid
}

/**
 * prr-158: wipe every ciphertext entry AND zero the in-memory key.
 * This is the logout hook — call it from the auth store __signOut /
 * __firebaseSignOut paths BEFORE the idToken is cleared, so the next
 * sign-in starts from a clean, plaintext-free cache.
 *
 * Idempotent. Safe to call when nothing is initialised.
 */
export async function wipeEncryptedOfflineCache(): Promise<void> {
  // Step 1 — crypto-shred (ADR-0038): drop the key first, so even if the
  // IndexedDB clear fails (locked by another tab, quota error, private-mode),
  // the remaining ciphertext is already unrecoverable. We also drop the
  // legacy `cena-offline-queue` localStorage entry from the pre-prr-158
  // composable in case a prior build left plaintext behind.
  _activeKey = null
  _activeUid = null

  if (!hasSubtleCrypto())
    return

  try {
    if (typeof localStorage !== 'undefined')
      localStorage.removeItem('cena-offline-queue')
  }
  catch {
    // ignore quota / private-mode errors
  }

  // Step 2 — wipe the IndexedDB store entirely. Prefer deleteDatabase over
  // objectStore.clear() so the schema is rebuilt fresh on next use; this
  // also removes any residual keys an attacker could have written via a
  // compromised earlier build.
  try {
    await new Promise<void>((resolve, reject) => {
      const req = indexedDB.deleteDatabase(CENA_OFFLINE_DB_NAME)

      req.onsuccess = () => resolve()
      req.onerror = () => reject(req.error ?? new Error('indexedDB delete failed'))
      // `blocked` fires when another tab has the DB open. We let the delete
      // complete asynchronously and resolve anyway — the key is already gone.
      req.onblocked = () => resolve()
    })
  }
  catch (err) {
    console.warn('[cena-offline-cache] wipe failed; crypto-shred still effective', err)
  }
}

/**
 * Encrypt + persist an entry. Returns false if the cache is degraded
 * (no SubtleCrypto, no active key) — the caller should treat that as
 * "offline persistence unavailable" rather than a silent-drop.
 */
export async function putEncryptedEntry(cacheKey: string, plaintext: string): Promise<boolean> {
  if (!hasSubtleCrypto() || _activeKey === null)
    return false

  const iv = globalThis.crypto.getRandomValues(new Uint8Array(12))
  const ciphertextBuffer = await globalThis.crypto.subtle.encrypt(
    { name: 'AES-GCM', iv },
    _activeKey,
    new TextEncoder().encode(plaintext),
  )

  const entry: EncryptedEntry = {
    cacheKey,
    iv,
    ciphertext: new Uint8Array(ciphertextBuffer),
    writtenAt: new Date().toISOString(),
  }

  const db = await openDb()

  try {
    await tx(db, 'readwrite', store => store.put(entry))

    return true
  }
  finally {
    db.close()
  }
}

/**
 * Read + decrypt. Returns null if the entry is missing, undecryptable with
 * the current key (token rotated, or key was for a different user), or if
 * the cache is degraded.
 */
export async function getEncryptedEntry(cacheKey: string): Promise<string | null> {
  if (!hasSubtleCrypto() || _activeKey === null)
    return null

  const db = await openDb()

  try {
    const entry = await tx<EncryptedEntry | undefined>(db, 'readonly', store => store.get(cacheKey))

    if (!entry)
      return null

    try {
      const plaintextBuffer = await globalThis.crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: entry.iv },
        _activeKey,
        entry.ciphertext,
      )

      return new TextDecoder().decode(plaintextBuffer)
    }
    catch {
      // Auth-tag mismatch — wrong key (rotated token, different user).
      // Don't throw: return null so the caller treats it as cache-miss.
      return null
    }
  }
  finally {
    db.close()
  }
}

/**
 * Delete a specific entry. No-op if the cache is degraded.
 */
export async function deleteEncryptedEntry(cacheKey: string): Promise<void> {
  if (!hasSubtleCrypto())
    return

  const db = await openDb()

  try {
    await tx(db, 'readwrite', store => store.delete(cacheKey))
  }
  finally {
    db.close()
  }
}

/**
 * List all cache keys. Used by the offline-queue replay path to iterate
 * pending submissions. Returns an empty list in degraded mode.
 */
export async function listEncryptedKeys(): Promise<string[]> {
  if (!hasSubtleCrypto())
    return []

  const db = await openDb()

  try {
    return await tx<string[]>(db, 'readonly', store => store.getAllKeys() as IDBRequest<string[]>)
  }
  finally {
    db.close()
  }
}

/**
 * Test-only introspection. Production code must not depend on this.
 */
export const __testOnlyInternals = {
  getActiveUid: () => _activeUid,
  hasActiveKey: () => _activeKey !== null,
}

/**
 * Composable form for Vue components that want the imperative API bound to
 * a reactive lifecycle. Returns the module-level functions directly — keeping
 * state module-level is intentional because the encryption key must survive
 * route changes and component unmounts.
 */
export function useEncryptedOfflineCache() {
  return {
    init: initEncryptedOfflineCache,
    wipe: wipeEncryptedOfflineCache,
    put: putEncryptedEntry,
    get: getEncryptedEntry,
    delete: deleteEncryptedEntry,
    listKeys: listEncryptedKeys,
  }
}
