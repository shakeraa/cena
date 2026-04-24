<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Pedagogy' } })

interface CohortArm {
  name: string
  studentCount: number
  avgMasteryDelta: number
  confusionResolutionRate: number
  avgTurns: number
}

interface ExperimentDetail {
  name: string
  description: string
  status: string
  arms: CohortArm[]
}

interface FunnelStage {
  stage: string
  count: number
}

const route = useRoute()
const router = useRouter()

const loading = ref(true)
const error = ref<string | null>(null)
const experiment = ref<ExperimentDetail | null>(null)
const funnel = ref<FunnelStage[]>([])

const experimentName = computed(() => String((route.params as Record<string, string>).id ?? ''))

const statusColor = (status: string): string => {
  switch (status.toLowerCase()) {
    case 'running': return 'success'
    case 'paused': return 'warning'
    case 'completed': return 'info'
    case 'draft': return 'secondary'
    default: return 'default'
  }
}

const cohortHeaders = [
  { title: 'Arm Name', key: 'name', sortable: true },
  { title: 'Students', key: 'studentCount', sortable: true, align: 'center' as const },
  { title: 'Avg Mastery Delta', key: 'avgMasteryDelta', sortable: true, align: 'center' as const },
  { title: 'Confusion Resolution', key: 'confusionResolutionRate', sortable: true, align: 'center' as const },
  { title: 'Avg Turns', key: 'avgTurns', sortable: true, align: 'center' as const },
]

const maxFunnelCount = computed(() => {
  if (!funnel.value.length) return 1

  return Math.max(...funnel.value.map(s => s.count))
})

const funnelPercentage = (count: number): number => {
  return (count / maxFunnelCount.value) * 100
}

const funnelColors = ['primary', 'info', 'warning', 'error', 'success']

const fetchExperiment = async () => {
  try {
    const data = await $api<ExperimentDetail>(`/admin/experiments/${experimentName.value}`)

    experiment.value = {
      name: data.name ?? experimentName.value,
      description: data.description ?? '',
      status: data.status ?? 'unknown',
      arms: (data.arms ?? []).map(arm => ({
        name: arm.name ?? '',
        studentCount: arm.studentCount ?? 0,
        avgMasteryDelta: arm.avgMasteryDelta ?? 0,
        confusionResolutionRate: arm.confusionResolutionRate ?? 0,
        avgTurns: arm.avgTurns ?? 0,
      })),
    }
  }
  catch (err: any) {
    console.error('Failed to fetch experiment:', err)
    error.value = err.message ?? 'Failed to load experiment'
  }
}

const fetchFunnel = async () => {
  try {
    const data = await $api<{ stages: FunnelStage[] }>(`/admin/experiments/${experimentName.value}/funnel`)

    funnel.value = (data.stages ?? []).map(s => ({
      stage: s.stage ?? '',
      count: s.count ?? 0,
    }))
  }
  catch (err: any) {
    console.error('Failed to fetch funnel:', err)
  }
}

const fetchAll = async () => {
  loading.value = true
  await Promise.all([fetchExperiment(), fetchFunnel()])
  loading.value = false
}

onMounted(fetchAll)
</script>

<template>
  <div>
    <VBtn
      variant="text"
      prepend-icon="tabler-arrow-left"
      class="mb-4"
      @click="router.push({ name: 'apps-experiments' })"
    >
      Back to Experiments
    </VBtn>

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="mb-6"
      closable
      @click:close="error = null"
    >
      {{ error }}
    </VAlert>

    <VProgressLinear
      v-if="loading"
      indeterminate
      class="mb-6"
    />

    <template v-if="experiment">
      <!-- Header -->
      <VCard class="mb-6">
        <VCardText>
          <div class="d-flex justify-space-between align-center flex-wrap gap-y-4">
            <div>
              <h4 class="text-h4 mb-1">
                {{ experiment.name }}
              </h4>
              <div class="text-body-1 text-medium-emphasis">
                {{ experiment.description }}
              </div>
            </div>
            <VChip
              :color="statusColor(experiment.status)"
              label
              size="large"
            >
              {{ experiment.status }}
            </VChip>
          </div>
        </VCardText>
      </VCard>

      <!-- Cohort Comparison -->
      <VCard class="mb-6">
        <VCardItem title="Cohort Comparison" />
        <VCardText>
          <VDataTable
            :headers="cohortHeaders"
            :items="experiment.arms"
            density="comfortable"
          >
            <template #item.avgMasteryDelta="{ item }">
              <VChip
                :color="item.avgMasteryDelta > 0 ? 'success' : item.avgMasteryDelta < 0 ? 'error' : 'default'"
                label
                size="small"
              >
                {{ item.avgMasteryDelta > 0 ? '+' : '' }}{{ item.avgMasteryDelta.toFixed(3) }}
              </VChip>
            </template>

            <template #item.confusionResolutionRate="{ item }">
              {{ (item.confusionResolutionRate * 100).toFixed(1) }}%
            </template>

            <template #item.avgTurns="{ item }">
              {{ item.avgTurns.toFixed(1) }}
            </template>

            <template #no-data>
              <div class="text-center py-4 text-disabled">
                No cohort data available
              </div>
            </template>
          </VDataTable>
        </VCardText>
      </VCard>

      <!-- Funnel Visualization -->
      <VCard>
        <VCardItem title="Engagement Funnel" />
        <VCardText>
          <div
            v-if="funnel.length"
            class="d-flex flex-column gap-y-4"
          >
            <div
              v-for="(stage, idx) in funnel"
              :key="stage.stage"
              class="d-flex align-center gap-x-4"
            >
              <div
                class="text-body-2 font-weight-medium"
                style="min-width: 100px;"
              >
                {{ stage.stage }}
              </div>
              <div class="flex-grow-1">
                <VProgressLinear
                  :model-value="funnelPercentage(stage.count)"
                  :color="funnelColors[idx % funnelColors.length]"
                  height="28"
                  rounded
                >
                  <template #default>
                    <span class="text-caption font-weight-medium">
                      {{ stage.count.toLocaleString() }} ({{ funnelPercentage(stage.count).toFixed(0) }}%)
                    </span>
                  </template>
                </VProgressLinear>
              </div>
            </div>
          </div>

          <div
            v-else-if="!loading"
            class="text-center py-8 text-disabled"
          >
            No funnel data available
          </div>
        </VCardText>
      </VCard>
    </template>
  </div>
</template>
