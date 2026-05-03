<script setup lang="ts">
// prr-207 — SidekickMessageStream.vue
// Renders the streamed tutor conversation bubbles. Each bubble is a
// session-scoped turn — NEVER persisted per ADR-0003.
// aria-live="polite" on the stream region; math in <bdi dir="ltr">.
// When a bubble is leak-redacted the content is blanked and a neutral
// placeholder renders in its place.

import { useI18n } from 'vue-i18n'
import type { SidekickMessage } from '@/composables/useSidekick'

interface Props {
  messages: SidekickMessage[]
  loading?: boolean
}

withDefaults(defineProps<Props>(), {
  loading: false,
})

const { t } = useI18n()
</script>

<template>
  <div
    class="sidekick-stream"
    role="log"
    aria-live="polite"
    :aria-label="t('sidekick.stream.aria')"
    data-testid="sidekick-stream"
  >
    <div
      v-for="msg in messages"
      :key="msg.id"
      class="sidekick-stream__bubble"
      :class="{
        'sidekick-stream__bubble--user': msg.role === 'user',
        'sidekick-stream__bubble--assistant': msg.role === 'assistant',
        'sidekick-stream__bubble--redacted': msg.leakRedacted,
      }"
      :data-testid="`sidekick-bubble-${msg.id}`"
    >
      <span
        v-if="msg.leakRedacted"
        class="sidekick-stream__redacted-note"
      >
        {{ t('sidekick.stream.leakRedacted') }}
      </span>
      <bdi
        v-else
        dir="ltr"
        class="sidekick-stream__content"
      >
        {{ msg.content }}
      </bdi>

      <span
        v-if="msg.streaming"
        class="sidekick-stream__cursor"
        aria-hidden="true"
      >
        …
      </span>
    </div>

    <div
      v-if="loading && messages.length === 0"
      class="sidekick-stream__placeholder"
      data-testid="sidekick-stream-loading"
    >
      {{ t('sidekick.stream.thinking') }}
    </div>
  </div>
</template>

<style scoped>
.sidekick-stream {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding-block: 0.5rem;
  min-block-size: 10rem;
}

.sidekick-stream__bubble {
  padding: 0.75rem 1rem;
  border-radius: 0.75rem;
  max-inline-size: 90%;
  line-height: 1.45;
  font-size: 0.95rem;
}

.sidekick-stream__bubble--user {
  align-self: flex-end;
  background: rgba(var(--v-theme-primary), 0.1);
}

.sidekick-stream__bubble--assistant {
  align-self: flex-start;
  background: rgba(var(--v-theme-on-surface), 0.05);
}

.sidekick-stream__bubble--redacted {
  background: rgba(var(--v-theme-on-surface), 0.08);
  font-style: italic;
}

.sidekick-stream__cursor {
  margin-inline-start: 0.25rem;
  opacity: 0.6;
}

.sidekick-stream__placeholder {
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 0.9rem;
  padding: 0.75rem 1rem;
}
</style>
