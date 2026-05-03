import { describe, expect, it } from 'vitest'
import { sanitizeReturnTo } from '@/utils/returnTo'

describe('sanitizeReturnTo', () => {
  it('accepts a normal same-origin path', () => {
    expect(sanitizeReturnTo('/progress/mastery')).toBe('/progress/mastery')
  })

  it('preserves query and hash', () => {
    const out = sanitizeReturnTo('/session/abc?q=5#top')

    expect(out).toBe('/session/abc?q=5#top')
  })

  it('rejects an absolute https URL', () => {
    expect(sanitizeReturnTo('https://evil.example.com/attack')).toBe('/home')
  })

  it('rejects an absolute http URL', () => {
    expect(sanitizeReturnTo('http://evil.example.com/attack')).toBe('/home')
  })

  it('rejects a protocol-relative URL', () => {
    expect(sanitizeReturnTo('//evil.example.com/attack')).toBe('/home')
  })

  it('rejects a javascript: scheme', () => {
    expect(sanitizeReturnTo('javascript:alert(1)')).toBe('/home')
  })

  it('rejects a data: scheme', () => {
    expect(sanitizeReturnTo('data:text/html,<script>alert(1)</script>')).toBe('/home')
  })

  it('rejects a backslash-prefixed path', () => {
    expect(sanitizeReturnTo('\\\\evil.example.com\\attack')).toBe('/home')
  })

  it('rejects a relative path not starting with /', () => {
    expect(sanitizeReturnTo('home/session')).toBe('/home')
  })

  it('rejects null and empty strings', () => {
    expect(sanitizeReturnTo(null)).toBe('/home')
    expect(sanitizeReturnTo('')).toBe('/home')
    expect(sanitizeReturnTo('   ')).toBe('/home')
  })

  it('keeps encoded same-origin paths as same-origin (no protocol leak)', () => {
    // `/%2F%2Fevil.example.com` looks like an attacker trick but URL-parses
    // as a same-origin path because `%2F` is a literal `/` character inside
    // the path segment. The sanitizer accepts it — the resulting navigation
    // goes to our own backend which will 404 or redirect. What matters is
    // that the URL never acquires a cross-origin scheme.
    const out = sanitizeReturnTo('/%2F%2Fevil.example.com')

    expect(out.startsWith('/')).toBe(true)
    expect(out).not.toMatch(/^https?:/)
    expect(out).not.toMatch(/^\/\//)
  })

  it('uses a custom fallback when provided', () => {
    expect(sanitizeReturnTo('https://evil.com', '/custom')).toBe('/custom')
    expect(sanitizeReturnTo(null, '/custom')).toBe('/custom')
  })
})
