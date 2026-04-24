<script setup lang="ts">
import { $api } from '@/utils/api'

interface HeatmapCell {
  timeSlot: string
  focusScore: number
}

interface HeatmapRow {
  studentId: string
  studentName: string
  cells: HeatmapCell[]
}

interface HeatmapData {
  timeSlots: string[]
  rows: HeatmapRow[]
}

const props = defineProps<{
  classId: string
}>()

const loading = ref(true)
const error = ref<string | null>(null)
const heatmapData = ref<HeatmapData>({ timeSlots: [], rows: [] })

const cellColor = (score: number): string => {
  if (score >= 80) return '#4CAF50'
  if (score >= 60) return '#8BC34A'
  if (score >= 40) return '#FFC107'
  if (score >= 20) return '#FF9800'
  return '#F44336'
}

const cellTextColor = (score: number): string => {
  if (score >= 60) return '#1a1a1a'
  return '#ffffff'
}

const fetchHeatmap = async () => {
  loading.value = true
  try {
    const data = await $api(`/admin/focus/classes/${props.classId}/heatmap`)

    heatmapData.value = {
      timeSlots: data.timeSlots ?? [],
      rows: (data.rows ?? []).map((r: any) => ({
        studentId: r.studentId,
        studentName: r.studentName,
        cells: r.cells ?? [],
      })),
    }

    error.value = null
  }
  catch (err: any) {
    console.error('Failed to fetch focus heatmap:', err)
    error.value = err.message ?? 'Failed to load heatmap data'
  }
  finally {
    loading.value = false
  }
}

onMounted(fetchHeatmap)

defineExpose({ refresh: fetchHeatmap })
</script>

<template>
  <VCard :loading="loading">
    <VCardItem
      title="Class Focus Heatmap"
      subtitle="Student focus scores by time slot (green = high, red = low)"
    />

    <VCardText>
      <VAlert
        v-if="error"
        type="error"
        variant="tonal"
        class="mb-4"
      >
        {{ error }}
      </VAlert>

      <div
        v-if="heatmapData.rows.length > 0"
        class="heatmap-container"
      >
        <table class="heatmap-table">
          <thead>
            <tr>
              <th class="student-header">
                Student
              </th>
              <th
                v-for="slot in heatmapData.timeSlots"
                :key="slot"
                class="slot-header"
              >
                {{ slot }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in heatmapData.rows"
              :key="row.studentId"
            >
              <td class="student-name">
                <RouterLink
                  :to="{ name: 'apps-focus-student-id', params: { id: row.studentId } }"
                  class="text-body-2 font-weight-medium text-high-emphasis"
                >
                  {{ row.studentName }}
                </RouterLink>
              </td>
              <td
                v-for="(cell, cellIdx) in row.cells"
                :key="cellIdx"
                class="heatmap-cell"
                :style="{
                  backgroundColor: cellColor(cell.focusScore),
                  color: cellTextColor(cell.focusScore),
                }"
                :title="`${row.studentName} — ${cell.timeSlot}: ${cell.focusScore}`"
              >
                {{ cell.focusScore }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div
        v-else-if="!loading && !error"
        class="d-flex justify-center align-center py-8"
      >
        <span class="text-body-1 text-disabled">No heatmap data available</span>
      </div>
    </VCardText>
  </VCard>
</template>

<style lang="scss" scoped>
.heatmap-container {
  overflow-x: auto;
}

.heatmap-table {
  border-collapse: collapse;
  font-size: 0.8125rem;
  inline-size: 100%;

  th,
  td {
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    padding-block: 6px;
    padding-inline: 10px;
    text-align: center;
    white-space: nowrap;
  }

  .student-header,
  .student-name {
    position: sticky;
    background: rgb(var(--v-theme-surface));
    inset-inline-start: 0;
    text-align: start;
    z-index: 1;
  }

  .student-header {
    z-index: 2;
  }

  .slot-header {
    background: rgb(var(--v-theme-surface));
    color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
    font-size: 0.75rem;
    font-weight: 500;
  }

  .heatmap-cell {
    font-size: 0.75rem;
    font-weight: 600;
    min-inline-size: 48px;
    transition: opacity 0.15s;

    &:hover {
      opacity: 0.85;
    }
  }
}
</style>
