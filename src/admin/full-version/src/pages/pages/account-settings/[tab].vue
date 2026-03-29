<script lang="ts" setup>
const route = useRoute('pages-account-settings-tab')

const activeTab = computed({
  get: () => route.params.tab,
  set: () => route.params.tab,
})

const tabs = [
  { title: 'Account', icon: 'tabler-users', tab: 'account' },
  { title: 'Security', icon: 'tabler-lock', tab: 'security' },
]

definePage({
  meta: {
    navActiveLink: 'pages-account-settings-tab',
  },
})
</script>

<template>
  <div>
    <VTabs
      v-model="activeTab"
      class="v-tabs-pill"
    >
      <VTab
        v-for="item in tabs"
        :key="item.icon"
        :value="item.tab"
        :to="{ name: 'pages-account-settings-tab', params: { tab: item.tab } }"
      >
        <VIcon
          size="20"
          start
          :icon="item.icon"
        />
        {{ item.title }}
      </VTab>
    </VTabs>

    <VWindow
      v-model="activeTab"
      class="mt-6 disable-tab-transition"
      :touch="false"
    >
      <VWindowItem value="account">
        <VCard>
          <VCardText>Account settings coming soon.</VCardText>
        </VCard>
      </VWindowItem>
      <VWindowItem value="security">
        <VCard>
          <VCardText>Security settings coming soon.</VCardText>
        </VCard>
      </VWindowItem>
    </VWindow>
  </div>
</template>
