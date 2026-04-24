<script setup lang="ts">
/**
 * TENANCY-P3c: Instructor-scoped view — classroom-only subset of mentor dashboard.
 * Instructors see only their assigned classrooms, not the full institute.
 * Limited analytics (their classrooms only, no institute-level data).
 */
import { ref, onMounted } from 'vue'

interface ClassroomOverview {
  id: string
  name: string
  mode: string
  status: string
  studentCount: number
  joinCode: string
}

const classrooms = ref<ClassroomOverview[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    const res = await fetch('/api/instructor/classrooms')
    if (res.ok) classrooms.value = await res.json()
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div>
    <VCard class="mb-6">
      <VCardTitle class="text-h5">My Classrooms</VCardTitle>
      <VCardText>View your assigned classrooms and student progress.</VCardText>
    </VCard>

    <VProgressLinear v-if="loading" indeterminate />

    <VRow v-else>
      <VCol v-for="cls in classrooms" :key="cls.id" cols="12" md="6" lg="4">
        <VCard :to="`/instructor/classrooms/${cls.id}`" hover>
          <VCardTitle>{{ cls.name }}</VCardTitle>
          <VCardSubtitle>{{ cls.mode }}</VCardSubtitle>
          <VCardText>
            <div class="d-flex gap-4 mb-2">
              <VChip size="small" :color="cls.status === 'Active' ? 'success' : 'default'">
                {{ cls.status }}
              </VChip>
              <span><strong>{{ cls.studentCount }}</strong> students</span>
            </div>
            <div>Join code: <code>{{ cls.joinCode }}</code></div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol v-if="classrooms.length === 0" cols="12">
        <VCard>
          <VCardText class="text-center py-8">
            No classrooms assigned. Contact your institute mentor.
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
