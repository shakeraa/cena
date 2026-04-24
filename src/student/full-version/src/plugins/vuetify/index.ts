import { deepMerge } from '@antfu/utils'
import type { App } from 'vue'
import { useI18n } from 'vue-i18n'
import { createVuetify } from 'vuetify'
import { VBtn } from 'vuetify/components/VBtn'
import { VVideo } from 'vuetify/labs/VVideo'
import { createVueI18nAdapter } from 'vuetify/locale/adapters/vue-i18n'
import defaults from './defaults'
import { icons } from './icons'
import { staticPrimaryColor, staticPrimaryDarkenColor, themes } from './theme'
import { themeConfig } from '@themeConfig'
import { getI18n } from '@/plugins/i18n/index'

// Styles
import { cookieRef } from '@/@layouts/stores/config'
import '@core/scss/template/libs/vuetify/index.scss'
import 'vuetify/styles'

let vuetifyInstance: ReturnType<typeof createVuetify> | null = null

/**
 * Exposes the created Vuetify instance to non-component callers (e.g. the
 * router guard's `?theme=` override handler). Returns `null` until the
 * plugin has run.
 */
export function getVuetify() {
  return vuetifyInstance
}

export default function (app: App) {
  const cookieThemeValues = {
    defaultTheme: resolveVuetifyTheme(themeConfig.app.theme),
    themes: {
      light: {
        colors: {
          'primary': cookieRef('lightThemePrimaryColor', staticPrimaryColor).value,
          'primary-darken-1': cookieRef('lightThemePrimaryDarkenColor', staticPrimaryDarkenColor).value,
        },
      },
      dark: {
        colors: {
          'primary': cookieRef('darkThemePrimaryColor', staticPrimaryColor).value,
          'primary-darken-1': cookieRef('darkThemePrimaryDarkenColor', staticPrimaryDarkenColor).value,
        },
      },
    },
  }

  const optionTheme = deepMerge({ themes }, cookieThemeValues)

  const vuetify = createVuetify({
    aliases: {
      IconBtn: VBtn,
    },
    components: {
      VVideo,
    },
    defaults,
    icons,
    theme: optionTheme,
    locale: {
      adapter: createVueI18nAdapter({ i18n: getI18n(), useI18n }),
    },
  })

  vuetifyInstance = vuetify

  app.use(vuetify)
}
