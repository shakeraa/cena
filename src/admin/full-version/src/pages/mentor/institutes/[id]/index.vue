<script setup lang="ts">
/**
 * TENANCY-P3b: Institute detail page — classrooms + programs + analytics.
 */
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const instituteId = route.params.id as string

const institute = ref<any>(null)
const classrooms = ref<any[]>([])
const loading = ref(true)
const activeTab = ref('classrooms')

onMounted(async () => {
  try {
    const [instRes, classRes] = await Promise.all([
      fetch(`/api/mentor/institutes/${instituteId}`),
      fetch(`/api/mentor/institutes/${instituteId}/classrooms`),
    ])
    if (instRes.ok) institute.value = await instRes.json()
    if (classRes.ok) classrooms.value = await classRes.json()
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div>
    <VProgressLinear v-if="loading" indeterminate />

    <template v-else-if="institute">
      <VCard class="mb-6">
        <VCardTitle class="text-h5">{{ institute.name }}</VCardTitle>
        <VCardSubtitle>{{ institute.type }} — {{ institute.country }}</VCardSubtitle>
      </VCard>

      <VTabs v-model="activeTab" class="mb-4">
        <VTab value="classrooms">Classrooms</VTab>
        <VTab value="programs">Programs</VTab>
        <VTab value="analytics">Analytics</VTab>
      </VTabs>

      <VWindow v-model="activeTab">
        <VWindowItem value="classrooms">
          <VRow>
            <VCol v-for="cls in classrooms" :key="cls.id" cols="12" md="6">
              <VCard :to="`/mentor/institutes/${instituteId}/classrooms/${cls.id}`" hover>
                <VCardTitle>{{ cls.name }}</VCardTitle>
                <VCardSubtitle>{{ cls.mode }} — {{ cls.status }}</VCardSubtitle>
                <VCardText>
                  <div>Join code: <code>{{ cls.joinCode }}</code></div>
                  <div>Students: {{ cls.studentCount || 0 }}</div>
                </VCardText>
              </VCard>
            </VCol>
          </VRow>
        </VWindowItem>

        <VWindowItem value="programs">
          <VCard>
            <VCardText class="text-center py-8">
              Program management coming in Phase 2.
            </VCardText>
          </VCard>
        </VWindowItem>

        <VWindowItem value="analytics">
          <VCard>
            <VCardText class="text-center py-8">
              Cross-classroom analytics will be available here.
            </VCardText>
          </VCard>
        </VWindowItem>
      </VWindow>
    </template>
  </div>
</template>
