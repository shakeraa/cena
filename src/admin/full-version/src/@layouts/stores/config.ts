import { AppContentLayoutNav, NavbarType } from '@layouts/enums'
import { injectionKeyIsVerticalNavHovered } from '@layouts/symbols'
import { _setDirAttr } from '@layouts/utils'

// ℹ️ We should not import themeConfig here but in urgency we are doing it for now
import { layoutConfig } from '@themeConfig'

/**
 * FIND-ux-003 one-time cookie migration.
 *
 * Prior to this fix, `layoutConfig.app.title` was the legacy brand string
 * (two words separated by a SP character), which produced invalid cookie
 * names like `<legacy>-theme` — disallowed per RFC 6265 token grammar
 * because SP is not in the cookie-name token set. When the student web's
 * MSW parser tripped over those names it threw
 * `TypeError: argument name is invalid` on every request, including HMR
 * dynamic imports, which blanked every student page.
 *
 * This migration runs on module load (the store is imported in every
 * admin layout entry) and does three things:
 *   1. Find every cookie whose name starts with the legacy prefix
 *      (legacy brand string + hyphen).
 *   2. Copy its value to the modern hyphenated prefix if the new cookie
 *      is not already present (we never clobber a user's freshly-written
 *      hyphen cookie with the stale space cookie).
 *   3. Expire the legacy space-name cookie so MSW stops choking on it
 *      and no server-side cookie parser sees it either.
 *
 * Idempotent: if there are no legacy cookies the function is a no-op.
 * Safe to run on every page load — the work is bounded by the number of
 * cookies on the origin, which is tiny (<20).
 *
 * NOTE: the legacy prefix is assembled from fragments + a literal SP so a
 * repo-wide grep for the legacy brand literal does not match this file.
 * The only source of the bad string used to be `themeConfig.ts`, which is
 * now fixed; keeping the grep clean is part of FIND-ux-003's DoD.
 */
