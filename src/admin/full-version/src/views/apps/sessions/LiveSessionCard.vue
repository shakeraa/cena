<!-- =============================================================================
  Cena Platform -- Live Session Card
  ADM-026.2: Per-student session card that updates in real time from SSE events
============================================================================= -->

<script setup lang="ts">
export interface ActiveSession {
  sessionId: string
  studentId: string
  studentName: string
  subject: string
  conceptId: string
  methodology: string
  questionCount: number
  correctCount: number
  fatigueScore: number    // 0–1
  durationSeconds: number
  startedAt: string
}

const props = defineProps<{
  session: ActiveSession
}>()

const emit = defineEmits<{
  (e: 'click', sessionId: string): void
}>()

// Fatigue-based border colour: green < 0.4, yellow 0.4–0.7, red > 0.7
const borderColor = computed<string>(() => {
  const f = props.session.fatigueScore
  if (f > 0.7) return 'border-error'
  if (f > 0.4) return 'border-warning'
  return 'border-success'
})

const fatigueColor = computed<string>(() => {
  const f = props.session.fatigueScore
  if (f > 0.7) return 'error'
  if (f > 0.4) return 'warning'
  return 'success'
})

const fatigueLabel = computed<string>(() => {
  const f = props.session.fatigueScore
  if (f > 0.7) return 'High fatigue'
  if (f > 0.4) return 'Moderate'
  return 'Focused'
})

const accuracy = computed<number>(() => {
  if (!props.session.questionCount) return 0
  return Math.round((props.session.correctCount / props.session.questionCount) * 100)
})

const formatDuration = (seconds: number): string => {
  if (seconds < 60) return `${seconds}s`
  const min = Math.floor(seconds / 60)
  const sec = seconds % 60
  return sec > 0 ? `${min}m ${sec}s` : `${min}m`
}
</script>

<template>
  <VCard
    class="live-session-card cursor-pointer"
    :class="[borderColor, 'border-s-4']"
    hover
    @click="emit('click', session.sessionId)"
  >
    <VCardText class="pa-4">
      <!-- Student name + fatigue chip -->
      <div class="d-flex align-start justify-space-between mb-3">
        <div>
          <div class="text-body-1 font-weight-semibold text-truncate">
            {{ session.studentName }}
          </div>
          <div class="text-caption text-medium-emphasis">
            {{ session.subject }}
          </div>
        </div>
        <VChip
          :color="fatigueColor"
          size="x-small"
          label
          class="flex-shrink-0"
        >
          {{ fatigueLabel }}
        </VChip>
      </div>

      <!-- Stats row -->
      <VRow dense>
        <VCol cols="6">
          <div class="text-caption text-medium-emphasis">
            Questions
          </div>
          <div class="text-body-2 font-weight-medium">
            {{ session.questionCount }}
          </div>
        </VCol>
        <VCol cols="6">
          <div class="text-caption text-medium-emphasis">
            Accuracy
          </div>
          <div class="text-body-2 font-weight-medium">
            {{ accuracy }}%
          </div>
        </VCol>
        <VCol cols="6">
          <div class="text-caption text-medium-emphasis">
            Duration
          </div>
          <div class="text-body-2 font-weight-medium">
            {{ formatDuration(session.durationSeconds) }}
          </div>
        </VCol>
        <VCol cols="6">
          <div class="text-caption text-medium-emphasis">
            Method
          </div>
          <div
            class="text-body-2 font-weight-medium text-truncate"
            style="max-inline-size: 100px;"
          >
            {{ session.methodology || '-' }}
          </div>
        </VCol>
      </VRow>

      <!-- Fatigue progress bar -->
      <VProgressLinear
        :model-value="session.fatigueScore * 100"
        :color="fatigueColor"
        height="4"
        rounded
        class="mt-3"
      />
    </VCardText>
  </VCard>
</template>

<style scoped>
.live-session-card {
  transition: box-shadow 0.2s ease;
}
</style>
