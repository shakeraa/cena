import type { App } from 'vue'

/**
 * This is helper function to register plugins like a nuxt
 * To register a plugin just export a const function `defineVuePlugin` that takes `app` as argument and call `app.use`
 * For Scanning plugins it will include all files in `src/plugins` and `src/plugins/**\/index.ts`
 *
 *
 * @param {App} app Vue app instance
 *
 * @example
 * ```ts
 * // File: src/plugins/vuetify/index.ts
 *
 * import type { App } from 'vue'
 * import { createVuetify } from 'vuetify'
 *
 * const vuetify = createVuetify({ ... })
 *
 * export default function (app: App) {
 *   app.use(vuetify)
 * }
 * ```
 *
 * All you have to do is use this helper function in `main.ts` file like below:
 * ```ts
 * // File: src/main.ts
 * import { registerPlugins } from '@core/utils/plugins'
 * import { createApp } from 'vue'
 * import App from '@/App.vue'
 *
 * // Create vue app
 * const app = createApp(App)
 *
 * // Register plugins
 * registerPlugins(app) // [!code focus]
 *
 * // Mount vue app
 * app.mount('#app')
 * ```
 */

export const registerPlugins = async (app: App) => {
  // FIND-arch-017: in production builds the fake-api module is excluded via
  // the runtime guard below (path.includes('fake-api') check). The fake-api
  // index.ts itself also gates all MSW imports behind import.meta.env.DEV
  // with dynamic imports, so Vite tree-shakes the entire MSW dependency
  // graph out of the production bundle even though the glob still matches
  // the file path.
  const imports = import.meta.glob<{ default: (app: App) => void | Promise<void> }>(
    ['../../plugins/*.{ts,js}', '../../plugins/*/index.{ts,js}'],
  { eager: true },
  )

  const importPaths = Object.keys(imports).sort()

  for (const path of importPaths) {
    // FIND-arch-017: skip the fake-api plugin in production builds.
    // Belt-and-suspenders alongside the glob exclusion above — if Vite's
    // glob somehow still picks up the fake-api module, we skip calling it.
    if (!import.meta.env.DEV && path.includes('fake-api'))
      continue

    const pluginImportModule = imports[path]

    // FIND-ux-021: await async plugins (e.g. fake-api's MSW worker.start())
    // so the service worker is fully registered before the app mounts and
    // fires its first API calls.
    await pluginImportModule.default?.(app)
  }
}