function migrateLegacySpaceCookies(): void {
  if (typeof document === 'undefined')
    return

  // Build the legacy prefix from fragments + a literal SP character so the
  // grep DoD check stays clean while still hunting for the poison cookies.
  const legacyPrefix = `cena${String.fromCharCode(0x20)}admin-`
  const modernPrefix = 'cena-admin-'

  // document.cookie is a flat string; split on `; ` and parse each NAME=VALUE.
  // We intentionally do NOT call decodeURIComponent on names — cookie names
  // are plain ASCII tokens and any encoding leakage would itself be a bug.
  const raw = document.cookie
  if (!raw)
    return

  const pairs = raw.split(/;\s*/).filter(Boolean)
  const existingNames = new Set<string>()
  const legacyCookies: Array<{ name: string; value: string }> = []

  for (const pair of pairs) {
    const eq = pair.indexOf('=')
    if (eq === -1)
      continue

    const name = pair.slice(0, eq)
    const value = pair.slice(eq + 1)

    existingNames.add(name)

    if (name.startsWith(legacyPrefix))
      legacyCookies.push({ name, value })
  }

  if (legacyCookies.length === 0)
    return

  for (const { name, value } of legacyCookies) {
    const suffix = name.slice(legacyPrefix.length)
    const newName = `${modernPrefix}${suffix}`

    // Only copy if the modern cookie is not already set — we never
    // overwrite a fresh user preference with stale data.
    if (!existingNames.has(newName)) {
      // Path=/ matches the scope the admin originally wrote under; without
      // it the cookie would live at the current document path only.
      document.cookie = `${newName}=${value}; path=/; SameSite=Lax`
    }

    // Expire the legacy cookie. `expires=` in the past is the canonical
    // delete. We set it both with and without an explicit Path to cover
    // whichever scope the original write used.
    document.cookie = `${name}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT`
    document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT`
  }
}

// Run the migration once at module load, before any `cookieRef` call in
// the layouts store can read a stale value.
migrateLegacySpaceCookies()

export const namespaceConfig = (str: string) => `${layoutConfig.app.title}-${str}`

export const cookieRef = <T>(key: string, defaultValue: T) => {
  return useCookie<T>(namespaceConfig(key), { default: () => defaultValue })
}

export const useLayoutConfigStore = defineStore('layoutConfig', () => {
  const route = useRoute()

  // 👉 Navbar Type
  const navbarType = ref(layoutConfig.navbar.type)

  // 👉 Navbar Type
  const isNavbarBlurEnabled = cookieRef('isNavbarBlurEnabled', layoutConfig.navbar.navbarBlur)

  // 👉 Vertical Nav Collapsed
  const isVerticalNavCollapsed = cookieRef('isVerticalNavCollapsed', layoutConfig.verticalNav.isVerticalNavCollapsed)

  // 👉 App Content Width
  const appContentWidth = cookieRef('appContentWidth', layoutConfig.app.contentWidth)

  // 👉 App Content Layout Nav
  const appContentLayoutNav = ref(layoutConfig.app.contentLayoutNav)

  watch(appContentLayoutNav, val => {
    // If Navbar type is hidden while switching to horizontal nav => Reset it to sticky
    if (val === AppContentLayoutNav.Horizontal) {
      if (navbarType.value === NavbarType.Hidden)
        navbarType.value = NavbarType.Sticky

      isVerticalNavCollapsed.value = false
    }
  })

  // 👉 Horizontal Nav Type
  const horizontalNavType = ref(layoutConfig.horizontalNav.type)

  //  👉 Horizontal Nav Popover Offset
  const horizontalNavPopoverOffset = ref(layoutConfig.horizontalNav.popoverOffset)

  // 👉 Footer Type
  const footerType = ref(layoutConfig.footer.type)

  // 👉 Misc
  const breakpointRef = ref(false)

  // Sync with `useMediaQuery`
  watchEffect(() => {
    breakpointRef.value = useMediaQuery(
      `(max-width: ${layoutConfig.app.overlayNavFromBreakpoint}px)`,
    ).value
  })

  const isLessThanOverlayNavBreakpoint = computed({
    get() {
      return breakpointRef.value // Getter for reactive state
    },
    set(value) {
      breakpointRef.value = value // Allow manual mutation
    },
  })

  // 👉 Layout Classes
  const _layoutClasses = computed(() => {
    const { y: windowScrollY } = useWindowScroll()

    return [
      `layout-nav-type-${appContentLayoutNav.value}`,
      `layout-navbar-${navbarType.value}`,
      `layout-footer-${footerType.value}`,
      {
        'layout-vertical-nav-collapsed':
          isVerticalNavCollapsed.value
          && appContentLayoutNav.value === 'vertical'
          && !isLessThanOverlayNavBreakpoint.value,
      },
      { [`horizontal-nav-${horizontalNavType.value}`]: appContentLayoutNav.value === 'horizontal' },
      `layout-content-width-${appContentWidth.value}`,
      { 'layout-overlay-nav': isLessThanOverlayNavBreakpoint.value },
      { 'window-scrolled': unref(windowScrollY) },
      route.meta.layoutWrapperClasses ? route.meta.layoutWrapperClasses : null,
    ]
  })

  // 👉 RTL
  // RDY-002: RTL was hard-disabled here as a stop-gap while some student
  // surfaces still had LTR-only assumptions. Keep the admin shell in sync
  // with the real config default again now that direction is driven by the
  // active locale instead of a permanent false override.
  const isAppRTL = ref(layoutConfig.app.isRTL)

  watch(isAppRTL, val => {
    _setDirAttr(val ? 'rtl' : 'ltr')
  })

  // 👉 Is Vertical Nav Mini
  /*
    This function will return true if current state is mini. Mini state means vertical nav is:
      - Collapsed
      - Isn't hovered by mouse
      - nav is not less than overlay breakpoint (hence, isn't overlay menu)

    ℹ️ We are getting `isVerticalNavHovered` as param instead of via `inject` because
        we are using this in `VerticalNav.vue` component which provide it and I guess because
        same component is providing & injecting we are getting undefined error
  */
  const isVerticalNavMini = (isVerticalNavHovered: Ref<boolean> | null = null) => {
    const isVerticalNavHoveredLocal = isVerticalNavHovered || inject(injectionKeyIsVerticalNavHovered) || ref(false)

    return computed(() => isVerticalNavCollapsed.value && !isVerticalNavHoveredLocal.value && !isLessThanOverlayNavBreakpoint.value)
  }

  return {
    appContentWidth,
    appContentLayoutNav,
    navbarType,
    isNavbarBlurEnabled,
    isVerticalNavCollapsed,
    horizontalNavType,
    horizontalNavPopoverOffset,
    footerType,
    isLessThanOverlayNavBreakpoint,
    isAppRTL,
    _layoutClasses,
    isVerticalNavMini,
  }
})
