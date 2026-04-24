<script setup lang="ts">
import StagnationInsightsPanel from './StagnationInsightsPanel.vue'
import { $api } from '@/utils/api'

interface StagnatingStudent {
  studentId: string
  studentName: string
  conceptCluster: string
  compositeScore: number
  attempts: number
  daysStuck: number
  mentorResistant: boolean
}

interface MentorResistantResponse {
  students: StagnatingStudent[]
}

const loading = ref(true)
const error = ref<string | null>(null)
const students = ref<StagnatingStudent[]>([])

const fetchStagnatingStudents = async () => {
  loading.value = true
  error.value = null
  try {
    const data = await $api<MentorResistantResponse>(
      '/admin/pedagogy/mentor-resistant',
    )

    students.value = data.students ?? []
  }
  catch (err: any) {
    error.value = err.message ?? 'Failed to load stagnating students'
    console.error('Failed to fetch stagnating students:', err)
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchStagnatingStudents)

const headers = [
  { title: 'Student', key: 'studentName' },
  { title: 'Concept Cluster', key: 'conceptCluster' },
  { title: 'Composite Score', key: 'compositeScore' },
  { title: 'Attempts', key: 'attempts' },
  { title: 'Days Stuck', key: 'daysStuck' },
  { title: 'Flags', key: 'flags', sortable: false },
  { title: 'Action', key: 'action', sortable: false },
]

const expandedRows = ref<string[]>([])

const toggleRow = (studentId: string) => {
  const idx = expandedRows.value.indexOf(studentId)
  if (idx >= 0)
    expandedRows.value.splice(idx, 1)
  else
    expandedRows.value.push(studentId)
}

const scoreColor = (score: number): string => {
  if (score >= 0.7) return 'success'
  if (score >= 0.4) return 'warning'
  return 'error'
}

const daysStuckColor = (days: number): string => {
  if (days > 14) return 'error'
  if (days > 7) return 'warning'
  return 'secondary'
}

defineExpose({ refresh: fetchStagnatingStudents })
</script>

<template>
  <VCard>
    <VCardItem title="Stagnating Students">
      <template #subtitle>
        Students stuck on concept clusters with low composite scores
      </template>
    </VCardItem>

    <VDivider />

    <VAlert
      v-if="error"
      type="error"
      variant="tonal"
      class="ma-4"
    >
      {{ error }}
    </VAlert>

    <VDataTable
      v-model:expanded="expandedRows"
      :headers="headers"
      :items="students"
      :loading="loading"
      item-value="studentId"
      show-expand
      class="text-no-wrap"
    >
      <template #item.studentName="{ item }">
        <RouterLink
          :to="{ path: `/apps/mastery/student/${item.studentId}` }"
          class="text-body-1 font-weight-medium text-high-emphasis text-link"
        >
          {{ item.studentName }}
        </RouterLink>
      </template>

      <template #item.conceptCluster="{ item }">
        <VChip
          label
          size="small"
          color="primary"
          variant="tonal"
        >
          {{ item.conceptCluster }}
        </VChip>
      </template>

      <template #item.compositeScore="{ item }">
        <div
          class="d-flex align-center gap-x-3"
          style="min-inline-size: 180px;"
        >
          <VProgressLinear
            :model-value="item.compositeScore * 100"
            :color="scoreColor(item.compositeScore)"
            rounded
            :height="8"
            class="flex-grow-1"
          />
          <span class="text-body-2 font-weight-medium">
            {{ (item.compositeScore * 100).toFixed(0) }}%
          </span>
        </div>
      </template>

      <template #item.attempts="{ item }">
        <span class="text-body-2 font-weight-medium">
          {{ item.attempts }}
        </span>
      </template>

      <template #item.daysStuck="{ item }">
        <VChip
          :color="daysStuckColor(item.daysStuck)"
          label
          size="small"
        >
          {{ item.daysStuck }}d
        </VChip>
      </template>

      <template #item.flags="{ item }">
        <VChip
          v-if="item.mentorResistant"
          color="error"
          variant="tonal"
          label
          size="small"
        >
          <VIcon
            icon="tabler-alert-triangle"
            size="14"
            start
          />
          Mentor-Resistant
        </VChip>
        <span
          v-else
          class="text-disabled"
        >--</span>
      </template>

      <template #item.action="{ item }">
        <div class="d-flex gap-x-1">
          <VBtn
            variant="text"
            color="primary"
            size="small"
            @click="toggleRow(item.studentId)"
          >
            <VIcon :icon="expandedRows.includes(item.studentId) ? 'ri-arrow-up-s-line' : 'ri-search-eye-line'" class="me-1" />
            {{ expandedRows.includes(item.studentId) ? 'Hide' : 'Analyze' }}
          </VBtn>
          <VBtn
            variant="text"
            color="default"
            size="small"
            :to="{ path: `/apps/mastery/student/${item.studentId}` }"
          >
            Mastery
          </VBtn>
        </div>
      </template>

      <!-- Expanded row: stagnation root-cause analysis panel -->
      <template #expanded-row="{ item }">
        <td :colspan="headers.length" class="pa-4">
          <StagnationInsightsPanel
            :student-id="item.studentId"
            :concept-id="item.conceptCluster"
          />
        </td>
      </template>

      <template #no-data>
        <div class="text-center pa-4 text-disabled">
          No stagnating students found
        </div>
      </template>
    </VDataTable>
  </VCard>
</template>
