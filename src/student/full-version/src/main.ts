import { createApp } from 'vue'

import App from '@/App.vue'
import { registerPlugins } from '@core/utils/plugins'
import { registerServiceWorker } from '@/utils/registerServiceWorker'

// Styles
import '@core/scss/template/index.scss'
import '@styles/styles.scss'

// Create vue app
const app = createApp(App)

// FIND-ux-021: await registerPlugins so that async plugins (e.g. MSW
// worker.start() in the fake-api plugin) complete BEFORE the app mounts.
// Without this, cold-load races produce 404s for /api/* paths that MSW
// should intercept, and raw error strings like '[GET] "/api/me": 404 Not
// Found' leak to the UI.
await registerPlugins(app)

// Mount vue app
app.mount('#app')

// STU-W-15: register the Cena service worker (prod only, skipped in dev
// where MSW owns the worker slot).
registerServiceWorker()
