<script setup lang="ts">
import { useI18n } from 'vue-i18n'

interface QuickAction {
  to: string
  icon: string
  titleKey: string
  subtitleKey: string
  testId: string
}

const ACTIONS: QuickAction[] = [
  {
    to: '/session',
    icon: 'tabler-player-play',
    titleKey: 'home.quick.startSession',
    subtitleKey: 'home.quick.startSessionSubtitle',
    testId: 'quick-action-session',
  },
  {
    to: '/tutor',
    icon: 'tabler-message-chatbot',
    titleKey: 'home.quick.askTutor',
    subtitleKey: 'home.quick.askTutorSubtitle',
    testId: 'quick-action-tutor',
  },
  {
    to: '/challenges/daily',
    icon: 'tabler-target',
    titleKey: 'home.quick.dailyChallenge',
    subtitleKey: 'home.quick.dailyChallengeSubtitle',
    testId: 'quick-action-challenge',
  },
  {
    to: '/progress',
    icon: 'tabler-chart-line',
    titleKey: 'home.quick.progress',
    subtitleKey: 'home.quick.progressSubtitle',
    testId: 'quick-action-progress',
  },
]

const { t } = useI18n()
</script>

<template>
  <div
    class="quick-actions"
    data-testid="quick-actions"
  >
    <VCard
      v-for="action in ACTIONS"
      :key="action.to"
      :to="action.to"
      :data-testid="action.testId"
      class="quick-actions__tile pa-5"
      variant="outlined"
      :aria-label="t(action.titleKey)"
    >
      <VIcon
        :icon="action.icon"
        size="32"
        color="primary"
        class="mb-2"
        aria-hidden="true"
      />
      <div class="text-subtitle-1 font-weight-medium">
        {{ t(action.titleKey) }}
      </div>
      <div class="text-caption text-medium-emphasis">
        {{ t(action.subtitleKey) }}
      </div>
    </VCard>
  </div>
</template>

<style scoped>
.quick-actions {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.quick-actions__tile {
  transition: transform 0.15s ease-out;
}

.quick-actions__tile:hover {
  transform: translateY(-2px);
}

/* RDY-030b: prefers-reduced-motion guard (WCAG 2.3.3).
   Component-local animations/transitions reduced to an imperceptible
   0.01ms so vestibular-sensitive users don't trigger motion-related
   symptoms. Complements the global reset in styles.scss. */
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
</style>
