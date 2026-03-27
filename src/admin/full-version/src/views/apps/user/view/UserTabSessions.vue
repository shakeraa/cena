<script setup lang="ts">
interface Props {
  userId: string
}

const props = defineProps<Props>()

interface SessionItem {
  id: string
  device: string
  browser: string
  ip: string
  location: string
  lastActive: string
  status: 'active' | 'expired'
}

const sessions = ref<SessionItem[]>([])
const isLoading = ref(true)
const loadError = ref('')

const headers = [
  { title: 'Device', key: 'device' },
  { title: 'Browser', key: 'browser' },
  { title: 'IP', key: 'ip' },
  { title: 'Location', key: 'location' },
  { title: 'Last Active', key: 'lastActive' },
  { title: 'Status', key: 'status' },
  { title: 'Actions', key: 'actions', sortable: false },
]

const fetchSessions = async () => {
  isLoading.value = true
  loadError.value = ''

  try {
    const data = await $api(`/admin/users/${props.userId}/sessions`)

    sessions.value = data.sessions ?? []
  }
  catch (e: any) {
    loadError.value = e?.data?.message ?? 'Failed to load sessions'
    console.error('Failed to fetch sessions', e)
  }
  finally {
    isLoading.value = false
  }
}

fetchSessions()

const revokeSession = async (sessionId: string) => {
  try {
    await $api(`/admin/users/${props.userId}/sessions/${sessionId}`, {
      method: 'DELETE',
    })

    sessions.value = sessions.value.filter(s => s.id !== sessionId)
  }
  catch (e) {
    console.error('Failed to revoke session', e)
  }
}

const resolveBrowserIcon = (browser: string) => {
  const b = browser.toLowerCase()

  if (b.includes('chrome')) return { icon: 'tabler-brand-chrome', color: 'info' }
  if (b.includes('firefox')) return { icon: 'tabler-brand-firefox', color: 'warning' }
  if (b.includes('safari')) return { icon: 'tabler-brand-safari', color: 'primary' }
  if (b.includes('edge')) return { icon: 'tabler-brand-edge', color: 'info' }

  return { icon: 'tabler-browser', color: 'secondary' }
}

const resolveDeviceIcon = (device: string) => {
  const d = device.toLowerCase()

  if (d.includes('iphone') || d.includes('android') || d.includes('mobile'))
    return 'tabler-device-mobile'
  if (d.includes('ipad') || d.includes('tablet'))
    return 'tabler-device-tablet'

  return 'tabler-device-desktop'
}

const formatLastActive = (dateStr: string) => {
  if (!dateStr) return '--'

  const date = new Date(dateStr)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  const diffHours = Math.floor(diffMs / 3600000)

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins} min ago`
  if (diffHours < 24) return `${diffHours} hours ago`

  return date.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard title="Active Sessions">
        <VCardText>
          <div
            v-if="isLoading"
            class="d-flex justify-center pa-8"
          >
            <VProgressCircular indeterminate />
          </div>

          <VAlert
            v-else-if="loadError"
            type="error"
            variant="tonal"
            :text="loadError"
          />

          <div
            v-else-if="sessions.length === 0"
            class="text-center pa-8 text-medium-emphasis"
          >
            No active sessions found for this user.
          </div>

          <template v-else>
            <VDataTable
              :items="sessions"
              :headers="headers"
              hide-default-footer
              class="text-no-wrap"
            >
              <template #item.device="{ item }">
                <div class="d-flex align-center gap-x-3">
                  <VIcon
                    :icon="resolveDeviceIcon(item.device)"
                    :size="22"
                  />
                  <span class="text-body-1 text-high-emphasis">
                    {{ item.device }}
                  </span>
                </div>
              </template>

              <template #item.browser="{ item }">
                <div class="d-flex align-center gap-x-3">
                  <VIcon
                    :icon="resolveBrowserIcon(item.browser).icon"
                    :color="resolveBrowserIcon(item.browser).color"
                    :size="22"
                  />
                  <span class="text-body-1 text-high-emphasis">
                    {{ item.browser }}
                  </span>
                </div>
              </template>

              <template #item.ip="{ item }">
                <code class="text-body-2">{{ item.ip }}</code>
              </template>

              <template #item.lastActive="{ item }">
                <span class="text-body-1">
                  {{ formatLastActive(item.lastActive) }}
                </span>
              </template>

              <template #item.status="{ item }">
                <VChip
                  :color="item.status === 'active' ? 'success' : 'secondary'"
                  size="small"
                  label
                  class="text-capitalize"
                >
                  {{ item.status }}
                </VChip>
              </template>

              <template #item.actions="{ item }">
                <VBtn
                  v-if="item.status === 'active'"
                  size="small"
                  variant="tonal"
                  color="error"
                  @click="revokeSession(item.id)"
                >
                  Revoke
                </VBtn>
              </template>

              <template #bottom />
            </VDataTable>
          </template>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>
