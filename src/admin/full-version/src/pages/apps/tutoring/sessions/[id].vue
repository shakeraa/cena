<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Tutoring' } })

interface ConversationTurn {
  role: string
  messagePreview: string
  timestamp: string
  ragSourceCount: number
}

interface TutoringSessionDetail {
  id: string
  sessionId: string
  studentId: string
  studentName: string
  status: string
  methodology: string
  conceptId: string
  subject: string
  durationSeconds: number
  startedAt: string
  endedAt: string | null
  turnCount: number
  tokensUsed: number
  budgetRemaining: number
  turns: ConversationTurn[]
  ragSourcesUsed: number
  safetyEventsCount: number
}

const route = useRoute()
const router = useRouter()

const loading = ref(true)
const error = ref<string | null>(null)
const session = ref<TutoringSessionDetail | null>(null)
const contextPanelOpen = ref(false)

const sessionId = computed(() => String((route.params as Record<string, string>).id ?? ''))

const statusColor = (status: string): string => {
  switch (status) {
    case 'active': return 'info'
    case 'completed': return 'success'
    case 'budget_exhausted': return 'warning'
    case 'safety_blocked': return 'error'
    case 'timeout': return 'secondary'
    default: return 'default'
  }
}

const formatDuration = (seconds: number): string => {
  if (seconds < 60) return `${seconds}s`
  const min = Math.floor(seconds / 60)
  const sec = seconds % 60

  return sec > 0 ? `${min}m ${sec}s` : `${min}m`
}

