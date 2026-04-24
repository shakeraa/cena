<!-- =============================================================================
  Cena Platform -- Live Session Monitor
  ADM-026.2: Real-time grid of active student sessions via SSE
============================================================================= -->

<script setup lang="ts">
import { $api } from '@/utils/api'
import { useLiveStream } from '@/composables/useLiveStream'
import LiveSessionCard from '@/views/apps/sessions/LiveSessionCard.vue'
import LiveActivityFeed from '@/views/apps/sessions/LiveActivityFeed.vue'
import type { ActiveSession } from '@/views/apps/sessions/LiveSessionCard.vue'

definePage({ meta: { action: 'read', subject: 'Tutoring' } })

const router = useRouter()

// ── SSE connection ──
const { events, connected, error: streamError } = useLiveStream('/admin/live/sessions')

// ── Session map: studentId -> snapshot, updated by SSE events ──
const sessionMap = ref<Map<string, ActiveSession>>(new Map())

// ── Filters / sort ──
const searchQuery    = ref('')
const subjectFilter  = ref<string | null>(null)
const sortBy         = ref<'duration' | 'fatigueScore' | 'accuracy'>('duration')
const soundEnabled   = ref(false)

// ── Initial REST snapshot on mount ──
const loadingSnapshot = ref(true)
const snapshotError   = ref<string | null>(null)

onMounted(async () => {
  try {
    const data = await $api<{ sessions: ActiveSession[]; totalActive: number }>('/admin/live/sessions/snapshot')
    const map = new Map<string, ActiveSession>()
    for (const s of data.sessions ?? [])
      map.set(s.studentId, s)
    sessionMap.value = map
  }
  catch (err: unknown) {
    snapshotError.value = err instanceof Error ? err.message : 'Failed to load active sessions'
  }
  finally {
    loadingSnapshot.value = false
  }
})

// ── Apply SSE events to session map ──
watch(events, (newEvents, oldEvents) => {
  const delta = newEvents.slice(oldEvents?.length ?? 0)

  for (const ev of delta) {
    applyEvent(ev)
  }
}, { deep: false })

function applyEvent(ev: { event: string; studentId: string; payload: Record<string, unknown> }) {
  const map = new Map(sessionMap.value)

  if (ev.event === 'session.snapshot' || ev.event === 'session.started') {
    const p = ev.payload as Record<string, unknown>
    map.set(ev.studentId, {
      sessionId:       String(p.sessionId ?? ''),
      studentId:       ev.studentId,
      studentName:     String(p.studentName ?? ev.studentId),
      subject:         String(p.subject ?? '-'),
      conceptId:       String(p.conceptId ?? '-'),
      methodology:     String(p.methodology ?? '-'),
      questionCount:   Number(p.questionCount ?? 0),
      correctCount:    Number(p.correctCount ?? 0),
      fatigueScore:    Number(p.fatigueScore ?? 0),
      durationSeconds: Number(p.durationSeconds ?? 0),
      startedAt:       String(p.startedAt ?? new Date().toISOString()),
    })
  }
  else if (ev.event === 'session.ended') {
    map.delete(ev.studentId)
  }
  else if (ev.event === 'question.attempted') {
    const existing = map.get(ev.studentId)
    if (existing) {
      const p       = ev.payload as Record<string, unknown>
      const correct = Boolean(p.isCorrect)
      map.set(ev.studentId, {
        ...existing,
        questionCount:   existing.questionCount + 1,
        correctCount:    existing.correctCount + (correct ? 1 : 0),
        fatigueScore:    Number(p.fatigueScore ?? existing.fatigueScore),
        durationSeconds: Math.round((Date.now() - new Date(existing.startedAt).getTime()) / 1000),
      })
    }
  }
  else if (ev.event === 'methodology.switched') {
    const existing = map.get(ev.studentId)
    if (existing) {
      const p = ev.payload as Record<string, unknown>
      map.set(ev.studentId, { ...existing, methodology: String(p.newMethodology ?? existing.methodology) })
    }
  }
  else if (ev.event === 'stagnation.detected' && soundEnabled.value) {
    playAlertSound()
  }

  sessionMap.value = map
}

