<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Tutoring' } })

interface TutoringMessage {
  role: string
  text: string
  timestamp: string
}

interface TutoringSessionDetail {
  sessionId: string
  studentId: string
  status: string
  methodology: string
  concept: string
  durationSeconds: number
  startedAt: string
  endedAt: string | null
  tokensUsed: number
  dailyTokenLimit: number
  messages: TutoringMessage[]
}

const route = useRoute()
const router = useRouter()

const loading = ref(true)
const error = ref<string | null>(null)
const session = ref<TutoringSessionDetail | null>(null)

const sessionId = computed(() => String((route.params as Record<string, string>).id ?? ''))

const statusColor = (status: string): string => {
  switch (status) {
    case 'active': return 'info'
    case 'completed': return 'success'
    case 'budget_exhausted': return 'warning'
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

const budgetPercent = computed(() => {
  if (!session.value || !session.value.dailyTokenLimit) return 0

  return Math.min(100, (session.value.tokensUsed / session.value.dailyTokenLimit) * 100)
})

const budgetColor = computed(() => {
  const pct = budgetPercent.value
  if (pct >= 90) return 'error'
  if (pct >= 70) return 'warning'

  return 'success'
})

const fetchSession = async () => {
  loading.value = true
  try {
    const data = await $api<TutoringSessionDetail>(`/admin/tutoring/sessions/${sessionId.value}`)

    session.value = {
      sessionId: data.sessionId ?? sessionId.value,
      studentId: data.studentId ?? '',
      status: data.status ?? 'unknown',
      methodology: data.methodology ?? '-',
      concept: data.concept ?? '-',
      durationSeconds: data.durationSeconds ?? 0,
      startedAt: data.startedAt ?? '',
      endedAt: data.endedAt ?? null,
      tokensUsed: data.tokensUsed ?? 0,
      dailyTokenLimit: data.dailyTokenLimit ?? 0,
      messages: data.messages ?? [],
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
                Session {{ session.sessionId }}
              </h4>
              <div class="d-flex flex-wrap gap-x-6 gap-y-2 text-body-2">
                <div>
                  <span class="text-medium-emphasis">Student:</span>
                  <span class="font-weight-medium ms-1">{{ session.studentId }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Methodology:</span>
                  <span class="font-weight-medium ms-1">{{ session.methodology }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Concept:</span>
                  <span class="font-weight-medium ms-1">{{ session.concept }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Duration:</span>
                  <span class="font-weight-medium ms-1">{{ formatDuration(session.durationSeconds) }}</span>
                </div>
                <div>
                  <span class="text-medium-emphasis">Started:</span>
                  <span class="font-weight-medium ms-1">{{ formatDate(session.startedAt) }}</span>
                </div>
              </div>
            </VCol>
            <VCol
              cols="12"
              md="4"
              class="d-flex align-center justify-end"
            >
              <VChip
                :color="statusColor(session.status)"
                label
                size="large"
              >
                {{ session.status.replace('_', ' ') }}
              </VChip>
            </VCol>
          </VRow>
        </VCardText>
      </VCard>

      <!-- Budget Meter -->
      <VCard class="mb-6">
        <VCardItem title="Token Budget" />
        <VCardText>
          <div class="d-flex justify-space-between text-body-2 mb-2">
            <span>{{ session.tokensUsed.toLocaleString() }} used</span>
            <span>{{ session.dailyTokenLimit.toLocaleString() }} daily limit</span>
          </div>
          <VProgressLinear
            :model-value="budgetPercent"
            :color="budgetColor"
            height="12"
            rounded
          />
          <div class="text-body-2 text-medium-emphasis mt-1">
            {{ budgetPercent.toFixed(1) }}% of daily budget consumed
          </div>
        </VCardText>
      </VCard>

      <!-- Chat Transcript -->
      <VCard>
        <VCardItem title="Conversation Transcript" />
        <VCardText>
          <div
            v-if="session.messages.length"
            class="d-flex flex-column gap-y-4"
          >
            <div
              v-for="(msg, idx) in session.messages"
              :key="idx"
              class="d-flex"
              :class="msg.role === 'student' ? 'justify-start' : 'justify-end'"
            >
              <VCard
                :color="msg.role === 'student' ? 'primary' : 'secondary'"
                variant="tonal"
                class="pa-3"
                :style="{ maxWidth: '70%' }"
              >
                <div class="d-flex align-center gap-x-2 mb-1">
                  <VChip
                    size="x-small"
                    :color="msg.role === 'student' ? 'primary' : 'secondary'"
                    label
                  >
                    {{ msg.role }}
                  </VChip>
                  <span class="text-caption text-medium-emphasis">
                    {{ formatDate(msg.timestamp) }}
                  </span>
                </div>
                <div class="text-body-2">
                  {{ msg.text }}
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
