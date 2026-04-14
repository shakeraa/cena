/**
 * FIND-qa-006 / FIND-ux-008: Brand title regression tests
 *
 * Verifies that the router's afterEach hook rewrites document.title
 * per-route according to the themeConfig.brandTitle pattern.
 *
 * Background: Previously document.title was static. The fix adds a
 * router.afterEach hook that reads to.meta.title, localizes it via
 * i18n, and sets document.title to "{localized} · Cena".
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'
import { setupGuards } from '@/plugins/1.router/guards'

// Minimal route set for title testing
const testRoutes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'root',
    meta: { title: 'nav.home' },
    component: { template: '<div>Home</div>' },
  },
  {
    path: '/login',
    name: 'login',
    meta: { title: 'nav.login' },
    component: { template: '<div>Login</div>' },
  },
  {
    path: '/progress',
    name: 'progress',
    meta: { title: 'nav.mastery' },
    component: { template: '<div>Progress</div>' },
  },
  {
    path: '/no-title',
    name: 'noTitle',
    component: { template: '<div>No Title</div>' },
  },
]

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        nav: {
          home: 'Home',
          login: 'Login',
          mastery: 'My Progress',
        },
      },
    },
  })
}

describe('brand title (FIND-ux-008)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())

    // Reset document.title before each test
    document.title = 'Cena'
  })

  it('sets document.title to "{localized} · Cena" for routes with meta.title', async () => {
    const i18n = makeI18n()

    const router = createRouter({
      history: createWebHistory(),
      routes: testRoutes,
    })

    // Apply the same guards used in the app
    setupGuards(router as any)

    // Mount the router (required for navigation to work)
    router.beforeEach(() => true)

    await router.push('/')
    await router.isReady()

    // After navigation to home, title should be "Home · Cena"
    expect(document.title).toBe('Home · Cena')
  })

  it('updates document.title on route change', async () => {
    const i18n = makeI18n()

    const router = createRouter({
      history: createWebHistory(),
      routes: testRoutes,
    })

    setupGuards(router as any)
    router.beforeEach(() => true)

    await router.push('/')
    await router.isReady()
    expect(document.title).toBe('Home · Cena')

    await router.push('/progress')
    expect(document.title).toBe('My Progress · Cena')

    await router.push('/login')
    expect(document.title).toBe('Login · Cena')
  })

  it('falls back to "Cena" when route has no meta.title', async () => {
    const i18n = makeI18n()

    const router = createRouter({
      history: createWebHistory(),
      routes: testRoutes,
    })

    setupGuards(router as any)
    router.beforeEach(() => true)

    await router.push('/no-title')
    await router.isReady()

    // Should fall back to just "Cena"
    expect(document.title).toBe('Cena')
  })

  it('falls back to key as-is when i18n translation is missing', async () => {
    // i18n with empty messages - forces fallback behavior
    const i18n = createI18n({
      legacy: false,
      locale: 'en',
      messages: { en: {} },
    })

    const router = createRouter({
      history: createWebHistory(),
      routes: testRoutes,
    })

    setupGuards(router as any)
    router.beforeEach(() => true)

    await router.push('/')
    await router.isReady()

    // vue-i18n returns the key as-is when translation is missing
    expect(document.title).toBe('nav.home · Cena')
  })

  /**
   * Synthetic regression test: if the afterEach hook is removed,
   * document.title should NOT update on navigation.
   */
  it('fails synthetic regression when afterEach hook is not registered', async () => {
    const router = createRouter({
      history: createWebHistory(),
      routes: testRoutes,
    })

    // Intentionally NOT calling setupGuards - simulating regression
    router.beforeEach(() => true)

    const initialTitle = document.title

    await router.push('/')
    await router.isReady()

    // Title should NOT have changed because no afterEach was registered
    expect(document.title).toBe(initialTitle)
    expect(document.title).not.toBe('Home · Cena')
  })
})
