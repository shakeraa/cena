<script setup lang="ts">
// prr-013 Phase 2 retirement 2026-04-20:
// The student picker used to populate from `/admin/mastery/at-risk`, which
// was retired under ADR-0003 + RDY-080 (session-scope, in-surface only).
// Loading a cross-session "needs intervention" roster from a teacher
// dashboard violates both rules.
//
// The picker is reduced to a direct Student-ID entry field. A proper
// roster-driven picker (class → student) is tracked as a follow-up once
// the admin API ships a non-retired roster endpoint; until then, teachers
// enter the student ID they already know from their class list.
import MethodologyHierarchyPanel from '@/views/apps/pedagogy/MethodologyHierarchyPanel.vue'

definePage({
  meta: {
    action: 'read',
    subject: 'Pedagogy',
  },
})

const selectedStudentId = ref<string | null>(null)

// Direct student ID entry
const directStudentId = ref('')
const goToStudent = () => {
  if (directStudentId.value.trim()) {
    selectedStudentId.value = directStudentId.value.trim()
  }
}
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between flex-wrap gap-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Methodology Hierarchy
        </h4>
        <p class="text-body-1 text-medium-emphasis mb-0">
          Per-student methodology tracking: Subject &rarr; Topic &rarr; Concept with confidence gates
        </p>
      </div>
    </div>

    <VRow>
      <!-- Student Selection Panel -->
      <VCol
        cols="12"
        :md="selectedStudentId ? 4 : 12"
      >
        <VCard class="mb-4">
          <VCardItem title="Select Student" />
          <VDivider />
          <VCardText>
            <!-- Direct ID entry -->
            <div class="d-flex gap-2 mb-2">
              <VTextField
                v-model="directStudentId"
                label="Student ID"
                placeholder="Enter student ID..."
                variant="outlined"
                density="compact"
                hide-details
                @keyup.enter="goToStudent"
              />
              <VBtn
                color="primary"
                :disabled="!directStudentId.trim()"
                @click="goToStudent"
              >
                View
              </VBtn>
            </div>

            <p class="text-caption text-medium-emphasis mt-2 mb-0">
              Enter the student ID from your class roster.
            </p>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Methodology Hierarchy Detail -->
      <VCol
        v-if="selectedStudentId"
        cols="12"
        md="8"
      >
        <MethodologyHierarchyPanel :student-id="selectedStudentId" />
      </VCol>

      <!-- Empty state -->
      <VCol
        v-else
        cols="12"
      >
        <VCard>
          <VCardText class="d-flex flex-column align-center justify-center py-16">
            <VAvatar
              variant="tonal"
              color="primary"
              rounded
              size="64"
              class="mb-4"
            >
              <VIcon
                icon="tabler-hierarchy-3"
                size="36"
              />
            </VAvatar>
            <h5 class="text-h5 mb-2">
              Select a Student
            </h5>
            <p class="text-body-1 text-medium-emphasis text-center" style="max-inline-size: 400px;">
              Enter a student ID to view their methodology hierarchy and make overrides.
            </p>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
