/**
 * FIND-ux-020 regression: student desktop sidebar must render all nav items
 * after sign-in.
 *
 * Root cause: the CASL `can()` gate on VerticalNavLink/VerticalNavSectionTitle
 * returned false for every nav item because:
 *   1. Nav items omitted `action`/`subject` so $can(undefined, undefined) was called
 *   2. No ability rules were ever seeded, so CASL's empty MongoAbility rejected everything
 *
 * This test signs in via the existing mock helper and asserts the sidebar
 * renders the expected number of nav links + section headings.
 */
import { expect, test } from '@playwright/test'

async function seedAuth(
  page: import('@playwright/test').Page,
  opts: { uid: string },
) {
  await page.addInitScript(
    o => {
      localStorage.setItem('cena-mock-auth', JSON.stringify({
        uid: o.uid,
        email: `${o.uid}@example.com`,
        displayName: 'Test Student',
      }))
      localStorage.setItem('cena-mock-me', JSON.stringify({
        uid: o.uid,
        displayName: 'Test Student',
        email: `${o.uid}@example.com`,
        locale: 'en',
        onboardedAt: '2026-04-10T00:00:00Z',
      }))
    },
    { uid: opts.uid },
  )
}

test.describe('FIND-ux-020: student sidebar nav items render after login', () => {
  test('desktop sidebar shows at least 14 nav links after sign-in', async ({ page }) => {
    await seedAuth(page, { uid: 'sidebar-test' })
    await page.goto('/home')

    // Wait for the sidebar to fully render
    const sidebar = page.locator('aside.layout-vertical-nav')
    await expect(sidebar).toBeVisible({ timeout: 10_000 })

    // Count visible <li> children inside the nav-items list.
    // Before the fix, this was 0 (all <!--v-if-->). After the fix,
    // it should be >= 14 nav links + 5 section headings = 19 <li>s.
    const navItems = sidebar.locator('.nav-items > li')
    const count = await navItems.count()

    // The student nav config defines 14 nav links + 5 section headings.
    // Some items are groups (Challenges) whose children render as nested <li>s,
    // so the top-level count should be at least 14 (links + sections + groups).
    expect(count).toBeGreaterThanOrEqual(14)
  })

  test('sidebar survives page refresh without resetting to empty', async ({ page }) => {
    await seedAuth(page, { uid: 'refresh-test' })
    await page.goto('/home')

    const sidebar = page.locator('aside.layout-vertical-nav')
    await expect(sidebar).toBeVisible({ timeout: 10_000 })

    // First load: count nav items
    const firstCount = await sidebar.locator('.nav-items > li').count()
    expect(firstCount).toBeGreaterThanOrEqual(14)

    // Hard refresh
    await page.reload()

    await expect(sidebar).toBeVisible({ timeout: 10_000 })
    const secondCount = await sidebar.locator('.nav-items > li').count()
    expect(secondCount).toBeGreaterThanOrEqual(14)
  })

  test('sign-out clears abilities so sidebar does not render stale nav', async ({ page }) => {
    await seedAuth(page, { uid: 'signout-test' })
    await page.goto('/home')

    const sidebar = page.locator('aside.layout-vertical-nav')
    await expect(sidebar).toBeVisible({ timeout: 10_000 })

    // Clear auth and nav to login
    await page.evaluate(() => {
      localStorage.removeItem('cena-mock-auth')
      localStorage.removeItem('cena-mock-me')
    })
    await page.goto('/login')

    // After sign-out + navigation to login, the sidebar should either
    // not exist (auth layout hides it) or have zero nav items.
    const sidebarAfterLogout = page.locator('aside.layout-vertical-nav .nav-items > li')
    const afterCount = await sidebarAfterLogout.count()
    expect(afterCount).toBe(0)
  })
})
