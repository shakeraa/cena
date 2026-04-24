<script setup lang="ts">
import type { TutorMessageDto } from '@/api/types/common'

interface Props {
  message: TutorMessageDto
}

defineProps<Props>()
</script>

<template>
  <div
    class="tutor-message"
    :class="{ 'tutor-message--user': message.role === 'user', 'tutor-message--assistant': message.role === 'assistant' }"
    :data-testid="`tutor-message-${message.messageId}`"
    :data-role="message.role"
  >
    <div class="tutor-message__bubble">
      <div class="text-body-1">
        {{ message.content }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.tutor-message {
  display: flex;
  margin-block: 0.75rem;
}

.tutor-message--user {
  justify-content: flex-end;
}

.tutor-message--assistant {
  justify-content: flex-start;
}

.tutor-message__bubble {
  max-inline-size: 80%;
  padding: 0.75rem 1rem;
  border-radius: 16px;
  word-wrap: break-word;
}

.tutor-message--user .tutor-message__bubble {
  background-color: rgb(var(--v-theme-primary));
  color: rgb(var(--v-theme-on-primary));
  border-end-end-radius: 4px;
}

.tutor-message--assistant .tutor-message__bubble {
  background-color: rgb(var(--v-theme-surface-variant));
  color: rgb(var(--v-theme-on-surface-variant));
  border-end-start-radius: 4px;
}
</style>
