<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.sessionDetail',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

// TASK-STU-W-09 line 26: /progress/sessions/:sessionId reuses the
// /session/:id/replay view from STU-W-06. Both that replay view and this
// page are placeholders today; we redirect on mount so the URL scheme is
// forward-compatible — when STU-W-06's replay.vue lands, this route resolves
// without any further code change here.
const route = useRoute()
const router = useRouter()

onMounted(() => {
  const sessionId = route.params.sessionId
  if (typeof sessionId === 'string' && sessionId.length > 0)
    router.replace(`/session/${encodeURIComponent(sessionId)}/replay`)
})
</script>

<template>
  <PlaceholderPage title-key="nav.sessionDetail" />
</template>
