import { createApp } from 'vue'

import App from '@/App.vue'
import { registerPlugins } from '@core/utils/plugins'
import { registerServiceWorker } from '@/utils/registerServiceWorker'

// Styles
import '@core/scss/template/index.scss'
import '@styles/styles.scss'

// Create vue app
const app = createApp(App)

// Register plugins
registerPlugins(app)

// Mount vue app
app.mount('#app')

// STU-W-15: register the Cena service worker (prod only, skipped in dev
// where MSW owns the worker slot).
registerServiceWorker()
