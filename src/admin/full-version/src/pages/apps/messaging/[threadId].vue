<script setup lang="ts">
import { useMessagingStore, type MessagingMessage } from '@/stores/useMessagingStore'

definePage({ meta: { action: 'read', subject: 'Messaging' } })

const route = useRoute()
const router = useRouter()
const store = useMessagingStore()

const threadId = computed(() => (route.params as Record<string, string>).threadId)
const messageInput = ref('')
const messagesContainer = ref<HTMLElement>()

const roleColor = (role: string): string => {
  switch (role) {
    case 'Teacher': return 'primary'
    case 'Parent': return 'warning'
    case 'Student': return 'info'
    case 'System': return 'secondary'
    default: return 'default'
  }
}

const formatTime = (iso: string): string => {
  const d = new Date(iso)

  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

const formatDate = (iso: string): string => {
  const d = new Date(iso)

  return d.toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' })
}

const isOwnMessage = (_msg: MessagingMessage): boolean => {
  // In admin view, messages from Teacher role are "ours"
  return _msg.senderRole === 'Teacher'
}

const sendMessage = async () => {
  const text = messageInput.value.trim()
  if (!text) return

  messageInput.value = ''
  await store.sendMessage(threadId.value, text)

  // Scroll to bottom
  nextTick(() => {
    if (messagesContainer.value)
      messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  })
}

const goBack = () => {
  router.push('/apps/messaging')
}

onMounted(async () => {
  await store.fetchThreadDetail(threadId.value)

  nextTick(() => {
    if (messagesContainer.value)
      messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  })
})
</script>

<template>
  <div>
    <VCard>
      <!-- Header -->
      <VCardTitle class="d-flex align-center pa-4">
        <VBtn icon variant="text" class="me-2" @click="goBack">
          <VIcon icon="tabler-arrow-left" />
        </VBtn>
        <template v-if="store.activeThread">
          <span>{{ store.activeThread.participantNames.join(', ') }}</span>
          <VChip
            :color="store.activeThread.threadType === 'DirectMessage' ? 'primary' : 'info'"
            variant="tonal"
            size="x-small"
            class="ms-2"
          >
            {{ store.activeThread.threadType }}
          </VChip>
        </template>
      </VCardTitle>

      <VDivider />

      <VProgressLinear v-if="store.loading" indeterminate color="primary" />

      <VAlert v-if="store.error" type="error" class="ma-4" closable>
        {{ store.error }}
      </VAlert>

      <!-- Messages -->
      <div
        ref="messagesContainer"
        class="pa-4"
        style="max-height: 500px; overflow-y: auto;"
      >
        <template v-if="store.activeThread?.messages.length">
          <div
            v-for="msg in store.activeThread.messages"
            :key="msg.messageId"
            :class="['d-flex mb-3', isOwnMessage(msg) ? 'justify-end' : 'justify-start']"
          >
            <div :style="{ maxWidth: '70%' }">
              <!-- Sender info -->
              <div class="d-flex align-center mb-1" :class="isOwnMessage(msg) ? 'justify-end' : ''">
                <VChip
                  :color="roleColor(msg.senderRole)"
                  variant="tonal"
                  size="x-small"
                  class="me-1"
                >
                  {{ msg.senderRole }}
                </VChip>
                <span class="text-caption text-medium-emphasis">
                  {{ msg.senderName }}
                </span>
              </div>

              <!-- Message bubble -->
              <VCard
                :color="isOwnMessage(msg) ? 'primary' : 'surface-variant'"
                :variant="isOwnMessage(msg) ? 'flat' : 'tonal'"
                class="pa-3"
                rounded="lg"
              >
                <!-- Blocked message indicator -->
                <VAlert
                  v-if="msg.wasBlocked"
                  type="warning"
                  density="compact"
                  variant="tonal"
                  class="mb-2"
                >
                  Blocked: {{ msg.blockReason }}
                </VAlert>

                <div :class="isOwnMessage(msg) ? 'text-white' : ''">
                  {{ msg.text }}
                </div>

                <div class="text-caption mt-1" :class="isOwnMessage(msg) ? 'text-white-50' : 'text-medium-emphasis'">
                  {{ formatTime(msg.sentAt) }} · {{ formatDate(msg.sentAt) }}
                  <VIcon v-if="msg.channel !== 'InApp'" :icon="`tabler-brand-${msg.channel.toLowerCase()}`" size="12" class="ms-1" />
                </div>
              </VCard>
            </div>
          </div>
        </template>

        <VEmptyState
          v-else-if="!store.loading"
          icon="tabler-message-off"
          title="No messages"
          description="Send a message to start the conversation."
        />
      </div>

      <VDivider />

      <!-- Message input -->
      <div class="pa-4 d-flex align-center gap-2">
        <VTextField
          v-model="messageInput"
          placeholder="Type a message..."
          density="compact"
          hide-details
          @keyup.enter="sendMessage"
        />
        <VBtn
          color="primary"
          icon
          :disabled="!messageInput.trim()"
          @click="sendMessage"
        >
          <VIcon icon="tabler-send" />
        </VBtn>
      </div>
    </VCard>
  </div>
</template>
