import type { RouteRecordRaw } from 'vue-router/auto'

// STU-W-00: stripped all Vuexy admin-app routes (apps/email, apps/academy,
// apps/ecommerce, apps/logistics, apps/kanban, apps/invoice, apps/chat,
// apps/user, permissions, roles, etc). The student app has its own sitemap
// under src/pages/ and the file-based router in unplugin-vue-router picks it
// up automatically. This file is kept for the root redirect and any future
// cross-cutting redirects; feature tasks will add entries as needed.
//
// STU-W-02 will replace the placeholder root redirect with a proper
// Firebase-auth guard that sends unauthed users to /login and first-run
// users to /onboarding.

// Redirects applied during router init. STU-W-00 redirected `/` to a
// non-existent `home` route and broke the scaffold landing page. STU-W-01
// removes the redirect so `src/pages/index.vue` is reachable again; a
// proper auth-aware redirect lands in STU-W-02 when the nav shell arrives.
export const redirects: RouteRecordRaw[] = []

// Additional static routes (non-file-based). Currently none — the student
// app's full sitemap lives under src/pages/ and is auto-registered by
// unplugin-vue-router.
export const routes: RouteRecordRaw[] = []
