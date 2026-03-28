<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Pedagogy' } })

interface ExperimentArm {
  name: string
  treatmentCount: number
  controlCount: number
}

interface Experiment {
  name: string
  description: string
  status: string
  arms: ExperimentArm[]
  treatmentCount: number
  controlCount: number
}

const loading = ref(true)
const error = ref<string | null>(null)
const experiments = ref<Experiment[]>([])

const router = useRouter()

const headers = [
  { title: 'Name', key: 'name', sortable: true },
  { title: 'Status', key: 'status', sortable: true },
  { title: 'Arms', key: 'arms', sortable: false },
  { title: 'Treatment Count', key: 'treatmentCount', sortable: true },
  { title: 'Control Count', key: 'controlCount', sortable: true },
]

const fetchExperiments = async () => {
  loading.value = true
  try {
    const data = await $api<Experiment[]>('/admin/experiments')

    experiments.value = (data ?? []).map(exp => ({
      name: exp.name ?? '',
      description: exp.description ?? '',
      status: exp.status ?? 'unknown',
      arms: exp.arms ?? [],
      treatmentCount: exp.treatmentCount ?? 0,
      controlCount: exp.controlCount ?? 0,
    }))
  }
  catch (err: any) {
    console.error('Failed to fetch experiments:', err)
    error.value = err.message ?? 'Failed to load experiments'
  }
  finally {
    loading.value = false
  }
}

const statusColor = (status: string): string => {
  switch (status.toLowerCase()) {
    case 'running': return 'success'
    case 'paused': return 'warning'
    case 'completed': return 'info'
    case 'draft': return 'secondary'
    default: return 'default'
  }
}

const onRowClick = (_event: Event, row: { item: Experiment }) => {
  router.push({ name: 'apps-experiments-id', params: { id: row.item.name } })
}

onMounted(fetchExperiments)
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Experiments
        </h4>
        <div class="text-body-1">
          A/B experiments and pedagogical treatment arms
        </div>
      </div>
    </div>

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

    <VCard>
      <VCardText>
        <VProgressLinear
          v-if="loading"
          indeterminate
          class="mb-4"
        />

        <VDataTable
          :headers="headers"
          :items="experiments"
          :loading="loading"
          hover
          @click:row="onRowClick"
        >
          <template #item.status="{ item }">
            <VChip
              :color="statusColor(item.status)"
              label
              size="small"
            >
              {{ item.status }}
            </VChip>
          </template>

          <template #item.arms="{ item }">
            <div class="d-flex flex-wrap gap-1">
              <VChip
                v-for="arm in item.arms"
                :key="arm.name"
                size="small"
                variant="tonal"
                color="primary"
              >
                {{ arm.name }}
              </VChip>
            </div>
          </template>

          <template #no-data>
            <div class="text-center py-4 text-disabled">
              No experiments found
            </div>
          </template>
        </VDataTable>
      </VCardText>
    </VCard>
  </div>
</template>
