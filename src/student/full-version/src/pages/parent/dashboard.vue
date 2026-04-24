<script setup lang="ts">
// =============================================================================
// Cena Platform — Parent dashboard page (EPIC-PRR-I PRR-320/321/322)
//
// Premium-tier only. Arabic + English + Hebrew parity via i18n. If the
// caller's tier doesn't include the parent dashboard, API returns 403 with
// error=tier_required — we upsell rather than error.
// =============================================================================

import { computed, ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApi } from '@/composables/useApi'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'parentDashboard.title',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

interface ParentStudent {
  studentId: string
  displayName: string
  activeTier: string
  weeklyMinutes: number
  monthlyMinutes: number
  topicsPracticed: number
  readinessScore: number | null
  lastActiveAt: string | null
}

interface DashboardResponse {
  students: ParentStudent[]
  householdMinutesWeekly: number
  householdMinutesMonthly: number
  generatedAt: string
}

const { t } = useI18n()
const router = useRouter()

const data = ref<DashboardResponse | null>(null)
const loading = ref(true)
const tierGate = ref(false)

async function load() {
  loading.value = true
  tierGate.value = false
  const { data: resp, error, response } = await useApi('/me/parent-dashboard')
    .get()
    .json<DashboardResponse>()
  if (response.value?.status === 403) {
    tierGate.value = true
  } else if (error.value) {
    tierGate.value = false
  } else {
    data.value = resp.value
  }
  loading.value = false
}

const upgrade = () => router.push('/pricing')

onMounted(load)

const hasStudents = computed(() => (data.value?.students.length ?? 0) > 0)
</script>

<template>
  <div class="parent-dashboard pa-4 pa-md-8" data-testid="parent-dashboard-page">
    <h1 class="text-h4 font-weight-bold mb-4">
      {{ t('parentDashboard.title') }}
    </h1>

    <div v-if="loading" class="d-flex justify-center pa-8" data-testid="parent-dashboard-loading">
      <VProgressCircular indeterminate color="primary" />
    </div>

    <!-- Tier gate: upsell to Premium -->
    <VCard v-else-if="tierGate" class="pa-6 text-center" variant="outlined" data-testid="parent-dashboard-tiergate">
      <VIcon icon="tabler-lock" color="primary" size="48" class="mb-3" />
      <h2 class="text-h5 mb-2">
        {{ t('parentDashboard.tierGate.title') }}
      </h2>
      <p class="text-body-2 text-medium-emphasis mb-4">
        {{ t('parentDashboard.tierGate.subtitle') }}
      </p>
      <VBtn color="primary" size="large" @click="upgrade">
        {{ t('parentDashboard.tierGate.cta') }}
      </VBtn>
    </VCard>

    <template v-else-if="data">
      <!-- Household rollup card -->
      <VRow class="mb-4">
        <VCol cols="12" md="6">
          <VCard class="pa-4" variant="outlined">
            <h2 class="text-subtitle-2 text-medium-emphasis mb-1">
              {{ t('parentDashboard.household.weekly') }}
            </h2>
            <p class="text-h4 font-weight-bold">
              <bdi dir="ltr">{{ data.householdMinutesWeekly }}</bdi>
              <span class="text-body-2 text-medium-emphasis ms-1">
                {{ t('parentDashboard.household.min') }}
              </span>
            </p>
          </VCard>
        </VCol>
        <VCol cols="12" md="6">
          <VCard class="pa-4" variant="outlined">
            <h2 class="text-subtitle-2 text-medium-emphasis mb-1">
              {{ t('parentDashboard.household.monthly') }}
            </h2>
            <p class="text-h4 font-weight-bold">
              <bdi dir="ltr">{{ data.householdMinutesMonthly }}</bdi>
              <span class="text-body-2 text-medium-emphasis ms-1">
                {{ t('parentDashboard.household.min') }}
              </span>
            </p>
          </VCard>
        </VCol>
      </VRow>

      <!-- Per-student cards -->
      <h2 class="text-h5 mb-3">
        {{ t('parentDashboard.students.heading') }}
      </h2>
      <VAlert
        v-if="!hasStudents"
        type="info"
        variant="tonal"
        data-testid="parent-dashboard-empty"
      >
        {{ t('parentDashboard.students.empty') }}
      </VAlert>
      <VRow v-else>
        <VCol
          v-for="s in data.students"
          :key="s.studentId"
          cols="12"
          md="6"
        >
          <VCard class="pa-4" variant="outlined" :data-testid="`parent-student-${s.studentId}`">
            <div class="d-flex justify-space-between align-center mb-2">
              <h3 class="text-h6">
                {{ s.displayName || t('parentDashboard.students.unnamed') }}
              </h3>
              <VChip size="small" color="primary">{{ s.activeTier }}</VChip>
            </div>
            <div class="d-flex ga-4 flex-wrap text-body-2">
              <span>
                {{ t('parentDashboard.students.weekly') }}:
                <bdi dir="ltr">{{ s.weeklyMinutes }}</bdi> {{ t('parentDashboard.household.min') }}
              </span>
              <span>
                {{ t('parentDashboard.students.topics') }}: <bdi dir="ltr">{{ s.topicsPracticed }}</bdi>
              </span>
              <span v-if="s.readinessScore !== null">
                {{ t('parentDashboard.students.readiness') }}: <bdi dir="ltr">{{ s.readinessScore }}</bdi>
              </span>
            </div>
          </VCard>
        </VCol>
      </VRow>
    </template>
  </div>
</template>
