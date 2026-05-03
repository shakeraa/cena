<script setup lang="ts">
// =============================================================================
// Cena Platform — Teacher Schedule Override (prr-150 skeleton)
//
// Phase 1 skeleton that wires to:
//   POST /api/admin/teacher/override/pin-topic
//   POST /api/admin/teacher/override/budget
//   POST /api/admin/teacher/override/motivation
//
// The form is intentionally thin for Phase 1: student id is typed, topic
// picker is a plain text input, no mastery-preview. Phase 2 follow-up will
// integrate a roster selector + topic hierarchy dropdown, tracked as a
// prr-150 follow-up task.
//
// TODO(prr-150-ui): replace StudentAnonId text input with the shared
// RosterPicker component (tenant-scoped) once that ships; wire
// mastery-preview from ClassMasteryHeatmap so the teacher sees current
// per-topic mastery before applying a pin.
// =============================================================================
import { ref } from 'vue'
import { $api } from '@/utils/api'

type MotivationProfile = 'Neutral' | 'Confident' | 'Anxious'

const studentAnonId = ref('')
const topicSlug = ref('')
const pinnedSessionCount = ref(3)
const pinRationale = ref('')

const weeklyBudgetHours = ref(5)
const budgetRationale = ref('')

const sessionTypeScope = ref('all')
const overrideProfile = ref<MotivationProfile>('Neutral')
const motivationRationale = ref('')

const busy = ref(false)
const lastResult = ref<string | null>(null)
const lastError = ref<string | null>(null)

async function call(path: string, body: Record<string, unknown>) {
  busy.value = true
  lastError.value = null
  lastResult.value = null
  try {
    const res = await $api(path, { method: 'POST', body })
    lastResult.value = JSON.stringify(res)
  }
  catch (err: any) {
    lastError.value = err?.message ?? 'request failed'
  }
  finally {
    busy.value = false
  }
}

async function submitPin() {
  await call('/admin/teacher/override/pin-topic', {
    studentAnonId: studentAnonId.value,
    topicSlug: topicSlug.value,
    pinnedSessionCount: pinnedSessionCount.value,
    rationale: pinRationale.value,
  })
}

async function submitBudget() {
  await call('/admin/teacher/override/budget', {
    studentAnonId: studentAnonId.value,
    weeklyBudgetHours: weeklyBudgetHours.value,
    rationale: budgetRationale.value,
  })
}

async function submitMotivation() {
  await call('/admin/teacher/override/motivation', {
    studentAnonId: studentAnonId.value,
    sessionTypeScope: sessionTypeScope.value,
    overrideProfile: overrideProfile.value,
    rationale: motivationRationale.value,
  })
}
</script>

<template>
  <VCard>
    <VCardTitle>Student Schedule Override (prr-150 skeleton)</VCardTitle>
    <VCardText>
      <p class="text-caption mb-4">
        Tenant-scoped. Cross-institute attempts return 403 and are logged to SIEM.
      </p>

      <VTextField
        v-model="studentAnonId"
        label="Student Anon ID"
        density="compact"
        class="mb-4"
      />

      <VDivider class="mb-3" />
      <h3 class="text-subtitle-1 mb-2">Pin topic</h3>
      <VTextField v-model="topicSlug" label="Topic slug" density="compact" class="mb-2" />
      <VTextField
        v-model.number="pinnedSessionCount"
        type="number"
        label="Sessions (1..20)"
        density="compact"
        class="mb-2"
      />
      <VTextarea v-model="pinRationale" label="Rationale" rows="2" density="compact" class="mb-2" />
      <VBtn :disabled="busy" color="primary" @click="submitPin">Pin Topic</VBtn>

      <VDivider class="my-4" />
      <h3 class="text-subtitle-1 mb-2">Adjust weekly budget</h3>
      <VTextField
        v-model.number="weeklyBudgetHours"
        type="number"
        label="Weekly hours (1..40)"
        density="compact"
        class="mb-2"
      />
      <VTextarea v-model="budgetRationale" label="Rationale" rows="2" density="compact" class="mb-2" />
      <VBtn :disabled="busy" color="primary" @click="submitBudget">Adjust Budget</VBtn>

      <VDivider class="my-4" />
      <h3 class="text-subtitle-1 mb-2">Override motivation profile</h3>
      <VTextField
        v-model="sessionTypeScope"
        label="Session-type scope (all / diagnostic / drill / ...)"
        density="compact"
        class="mb-2"
      />
      <VSelect
        v-model="overrideProfile"
        :items="['Neutral', 'Confident', 'Anxious']"
        label="Override profile"
        density="compact"
        class="mb-2"
      />
      <VTextarea v-model="motivationRationale" label="Rationale" rows="2" density="compact" class="mb-2" />
      <VBtn :disabled="busy" color="primary" @click="submitMotivation">Override Motivation</VBtn>

      <VDivider class="my-4" />
      <div v-if="lastResult" class="text-success">Result: {{ lastResult }}</div>
      <div v-if="lastError" class="text-error">Error: {{ lastError }}</div>
    </VCardText>
  </VCard>
</template>
