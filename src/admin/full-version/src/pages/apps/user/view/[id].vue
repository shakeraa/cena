<script setup lang="ts">
import UserBioPanel from '@/views/apps/user/view/UserBioPanel.vue'
import UserTabAccount from '@/views/apps/user/view/UserTabAccount.vue'
import UserTabSecurity from '@/views/apps/user/view/UserTabSecurity.vue'
import UserTabActivity from '@/views/apps/user/view/UserTabActivity.vue'
import UserTabSessions from '@/views/apps/user/view/UserTabSessions.vue'

const route = useRoute('apps-user-view-id')

const userTab = ref(null)

const tabs = [
  { icon: 'tabler-user', title: 'Overview' },
  { icon: 'tabler-lock', title: 'Security' },
  { icon: 'tabler-activity', title: 'Activity' },
  { icon: 'tabler-devices', title: 'Sessions' },
]

const { data: userData, execute: refetchUser } = await useApi<any>(`/admin/users/${route.params.id}`)
</script>

<template>
  <VRow v-if="userData">
    <VCol
      cols="12"
      md="5"
      lg="4"
    >
      <UserBioPanel
        :user-data="userData"
        @user-updated="refetchUser"
      />
    </VCol>

    <VCol
      cols="12"
      md="7"
      lg="8"
    >
      <VTabs
        v-model="userTab"
        class="v-tabs-pill"
      >
        <VTab
          v-for="tab in tabs"
          :key="tab.icon"
        >
          <VIcon
            :size="18"
            :icon="tab.icon"
            class="me-1"
          />
          <span>{{ tab.title }}</span>
        </VTab>
      </VTabs>

      <VWindow
        v-model="userTab"
        class="mt-6 disable-tab-transition"
        :touch="false"
      >
        <VWindowItem>
          <UserTabAccount
            :user-data="userData"
            @user-updated="refetchUser"
          />
        </VWindowItem>

        <VWindowItem>
          <UserTabSecurity :user-id="String(route.params.id)" />
        </VWindowItem>

        <VWindowItem>
          <UserTabActivity :user-id="String(route.params.id)" />
        </VWindowItem>

        <VWindowItem>
          <UserTabSessions :user-id="String(route.params.id)" />
        </VWindowItem>
      </VWindow>
    </VCol>
  </VRow>
  <div v-else>
    <VAlert
      type="error"
      variant="tonal"
    >
      User with ID {{ route.params.id }} not found!
    </VAlert>
  </div>
</template>
