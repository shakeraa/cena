import { existsSync, readFileSync, unlinkSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import VueI18nPlugin from '@intlify/unplugin-vue-i18n/vite'
import { sentryVitePlugin } from '@sentry/vite-plugin'
import vue from '@vitejs/plugin-vue'
import vueJsx from '@vitejs/plugin-vue-jsx'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { VueRouterAutoImports, getPascalCaseRouteName } from 'unplugin-vue-router'
import VueRouter from 'unplugin-vue-router/vite'
import type { Plugin } from 'vite'
import { defineConfig } from 'vite'
import VueDevTools from 'vite-plugin-vue-devtools'
import MetaLayouts from 'vite-plugin-vue-meta-layouts'
import vuetify from 'vite-plugin-vuetify'
import svgLoader from 'vite-svg-loader'
import { VitePWA } from 'vite-plugin-pwa'

// ADR-0058 §3 — Sentry release correlation. `VITE_CENA_RELEASE` is set
// by the CI build job (or the Dockerfile) from `git rev-parse --short=12
// HEAD`. Exposed as a compile-time global `__SENTRY_RELEASE__` so the
// SPA plugin can read it without needing `import.meta.env` to be the
// only source of truth (matches the .NET side which reads from an env
// var). Falls back to 'unknown' for local dev.
const cenaRelease = process.env.VITE_CENA_RELEASE ?? 'unknown'

// ADR-0058 §3 — Source-map upload: ONLY runs when `SENTRY_AUTH_TOKEN` is
// present in the build environment. Keeps local dev silent and avoids
// leaking an auth token into any build that doesn't need it.
const sentryPlugins: Plugin[] = process.env.SENTRY_AUTH_TOKEN
  ? sentryVitePlugin({
      org: process.env.SENTRY_ORG ?? 'cena',
      project: process.env.SENTRY_PROJECT ?? 'student-spa',
      authToken: process.env.SENTRY_AUTH_TOKEN,
      release: { name: cenaRelease },
      telemetry: false,
    })
  : []

/**
 * FIND-ux-029: Vite plugin that validates PWA manifest icons exist at
 * build time. Reads manifest.webmanifest from public/, checks every
 * icon src resolves to a real file, and fails the build if any are missing.
 *
 * In dev mode it logs a warning on server start; in build mode it throws
 * to prevent broken manifests from shipping.
 */
function validateManifestIcons() {
  let publicDir = 'public'
  let isBuild = false

  return {
    name: 'validate-manifest-icons',
    configResolved(config: { publicDir: string; command: string }) {
      publicDir = config.publicDir
      isBuild = config.command === 'build'
    },
    buildStart() {
      const manifestPath = resolve(publicDir, 'manifest.webmanifest')
      if (!existsSync(manifestPath)) {
        const msg = '[validate-manifest-icons] manifest.webmanifest not found in public/'
        if (isBuild)
          throw new Error(msg)

        console.warn(msg)

        return
      }

      const raw = readFileSync(manifestPath, 'utf-8')
      const manifest = JSON.parse(raw)
      const icons: Array<{ src: string }> = manifest.icons || []
      const missing: string[] = []

      for (const icon of icons) {
        const src: string = icon.src || ''
        const rel = src.startsWith('/') ? src.slice(1) : src
        const fullPath = resolve(publicDir, rel)
        if (!existsSync(fullPath))
          missing.push(src)
      }

      if (missing.length > 0) {
        const msg = `[validate-manifest-icons] Missing icon files in public/:\n${missing.map(m => `  - ${m}`).join('\n')}\nBrowsers will receive the SPA HTML fallback instead of the icon image.`

        console.error(msg)
        if (isBuild)
          throw new Error(msg)
      }
      else {
        console.log(`[validate-manifest-icons] All ${icons.length} manifest icon(s) verified.`)
      }
    },
  }
}

/**
 * FIND-arch-017: Vite plugin that strips mockServiceWorker.js from
 * production builds. In dev mode the file is served from public/ as
 * usual; during `vite build` the plugin deletes the file from dist/
 * after Vite copies it from public/.
 *
 * Vite copies public/ files to dist/ outside the Rollup pipeline, so
 * we use `closeBundle` (runs after all writes are flushed) to remove
 * the file from the output directory.
 */
function stripMswInProduction() {
  let outDir = 'dist'

  return {
    name: 'strip-msw-production',
    apply: 'build' as const,
    configResolved(config: { build: { outDir: string } }) {
      outDir = config.build.outDir
    },
    closeBundle() {
      const mswPath = resolve(outDir, 'mockServiceWorker.js')
      if (existsSync(mswPath)) {
        unlinkSync(mswPath)

        console.log('[strip-msw-production] Removed mockServiceWorker.js from dist/')
      }
    },
  }
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    ...sentryPlugins,
    // Docs: https://github.com/posva/unplugin-vue-router
    // ℹ️ This plugin should be placed before vue plugin
    VueRouter({
      getRouteName: routeNode => {
        // Convert pascal case to kebab case
        return getPascalCaseRouteName(routeNode)
          .replace(/([a-z\d])([A-Z])/g, '$1-$2')
          .toLowerCase()
      },

      // STU-W-00: removed admin apps/email manual route inserts (referenced
      // src/pages/apps/email/index.vue which was pruned). If any student
      // feature tasks need manual route inserts, add them here.
    }),
    vue({
      template: {
        compilerOptions: {
          isCustomElement: tag => tag === 'swiper-container' || tag === 'swiper-slide',
        },
      },
    }),
    VueDevTools(),
    vueJsx(),

    // Docs: https://github.com/vuetifyjs/vuetify-loader/tree/master/packages/vite-plugin
    vuetify({
      styles: {
        configFile: 'src/assets/styles/variables/_vuetify.scss',
      },
    }),

    // Docs: https://github.com/dishait/vite-plugin-vue-meta-layouts?tab=readme-ov-file
    MetaLayouts({
      target: './src/layouts',
      defaultLayout: 'default',
    }),

    // Docs: https://github.com/antfu/unplugin-vue-components#unplugin-vue-components
    Components({
      // STU-W-00: removed 'src/views/demos' (views/ directory pruned along
      // with admin-specific subtrees). Student app's component scanning:
      // @core primitives + student/components + future src/components/common.
      dirs: ['src/@core/components', 'src/components'],
      dts: true,
      resolvers: [
        componentName => {
          // Auto import `VueApexCharts`
          if (componentName === 'VueApexCharts')
            return { name: 'default', from: 'vue3-apexcharts', as: 'VueApexCharts' }
        },
      ],
    }),

    // Docs: https://github.com/antfu/unplugin-auto-import#unplugin-auto-import
    AutoImport({
      imports: ['vue', VueRouterAutoImports, '@vueuse/core', '@vueuse/math', 'vue-i18n', 'pinia'],
      dirs: [
        './src/@core/utils',
        './src/@core/composable/',
        './src/composables/',
        './src/utils/',
        './src/plugins/*/composables/*',
      ],
      vueTemplate: true,

      // ℹ️ Disabled to avoid confusion & accidental usage
      ignore: ['useCookies', 'useStorage'],
    }),

    // Docs: https://github.com/intlify/bundle-tools/tree/main/packages/unplugin-vue-i18n#intlifyunplugin-vue-i18n
    VueI18nPlugin({
      runtimeOnly: true,
      compositionOnly: true,
      include: [
        fileURLToPath(new URL('./src/plugins/i18n/locales/**', import.meta.url)),
      ],
    }),
    svgLoader(),

    // FIND-ux-029: validate PWA manifest icon files exist
    validateManifestIcons(),

    // FIND-arch-017: strip mockServiceWorker.js from production dist/
    stripMswInProduction(),

    // PWA-001: Workbox-powered service worker via vite-plugin-pwa
    VitePWA({
      registerType: 'prompt',
      injectRegister: false,
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,woff2}'],
        globIgnores: ['**/katex/**', '**/mockServiceWorker.js'],
        runtimeCaching: [
          {
            urlPattern: /\/api\/questions\/.*/,
            handler: 'NetworkFirst',
            options: {
              cacheName: 'cena-questions',
              expiration: { maxEntries: 50, maxAgeSeconds: 86400 },
              networkTimeoutSeconds: 5,
            },
          },
          {
            urlPattern: /\/api\/progress\/.*/,
            handler: 'NetworkFirst',
            options: {
              cacheName: 'cena-progress',
              expiration: { maxEntries: 10, maxAgeSeconds: 3600 },
              networkTimeoutSeconds: 3,
            },
          },
          {
            urlPattern: /\/api\/sessions\/.*/,
            handler: 'NetworkOnly',
          },
          {
            urlPattern: /\/katex\/.*/,
            handler: 'CacheFirst',
            options: {
              cacheName: 'cena-katex',
              expiration: { maxEntries: 50, maxAgeSeconds: 2592000 },
            },
          },
          {
            urlPattern: /\.(?:woff2?|ttf|otf)$/,
            handler: 'CacheFirst',
            options: {
              cacheName: 'cena-fonts',
              expiration: { maxEntries: 30, maxAgeSeconds: 2592000 },
            },
          },
          {
            urlPattern: /\.(?:png|svg|jpg|jpeg|webp|gif)$/,
            handler: 'StaleWhileRevalidate',
            options: {
              cacheName: 'cena-images',
              expiration: { maxEntries: 100, maxAgeSeconds: 604800 },
            },
          },
        ],
        navigateFallback: '/offline.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/mockServiceWorker/],
      },
      manifest: false,
      devOptions: {
        enabled: false,
      },
    }),
  ],
  define: {
    'process.env': {},

    // ADR-0058 §3 — release string consumed by the Sentry plugin init
    // (`__SENTRY_RELEASE__` read in `src/plugins/sentry.ts`).
    '__SENTRY_RELEASE__': JSON.stringify(cenaRelease),
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@themeConfig': fileURLToPath(new URL('./themeConfig.ts', import.meta.url)),
      '@core': fileURLToPath(new URL('./src/@core', import.meta.url)),
      '@layouts': fileURLToPath(new URL('./src/@layouts', import.meta.url)),
      '@images': fileURLToPath(new URL('./src/assets/images/', import.meta.url)),
      '@styles': fileURLToPath(new URL('./src/assets/styles/', import.meta.url)),
      '@configured-variables': fileURLToPath(new URL('./src/assets/styles/variables/_template.scss', import.meta.url)),
      '@db': fileURLToPath(new URL('./src/plugins/fake-api/handlers/', import.meta.url)),
      '@api-utils': fileURLToPath(new URL('./src/plugins/fake-api/utils/', import.meta.url)),
    },
  },
  server: {
    port: 5175,
    strictPort: true,
    // EPIC-PRR-I: proxy /api → student-api so the SPA's fetch('/api/...')
    // paths resolve in `vite` dev. In docker compose the student-api is
    // reachable via the compose-network DNS name; outside docker, set
    // VITE_DEV_API_PROXY_TARGET to http://localhost:5050 (or similar).
    proxy: {
      '/api': {
        target: process.env.VITE_DEV_API_PROXY_TARGET ?? 'http://cena-student-api:5050',
        changeOrigin: true,
      },
    },
  },
  build: {
    chunkSizeWarningLimit: 5000,
  },
  optimizeDeps: {
    exclude: ['vuetify'],
    entries: [
      './src/**/*.vue',
    ],
  },
})
