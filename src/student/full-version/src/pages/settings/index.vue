<script setup lang="ts">
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.settings',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

interface SectionCard {
  id: string
  icon: string
  titleKey: string
  subtitleKey: string
  to: string
}

const SECTIONS: SectionCard[] = [
  { id: 'account', icon: 'tabler-user-circle', titleKey: 'settingsPage.account.title', subtitleKey: 'settingsPage.account.subtitle', to: '/settings/account' },
  { id: 'appearance', icon: 'tabler-palette', titleKey: 'settingsPage.appearance.title', subtitleKey: 'settingsPage.appearance.subtitle', to: '/settings/appearance' },
  { id: 'notifications', icon: 'tabler-bell-ringing', titleKey: 'settingsPage.notifications.title', subtitleKey: 'settingsPage.notifications.subtitle', to: '/settings/notifications' },
  { id: 'privacy', icon: 'tabler-shield-lock', titleKey: 'settingsPage.privacy.title', subtitleKey: 'settingsPage.privacy.subtitle', to: '/settings/privacy' },
]
</script>

<template>
  <div
    class="settings-index-page pa-4"
    data-testid="settings-index-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('settingsPage.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('settingsPage.subtitle') }}
    </p>

    <div class="settings-index-page__grid">
      <VCard
        v-for="section in SECTIONS"
        :key="section.id"
        variant="outlined"
        class="pa-4 cursor-pointer"
        :to="section.to"
        :data-testid="`settings-link-${section.id}`"
      >
        <div class="d-flex align-center">
          <VAvatar
            color="primary"
            size="48"
            class="me-4"
          >
            <VIcon
              :icon="section.icon"
              size="24"
              color="white"
              aria-hidden="true"
            />
          </VAvatar>
          <div class="flex-grow-1 min-w-0">
            <div class="text-subtitle-1 font-weight-medium">
              {{ t(section.titleKey) }}
            </div>
            <div class="text-body-2 text-medium-emphasis">
              {{ t(section.subtitleKey) }}
            </div>
          </div>
          <VIcon
            icon="tabler-chevron-right"
            size="20"
            class="text-medium-emphasis flip-in-rtl"
            aria-hidden="true"
          />
        </div>
      </VCard>
    </div>
  </div>
</template>

<style scoped>
.settings-index-page {
  max-inline-size: 900px;
  margin-inline: auto;
}

.settings-index-page__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 1rem;
}
</style>
