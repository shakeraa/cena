<script setup lang="ts">
import { $api } from '@/utils/api'
import UserActivityChart from '@/views/admin/dashboard/UserActivityChart.vue'
import ContentPipelineChart from '@/views/admin/dashboard/ContentPipelineChart.vue'
import SystemAlerts from '@/views/admin/dashboard/SystemAlerts.vue'
import RecentActivityTimeline from '@/views/admin/dashboard/RecentActivityTimeline.vue'
import TutoringBudgetCard from '@/views/admin/dashboard/TutoringBudgetCard.vue'

definePage({
  meta: {
    action: 'read',
    subject: 'Analytics',
  },
})

const { data: overviewData, isFetching } = await useApi<any>('/admin/dashboard/overview')

// SAI Feature Cards (corpus and experiments only; tutoring handled by TutoringBudgetCard)
const corpusBlocks = ref<number>(0)
const activeExperiments = ref<number>(0)
const saiLoading = ref(true)

const fetchSaiStats = async () => {
  saiLoading.value = true
  try {
    const [corpus, experiments] = await Promise.all([
      $api<any>('/admin/embeddings/corpus-stats').catch(() => null),
      $api<any>('/admin/experiments').catch(() => null),
    ])
    corpusBlocks.value = corpus?.totalBlocks ?? corpus?.blockCount ?? 0
    activeExperiments.value = Array.isArray(experiments) ? experiments.length : (experiments?.count ?? 0)
  }
  catch {
    // SAI data unavailable
  }
  finally {
    saiLoading.value = false
  }
}

onMounted(fetchSaiStats)

