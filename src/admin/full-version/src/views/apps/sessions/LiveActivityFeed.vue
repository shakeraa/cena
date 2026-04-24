<!-- =============================================================================
  Cena Platform -- Live Activity Feed
  ADM-026.2: Scrolling chronological feed of SSE events across all students
============================================================================= -->

<script setup lang="ts">
import type { LiveEvent } from '@/composables/useLiveStream'

const props = defineProps<{
  events: LiveEvent[]
}>()

// Derive a human-readable sentence for each event type
function formatEntry(ev: LiveEvent): string {
  const name    = (ev.payload as Record<string, unknown>)?.studentName as string || ev.studentId
  const concept = (ev.payload as Record<string, unknown>)?.conceptId as string || 'concept'
  const correct = (ev.payload as Record<string, unknown>)?.isCorrect

  switch (ev.event) {
    case 'question.attempted':
      return correct
        ? `${name} answered ${concept} correctly`
        : `${name} answered ${concept} incorrectly`
    case 'mastery.updated':
      return `${name} mastered ${concept}`
    case 'session.started':
      return `${name} started a session`
    case 'session.ended':
      return `${name} ended their session`
    case 'tutoring.started':
      return `${name} started tutoring on ${concept}`
    case 'tutoring.message':
      return `${name} received a tutoring message`
    case 'tutoring.ended':
      return `${name} finished tutoring on ${concept}`
    case 'stagnation.detected':
      return `${name} — stagnation detected on ${concept}`
    case 'methodology.switched':
      return `${name} — methodology switched for ${concept}`
    default:
      return `${name} — ${ev.event}`
  }
}

function eventIcon(eventType: string): string {
  switch (eventType) {
    case 'question.attempted': return 'tabler-question-mark'
    case 'mastery.updated':    return 'tabler-award'
    case 'session.started':    return 'tabler-player-play'
    case 'session.ended':      return 'tabler-player-stop'
    case 'tutoring.started':   return 'tabler-messages'
    case 'tutoring.message':   return 'tabler-message'
    case 'tutoring.ended':     return 'tabler-messages-off'
    case 'stagnation.detected': return 'tabler-alert-triangle'
    case 'methodology.switched': return 'tabler-switch-horizontal'
    default:                   return 'tabler-activity'
  }
}

function eventColor(eventType: string): string {
  switch (eventType) {
    case 'question.attempted':
      return 'primary'
    case 'mastery.updated':
      return 'success'
    case 'session.started':
      return 'info'
    case 'session.ended':
      return 'secondary'
    case 'tutoring.started':
    case 'tutoring.message':
    case 'tutoring.ended':
      return 'info'
    case 'stagnation.detected':
      return 'error'
    case 'methodology.switched':
      return 'warning'
    default:
      return 'default'
  }
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('en-US', {
    hour:   '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

// Show most-recent first
const reversed = computed(() => [...props.events].reverse())
</script>

<template>
  <div
    class="live-activity-feed"
    style="max-block-size: 420px; overflow-y: auto;"
  >
    <div
      v-if="!reversed.length"
      class="text-center py-8 text-disabled"
    >
      <VIcon
        icon="tabler-activity"
        size="32"
        class="mb-2 d-block mx-auto"
      />
      Waiting for events...
    </div>

    <VList
      v-else
      density="compact"
      lines="one"
    >
      <VListItem
        v-for="ev in reversed"
        :key="ev.id"
        :prepend-icon="eventIcon(ev.event)"
      >
        <template #prepend>
          <VAvatar
            :color="eventColor(ev.event)"
            variant="tonal"
            size="28"
            class="me-3"
          >
            <VIcon
              :icon="eventIcon(ev.event)"
              size="14"
            />
          </VAvatar>
        </template>

        <VListItemTitle class="text-body-2">
          {{ formatEntry(ev) }}
        </VListItemTitle>

        <template #append>
          <span class="text-caption text-medium-emphasis">
            {{ formatTime(ev.timestamp) }}
          </span>
        </template>
      </VListItem>
    </VList>
  </div>
</template>
