<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import SessionHistoryItem from '@/components/progress/SessionHistoryItem.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import type { ActiveSessionDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.sessionHistory',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

// Phase A: the real `/api/sessions/history` endpoint lands in STB-01c.
// For now we seed a deterministic stub list and reflect the active session
// at the top if one exists.
const activeQuery = useApiQuery<ActiveSessionDto | null>('/api/sessions/active')

interface StubSession {
  sessionId: string
  subject: string
  startedAt: string
  durationSeconds: number
  accuracyPercent: number
  xpAwarded: number
}

const stubHistory = computed<StubSession[]>(() => {
  const now = Date.now()

  return [
    {
      sessionId: 's-hist-1',
      subject: 'math',
      startedAt: new Date(now - 3 * 3600_000).toISOString(),
      durationSeconds: 12 * 60,
      accuracyPercent: 87,
      xpAwarded: 45,
    },
    {
      sessionId: 's-hist-2',
      subject: 'physics',
      startedAt: new Date(now - 1 * 86400_000).toISOString(),
      durationSeconds: 18 * 60,
      accuracyPercent: 72,
      xpAwarded: 30,
    },
    {
      sessionId: 's-hist-3',
      subject: 'chemistry',
      startedAt: new Date(now - 2 * 86400_000).toISOString(),
      durationSeconds: 9 * 60,
      accuracyPercent: 91,
      xpAwarded: 40,
    },
    {
      sessionId: 's-hist-4',
      subject: 'math',
      startedAt: new Date(now - 4 * 86400_000).toISOString(),
      durationSeconds: 15 * 60,
      accuracyPercent: 60,
      xpAwarded: 20,
    },
    {
      sessionId: 's-hist-5',
      subject: 'biology',
      startedAt: new Date(now - 6 * 86400_000).toISOString(),
      durationSeconds: 22 * 60,
      accuracyPercent: 95,
      xpAwarded: 55,
    },
  ]
})
</script>

<template>
  <div
    class="progress-sessions-page pa-4"
    data-testid="progress-sessions-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('progress.sessions.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('progress.sessions.subtitle') }}
    </p>

    <!-- Active session pinned to top if any -->
    <VCard
      v-if="activeQuery.data.value"
      variant="flat"
      color="primary"
      class="pa-4 mb-4"
      data-testid="active-session-card"
    >
      <div class="d-flex align-center justify-space-between">
        <div>
          <div class="text-caption text-white opacity-80">
            {{ t('progress.sessions.activeLabel') }}
          </div>
          <div class="text-h6 text-white">
            {{ t(`session.setup.subjects.${activeQuery.data.value.subjects[0]}`, activeQuery.data.value.subjects[0]) }}
          </div>
        </div>
        <VBtn
          color="white"
          variant="flat"
          :to="`/session/${activeQuery.data.value.sessionId}`"
          data-testid="active-session-resume"
        >
          {{ t('progress.sessions.resume') }}
        </VBtn>
      </div>
    </VCard>

    <div data-testid="session-history-list">
      <SessionHistoryItem
        v-for="s in stubHistory"
        :key="s.sessionId"
        :session-id="s.sessionId"
        :subject="s.subject"
        :started-at="s.startedAt"
        :duration-seconds="s.durationSeconds"
        :accuracy-percent="s.accuracyPercent"
        :xp-awarded="s.xpAwarded"
      />
    </div>
  </div>
</template>

<style scoped>
.progress-sessions-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
