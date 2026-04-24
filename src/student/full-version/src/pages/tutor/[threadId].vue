<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import TutorMessageBubble from '@/components/tutor/TutorMessageBubble.vue'
import TutorComposeForm from '@/components/tutor/TutorComposeForm.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import type {
  SendMessageResponse,
  TutorMessageDto,
  TutorMessageListDto,
} from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.tutorThread',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const route = useRoute()

const threadId = computed(() => String(route.params.threadId))

const messagesQuery = useApiQuery<TutorMessageListDto>(`/api/tutor/threads/${threadId.value}/messages`)

const sendMutation = useApiMutation<SendMessageResponse, { content: string }>(
  `/api/tutor/threads/${threadId.value}/messages`,
  'POST',
)

// Local message buffer — seeded from the query, then appended to
// optimistically so the user sees their message instantly.
const localMessages = ref<TutorMessageDto[]>([])

watch(
  () => messagesQuery.data.value,
  next => {
    if (next)
      localMessages.value = [...next.messages]
  },
  { immediate: true },
)

const scrollContainer = ref<HTMLElement | null>(null)

async function scrollToBottom() {
  await nextTick()

  const el = scrollContainer.value
  if (el)
    el.scrollTop = el.scrollHeight
}

watch(localMessages, () => {
  scrollToBottom()
}, { deep: true })

async function handleSubmit(content: string) {
  // Optimistic user message
  const optimistic: TutorMessageDto = {
    messageId: `local-${Date.now()}`,
    role: 'user',
    content,
    createdAt: new Date().toISOString(),
    model: null,
  }

  localMessages.value.push(optimistic)

  try {
    const reply = await sendMutation.execute({ content })

    localMessages.value.push({
      messageId: reply.messageId,
      role: reply.role,
      content: reply.content,
      createdAt: reply.createdAt,
      model: null,
    })
  }
  catch {
    // On failure, rollback the optimistic append. Error surfaced by the form.
    localMessages.value = localMessages.value.filter(m => m.messageId !== optimistic.messageId)
  }
}
</script>

<template>
  <div
    class="tutor-thread-page"
    data-testid="tutor-thread-page"
  >
    <div
      ref="scrollContainer"
      class="tutor-thread-page__messages pa-4"
      data-testid="tutor-messages"
    >
      <div
        v-if="messagesQuery.loading.value && localMessages.length === 0"
        class="d-flex justify-center py-12"
        data-testid="tutor-messages-loading"
      >
        <VProgressCircular indeterminate />
      </div>

      <VAlert
        v-else-if="messagesQuery.error.value"
        type="error"
        variant="tonal"
        data-testid="tutor-messages-error"
      >
        {{ t(messagesQuery.error.value.i18nKey ?? 'tutor.threadsUnavailable') }}
      </VAlert>

      <div
        v-else-if="localMessages.length === 0"
        class="text-center py-12 text-medium-emphasis"
        data-testid="tutor-messages-empty"
      >
        <VIcon
          icon="tabler-sparkles"
          size="40"
          class="mb-3"
          aria-hidden="true"
        />
        <div>{{ t('tutor.thread.emptyTitle') }}</div>
        <div class="text-caption mt-1">
          {{ t('tutor.thread.emptySubtitle') }}
        </div>
      </div>

      <template v-else>
        <TutorMessageBubble
          v-for="message in localMessages"
          :key="message.messageId"
          :message="message"
        />

        <div
          v-if="sendMutation.loading.value"
          class="d-flex align-center text-caption text-medium-emphasis mt-2"
          data-testid="tutor-thinking"
        >
          <VProgressCircular
            indeterminate
            size="14"
            width="2"
            class="me-2"
          />
          {{ t('tutor.thread.thinking') }}
        </div>
      </template>
    </div>

    <div class="tutor-thread-page__compose pa-4">
      <VAlert
        v-if="sendMutation.error.value"
        type="error"
        variant="tonal"
        class="mb-3"
        data-testid="tutor-compose-error"
      >
        {{ t(sendMutation.error.value.i18nKey ?? 'common.errorGeneric') }}
      </VAlert>
      <TutorComposeForm
        :loading="sendMutation.loading.value"
        @submit="handleSubmit"
      />
      <!-- FIND-privacy-008: DPA disclosure for AI processing -->
      <p
        class="text-caption text-medium-emphasis mt-2 text-center"
        data-testid="tutor-dpa-disclosure"
      >
        {{ t('tutor.thread.dpaDisclosure') }}
      </p>
    </div>
  </div>
</template>

<style scoped>
.tutor-thread-page {
  display: flex;
  flex-direction: column;
  block-size: calc(100dvh - 120px);
  max-inline-size: 900px;
  margin-inline: auto;
}

.tutor-thread-page__messages {
  flex: 1;
  overflow-y: auto;
}

.tutor-thread-page__compose {
  border-block-start: 1px solid rgb(var(--v-theme-on-surface) / 0.1);
  background-color: rgb(var(--v-theme-surface));
}
</style>
