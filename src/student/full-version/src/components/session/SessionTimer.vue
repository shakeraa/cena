<script setup lang="ts">
/**
 * RDY-022: Session Timer
 *
 * Displays elapsed time discreetly in the session header.
 * At configurable milestone (default 25 min), shows a polite suggestion.
 * Timer is informational only — does not auto-end the session.
 *
 * a11y: role="timer" + aria-live="off" (no constant announcements).
 * Milestone alert uses aria-live="polite" one-shot.
 */
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  /** Whether the timer is paused. */
  paused?: boolean
  /** Milestone in minutes for soft suggestion (default 25). */
  milestoneMinutes?: number
}

const props = withDefaults(defineProps<Props>(), {
  paused: false,
  milestoneMinutes: 25,
})

const emit = defineEmits<{
  /** Emitted when the milestone is reached. */
  'milestone-reached': []
  /** Emitted when user clicks pause. */
  'pause': []
}>()

const { t } = useI18n()

const elapsedSeconds = ref(0)
const milestoneShown = ref(false)
let intervalId: ReturnType<typeof setInterval> | null = null

const formattedTime = computed(() => {
  const mins = Math.floor(elapsedSeconds.value / 60)
  const secs = elapsedSeconds.value % 60
  return `${mins}:${secs.toString().padStart(2, '0')}`
})

const milestoneAnnouncement = ref('')

function tick() {
  if (!props.paused) {
    elapsedSeconds.value++

    // Check milestone
    if (!milestoneShown.value && elapsedSeconds.value >= props.milestoneMinutes * 60) {
      milestoneShown.value = true
      milestoneAnnouncement.value = t(
        'session.timer.milestoneReached',
        { minutes: props.milestoneMinutes },
      )
      emit('milestone-reached')
    }
  }
}

onMounted(() => {
  intervalId = setInterval(tick, 1000)
})

onBeforeUnmount(() => {
  if (intervalId) clearInterval(intervalId)
})

// Reset milestone when paused state changes (resume)
watch(() => props.paused, (paused) => {
  if (!paused) milestoneAnnouncement.value = ''
})

defineExpose({ elapsedSeconds })
</script>

<template>
  <div class="session-timer d-flex align-center gap-2">
    <!-- Timer display: role="timer" with aria-live="off" per Tamar's review -->
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
    </div>

    <!-- Pause button -->
    <VBtn
      variant="text"
      size="small"
      :icon="paused ? 'tabler-player-play' : 'tabler-player-pause'"
      :aria-label="paused ? t('session.timer.resume') : t('session.timer.pause')"
      data-testid="session-pause-btn"
      @click="emit('pause')"
    />

    <!-- Milestone one-shot announcement (aria-live="polite") -->
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