const formatDate = (iso: string): string => {
  if (!iso) return '-'

  return new Date(iso).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

const dailyTokenLimit = computed(() => {
  if (!session.value) return 0

  return session.value.tokensUsed + session.value.budgetRemaining
})

const budgetPercent = computed(() => {
  const limit = dailyTokenLimit.value
  if (!session.value || !limit) return 0

  return Math.min(100, (session.value.tokensUsed / limit) * 100)
})

const budgetColor = computed(() => {
  const pct = budgetPercent.value
  if (pct >= 90) return 'error'
  if (pct >= 70) return 'warning'

  return 'success'
})

// Turns with student messages on the left, tutor on the right
const studentTurns = computed(() =>
  session.value?.turns.filter(t => t.role === 'student') ?? [],
)

const tutorTurns = computed(() =>
  session.value?.turns.filter(t => t.role === 'tutor' || t.role === 'assistant') ?? [],
)

// RAG source turns for context panel
const turnsWithRagSources = computed(() =>
  session.value?.turns.filter(t => t.ragSourceCount > 0) ?? [],
)

const fetchSession = async () => {
  loading.value = true
  try {
    const data = await $api<TutoringSessionDetail>(`/admin/tutoring/sessions/${sessionId.value}`)

    session.value = {
      id: data.id ?? sessionId.value,
      sessionId: data.sessionId ?? sessionId.value,
      studentId: data.studentId ?? '',
      studentName: data.studentName || data.studentId || '',
      status: data.status ?? 'unknown',
      methodology: data.methodology ?? '-',
      conceptId: data.conceptId ?? '-',
      subject: data.subject ?? '-',
      durationSeconds: data.durationSeconds ?? 0,
      startedAt: data.startedAt ?? '',
      endedAt: data.endedAt ?? null,
      turnCount: data.turnCount ?? 0,
      tokensUsed: data.tokensUsed ?? 0,
      budgetRemaining: data.budgetRemaining ?? 0,
      turns: data.turns ?? [],
      ragSourcesUsed: data.ragSourcesUsed ?? 0,
      safetyEventsCount: data.safetyEventsCount ?? 0,
    }
  }
  catch (err: any) {
    console.error('Failed to fetch tutoring session:', err)
    error.value = err.message ?? 'Failed to load tutoring session'
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchSession)
</script>

<template>
  <div>
    <VBtn
      variant="text"
      prepend-icon="tabler-arrow-left"
      class="mb-4"
      @click="router.push({ name: 'apps-tutoring-sessions' })"
    >
      Back to Sessions
    </VBtn>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="error = null"
    >
      {{ error }}
    </VAlert>

    <VProgressLinear
      v-if="loading"
      indeterminate
      class="mb-6"
    />

    <template v-if="session">
      <!-- Header -->
      <VCard class="mb-6">
        <VCardText>
          <VRow>
            <VCol
              cols="12"
              md="8"
            >
              <h4 class="text-h4 mb-2">
                {{ session.studentName || session.studentId }}
              </h4>
              <div class="d-flex flex-wrap gap-x-6 gap-y-2 text-body-2">
                <div>
                  <span class="text-medium-emphasis">Student ID:</span>
                  <span class="font-weight-medium ms-1">{{ session.studentId }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Methodology:</span>
                  <span class="font-weight-medium ms-1">{{ session.methodology }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Subject:</span>
                  <span class="font-weight-medium ms-1">{{ session.subject }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Concept:</span>
                  <span class="font-weight-medium ms-1">{{ session.conceptId }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Turns:</span>
                  <span class="font-weight-medium ms-1">{{ session.turnCount }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Duration:</span>
                  <span class="font-weight-medium ms-1">{{ formatDuration(session.durationSeconds) }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Started:</span>
                  <span class="font-weight-medium ms-1">{{ formatDate(session.startedAt) }}</span>
                </div>
                <div v-if="session.endedAt">
                  <span class="text-medium-emphasis">Ended:</span>
                  <span class="font-weight-medium ms-1">{{ formatDate(session.endedAt) }}</span>
                </div>
              </div>
            </VCol>
            <VCol
              cols="12"
              md="4"
              class="d-flex flex-column align-end gap-y-2"
            >
              <VChip
                :color="statusColor(session.status)"
                label
                size="large"
              >
                {{ session.status.replace(/_/g, ' ') }}
              </VChip>

              <VChip
                v-if="session.safetyEventsCount > 0"
                color="error"
                label
                size="small"
                prepend-icon="tabler-shield-exclamation"
              >
                {{ session.safetyEventsCount }} safety event{{ session.safetyEventsCount !== 1 ? 's' : '' }}
              </VChip>

              <div
                v-if="session.ragSourcesUsed > 0"
                class="text-caption text-medium-emphasis"
              >
                {{ session.ragSourcesUsed }} RAG source{{ session.ragSourcesUsed !== 1 ? 's' : '' }} used
              </div>
            </VCol>
          </VRow>
        </VCardText>
      </VCard>

      <!-- Budget Meter -->
      <VCard class="mb-6">
        <VCardItem title="Token Budget" />
        <VCardText>
          <div class="d-flex justify-space-between text-body-2 mb-2">
            <span>{{ session.tokensUsed.toLocaleString() }} tokens used</span>
            <span>{{ dailyTokenLimit.toLocaleString() }} daily limit</span>
          </div>
          <VProgressLinear
            :model-value="budgetPercent"
            :color="budgetColor"
            height="12"
            rounded
          />
          <div class="d-flex justify-space-between text-body-2 mt-1">
            <span class="text-medium-emphasis">{{ budgetPercent.toFixed(1) }}% consumed</span>
            <span class="text-medium-emphasis">{{ session.budgetRemaining.toLocaleString() }} remaining</span>
          </div>
        </VCardText>
      </VCard>

      <!-- Context Panel (collapsible RAG blocks) -->
      <VCard
        v-if="turnsWithRagSources.length > 0"
        class="mb-6"
      >
        <VCardItem>
          <VCardTitle>RAG Context Sources</VCardTitle>
          <template #append>
            <VBtn
              :icon="contextPanelOpen ? 'tabler-chevron-up' : 'tabler-chevron-down'"
              variant="text"
              size="small"
              @click="contextPanelOpen = !contextPanelOpen"
            />
          </template>
        </VCardItem>

        <VExpandTransition>
          <div v-show="contextPanelOpen">
            <VCardText>
              <VList density="compact">
                <VListItem
                  v-for="(turn, idx) in turnsWithRagSources"
                  :key="idx"
                  :subtitle="`${turn.ragSourceCount} source${turn.ragSourceCount !== 1 ? 's' : ''} retrieved at ${formatDate(turn.timestamp)}`"
                  :title="`Turn ${idx + 1} (${turn.role})`"
                  prepend-icon="tabler-database-search"
                />
              </VList>
            </VCardText>
          </div>
        </VExpandTransition>
      </VCard>

      <!-- Chat Transcript -->
      <VCard>
        <VCardItem title="Conversation Transcript" />
        <VCardText>
          <div
            v-if="session.turns.length"
            class="d-flex flex-column gap-y-4"
          >
            <div
              v-for="(turn, idx) in session.turns"
              :key="idx"
              class="d-flex"
              :class="(turn.role === 'student') ? 'justify-start' : 'justify-end'"
            >
              <VCard
                :color="turn.role === 'student' ? 'primary' : 'secondary'"
                variant="tonal"
                class="pa-3"
                :style="{ maxWidth: '72%' }"
                :class="{ 'border border-error': turn.role === 'safety' }"
              >
                <div class="d-flex align-center gap-x-2 mb-1 flex-wrap">
                  <VChip
                    size="x-small"
                    :color="turn.role === 'student' ? 'primary' : turn.role === 'safety' ? 'error' : 'secondary'"
                    label
                  >
                    {{ turn.role }}
                  </VChip>

                  <span class="text-caption text-medium-emphasis">
                    {{ formatDate(turn.timestamp) }}
                  </span>

                  <VChip
                    v-if="turn.role !== 'student' && turn.role !== 'safety' && session.methodology"
                    size="x-small"
                    color="info"
                    variant="text"
                  >
                    {{ session.methodology }}
                  </VChip>

                  <VChip
                    v-if="turn.ragSourceCount > 0"
                    size="x-small"
                    color="success"
                    variant="text"
                    prepend-icon="tabler-database"
                  >
                    {{ turn.ragSourceCount }} source{{ turn.ragSourceCount !== 1 ? 's' : '' }}
                  </VChip>
                </div>

                <div
                  class="text-body-2"
                  :class="{ 'text-error': turn.role === 'safety' }"
                >
                  {{ turn.messagePreview }}
                </div>
              </VCard>
            </div>
          </div>

          <div
            v-else
            class="text-center py-8 text-disabled"
          >
            No messages in this session
          </div>
        </VCardText>
      </VCard>
    </template>
  </div>
</template>
