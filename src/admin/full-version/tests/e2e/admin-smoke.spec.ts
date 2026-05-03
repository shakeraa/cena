/**
 * Smoke E2E tests for the Cena Admin dashboard.
 *
 * These tests validate basic page accessibility and routing behavior.
 * They do NOT require a real Firebase session — they test the unauthenticated
 * user experience and page structure.
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { test, expect } from '@playwright/test'

test.describe('Admin Login page', () => {
  test('login page renders with Cena Admin branding', async ({ page }) => {
    await page.goto('/login')

    // Check the page loaded and has the brand title
    await expect(page.locator('text=Cena Admin')).toBeVisible()
  })

  test('login page has email and password fields', async ({ page }) => {
    await page.goto('/login')

    const emailField = page.locator('input[type="email"]')
    const passwordField = page.locator('input[type="password"]')

    await expect(emailField).toBeVisible()
    await expect(passwordField).toBeVisible()
  })

  test('login page has Sign In button', async ({ page }) => {
    await page.goto('/login')

    const signInButton = page.locator('button[type="submit"]')

    await expect(signInButton).toBeVisible()
    await expect(signInButton).toContainText('Sign In')
  })

  test('login page has social login buttons', async ({ page }) => {
    await page.goto('/login')

    await expect(page.locator('text=Sign in with Google')).toBeVisible()
    await expect(page.locator('text=Sign in with Apple')).toBeVisible()
  })

  test('login page has Forgot Password link', async ({ page }) => {
    await page.goto('/login')

    const forgotLink = page.locator('text=Forgot Password?')

    await expect(forgotLink).toBeVisible()
  })
})

test.describe('Auth guard redirects', () => {
  test('unauthenticated user accessing / is redirected to /login', async ({ page }) => {
    await page.goto('/')

    // The auth guard should redirect to /login
    // Give it a moment for the client-side redirect
    await page.waitForURL(/\/login/, { timeout: 10_000 })

    expect(page.url()).toContain('/login')
  })

  test('unauthenticated user accessing /dashboards/admin is redirected to /login', async ({ page }) => {
    await page.goto('/dashboards/admin')

    await page.waitForURL(/\/login/, { timeout: 10_000 })

    expect(page.url()).toContain('/login')
  })

  test('unauthenticated user accessing /apps/user/list is redirected to /login', async ({ page }) => {
    await page.goto('/apps/user/list')

    await page.waitForURL(/\/login/, { timeout: 10_000 })

    expect(page.url()).toContain('/login')
  })
})

test.describe('Dashboard page structure (requires auth)', () => {
  // These tests verify the page loads but will redirect to login
  // since there is no Firebase session in E2E. The redirect itself
  // is the expected behavior we validate.

  test('protected routes redirect unauthenticated users consistently', async ({ page }) => {
    const protectedRoutes = [
      '/dashboards/admin',
      '/apps/user/list',
      '/apps/questions/list',
      '/apps/focus/dashboard',
      '/apps/system/ai-settings',
    ]

    for (const route of protectedRoutes) {
      await page.goto(route)
      await page.waitForURL(/\/login/, { timeout: 10_000 })
      expect(page.url()).toContain('/login')
    }
  })
})
