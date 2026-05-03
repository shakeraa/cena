<script setup lang="ts">
/**
 * RDY-022: Session Timer (Architecture-aware)
 *
 * Computes elapsed time from the backend-provided session start timestamp,
 * NOT a frontend counter. This means:
 *   - Timer survives page refresh (derived from startedAt)
 *   - Paused time is tracked separately (accumulated pause duration)
 *   - Milestone detection is based on actual study time, not wall time
 *
 * a11y: role="timer" + aria-live="off" (no constant announcements).
 * Milestone alert uses aria-live="polite" one-shot.
 */
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  /** ISO timestamp from backend: when the session started. */
  startedAt: string
  /** Whether the timer is paused. */
  paused?: boolean
  /** Milestone in minutes for soft suggestion (default 25). */
  milestoneMinutes?: number
  /** Previously accumulated pause duration in seconds (persisted via snapshot). */
  pausedDurationSeconds?: number
}

const props = withDefaults(defineProps<Props>(), {
  paused: false,
  milestoneMinutes: 25,
  pausedDurationSeconds: 0,
})

const emit = defineEmits<{
  'milestone-reached': []
  'pause': []
  /** Reports current elapsed seconds on each tick (for snapshot persistence). */
  'tick': [elapsedSeconds: number]
}>()

const { t } = useI18n()

// Track pause accumulation: when paused, we stop counting time.
// Total pause time = previously persisted pauses + current pause duration.
const pauseStartedAt = ref<number | null>(null)
const accumulatedPauseMs = ref(props.pausedDurationSeconds * 1000)

const milestoneShown = ref(false)
const milestoneAnnouncement = ref('')
const now = ref(Date.now())

let intervalId: ReturnType<typeof setInterval> | null = null

// Compute elapsed study time (wall time - pause time)
const elapsedMs = computed(() => {
  const sessionStartMs = new Date(props.startedAt).getTime()
  const wallElapsed = now.value - sessionStartMs

  let totalPauseMs = accumulatedPauseMs.value
  if (pauseStartedAt.value !== null) {
    // Currently paused — add the ongoing pause duration
    totalPauseMs += now.value - pauseStartedAt.value
  }

  return Math.max(0, wallElapsed - totalPauseMs)
})

const elapsedSeconds = computed(() => Math.floor(elapsedMs.value / 1000))

const formattedTime = computed(() => {
  const totalSecs = elapsedSeconds.value
  const mins = Math.floor(totalSecs / 60)
  const secs = totalSecs % 60
  return `${mins}:${secs.toString().padStart(2, '0')}`
})

function tick() {
  now.value = Date.now()

  // Emit tick for snapshot persistence (parent can save this)
  emit('tick', elapsedSeconds.value)

  // Check milestone (based on actual study time, not wall time)
  if (!milestoneShown.value && elapsedSeconds.value >= props.milestoneMinutes * 60) {
    milestoneShown.value = true
    milestoneAnnouncement.value = t(
      'session.timer.milestoneReached',
      { minutes: props.milestoneMinutes },
    )
    emit('milestone-reached')
  }
}

// Track pause start/end for accurate pause duration
watch(() => props.paused, (isPaused, wasPaused) => {
  if (isPaused && !wasPaused) {
    // Just paused — record when pause started
    pauseStartedAt.value = Date.now()
  } else if (!isPaused && wasPaused && pauseStartedAt.value !== null) {
    // Just resumed — accumulate the pause duration
    accumulatedPauseMs.value += Date.now() - pauseStartedAt.value
    pauseStartedAt.value = null
    milestoneAnnouncement.value = ''
  }
})

onMounted(() => {
  // Check if startedAt is valid
  const startMs = new Date(props.startedAt).getTime()
  if (isNaN(startMs)) {
    console.warn('SessionTimer: invalid startedAt timestamp, timer will show 0:00')
  }

  // If session already past milestone on mount (e.g., page refresh at 30 min),
  // don't show the milestone again — it was already shown
  if (elapsedSeconds.value >= props.milestoneMinutes * 60) {
    milestoneShown.value = true
  }

  intervalId = setInterval(tick, 1000)
})

onBeforeUnmount(() => {
  if (intervalId) clearInterval(intervalId)
})

defineExpose({
  elapsedSeconds,
  /** Total pause duration in seconds (for snapshot persistence). */
  totalPausedSeconds: computed(() => Math.floor(accumulatedPauseMs.value / 1000)),
})
</script>

<template>
  <div class="session-timer d-flex align-center gap-2">
    <div
      role="timer"
      aria-live="off"
      :aria-label="t('session.timer.elapsed', { time: formattedTime })"
      class="session-timer__display text-caption text-medium-emphasis"
      data-testid="session-timer"
    >
      <VIcon
        icon="tabler-clock"
        size="16"
        class="me-1"
        aria-hidden="true"
      />
      {{ formattedTime }}
      <span
        v-if="paused"
        class="text-warning ms-1"
        aria-label="paused"
      >
        ||
      </span>
    </div>

    <VBtn
      variant="text"
      size="small"
      :icon="paused ? 'tabler-player-play' : 'tabler-player-pause'"
      :aria-label="paused ? t('session.timer.resume') : t('session.timer.pause')"
      data-testid="session-pause-btn"
      @click="emit('pause')"
    />

    <div
      aria-live="polite"
      class="sr-only"
    >
      {{ milestoneAnnouncement }}
    </div>
  </div>
</template>

<style scoped>
.session-timer__display {
  font-variant-numeric: tabular-nums;
  min-inline-size: 48px;
}
</style>
