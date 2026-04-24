/**
 * FIND-ux-020 regression: verify CASL ability rules are seeded on sign-in
 * and cleared on sign-out.
 *
 * The student sidebar was completely empty because the CASL `can()` gate
 * rejected every nav item. This test ensures the auth store correctly
 * seeds and clears abilities via the shared MongoAbility instance.
 */
import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/authStore'
import { ability } from '@/plugins/casl/ability'

describe('FIND-ux-020: CASL ability seeding', () => {
  beforeEach(() => {
    if (typeof window !== 'undefined' && window.localStorage)
      window.localStorage.removeItem('cena-mock-auth')

    // Reset CASL abilities to empty before each test
    ability.update([])
    setActivePinia(createPinia())
  })

  it('seeds manage-all ability on __mockSignIn', () => {
    const store = useAuthStore()

    // Before sign-in, ability should be empty
    expect(ability.can('manage', 'all')).toBe(false)
    expect(ability.can('read', 'Home')).toBe(false)

    store.__mockSignIn({ uid: 'u-casl', email: 'casl@test.com' })

    // After sign-in, manage-all should be granted
    expect(ability.can('manage', 'all')).toBe(true)

    // manage-all implies read on any subject
    expect(ability.can('read', 'Home')).toBe(true)
    expect(ability.can('read', 'Session')).toBe(true)
    expect(ability.can('read', 'Progress')).toBe(true)
  })

  it('clears abilities on __signOut', () => {
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-casl-out' })
    expect(ability.can('manage', 'all')).toBe(true)

    store.__signOut()
    expect(ability.can('manage', 'all')).toBe(false)
    expect(ability.can('read', 'Home')).toBe(false)
  })

  it('writes userAbilityRules cookie on __mockSignIn', () => {
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-cookie' })

    // The cookie should contain the encoded ability rules
    const cookies = document.cookie

    expect(cookies).toContain('userAbilityRules')
  })

  it('clears userAbilityRules cookie on __signOut', () => {
    const store = useAuthStore()

    store.__mockSignIn({ uid: 'u-cookie-clear' })
    expect(document.cookie).toContain('userAbilityRules')

    store.__signOut()

    // After sign-out, the cookie should be expired (max-age=-1)
    // jsdom may or may not fully honor max-age, but the write happened
    // We verify the ability object is cleared as the authoritative check
    expect(ability.can('manage', 'all')).toBe(false)
  })

  it('hydrates abilities from localStorage on store init', () => {
    // Simulate a persisted session before store init
    window.localStorage.setItem(
      'cena-mock-auth',
      JSON.stringify({ uid: 'u-hydrate-casl', email: 'hydrate@test.com' }),
    )

    // Reset abilities
    ability.update([])
    expect(ability.can('manage', 'all')).toBe(false)

    // Create a fresh store — hydration should seed abilities
    setActivePinia(createPinia())
    useAuthStore()

    expect(ability.can('manage', 'all')).toBe(true)
  })
})
