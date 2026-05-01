import { createApp } from 'vue'

import App from '@/App.vue'
import { registerPlugins } from '@core/utils/plugins'

// Styles
import '@core/scss/template/index.scss'
import '@styles/styles.scss'
// KaTeX CSS — required for math rendering in ItemDetailPanel (curator
// review of OCR'd Bagrut content). Bundled once at the app root rather
// than per-component so the styles are available on first paint.
import 'katex/dist/katex.min.css'

// Create vue app
const app = createApp(App)

// Register plugins
registerPlugins(app)

// Mount vue app
app.mount('#app')
