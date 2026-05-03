/**
 * Tests for the $api client (src/utils/api.ts).
 *
 * Validates:
 *  - Authorization header is attached when a Firebase user exists
 *  - No Authorization header when no user is signed in
 *  - 401 response clears auth cookies
 *  - 403 response triggers navigation to not-authorized
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'

// Track cookie values
const cookieStore: Record<string, unknown> = {}

vi.stubGlobal('useCookie', (name: string) => ({
  get value() { return cookieStore[name] ?? null },
  set value(v: unknown) { cookieStore[name] = v },
}))

// Mock Firebase module
let mockCurrentUser: { getIdToken: ReturnType<typeof vi.fn> } | null = null

vi.mock('@/plugins/firebase', () => ({
  firebaseAuth: {
    get currentUser() { return mockCurrentUser },
  },
}))

// Mock CASL ability
const mockAbilityUpdate = vi.fn()
vi.mock('@/plugins/casl/ability', () => ({
  ability: { update: mockAbilityUpdate },
}))

// Mock router for 401/403 redirects
const mockRouterPush = vi.fn()
vi.mock('@/plugins/1.router', () => ({
  router: { push: mockRouterPush },
}))

// Track ofetch.create calls to capture the hooks
let capturedOptions: any = null

vi.mock('ofetch', () => ({
  ofetch: {
    create: (opts: any) => {
      capturedOptions = opts
      return vi.fn()
    },
  },
}))

describe('$api client configuration', () => {
  beforeEach(async () => {
    mockCurrentUser = null
    Object.keys(cookieStore).forEach(k => delete cookieStore[k])
    mockAbilityUpdate.mockClear()
    mockRouterPush.mockClear()
    capturedOptions = null

    // Re-import to trigger ofetch.create and capture hooks
    vi.resetModules()
    await import('@/utils/api')
  })

  it('creates ofetch instance with /api baseURL', () => {
    expect(capturedOptions).toBeDefined()
    expect(capturedOptions.baseURL).toBe('/api')
  })

  it('attaches Authorization header when user is signed in', async () => {
    mockCurrentUser = {
      getIdToken: vi.fn().mockResolvedValue('fresh-token-xyz'),
    }

    const options: any = { headers: new Headers() }

    await capturedOptions.onRequest({ options })

    expect(options.headers.get('Authorization')).toBe('Bearer fresh-token-xyz')
  })

  it('does not attach Authorization header when no user', async () => {
    mockCurrentUser = null
    const options: any = { headers: new Headers() }

    await capturedOptions.onRequest({ options })

    expect(options.headers.has('Authorization')).toBe(false)
  })

  it('clears cookies on 401 response', async () => {
    cookieStore.userData = { uid: 'test' }
    cookieStore.accessToken = 'some-token'

    await capturedOptions.onResponseError({
      response: { status: 401 },
      request: '/admin/users',
    })

    expect(cookieStore.userData).toBeNull()
    expect(cookieStore.accessToken).toBeNull()
  })

  it('navigates to not-authorized on 403 response', async () => {
    await capturedOptions.onResponseError({
      response: { status: 403 },
      request: '/admin/system/settings',
    })

    expect(mockRouterPush).toHaveBeenCalledWith({ name: 'not-authorized' })
  })
})
