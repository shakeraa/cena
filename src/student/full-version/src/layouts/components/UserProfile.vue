<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'

const router = useRouter()
const { t } = useI18n()
const authStore = useAuthStore()
const meStore = useMeStore()

// Simplified in STU-W-UI-POLISH. Removed Vuexy admin-specific menu items
// (Billing Plan, Pricing, FAQ, pages-account-settings-tab deep links) that
// don't exist in the student sitemap. Student menu: Profile, Settings,
// Sign out. Uses the authStore + meStore from STU-W-02 instead of the old
// `useCookie('userData')` Vuexy convention.

const displayName = computed(() => meStore.profile?.displayName || authStore.email || 'Student')

const initials = computed(() => {
  const name = displayName.value

  return name.split(/\s+/).filter(Boolean).slice(0, 2).map(s => s[0]?.toUpperCase()).join('') || '?'
})

async function signOut() {
  authStore.__signOut()
  meStore.__setProfile(null)
  if (typeof localStorage !== 'undefined') {
    localStorage.removeItem('cena-mock-auth')
    localStorage.removeItem('cena-mock-me')
  }
  await router.push('/login')
}
</script>

<template>
  <VBadge
    v-if="authStore.isSignedIn"
    dot
    bordered
    location="bottom right"
    offset-x="1"
    offset-y="2"
    color="success"
  >
    <VAvatar
      size="38"
      class="cursor-pointer"
      color="primary"
      variant="tonal"
      data-testid="user-profile-avatar"
    >
      <span class="text-caption font-weight-bold">{{ initials }}</span>

      <VMenu
        activator="parent"
        width="240"
        location="bottom end"
        offset="12px"
      >
        <VList density="compact">
          <VListItem>
            <template #prepend>
              <VAvatar
                size="36"
                color="primary"
                variant="tonal"
              >
                <span class="text-caption font-weight-bold">{{ initials }}</span>
              </VAvatar>
            </template>
            <VListItemTitle class="font-weight-medium">
              {{ displayName }}
            </VListItemTitle>
            <VListItemSubtitle class="text-disabled">
              {{ authStore.email ?? '' }}
            </VListItemSubtitle>
          </VListItem>

          <VDivider class="my-2" />

          <VListItem
            :to="{ name: 'profile' }"
            prepend-icon="tabler-user"
            data-testid="user-profile-menu-profile"
          >
            <VListItemTitle>{{ t('nav.profile') }}</VListItemTitle>
          </VListItem>
          <VListItem
            :to="{ name: 'settings' }"
            prepend-icon="tabler-settings"
            data-testid="user-profile-menu-settings"
          >
            <VListItemTitle>{{ t('nav.settings') }}</VListItemTitle>
          </VListItem>

          <VDivider class="my-2" />

          <div class="px-4 py-2">
            <VBtn
              block
              size="small"
              color="error"
              variant="tonal"
              append-icon="tabler-logout"
              data-testid="user-profile-signout"
              @click="signOut"
            >
              {{ t('nav.signOut') }}
            </VBtn>
          </div>
        </VList>
      </VMenu>
    </VAvatar>
  </VBadge>
</template>
