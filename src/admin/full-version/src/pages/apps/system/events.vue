<script setup lang="ts">
import EventCard from '@/views/apps/system/events/EventCard.vue'
import { useAdminLiveStream } from '@/composables/useAdminLiveStream'

definePage({
  meta: {
    action: 'read',
    subject: 'System',
  },
})

// RDY-060 Phase 5e: live-tail via admin SignalR. Initial /admin/events/recent
// hydrates the list; stream envelopes prepend rows as they arrive. The 5s
// poll is replaced by a 30s safety poll for degraded-mode consistency.
const SAFETY_POLL_INTERVAL_MS = 30_000
// Cap in-memory to prevent unbounded growth on a busy prod stream.
const MAX_VISIBLE_EVENTS = 500

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

// ── RDY-060 Phase 5e: SignalR live-tail ─────────────────────────────
const stream = useAdminLiveStream()
const streamStatus = stream.status
let unsubscribeStream: (() => void) | null = null

function streamEnvelopeToEvent(env: {
  subject: string
  payloadJson: string
  serverTimestamp: string
}): EventData | null {
  if (!env.subject) return null
  // Last segment of the NATS subject is the event type.
  // cena.events.student.stu-1.answer_evaluated_v1 → answer_evaluated_v1
  const parts = env.subject.split('.')
  const type = parts[parts.length - 1] || env.subject
  const summary = env.subject
  let payload: Record<string, unknown> | undefined
  try {
    payload = env.payloadJson ? JSON.parse(env.payloadJson) : undefined
  }
  catch {
    payload = { raw: env.payloadJson }
  }
  return {
    // Unique per envelope — subject + timestamp + random tail guards
    // against rapid identical bursts (tick collision).
    id: `${env.subject}-${env.serverTimestamp}-${Math.random().toString(36).slice(2, 8)}`,
    timestamp: env.serverTimestamp,
    type,
    summary,
    payload,
  }
}

function onStreamEnvelope(env: { subject: string; payloadJson: string; serverTimestamp: string }) {
  if (isPaused.value) return
  // Apply the current filter at ingress so paused-state filtering
  // matches post-unpause exactly.
  if (filterType.value && !env.subject.includes(filterType.value)) return

  const mapped = streamEnvelopeToEvent(env)
  if (!mapped) return

  // Prepend; cap the tail so long sessions don't leak memory.
  const next = [mapped, ...events.value]
  events.value = next.length > MAX_VISIBLE_EVENTS ? next.slice(0, MAX_VISIBLE_EVENTS) : next
}

onMounted(async () => {
  fetchEvents()
  // 30-second safety poll — reconciles any events the stream missed
  // (reconnect gap, message loss).
  pollInterval = setInterval(fetchEvents, SAFETY_POLL_INTERVAL_MS)
  try {
    await stream.connect()
    await stream.join('system')
    unsubscribeStream = stream.on(onStreamEnvelope)
  }
  catch (err) {
    console.warn('[event-stream] admin-hub unavailable; 30s poll covers it:', err)
  }
})

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval)
  unsubscribeStream?.()
  void stream.leave('system')
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
          :color="streamStatus === 'connected' ? 'success' : 'warning'"
          variant="tonal"
          size="small"
          :prepend-icon="streamStatus === 'connected' ? 'tabler-bolt' : 'tabler-bolt-off'"
        >
          {{ streamStatus === 'connected' ? 'Live' : streamStatus }}
        </VChip>
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
