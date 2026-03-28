<script setup lang="ts">
import MethodologyHierarchyPanel from '@/views/apps/pedagogy/MethodologyHierarchyPanel.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Pedagogy',
  },
})

// --- Student search ---
interface StudentSearchResult {
  studentId: string
  studentName: string
  avgMastery: number
  totalConcepts: number
  masteredCount: number
}

const searchQuery = ref('')
const searchLoading = ref(false)
const searchResults = ref<StudentSearchResult[]>([])
const selectedStudentId = ref<string | null>(null)

const searchStudents = async () => {
  if (!searchQuery.value.trim()) return
  searchLoading.value = true
  try {
    const data = await $api<{ students: StudentSearchResult[] }>(
      `/admin/mastery/at-risk`,
    )
    // Filter by search query (client-side for now)
    searchResults.value = (data.students ?? []).filter(s =>
      s.studentId.toLowerCase().includes(searchQuery.value.toLowerCase())
      || (s.studentName ?? '').toLowerCase().includes(searchQuery.value.toLowerCase()),
    )
  }
  catch (err: any) {
    console.error('Student search failed:', err)
    searchResults.value = []
  }
  finally {
    searchLoading.value = false
  }
}

// Quick student list for initial view
const recentStudents = ref<StudentSearchResult[]>([])
const loadingRecent = ref(true)

const fetchRecentStudents = async () => {
  loadingRecent.value = true
  try {
    const data = await $api<{ students: { studentId: string; studentName: string; riskLevel: string; currentAvgMastery: number }[] }>(
      '/admin/mastery/at-risk',
    )
    recentStudents.value = (data.students ?? []).map(s => ({
      studentId: s.studentId,
      studentName: s.studentName,
      avgMastery: s.currentAvgMastery ?? 0,
      totalConcepts: 0,
      masteredCount: 0,
    }))
  }
  catch (err: any) {
    console.error('Failed to fetch student list:', err)
  }
  finally {
    loadingRecent.value = false
  }
}

onMounted(fetchRecentStudents)

const selectStudent = (id: string) => {
  selectedStudentId.value = id
}

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
            <div class="d-flex gap-2 mb-4">
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

            <VDivider class="mb-4" />

            <!-- Recent / At-risk students -->
            <h6 class="text-subtitle-2 text-medium-emphasis mb-2">
              At-Risk Students
            </h6>

            <VProgressLinear
              v-if="loadingRecent"
              indeterminate
              color="primary"
              class="mb-2"
            />

            <VList
              v-else-if="recentStudents.length"
              density="compact"
              nav
            >
              <VListItem
                v-for="s in recentStudents"
                :key="s.studentId"
                :active="selectedStudentId === s.studentId"
                @click="selectStudent(s.studentId)"
              >
                <template #prepend>
                  <VAvatar
                    color="primary"
                    variant="tonal"
                    size="32"
                  >
                    <span class="text-caption">{{ s.studentName?.charAt(0) ?? '?' }}</span>
                  </VAvatar>
                </template>

                <VListItemTitle class="text-body-2 font-weight-medium">
                  {{ s.studentName }}
                </VListItemTitle>
                <VListItemSubtitle class="text-caption">
                  {{ s.studentId }}
                </VListItemSubtitle>

                <template #append>
                  <VChip
                    :color="s.avgMastery >= 0.7 ? 'success' : s.avgMastery >= 0.4 ? 'warning' : 'error'"
                    label
                    size="x-small"
                  >
                    {{ (s.avgMastery * 100).toFixed(0) }}%
                  </VChip>
                </template>
              </VListItem>
            </VList>

            <div
              v-else
              class="text-disabled text-center pa-4"
            >
              No students found
            </div>
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
              Enter a student ID or select from the at-risk list to view their methodology hierarchy and make overrides.
            </p>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
