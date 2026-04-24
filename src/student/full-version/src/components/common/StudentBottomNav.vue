<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

interface BottomNavItem {
  to: { name: string }
  icon: string
  titleKey: string
  testId: string
}

const BOTTOM_NAV: BottomNavItem[] = [
  { to: { name: 'home' }, icon: 'tabler-home', titleKey: 'nav.home', testId: 'bottom-nav-home' },
  { to: { name: 'session' }, icon: 'tabler-player-play', titleKey: 'nav.startSession', testId: 'bottom-nav-session' },
  { to: { name: 'tutor' }, icon: 'tabler-message-chatbot', titleKey: 'nav.tutor', testId: 'bottom-nav-tutor' },
  { to: { name: 'progress' }, icon: 'tabler-chart-line', titleKey: 'nav.progress', testId: 'bottom-nav-progress' },
  { to: { name: 'profile' }, icon: 'tabler-user', titleKey: 'nav.profile', testId: 'bottom-nav-profile' },
]

const route = useRoute()
const { t } = useI18n()

const activeIndex = ref(0)

watch(
  () => route.name,
  name => {
    const idx = BOTTOM_NAV.findIndex(item => {
      const rn = name?.toString() || ''

      return rn === item.to.name || rn.startsWith(`${item.to.name}-`)
    })

    if (idx >= 0)
      activeIndex.value = idx
  },
  { immediate: true },
)

const shouldRender = computed(() => {
  return (
    route.meta.layout !== 'blank'
    && route.meta.layout !== 'auth'
    && route.meta.hideSidebar !== true
    && route.query.embed !== '1'
  )
})
</script>

<template>
  <VBottomNavigation
    v-if="shouldRender"
    v-model="activeIndex"
    grow
    class="student-bottom-nav d-md-none"
    data-testid="student-bottom-nav"
    height="64"
  >
    <VBtn
      v-for="item in BOTTOM_NAV"
      :key="item.to.name"
      :to="item.to"
      :data-testid="item.testId"
      :aria-label="t(item.titleKey)"
    >
      <VIcon :icon="item.icon" />
      <span class="text-caption">{{ t(item.titleKey) }}</span>
    </VBtn>
  </VBottomNavigation>
</template>

<style scoped>
.student-bottom-nav {
  border-block-start: 1px solid rgb(var(--v-theme-on-surface) / 0.12);
}
</style>
