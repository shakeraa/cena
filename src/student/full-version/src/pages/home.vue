<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FlowState } from '@/plugins/vuetify/theme'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.home',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

// STU-W-05A: mock data only. STU-W-05B replaces these with real
// `useApiQuery` calls to STB-00 / STB-02 endpoints once the backend
// feature tasks ship.
interface MockHomeData {
  minutesToday: number
  xp: number
  xpToNextLevel: number
  level: number
  streakDays: number
  streakIsNewBest: boolean
  questionsAnswered: number
  accuracyPercent: number
  flowState: FlowState
  activeSession: null | {
    sessionId: string
    subject: string
    startedAt: string
    progressPercent: number
  }
}

const mockData = ref<MockHomeData>({
  minutesToday: 18,
  xp: 2340,
  xpToNextLevel: 500,
  level: 7,
  streakDays: 12,
  streakIsNewBest: false,
  questionsAnswered: 84,
  accuracyPercent: 76,
  flowState: 'approaching',
  activeSession: {
    sessionId: 'demo-session-abc123',
    subject: 'Algebra II — quadratic equations',
    startedAt: new Date(Date.now() - 14 * 60 * 1000).toISOString(),
    progressPercent: 35,
  },
})

const xpProgressPercent = computed(() => {
  const total = mockData.value.xpToNextLevel
  const current = mockData.value.xp % total

  return Math.round((current / total) * 100)
})
</script>

<template>
  <FlowAmbientBackground :flow-state="mockData.flowState" />

  <div
    class="home-page pa-6"
    data-testid="home-page"
  >
    <HomeGreeting />

    <ResumeSessionCard
      v-if="mockData.activeSession"
      :session-id="mockData.activeSession.sessionId"
      :subject="mockData.activeSession.subject"
      :started-at="mockData.activeSession.startedAt"
      :progress-percent="mockData.activeSession.progressPercent"
      class="mb-6"
    />

    <section
      class="home-page__kpis mb-6"
      aria-labelledby="home-kpis-heading"
    >
      <h2
        id="home-kpis-heading"
        class="sr-only"
      >
        Today's stats
      </h2>
      <StreakWidget
        :days="mockData.streakDays"
        :is-new-best="mockData.streakIsNewBest"
      />
      <KpiCard
        label="Minutes today"
        :value="mockData.minutesToday"
        :trend="12"
        icon="tabler-clock"
        data-testid="kpi-minutes-today"
      />
      <KpiCard
        label="Questions"
        :value="mockData.questionsAnswered"
        :trend="8"
        icon="tabler-question-mark"
        data-testid="kpi-questions"
      />
      <KpiCard
        label="Accuracy"
        :value="`${mockData.accuracyPercent}%`"
        :trend="3"
        icon="tabler-target"
        data-testid="kpi-accuracy"
      />
      <KpiCard
        :label="`Level ${mockData.level}`"
        :value="`${xpProgressPercent}%`"
        icon="tabler-bolt"
        data-testid="kpi-level"
      />
    </section>

    <section
      class="home-page__quick-actions-section mb-6"
      aria-labelledby="home-quick-heading"
    >
      <h2
        id="home-quick-heading"
        class="text-h6 mb-3"
      >
        Quick actions
      </h2>
      <QuickActions />
    </section>
  </div>
</template>

<style scoped>
.home-page {
  max-inline-size: 1280px;
  margin-inline: auto;
}

.home-page__kpis {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.sr-only {
  position: absolute;
  inline-size: 1px;
  block-size: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