function playAlertSound() {
  try {
    const ctx  = new AudioContext()
    const osc  = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.connect(gain)
    gain.connect(ctx.destination)
    osc.frequency.value = 440
    gain.gain.setValueAtTime(0.3, ctx.currentTime)
    gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.4)
    osc.start(ctx.currentTime)
    osc.stop(ctx.currentTime + 0.4)
  }
  catch {
    // AudioContext not available
  }
}

// ── Derived data ──
const allSubjects = computed<string[]>(() => {
  const subjects = new Set<string>()
  for (const s of sessionMap.value.values())
    if (s.subject && s.subject !== '-') subjects.add(s.subject)
  return [...subjects].sort()
})

const filteredSessions = computed<ActiveSession[]>(() => {
  let list = [...sessionMap.value.values()]

  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase()
    list = list.filter(s => s.studentName.toLowerCase().includes(q) || s.studentId.toLowerCase().includes(q))
  }

  if (subjectFilter.value)
    list = list.filter(s => s.subject === subjectFilter.value)

  list.sort((a, b) => {
    switch (sortBy.value) {
      case 'fatigueScore': return b.fatigueScore - a.fatigueScore
      case 'accuracy':
        return (b.questionCount ? b.correctCount / b.questionCount : 0)
          - (a.questionCount ? a.correctCount / a.questionCount : 0)
      default: return b.durationSeconds - a.durationSeconds
    }
  })

  return list
})

// ── Summary bar metrics ──
const activeCount = computed(() => sessionMap.value.size)

const avgFatigue = computed<number>(() => {
  const vals = [...sessionMap.value.values()].map(s => s.fatigueScore)
  return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0
})

const highFatigueCount = computed(() =>
  [...sessionMap.value.values()].filter(s => s.fatigueScore > 0.7).length,
)

const stagnationCount = computed(() =>
  events.value.filter(e => e.event === 'stagnation.detected').length,
)

// ── Navigation ──
function openSession(sessionId: string) {
  router.push({ name: 'apps-tutoring-sessions-id', params: { id: sessionId } })
}
</script>

