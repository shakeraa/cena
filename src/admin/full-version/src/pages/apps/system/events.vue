<script setup lang="ts">
import EventCard from '@/views/apps/system/events/EventCard.vue'

definePage({
  meta: {
    action: 'read',
    subject: 'System',
  },
})

interface EventData {
  id: string
  timestamp: string
  type: string
  summary: string
  payload?: Record<string, unknown>
}

const events = ref<EventData[]>([])
const isLoading = ref(true)
const isPaused = ref(false)
const expandedEventId = ref<string | null>(null)
const filterType = ref<string | null>(null)
const eventsPerSecond = ref(0)

const eventTypes = [
  'ConceptAttempted', 'ConceptMastered', 'StagnationDetected',
  'MethodologySwitched', 'SessionStarted', 'SessionEnded',
  'FocusAlert', 'MicrobreakTaken',
  'TutoringSessionStarted', 'TutoringMessageSent', 'TutoringSessionEnded',
  'ExplanationGenerated', 'ErrorClassified', 'HintDelivered',
  'Embedding', 'Experiment', 'Confusion',
]

const fetchEvents = async () => {
  try {
    const params = new URLSearchParams()
    if (filterType.value) params.set('type', filterType.value)
    const response = await $api<any>(`/admin/events/recent?${params.toString()}`)
    const rawEvents = response?.events ?? response ?? []
    const newEvents: EventData[] = (Array.isArray(rawEvents) ? rawEvents : []).map((e: any) => ({
      id: e.id ?? e.Id ?? '',
      timestamp: e.timestamp ?? e.Timestamp ?? '',
      type: e.type ?? e.eventType ?? e.EventType ?? '',
      summary: e.summary ?? `${e.eventType ?? e.EventType ?? 'Event'} on ${e.aggregateType ?? e.AggregateType ?? 'unknown'}`,
      payload: e.payload ?? (e.payloadJson || e.PayloadJson ? JSON.parse(e.payloadJson ?? e.PayloadJson ?? '{}') : undefined),
    })).filter((e: EventData) => e.id)
    if (!isPaused.value) {
      events.value = newEvents
      eventsPerSecond.value = Math.round(newEvents.length / 30)
    }
  }
  catch (e) {
    console.error('Failed to fetch events:', e)
  }
  finally {
    isLoading.value = false
  }
}

const toggleExpand = (id: string) => {
  expandedEventId.value = expandedEventId.value === id ? null : id
}

const filteredEvents = computed(() => {
  if (!filterType.value) return events.value
  return events.value.filter(e => e.type === filterType.value)
})

let pollInterval: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  fetchEvents()
  pollInterval = setInterval(fetchEvents, 5000)
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
})

watch(filterType, () => {
  isLoading.value = true
  fetchEvents()
})
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h4 class="text-h4">
          Event Stream
        </h4>
        <p class="text-body-1 mb-0">
          Real-time domain events from the Cena platform
        </p>
      </div>
      <div class="d-flex align-center gap-3">
        <VChip
          color="info"
          variant="tonal"
          size="small"
        >
          ~{{ eventsPerSecond }} events/sec
        </VChip>
        <VBtn
          :color="isPaused ? 'success' : 'warning'"
          variant="tonal"
          size="small"
          :prepend-icon="isPaused ? 'tabler-player-play' : 'tabler-player-pause'"
          @click="isPaused = !isPaused"
        >
          {{ isPaused ? 'Resume' : 'Pause' }}
        </VBtn>
      </div>
    </div>

    <VCard>
      <VCardText class="d-flex align-center gap-4 pb-0">
        <AppSelect
          v-model="filterType"
          :items="eventTypes"
          placeholder="Filter by event type"
          clearable
          clear-icon="tabler-x"
          style="max-inline-size: 250px;"
        />
      </VCardText>

      <VCardText>
        <VProgressLinear
          v-if="isLoading"
          indeterminate
        />

        <div
          v-else-if="filteredEvents.length === 0"
          class="text-center py-8 text-disabled"
        >
          <VIcon
            icon="tabler-activity"
            size="48"
            class="mb-2"
          />
          <p>No events to display</p>
        </div>

        <div
          v-else
          style="max-height: 600px; overflow-y: auto;"
        >
          <EventCard
            v-for="event in filteredEvents"
            :key="event.id"
            :event="event"
            :is-expanded="expandedEventId === event.id"
            @toggle="toggleExpand(event.id)"
          />
        </div>
      </VCardText>
    </VCard>
  </div>
</template>
