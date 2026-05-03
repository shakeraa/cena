<script setup lang="ts">
import { $api } from '@/utils/api'
import { useRouter } from 'vue-router'

interface TutoringAnalytics {
  activeSessionCount: number
  sessionsToday: number
  avgBudgetUsagePercent: number
}

interface BudgetStatus {
  students: Array<{
    studentId: string
    studentName: string
    tokensUsedToday: number
    dailyLimit: number
    percentUsed: number
    isExhausted: boolean
  }>
  totalTokensToday: number
  totalStudentsNearLimit: number
}

const router = useRouter()

const loading = ref(true)
const analytics = ref<TutoringAnalytics | null>(null)
const budgetStatus = ref<BudgetStatus | null>(null)

const hasExhaustedStudents = computed(() =>
  budgetStatus.value?.students.some(s => s.isExhausted) ?? false,
)

const studentsNearLimit = computed(() =>
  budgetStatus.value?.totalStudentsNearLimit ?? 0,
)

// Build last 7-day sparkline data from sessionsThisWeek approximation
// We use tokensUsedToday as the current day anchor and distribute evenly
const sparklineData = computed(() => {
  if (!budgetStatus.value) return [0, 0, 0, 0, 0, 0, 0]

  // Construct 7 mock-free data points: days prior have no data from this endpoint,
  // so we show only today's real value and zeros for prior days
  const today = budgetStatus.value.totalTokensToday
  return [0, 0, 0, 0, 0, 0, today]
})

const fetchData = async () => {
  loading.value = true
  try {
    const [analyticsData, budgetData] = await Promise.all([
      $api<TutoringAnalytics>('/admin/tutoring/analytics').catch(() => null),
      $api<BudgetStatus>('/admin/tutoring/budget-status').catch(() => null),
    ])

    analytics.value = analyticsData
    budgetStatus.value = budgetData
  }
  catch (err) {
    console.error('TutoringBudgetCard: fetch failed', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchData)
</script>

<template>
  <VCard
    :loading="loading"
    class="cursor-pointer"
    hover
    @click="router.push({ name: 'apps-tutoring-sessions' })"
  >
    <VCardItem>
      <VCardTitle class="d-flex align-center gap-x-2">
        <VIcon
          icon="tabler-messages"
          size="20"
          color="info"
        />
        AI Tutoring
        <VSpacer />
        <VBadge
          v-if="hasExhaustedStudents"
          color="error"
          content="!"
          floating
        >
          <VIcon
            icon="tabler-alert-triangle"
            color="error"
            size="18"
          />
        </VBadge>
      </VCardTitle>
    </VCardItem>

    <VCardText>
      <VRow
        dense
        class="mb-3"
      >
        <VCol cols="4">
          <div class="text-body-2 text-medium-emphasis">
            Active
          </div>
          <h5 class="text-h5 font-weight-bold text-info">
            {{ analytics?.activeSessionCount ?? '—' }}
          </h5>
        </VCol>
        <VCol cols="4">
          <div class="text-body-2 text-medium-emphasis">
            Tokens today
          </div>
          <h5 class="text-h5 font-weight-bold">
            {{ budgetStatus ? budgetStatus.totalTokensToday.toLocaleString() : '—' }}
          </h5>
        </VCol>
        <VCol cols="4">
          <div class="text-body-2 text-medium-emphasis">
            Near limit
          </div>
          <h5
            class="text-h5 font-weight-bold"
            :class="studentsNearLimit > 0 ? 'text-warning' : ''"
          >
            {{ budgetStatus ? studentsNearLimit : '—' }}
          </h5>
        </VCol>
      </VRow>

      <!-- Sparkline: token usage over last 7 days (today = rightmost) -->
      <div class="d-flex gap-x-1 align-end mb-2">
        <div
          v-for="(val, idx) in sparklineData"
          :key="idx"
          class="rounded-t flex-1"
          :style="{
            height: `${val > 0 ? Math.max(4, Math.round((val / Math.max(...sparklineData, 1)) * 32)) : 3}px`,
            backgroundColor: idx === sparklineData.length - 1 ? 'rgb(var(--v-theme-info))' : 'rgba(var(--v-theme-on-surface), 0.12)',
          }"
        />
      </div>
      <div class="d-flex justify-space-between text-caption text-medium-emphasis mb-2">
        <span>7 days ago</span>
        <span>Today</span>
      </div>

      <VAlert
        v-if="hasExhaustedStudents"
        type="error"
        variant="tonal"
        density="compact"
        class="mt-2"
        prepend-icon="tabler-alert-circle"
      >
        {{ budgetStatus?.students.filter(s => s.isExhausted).length }} student(s) exhausted daily budget
      </VAlert>
    </VCardText>
  </VCard>
</template>