<template>
  <div>
    <!-- Header -->
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Live Session Monitor
        </h4>
        <div class="text-body-1">
          Real-time view of active student sessions
        </div>
      </div>

      <div class="d-flex align-center gap-3">
        <!-- SSE connection badge -->
        <VChip
          :color="connected ? 'success' : 'warning'"
          :prepend-icon="connected ? 'tabler-wifi' : 'tabler-wifi-off'"
          size="small"
          label
        >
          {{ connected ? 'Live' : 'Connecting...' }}
        </VChip>

        <!-- Sound toggle -->
        <VBtn
          :color="soundEnabled ? 'primary' : 'secondary'"
          :prepend-icon="soundEnabled ? 'tabler-bell' : 'tabler-bell-off'"
          variant="tonal"
          size="small"
          @click="soundEnabled = !soundEnabled"
        >
          {{ soundEnabled ? 'Sound on' : 'Sound off' }}
        </VBtn>
      </div>
    </div>

    <!-- Stream error -->
    <VAlert
      v-if="streamError"
      type="warning"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="() => {}"
    >
      {{ streamError }}
    </VAlert>

    <!-- Snapshot error -->
    <VAlert
      v-if="snapshotError"
      type="error"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="snapshotError = null"
    >
      {{ snapshotError }}
    </VAlert>

    <!-- Summary bar -->
    <VRow class="mb-6">
      <VCol
        cols="12"
        sm="6"
        lg="3"
      >
        <VCard>
          <VCardText class="d-flex align-center gap-x-4">
            <VAvatar
              variant="tonal"
              color="info"
              rounded
              size="48"
            >
              <VIcon
                icon="tabler-users"
                size="28"
              />
            </VAvatar>
            <div>
              <h4 class="text-h4">
                {{ activeCount }}
              </h4>
              <div class="text-body-2 text-medium-emphasis">
                Active sessions
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol
        cols="12"
        sm="6"
        lg="3"
      >
        <VCard>
          <VCardText class="d-flex align-center gap-x-4">
            <VAvatar
              variant="tonal"
              color="success"
              rounded
              size="48"
            >
              <VIcon
                icon="tabler-brain"
                size="28"
              />
            </VAvatar>
            <div>
              <h4 class="text-h4">
                {{ (avgFatigue * 100).toFixed(0) }}%
              </h4>
              <div class="text-body-2 text-medium-emphasis">
                Avg fatigue
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol
        cols="12"
        sm="6"
        lg="3"
      >
        <VCard>
          <VCardText class="d-flex align-center gap-x-4">
            <VAvatar
              variant="tonal"
              color="error"
              rounded
              size="48"
            >
              <VIcon
                icon="tabler-alert-triangle"
                size="28"
              />
            </VAvatar>
            <div>
              <h4 class="text-h4">
                {{ highFatigueCount }}
              </h4>
              <div class="text-body-2 text-medium-emphasis">
                High fatigue
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol
        cols="12"
        sm="6"
        lg="3"
      >
        <VCard>
          <VCardText class="d-flex align-center gap-x-4">
            <VAvatar
              variant="tonal"
              color="warning"
              rounded
              size="48"
            >
              <VIcon
                icon="tabler-circle-dot"
                size="28"
              />
            </VAvatar>
            <div>
              <h4 class="text-h4">
                {{ stagnationCount }}
              </h4>
              <div class="text-body-2 text-medium-emphasis">
                Stagnation alerts
              </div>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Filters -->
    <VCard class="mb-6">
      <VCardText>
        <VRow>
          <VCol
            cols="12"
            md="4"
          >
            <VTextField
              v-model="searchQuery"
              label="Search by student name"
              prepend-inner-icon="tabler-search"
              clearable
              density="compact"
            />
          </VCol>
          <VCol
            cols="12"
            md="3"
          >
            <VSelect
              v-model="subjectFilter"
              :items="allSubjects"
              label="Filter by subject"
              density="compact"
              clearable
            />
          </VCol>
          <VCol
            cols="12"
            md="3"
          >
            <VSelect
              v-model="sortBy"
              :items="[
                { title: 'Duration', value: 'duration' },
                { title: 'Fatigue score', value: 'fatigueScore' },
                { title: 'Accuracy', value: 'accuracy' },
              ]"
              label="Sort by"
              density="compact"
            />
          </VCol>
        </VRow>
      </VCardText>
    </VCard>

    <!-- Main content: session grid + activity feed -->
    <VRow>
      <!-- Session grid -->
      <VCol
        cols="12"
        lg="8"
      >
        <VCard>
          <VCardItem>
            <VCardTitle>
              Active Sessions
              <VBadge
                :content="activeCount"
                color="info"
                inline
                class="ms-2"
              />
            </VCardTitle>
          </VCardItem>

          <VCardText>
            <VProgressLinear
              v-if="loadingSnapshot"
              indeterminate
              class="mb-4"
            />

            <div
              v-else-if="!filteredSessions.length"
              class="text-center py-8 text-disabled"
            >
              <VIcon
                icon="tabler-users"
                size="48"
                class="mb-2 d-block mx-auto"
              />
              No active sessions
            </div>

            <VRow
              v-else
              class="match-height"
            >
              <VCol
                v-for="session in filteredSessions"
                :key="session.studentId"
                cols="12"
                sm="6"
                xl="4"
              >
                <LiveSessionCard
                  :session="session"
                  @click="openSession"
                />
              </VCol>
            </VRow>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Activity feed -->
      <VCol
        cols="12"
        lg="4"
      >
        <VCard>
          <VCardItem title="Activity Feed" />
          <VCardText>
            <LiveActivityFeed :events="events" />
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
