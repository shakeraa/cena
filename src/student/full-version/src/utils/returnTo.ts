/**
 * Sanitize a `returnTo` query parameter against open-redirect attacks.
 *
 * Rules (both must hold to accept):
 *   1. The raw string must not start with `http:` / `https:` / `//` / `\\`.
 *   2. Once URL-parsed against the current origin, the final origin MUST
 *      equal the current origin AND the path must begin with `/`.
 *
 * Rejected inputs return the fallback (default `/home`), NEVER the raw
 * input — so an attacker cannot smuggle a cross-origin URL through
 * whitespace-padding or encoded protocol tricks.
 */
export function sanitizeReturnTo(
  raw: string | null | undefined,
  fallback = '/home',
): string {
  if (!raw)
    return fallback

  const trimmed = raw.trim()
  if (trimmed.length === 0)
    return fallback

  // Reject absolute URLs and protocol-relative URLs outright.
  const lower = trimmed.toLowerCase()
  if (
    lower.startsWith('http:')
    || lower.startsWith('https:')
    || lower.startsWith('//')
    || lower.startsWith('\\\\')
    || lower.startsWith('javascript:')
    || lower.startsWith('data:')
  )
    return fallback

  // Must start with `/`.
  if (!trimmed.startsWith('/'))
    return fallback

  // Parse defensively against window.location to catch encoded cross-origin
  // tricks like `/%2F%2Fevil.example.com`.
  if (typeof window !== 'undefined') {
    try {
      const resolved = new URL(trimmed, window.location.origin)
      if (resolved.origin !== window.location.origin)
        return fallback

      return resolved.pathname + resolved.search + resolved.hash
    }
    catch {
      return fallback
    }
  }

  return trimmed
}
