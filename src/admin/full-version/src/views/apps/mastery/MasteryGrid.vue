<script setup lang="ts">
interface MasteryCell {
  conceptId: string
  mastery: number | null
}

interface StudentRow {
  studentId: string
  studentName: string
  cells: MasteryCell[]
}

interface Props {
  students: StudentRow[]
  concepts: Array<{ id: string; name: string }>
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
})

const getMasteryColor = (mastery: number | null): string => {
  if (mastery === null) return 'rgba(var(--v-theme-on-surface), 0.08)'
  if (mastery >= 0.8) return 'rgba(var(--v-theme-success), 0.6)'
  if (mastery >= 0.4) return 'rgba(var(--v-theme-warning), 0.6)'
  return 'rgba(var(--v-theme-error), 0.6)'
}

const getMasteryTextColor = (mastery: number | null): string => {
  if (mastery === null) return 'rgba(var(--v-theme-on-surface), 0.38)'
  return 'rgba(var(--v-theme-on-surface), var(--v-high-emphasis-opacity))'
}

const formatMastery = (mastery: number | null): string => {
  if (mastery === null) return '--'
  return mastery.toFixed(2)
}
</script>

<template>
  <div class="mastery-grid-wrapper">
    <VProgressLinear
      v-if="props.loading"
      indeterminate
      color="primary"
    />

    <div
      v-if="!props.loading && !props.students.length"
      class="d-flex align-center justify-center pa-8"
    >
      <span class="text-disabled">No mastery data available</span>
    </div>

    <div
      v-else
      class="mastery-grid-scroll"
    >
      <table class="mastery-grid-table">
        <thead>
          <tr>
            <th class="mastery-grid-sticky-col mastery-grid-header-cell">
              Student
            </th>
            <th
              v-for="concept in props.concepts"
              :key="concept.id"
              class="mastery-grid-header-cell"
            >
              <div class="mastery-grid-concept-name">
                {{ concept.name }}
              </div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="student in props.students"
            :key="student.studentId"
          >
            <td class="mastery-grid-sticky-col mastery-grid-student-cell">
              <RouterLink
                :to="{ path: `/apps/mastery/student/${student.studentId}` }"
                class="text-link font-weight-medium"
              >
                {{ student.studentName }}
              </RouterLink>
            </td>
            <td
              v-for="cell in student.cells"
              :key="cell.conceptId"
              class="mastery-grid-cell"
              :style="{
                backgroundColor: getMasteryColor(cell.mastery),
                color: getMasteryTextColor(cell.mastery),
              }"
            >
              {{ formatMastery(cell.mastery) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.mastery-grid-wrapper {
  position: relative;
  overflow: hidden;
  border-radius: 6px;
}

.mastery-grid-scroll {
  overflow-x: auto;
  max-block-size: 600px;
  overflow-y: auto;
}

.mastery-grid-table {
  border-collapse: collapse;
  min-inline-size: 100%;
  font-size: 0.8125rem;

  th,
  td {
    padding-block: 6px;
    padding-inline: 10px;
    text-align: center;
    white-space: nowrap;
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  }
}

.mastery-grid-header-cell {
  position: sticky;
  inset-block-start: 0;
  z-index: 2;
  background: rgb(var(--v-theme-surface));
  font-weight: 600;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.mastery-grid-concept-name {
  max-inline-size: 100px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mastery-grid-sticky-col {
  position: sticky;
  inset-inline-start: 0;
  z-index: 3;
  background: rgb(var(--v-theme-surface));
  min-inline-size: 150px;
  text-align: start;
}

.mastery-grid-student-cell {
  font-weight: 500;
}

.mastery-grid-cell {
  min-inline-size: 60px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}
</style>
