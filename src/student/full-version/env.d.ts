import 'vue-router'
declare module 'vue-router' {
  interface RouteMeta {
    action?: string
    subject?: string
    layoutWrapperClasses?: string
    navActiveLink?: RouteLocationRaw
    layout?: 'blank' | 'default'
    unauthenticatedOnly?: boolean
    public?: boolean
  }
}

// Vite env typing. The actual values live in .env.example / .env.*.
// Keep this in sync with .env.example so autocomplete works in IDEs.
interface ImportMetaEnv {
  readonly VITE_MAPBOX_ACCESS_TOKEN?: string
  /**
   * Build-time gate for the Hebrew locale in the language switcher.
   * Defaults to unset (= false) so Hebrew is hidden outside Israel per
   * user rule (2026-03-27). Set to 'true' for Israeli tenant builds.
   * See src/composables/useAvailableLocales.ts for the consumer.
   */
  readonly VITE_ENABLE_HEBREW?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
