<script setup lang="ts">
definePage({
  meta: {
    // FIND-ux-002: `public: true` so the auth guard does not
    // synchronously bounce to /login before the onMounted redirect
    // fires — we want every hit on `/` to immediately forward to
    // `/home`, and `/home`'s own `requiresAuth: true` meta is what
    // eventually forwards unauthed users to /login.
    layout: 'blank',
    public: true,
    requiresAuth: false,
    requiresOnboarded: false,
  },
})

/*
 * FIND-ux-002: replace the STU-W-01 dev chassis with a pure redirect.
 *
 * The root route `/` used to render a "design system chassis" card with
 * a dead "Save" button and a task ID leaking into the UI. The fix:
 *
 *  - Signed-in users land on `/home` (the real student dashboard).
 *  - Signed-out users are bounced to `/login` by the auth guard in
 *    `plugins/1.router/guards.ts` when they try to enter `/home`.
 *
 * This component renders nothing visible; it only triggers an immediate
 * router.replace so `/` is never a reachable surface with UI of its own.
 * The dev chassis has been moved to `/_dev/design-system` and
 * `/_dev/flow-states`, which are both already present.
 *
 * `onMounted` and `useRouter` are provided by unplugin-auto-import (see
 * vite.config.ts → imports: ['vue', VueRouterAutoImports, ...]); no
 * explicit imports needed.
 */
const router = useRouter()

onMounted(() => {
  // Use replace so the empty `/` entry never shows in browser history.
  router.replace({ path: '/home' })
})
</script>

<template>
  <!-- Intentionally empty: this route exists only to forward to /home. -->
  <div />
</template>