const widgetData = computed(() => {
  if (!overviewData.value) {
    return [
      { title: 'Active Users', value: '—', change: 0, desc: 'Currently online', icon: 'tabler-users', iconColor: 'primary' },
      { title: 'Total Students', value: '—', change: 0, desc: 'All students', icon: 'tabler-school', iconColor: 'success' },
      { title: 'Content Items', value: '—', change: 0, desc: 'Questions & lessons', icon: 'tabler-database', iconColor: 'info' },
      { title: 'Avg Focus Score', value: '—', change: 0, desc: 'Platform average', icon: 'tabler-eye-check', iconColor: 'warning' },
    ]
  }

  const d = overviewData.value

  return [
    { title: 'Active Users', value: String(d.activeUsers ?? 0), change: d.activeUsersChange ?? 0, desc: 'Currently online', icon: 'tabler-users', iconColor: 'primary' },
    { title: 'Total Students', value: String(d.totalStudents ?? 0), change: d.totalStudentsChange ?? 0, desc: 'vs last week', icon: 'tabler-school', iconColor: 'success' },
    { title: 'Content Items', value: String(d.contentItems ?? 0), change: d.pendingReview ?? 0, desc: `${d.pendingReview ?? 0} pending review`, icon: 'tabler-database', iconColor: 'info' },
    { title: 'Avg Focus Score', value: d.avgFocusScore ? `${d.avgFocusScore}%` : '—', change: d.avgFocusScoreChange ?? 0, desc: 'Platform average', icon: 'tabler-eye-check', iconColor: 'warning' },
  ]
})
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h4 class="text-h4">
          Platform Overview
        </h4>
        <p class="text-body-1 mb-0">
          Cena Learning Platform Dashboard
        </p>
      </div>
    </div>

    <!-- Overview Widgets (visible to all admin roles) -->
    <VRow class="mb-6">
      <VCol
        v-for="(data, index) in widgetData"
        :key="index"
        cols="12"
        md="3"
        sm="6"
      >
        <VCard :loading="isFetching">
          <VCardText>
            <div class="d-flex justify-space-between">
              <div class="d-flex flex-column gap-y-1">
                <div class="text-body-1 text-high-emphasis">
                  {{ data.title }}
                </div>
                <div class="d-flex gap-x-2 align-center">
                  <h4 class="text-h4">
                    {{ data.value }}
                  </h4>
                  <div
                    v-if="data.change !== 0"
                    class="text-base"
                    :class="data.change > 0 ? 'text-success' : 'text-error'"
                  >
                    ({{ data.change > 0 ? '+' : '' }}{{ data.change }}%)
                  </div>
                </div>
                <div class="text-sm">
                  {{ data.desc }}
                </div>
              </div>
              <VAvatar
                :color="data.iconColor"
                variant="tonal"
                rounded
                size="42"
              >
                <VIcon
                  :icon="data.icon"
                  size="26"
                />
              </VAvatar>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- User Activity Chart (SUPER_ADMIN / ADMIN only) -->
    <VRow
      v-if="$can('read', 'Analytics')"
      class="mb-6"
    >
      <VCol
        cols="12"
        md="8"
      >
        <UserActivityChart />
      </VCol>

      <VCol
        cols="12"
        md="4"
      >
        <!-- System Alerts (SUPER_ADMIN / ADMIN only) -->
        <SystemAlerts v-if="$can('read', 'System')" />

        <!-- Quick Actions fallback when alerts not visible -->
        <VCard v-else>
          <VCardItem>
            <VCardTitle>Quick Actions</VCardTitle>
          </VCardItem>
          <VCardText>
            <div class="d-flex flex-column gap-4">
              <VBtn
                :to="{ name: 'apps-user-list' }"
                variant="tonal"
                block
                prepend-icon="tabler-user-plus"
              >
                Manage Users
              </VBtn>
              <VBtn
                :to="{ name: 'apps-roles' }"
                variant="tonal"
                block
                prepend-icon="tabler-lock"
              >
                Roles & Permissions
              </VBtn>
              <VBtn
                :to="{ name: 'pages-account-settings-tab', params: { tab: 'account' } }"
                variant="tonal"
                block
                prepend-icon="tabler-settings"
              >
                Account Settings
              </VBtn>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- SAI Feature Cards -->
    <VRow
      v-if="$can('read', 'System')"
      class="mb-6"
    >
      <VCol
        cols="12"
        sm="4"
      >
        <TutoringBudgetCard />
      </VCol>
      <VCol
        cols="12"
        sm="4"
      >
        <VCard :loading="saiLoading">
          <VCardText>
            <div class="d-flex justify-space-between align-center">
              <div>
                <div class="text-body-2 text-medium-emphasis mb-1">
                  Content Corpus
                </div>
                <h4 class="text-h4">
                  {{ corpusBlocks.toLocaleString() }}
                </h4>
                <div class="text-sm">
                  Total content blocks
                </div>
              </div>
              <VAvatar
                color="success"
                variant="tonal"
                rounded
                size="42"
              >
                <VIcon
                  icon="tabler-vector"
                  size="26"
                />
              </VAvatar>
            </div>
          </VCardText>
        </VCard>
      </VCol>
      <VCol
        cols="12"
        sm="4"
      >
        <VCard :loading="saiLoading">
          <VCardText>
            <div class="d-flex justify-space-between align-center">
              <div>
                <div class="text-body-2 text-medium-emphasis mb-1">
                  Active Experiments
                </div>
                <h4 class="text-h4">
                  {{ activeExperiments }}
                </h4>
                <div class="text-sm">
                  Running A/B tests
                </div>
              </div>
              <VAvatar
                color="warning"
                variant="tonal"
                rounded
                size="42"
              >
                <VIcon
                  icon="tabler-flask"
                  size="26"
                />
              </VAvatar>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Content Pipeline Chart (all admin roles including MODERATOR) -->
    <VRow class="mb-6">
      <VCol cols="12">
        <ContentPipelineChart />
      </VCol>
    </VRow>

    <!-- Bottom Row: Quick Actions + Recent Activity -->
    <VRow>
      <!-- Quick Actions (SUPER_ADMIN / ADMIN) -->
      <VCol
        v-if="$can('read', 'System')"
        cols="12"
        md="4"
      >
        <VCard>
          <VCardItem>
            <VCardTitle>Quick Actions</VCardTitle>
          </VCardItem>
          <VCardText>
            <div class="d-flex flex-column gap-4">
              <VBtn
                :to="{ name: 'apps-user-list' }"
                variant="tonal"
                block
                prepend-icon="tabler-user-plus"
              >
                Manage Users
              </VBtn>
              <VBtn
                :to="{ name: 'apps-roles' }"
                variant="tonal"
                block
                prepend-icon="tabler-lock"
              >
                Roles & Permissions
              </VBtn>
              <VBtn
                :to="{ name: 'pages-account-settings-tab', params: { tab: 'account' } }"
                variant="tonal"
                block
                prepend-icon="tabler-settings"
              >
                Account Settings
              </VBtn>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Recent Activity Timeline (all admin roles) -->
      <VCol
        cols="12"
        :md="$can('read', 'System') ? 8 : 12"
      >
        <RecentActivityTimeline />
      </VCol>
    </VRow>
  </div>
</template>
