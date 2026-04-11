<script setup lang="ts">
import { computed } from 'vue'
import type {
  AnalyticsSummaryDto,
  MeBootstrapDto,
  TimeBreakdownDto,
} from '@/api/types/common'
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

// ── Bootstrap: identity + level + streak days (real endpoint). ────────────
// MSW mocks `/api/me` in dev (see src/plugins/fake-api/handlers/student-me);
// production hits the real Cena.Student.Api.Host endpoint.
const {
  data: me,
  error: meError,
  loading: meLoading,
} = useApiQuery<MeBootstrapDto>('/api/me')

// ── Analytics summary: overall-to-date stats. ─────────────────────────────
// Mirrors the real C# record AnalyticsSummaryDto from
// Cena.Api.Contracts.Analytics. `overallAccuracy` is a normalized 0..1
// value. These are OVERALL counts, not today-specific.
const {
  data: summary,
  error: summaryError,
} = useApiQuery<AnalyticsSummaryDto>('/api/analytics/summary')

// ── Time breakdown: last 30 days of learning minutes. ─────────────────────
// The last entry is today. We read it to render a real "Minutes today" KPI
// only when the entry's date is actually in the current UTC day.
const {
  data: timeBreakdown,
  error: timeBreakdownError,
} = useApiQuery<TimeBreakdownDto>('/api/analytics/time-breakdown')

// The page is "loading" until the primary /api/me call resolves. The
// analytics calls fire in parallel — if they are still inflight when /api/me
// resolves, the individual KPI cards drop out until their own data arrives.
const loading = computed(() => meLoading.value)
const error = computed(() => meError.value)

// Derived identity/profile values from the real /api/me payload.
const level = computed(() => me.value?.level ?? 1)
const streakDays = computed(() => me.value?.streakDays ?? 0)

// ── Derived values from /api/analytics/summary. ───────────────────────────
const hasSummary = computed(() => summary.value !== null && !summaryError.value)

// Overall-to-date question count. Explicitly relabelled to "Questions
// answered" so the UI does not claim these are today's answers.
const questionsAnswered = computed(() => summary.value?.totalQuestionsAttempted ?? null)

// Overall-to-date accuracy, rendered as a percentage with no decimals.
// The backend already rounds to 3 decimal places.
const accuracyPercentLabel = computed(() => {
  const value = summary.value?.overallAccuracy
  if (value == null)
    return null

  return `${Math.round(value * 100)}%`
})

// Session count — real. Shown alongside the level card instead of a fake
// "xp to next level" percentage, because the backend does not yet surface
// xp-within-level and inventing a value would be a lie.
const totalSessions = computed(() => summary.value?.totalSessions ?? null)

// ── Derived values from /api/analytics/time-breakdown. ────────────────────
const minutesTodayLabel = computed(() => {
  if (timeBreakdownError.value)
    return null

  const items = timeBreakdown.value?.items
  if (!items || items.length === 0)
    return null

  const last = items[items.length - 1]
  if (!last)
    return null

  const todayIso = new Date().toISOString().slice(0, 10)
  const itemIso = (last.date ?? '').slice(0, 10)
  if (itemIso !== todayIso)
    return 0

  return last.minutes ?? 0
})

// True once we have at least one real analytics KPI to render. If all
// analytics endpoints fail, the KPI section collapses to just the streak
// widget and the empty-state card explains what will appear here.
const hasAnyAnalyticsKpi = computed(() =>
  minutesTodayLabel.value != null
  || questionsAnswered.value != null
  || accuracyPercentLabel.value != null
  || totalSessions.value != null,
)

// The resume-session card is hidden until the backend `GET /api/sessions/active`
// wire-up lands. Explicitly typed as `null` so no stale shape ships.
const activeSession = null as null | {
  sessionId: string
  subject: string
  startedAt: string
  progressPercent: number
}
</script>

<template>
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
          Your learning stats
        </h2>

        <!--
          Streak is always rendered — it comes from the real /api/me payload
          and is 0 for brand-new students. A 0-day streak is an honest value.
        -->
        <StreakWidget
          :days="streakDays"
          :is-new-best="false"
        />

        <!--
          Minutes today: rendered only when the time-breakdown endpoint
          returned today's entry. On the very first session of the day the
          value is 0, which is honest. If the endpoint errored we drop the
          card entirely rather than fabricate.
        -->
        <KpiCard
          v-if="minutesTodayLabel != null"
          label="Minutes today"
          :value="minutesTodayLabel"
          icon="tabler-clock"
          data-testid="kpi-minutes-today"
        />

        <!--
          Questions answered: overall-to-date count, NOT today's count.
          The server has no per-day question count wired into
          /api/analytics/summary, so we explicitly label it as the
          cumulative total.
        -->
        <KpiCard
          v-if="questionsAnswered != null"
          label="Questions answered"
          :value="questionsAnswered"
          icon="tabler-question-mark"
          data-testid="kpi-questions-answered"
        />

        <!-- Overall accuracy, computed server-side from the event stream. -->
        <KpiCard
          v-if="accuracyPercentLabel != null"
          label="Overall accuracy"
          :value="accuracyPercentLabel"
          icon="tabler-target"
          data-testid="kpi-overall-accuracy"
        />

        <!--
          Level card: shows the real level from /api/analytics/summary
          (falling back to /api/me when summary is not available). We do
          NOT show a "XX% to next level" percentage because the backend
          does not yet surface xp-within-level. The secondary value is the
          real session count, or an em-dash when unavailable.
        -->
        <KpiCard
          :label="`Level ${level}`"
          :value="totalSessions != null ? `${totalSessions} sessions` : '—'"
          icon="tabler-bolt"
          data-testid="kpi-level"
        />
      </section>

      <!--
        Empty state: if /api/me succeeds but every analytics KPI above
        has no data (new student, or analytics endpoint errors) we tell
        the user explicitly what will appear here, instead of silently
        collapsing to a single streak card.
      -->
      <VAlert
        v-if="!hasSummary && !hasAnyAnalyticsKpi"
        variant="tonal"
        color="primary"
        class="mb-6"
        data-testid="home-empty-stats"
      >
        <VAlertTitle>Your stats appear here after your first session</VAlertTitle>
        Finish a learning session and your minutes, questions answered, and
        accuracy will show up on this dashboard.
      </VAlert>

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
