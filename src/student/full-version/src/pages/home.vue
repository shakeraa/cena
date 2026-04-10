<script setup lang="ts">
import { computed } from 'vue'
import type { FlowState } from '@/plugins/vuetify/theme'
import type { MeBootstrapDto } from '@/api/types/common'
import { useApiQuery } from '@/composables/useApiQuery'

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

// STU-W-05B: real $api wiring. MSW mocks `/api/me` in dev (see
// src/plugins/fake-api/handlers/student-me); production hits the
// real Cena.Api.Host from STB-00.
const { data: me, error, loading } = useApiQuery<MeBootstrapDto>('/api/me')

// Constants that STB-00 doesn't return yet — STU-W-05C wires them
// when STB-02 (plan/review/recommendations) lands and STU-W-05D
// picks up live values from SignalR.
const MOCK_MINUTES_TODAY = 18
const MOCK_QUESTIONS_TODAY = 84
const MOCK_ACCURACY = 76
const MOCK_FLOW_STATE: FlowState = 'approaching'

// Derived values from the real /api/me payload.
const level = computed(() => me.value?.level ?? 1)
const streakDays = computed(() => me.value?.streakDays ?? 0)

const xpProgressPercent = computed(() => {
  // STB-00's MeBootstrapDto returns level but not xp-within-level.
  // Stub 40% as a visual placeholder until STB-03 lands the
  // real gamification endpoint with xp + xpToNextLevel.
  return 40
})

// STU-W-05C will wire `GET /api/sessions/active` from STB-01.
const activeSession = null as null | {
  sessionId: string
  subject: string
  startedAt: string
  progressPercent: number
}
</script>

<template>
  <FlowAmbientBackground :flow-state="MOCK_FLOW_STATE" />

  <div
    class="home-page pa-6"
    data-testid="home-page"
  >
    <!-- Loading state: show skeleton cards for every slot -->
    <template v-if="loading">
      <div
        class="home-page__skeletons"
        data-testid="home-loading-skeleton"
      >
        <StudentSkeletonCard
          :lines="2"
          height="80px"
        />
        <div class="home-page__kpis mt-6">
          <StudentSkeletonCard :lines="3" />
          <StudentSkeletonCard :lines="3" />
          <StudentSkeletonCard :lines="3" />
          <StudentSkeletonCard :lines="3" />
          <StudentSkeletonCard :lines="3" />
        </div>
      </div>
    </template>

    <!-- Error state -->
    <template v-else-if="error">
      <VAlert
        type="error"
        variant="tonal"
        prominent
        data-testid="home-error-state"
      >
        <VAlertTitle>Could not load your home dashboard</VAlertTitle>
        <div class="mb-3">
          {{ error.message || 'Try refreshing the page.' }}
        </div>
      </VAlert>
    </template>

    <!-- Happy path -->
    <template v-else-if="me">
      <HomeGreeting />

      <ResumeSessionCard
        v-if="activeSession"
        :session-id="activeSession.sessionId"
        :subject="activeSession.subject"
        :started-at="activeSession.startedAt"
        :progress-percent="activeSession.progressPercent"
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
          :days="streakDays"
          :is-new-best="false"
        />
        <KpiCard
          label="Minutes today"
          :value="MOCK_MINUTES_TODAY"
          :trend="12"
          icon="tabler-clock"
          data-testid="kpi-minutes-today"
        />
        <KpiCard
          label="Questions"
          :value="MOCK_QUESTIONS_TODAY"
          :trend="8"
          icon="tabler-question-mark"
          data-testid="kpi-questions"
        />
        <KpiCard
          label="Accuracy"
          :value="`${MOCK_ACCURACY}%`"
          :trend="3"
          icon="tabler-target"
          data-testid="kpi-accuracy"
        />
        <KpiCard
          :label="`Level ${level}`"
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
    </template>
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
