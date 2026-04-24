<script setup lang="ts">
/**
 * TENANCY-P3b: Mentor Dashboard — home page.
 * Shows institutes the mentor is associated with + quick stats.
 */
import { ref, onMounted } from 'vue'

interface InstituteOverview {
  id: string
  name: string
  type: string
  classroomCount: number
  studentCount: number
}

const institutes = ref<InstituteOverview[]>([])
const loading = ref(true)

onMounted(async () => {
  try {
    const res = await fetch('/api/mentor/institutes')
    if (res.ok) institutes.value = await res.json()
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div>
    <VCard class="mb-6">
      <VCardTitle class="text-h5">Mentor Dashboard</VCardTitle>
      <VCardText>Manage your institutes, classrooms, and students.</VCardText>
    </VCard>

    <VProgressLinear v-if="loading" indeterminate />

    <VRow v-else>
      <VCol v-for="inst in institutes" :key="inst.id" cols="12" md="6" lg="4">
        <VCard :to="`/mentor/institutes/${inst.id}`" hover>
          <VCardTitle>{{ inst.name }}</VCardTitle>
          <VCardSubtitle>{{ inst.type }}</VCardSubtitle>
          <VCardText>
            <div class="d-flex gap-4">
              <div><strong>{{ inst.classroomCount }}</strong> classrooms</div>
              <div><strong>{{ inst.studentCount }}</strong> students</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol v-if="institutes.length === 0" cols="12">
        <VCard>
          <VCardText class="text-center py-8">
            No institutes assigned. Contact your administrator.
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
