<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

const route = useRoute()
const { t, te } = useI18n()

interface Crumb {
  title: string
  to?: string
  disabled: boolean
}

const crumbs = computed<Crumb[]>(() => {
  const matched = route.matched.filter(m => m.meta?.title)

  const rawCrumbs: Crumb[] = matched.map((m, idx) => {
    const titleKey = m.meta.title as string
    const title = te(titleKey) ? t(titleKey) : titleKey
    const isLast = idx === matched.length - 1

    return {
      title,
      to: isLast ? undefined : m.path,
      disabled: isLast,
    }
  })

  // Prepend Home unless we are already on it.
  const homeTitle = te('nav.home') ? t('nav.home') : 'Home'
  if (rawCrumbs[0]?.title !== homeTitle) {
    rawCrumbs.unshift({
      title: homeTitle,
      to: '/home',
      disabled: false,
    })
  }

  return rawCrumbs
})

const shouldRender = computed(() => {
  if (route.meta.breadcrumbs === false)
    return false
  if (route.meta.layout === 'blank' || route.meta.layout === 'auth')
    return false
  if (route.query.embed === '1')
    return false

  return crumbs.value.length > 1
})
</script>

<template>
  <VBreadcrumbs
    v-if="shouldRender"
    :items="crumbs"
    density="compact"
    class="student-breadcrumbs px-6 py-2"
    data-testid="student-breadcrumbs"
  >
    <template #divider>
      <VIcon
        icon="tabler-chevron-right"
        size="14"
      />
    </template>
  </VBreadcrumbs>
</template>

<style scoped>
.student-breadcrumbs {
  background-color: transparent;
}

:deep(.v-breadcrumbs-item) {
  padding: 0;
}
</style>
