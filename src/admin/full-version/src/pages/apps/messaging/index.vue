<script setup lang="ts">
import { useMessagingStore, type MessagingThread } from '@/stores/useMessagingStore'

definePage({ meta: { action: 'read', subject: 'Messaging' } })

const store = useMessagingStore()
const router = useRouter()

const searchQuery = ref('')
const typeFilter = ref<string | null>(null)
const showNewThreadDialog = ref(false)
const newThreadType = ref('DirectMessage')
const newThreadParticipants = ref<string[]>([])
const newThreadMessage = ref('')
const contactSearch = ref('')
let contactSearchTimeout: ReturnType<typeof setTimeout> | null = null

const onContactSearch = (val: string) => {
  if (contactSearchTimeout) clearTimeout(contactSearchTimeout)
  contactSearchTimeout = setTimeout(() => {
    store.fetchContacts(val || undefined)
  }, 300)
}

const roleColor = (role: string): string => {
  switch (role) {
    case 'STUDENT': return 'info'
    case 'TEACHER': return 'success'
    case 'PARENT': return 'warning'
    case 'ADMIN': return 'error'
    case 'SUPER_ADMIN': return 'error'
    case 'MODERATOR': return 'primary'
    default: return 'default'
  }
}

const typeOptions = [
  { title: 'All', value: null },
  { title: 'Direct Message', value: 'DirectMessage' },
  { title: 'Class Broadcast', value: 'ClassBroadcast' },
  { title: 'Parent Thread', value: 'ParentThread' },
]

const threadTypeIcon = (type: string): string => {
  switch (type) {
    case 'DirectMessage': return 'tabler-message'
    case 'ClassBroadcast': return 'tabler-speakerphone'
    case 'ParentThread': return 'tabler-users'
    default: return 'tabler-message'
  }
}

const threadTypeColor = (type: string): string => {
  switch (type) {
    case 'DirectMessage': return 'primary'
    case 'ClassBroadcast': return 'info'
    case 'ParentThread': return 'warning'
    default: return 'default'
  }
}

const formatTime = (iso: string): string => {
  const d = new Date(iso)
  const now = new Date()
  const diff = now.getTime() - d.getTime()
  const mins = Math.floor(diff / 60000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)

  return `${days}d ago`
}

const fetchThreads = async () => {
  await store.fetchThreads({
    type: typeFilter.value ?? undefined,
    search: searchQuery.value || undefined,
  })
}

const openThread = (thread: MessagingThread) => {
  router.push(`/apps/messaging/${thread.threadId}`)
}

const openNewThread = async () => {
  await store.fetchContacts(contactSearch.value || undefined)
  showNewThreadDialog.value = true
}

const createThread = async () => {
  if (newThreadParticipants.value.length === 0) return

  const threadId = await store.createThread(
    newThreadType.value,
    newThreadParticipants.value,
    newThreadMessage.value || undefined,
  )

  showNewThreadDialog.value = false
  newThreadParticipants.value = []
  newThreadMessage.value = ''

  if (threadId)
    router.push(`/apps/messaging/${threadId}`)
}

watch([searchQuery, typeFilter], fetchThreads, { immediate: false })

onMounted(async () => {
  await fetchThreads()
  store.startPolling()
})

onUnmounted(() => {
  store.stopPolling()
})
</script>

<template>
  <div>
    <VCard>
      <VCardTitle class="d-flex align-center pa-4">
        <VIcon icon="tabler-messages" class="me-2" />
        Messaging
        <VSpacer />
        <VBtn
          color="primary"
          prepend-icon="tabler-plus"
          @click="openNewThread"
        >
          New Thread
        </VBtn>
      </VCardTitle>

      <VCardText>
        <VRow class="mb-4">
          <VCol cols="12" md="6">
            <VTextField
              v-model="searchQuery"
              placeholder="Search threads..."
              prepend-inner-icon="tabler-search"
              density="compact"
              clearable
              @update:model-value="fetchThreads"
            />
          </VCol>
          <VCol cols="12" md="3">
            <VSelect
              v-model="typeFilter"
              :items="typeOptions"
              label="Thread Type"
              density="compact"
              @update:model-value="fetchThreads"
            />
          </VCol>
          <VCol cols="12" md="3" class="d-flex align-center justify-end">
            <VChip color="secondary" variant="tonal" size="small">
              {{ store.totalThreads }} threads
            </VChip>
          </VCol>
        </VRow>

        <VProgressLinear v-if="store.loading" indeterminate color="primary" />

        <VAlert v-if="store.error" type="error" class="mb-4" closable>
          {{ store.error }}
        </VAlert>

        <VList v-if="store.sortedThreads.length" lines="two">
          <VListItem
            v-for="thread in store.sortedThreads"
            :key="thread.threadId"
            @click="openThread(thread)"
          >
            <template #prepend>
              <VAvatar :color="threadTypeColor(thread.threadType)" variant="tonal">
                <VIcon :icon="threadTypeIcon(thread.threadType)" />
              </VAvatar>
            </template>

            <VListItemTitle>
              {{ thread.participantNames.join(', ') }}
            </VListItemTitle>
            <VListItemSubtitle>
              {{ thread.lastMessagePreview || 'No messages yet' }}
            </VListItemSubtitle>

            <template #append>
              <div class="d-flex flex-column align-end">
                <span class="text-caption text-medium-emphasis">
                  {{ formatTime(thread.lastMessageAt) }}
                </span>
                <VChip
                  :color="threadTypeColor(thread.threadType)"
                  variant="tonal"
                  size="x-small"
                  class="mt-1"
                >
                  {{ thread.threadType }}
                </VChip>
                <VBadge
                  v-if="thread.messageCount > 0"
                  :content="thread.messageCount"
                  color="primary"
                  inline
                  class="mt-1"
                />
              </div>
            </template>
          </VListItem>
        </VList>

        <VEmptyState
          v-else-if="!store.loading"
          icon="tabler-message-off"
          title="No threads found"
          description="Create a new thread to start messaging."
        />
      </VCardText>
    </VCard>

    <!-- New Thread Dialog -->
    <VDialog v-model="showNewThreadDialog" max-width="500">
      <VCard title="New Thread">
        <VCardText>
          <VSelect
            v-model="newThreadType"
            :items="typeOptions.filter(o => o.value)"
            label="Thread Type"
            class="mb-4"
          />
          <VAutocomplete
            v-model="newThreadParticipants"
            v-model:search="contactSearch"
            :items="store.contacts"
            item-title="displayName"
            item-value="userId"
            label="Participants"
            placeholder="Search by name or email..."
            multiple
            chips
            closable-chips
            :loading="store.loading"
            no-data-text="Type to search for students, teachers, or parents"
            class="mb-4"
            @update:search="onContactSearch"
          >
            <template #item="{ props: itemProps, item }">
              <VListItem v-bind="itemProps">
                <template #append>
                  <VChip size="x-small" variant="tonal" :color="roleColor(item.raw.role)">
                    {{ item.raw.role }}
                  </VChip>
                </template>
              </VListItem>
            </template>
          </VAutocomplete>
          <VTextarea
            v-model="newThreadMessage"
            label="Initial Message (optional)"
            rows="3"
          />
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn variant="text" @click="showNewThreadDialog = false">
            Cancel
          </VBtn>
          <VBtn
            color="primary"
            :disabled="newThreadParticipants.length === 0"
            @click="createThread"
          >
            Create
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </div>
</template>
