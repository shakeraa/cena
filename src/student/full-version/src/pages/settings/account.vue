<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/authStore'
import { useApiQuery } from '@/composables/useApiQuery'
import type { MeBootstrapDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.settingsAccount',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const authStore = useAuthStore()

const meQuery = useApiQuery<MeBootstrapDto>('/api/me')

function handleSignOut() {
  authStore.__signOut()
}
</script>

<template>
  <div
    class="settings-account-page pa-4"
    data-testid="settings-account-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('settingsPage.account.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('settingsPage.account.subtitle') }}
    </p>

    <VCard
      v-if="meQuery.data.value"
      variant="outlined"
      class="pa-5 mb-4"
    >
      <div class="text-h6 mb-3">
        {{ t('settingsPage.account.accountInfo') }}
      </div>
      <div class="d-flex align-center mb-2">
        <div class="text-body-2 text-medium-emphasis me-2 flex-shrink-0">
          {{ t('settingsPage.account.studentId') }}:
        </div>
        <div
          class="text-body-2 font-mono"
          data-testid="account-student-id"
        >
          {{ meQuery.data.value.studentId }}
        </div>
      </div>
      <div class="d-flex align-center">
        <div class="text-body-2 text-medium-emphasis me-2">
          {{ t('settingsPage.account.role') }}:
        </div>
        <VChip
          size="small"
          variant="tonal"
          data-testid="account-role"
        >
          {{ meQuery.data.value.role }}
        </VChip>
      </div>
    </VCard>

    <VCard
      variant="outlined"
      class="pa-5"
      data-testid="account-danger-zone"
    >
      <div class="text-h6 text-error mb-2">
        {{ t('settingsPage.account.signOutTitle') }}
      </div>
      <p class="text-body-2 text-medium-emphasis mb-4">
        {{ t('settingsPage.account.signOutSubtitle') }}
      </p>
      <VBtn
        color="error"
        variant="outlined"
        prepend-icon="tabler-logout"
        data-testid="account-sign-out"
        @click="handleSignOut"
      >
        {{ t('nav.signOut') }}
      </VBtn>
    </VCard>
  </div>
</template>

<style scoped>
.settings-account-page {
  max-inline-size: 700px;
  margin-inline: auto;
}
</style>
