<script setup lang="ts">
import { $api } from '@/utils/api'
import FocusDegradationChart from '@/views/apps/focus/FocusDegradationChart.vue'
import FocusGaugeCard from '@/views/apps/focus/FocusGaugeCard.vue'

definePage({ meta: { action: 'read', subject: 'Focus' } })

interface FocusAlert {
  studentId: string
  studentName: string
  avgFocusScore: number
  trend: number
}

const alertsLoading = ref(true)
const alertsError = ref<string | null>(null)
const alerts = ref<FocusAlert[]>([])

const focusScoreColor = (score: number): string => {
  if (score >= 70) return 'success'
  if (score >= 40) return 'warning'
  return 'error'
}

const fetchAlerts = async () => {
  alertsLoading.value = true
  try {
    const data = await $api('/admin/focus/alerts')

    alerts.value = (Array.isArray(data) ? data : (data.students ?? [])).map((s: any) => ({
      studentId: s.studentId,
      studentName: s.studentName,
      avgFocusScore: s.avgFocusScore ?? 0,
      trend: s.trend ?? 0,
    }))

    alertsError.value = null
  }
  catch (err: any) {
    console.error('Failed to fetch focus alerts:', err)
    alertsError.value = err.message ?? 'Failed to load focus alerts'
  }
  finally {
    alertsLoading.value = false
  }
}

onMounted(fetchAlerts)
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Focus & Attention Analytics
        </h4>
        <div class="text-body-1">
          Monitor student attention, mind wandering, and microbreak effectiveness
        </div>
      </div>
    </div>

    <VRow class="match-height">
      <!-- Widget Cards -->
      <VCol cols="12">
        <FocusGaugeCard :poll-interval-ms="30000" />
      </VCol>

      <!-- Degradation Curve -->
      <VCol
        cols="12"
        md="8"
      >
        <FocusDegradationChart />
      </VCol>

      <!-- Students Needing Attention -->
      <VCol
        cols="12"
        md="4"
      >
        <VCard :loading="alertsLoading">
          <VCardItem title="Students Needing Attention">
            <template #subtitle>
              Consistently low or declining focus
            </template>
          </VCardItem>

          <VCardText>
            <VAlert
              v-if="alertsError"
              type="error"
              variant="tonal"
              class="mb-4"
            >
              {{ alertsError }}
            </VAlert>

            <VList
              v-if="alerts.length > 0"
              lines="two"
              density="compact"
            >
              <VListItem
                v-for="student in alerts"
                :key="student.studentId"
                :to="{ name: 'apps-focus-student-id', params: { id: student.studentId } }"
              >
                <template #prepend>
                  <VAvatar
                    :color="focusScoreColor(student.avgFocusScore)"
                    variant="tonal"
                    size="36"
                  >
                    <span class="text-caption font-weight-bold">{{ student.avgFocusScore }}</span>
                  </VAvatar>
                </template>

                <VListItemTitle class="text-body-1 font-weight-medium">
                  {{ student.studentName }}
                </VListItemTitle>

                <VListItemSubtitle>
                  <div class="d-flex align-center gap-x-1">
                    <span>Focus: {{ student.avgFocusScore }}/100</span>
                    <VIcon
                      :icon="student.trend >= 0 ? 'tabler-trending-up' : 'tabler-trending-down'"
                      :color="student.trend >= 0 ? 'success' : 'error'"
                      size="16"
                    />
                    <span :class="student.trend >= 0 ? 'text-success' : 'text-error'">
                      {{ student.trend > 0 ? '+' : '' }}{{ student.trend }}%
                    </span>
                  </div>
                </VListItemSubtitle>

                <template #append>
                  <VIcon
                    icon="tabler-chevron-right"
                    size="20"
                    class="text-disabled"
                  />
                </template>
              </VListItem>
            </VList>

            <div
              v-else-if="!alertsLoading && !alertsError"
              class="d-flex justify-center align-center py-6"
            >
              <span class="text-body-1 text-disabled">No students flagged</span>
            </div>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>

<style lang="scss">
@use "@core/scss/template/libs/apex-chart.scss";
</style>
