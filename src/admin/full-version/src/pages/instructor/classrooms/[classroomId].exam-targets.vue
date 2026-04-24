<script setup lang="ts">
/**
 * PRR-236 — Classroom-assigned ExamTarget teacher UI.
 *
 * Teachers use this page to assign a single ExamTarget (exam + track +
 * sitting + weekly hours) to every student enrolled in a specific
 * classroom. Per ADR-0050, the assignment fans out server-side into one
 * ExamTargetAdded_V1 per student with Source=Classroom.
 *
 * Routing: /instructor/classrooms/:classroomId/exam-targets
 *
 * Scope (PRR-236 MVP):
 *  - POST a new assignment (re-submit is idempotent server-side).
 *  - Show the per-student outcome of the most recent submission.
 *  - Empty-roster warning is surfaced inline.
 *
 * Non-goals for this MVP (follow-ups):
 *  - Previewing which students already carry the target before submit.
 *  - Un-assign / archive UI for teacher — students archive from their
 *    own settings page (Source=Classroom does not lock them out).
 */
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'

interface SittingDto {
  academicYear: string
  season: 'Summer' | 'Winter'
  moed: 'A' | 'B' | 'C' | 'Special'
}

interface AssignRequest {
  examCode: string
  track: string | null
  sitting: SittingDto
  weeklyHoursDefault: number
  questionPaperCodes: string[] | null
}

interface PerStudent {
  studentAnonId: string
  kind: 'Assigned' | 'AlreadyAssigned' | 'Failed'
  targetId?: string
  error?: string
}

interface AssignResponse {
  classroomId: string
  examCode: string
  rosterSize: number
  studentsAssigned: number
  studentsAlreadyAssigned: number
  studentsFailed: number
  warning?: string
  perStudent: PerStudent[]
}

const route = useRoute()
const classroomId = computed(() => String(route.params.classroomId ?? ''))
const instituteId = ref<string>('')

// Teacher picks these; defaults chosen to match the most common Bagrut flow.
const examCode = ref<string>('BAGRUT_MATH_5U')
const track = ref<string | null>('5U')
const academicYear = ref<string>('תשפ״ו')
const season = ref<'Summer' | 'Winter'>('Summer')
const moed = ref<'A' | 'B' | 'C' | 'Special'>('A')
const weeklyHours = ref<number>(4)
const papersRaw = ref<string>('035581')

const submitting = ref(false)
const lastResult = ref<AssignResponse | null>(null)
const errorMessage = ref<string | null>(null)

function normalisePapers(): string[] | null {
  const parts = papersRaw.value
    .split(/[\s,]+/)
    .map(s => s.trim())
    .filter(Boolean)
  return parts.length > 0 ? parts : null
}

async function submit() {
  errorMessage.value = null
  if (!classroomId.value || !instituteId.value) {
    errorMessage.value = 'Classroom and institute id are required.'
    return
  }
  if (!examCode.value) {
    errorMessage.value = 'Exam code is required.'
    return
  }

  const body: AssignRequest = {
    examCode: examCode.value,
    track: track.value,
    sitting: {
      academicYear: academicYear.value,
      season: season.value,
      moed: moed.value,
    },
    weeklyHoursDefault: weeklyHours.value,
    questionPaperCodes: normalisePapers(),
  }

  const url =
    `/api/admin/institutes/${encodeURIComponent(instituteId.value)}` +
    `/classrooms/${encodeURIComponent(classroomId.value)}/assigned-targets`

  submitting.value = true
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (!res.ok) {
      const text = await res.text()
      errorMessage.value = `${res.status}: ${text}`
      lastResult.value = null
      return
    }
    lastResult.value = (await res.json()) as AssignResponse
  } catch (err) {
    errorMessage.value = err instanceof Error ? err.message : String(err)
  } finally {
    submitting.value = false
  }
}

function kindColor(kind: PerStudent['kind']): string {
  if (kind === 'Assigned') return 'success'
  if (kind === 'AlreadyAssigned') return 'info'
  return 'error'
}
</script>

<template>
  <div>
    <VCard class="mb-6">
      <VCardTitle class="text-h5">Classroom exam-target assignment</VCardTitle>
      <VCardSubtitle>
        Assign a Bagrut / PET / SAT target to every enrolled student in classroom
        <code>{{ classroomId }}</code>. Re-submitting the same exam/track/sitting
        is a no-op — students keep their existing target.
      </VCardSubtitle>
    </VCard>

    <VCard class="mb-6">
      <VCardTitle>Assignment</VCardTitle>
      <VCardText>
        <VRow>
          <VCol cols="12" md="6">
            <VTextField
              v-model="instituteId"
              label="Institute id"
              hint="From your Firebase claim (institute_id)"
              required
            />
          </VCol>
          <VCol cols="12" md="6">
            <VTextField
              v-model="examCode"
              label="Exam code"
              hint="e.g. BAGRUT_MATH_5U, PET, SAT_MATH"
              required
            />
          </VCol>
          <VCol cols="12" md="4">
            <VTextField v-model="track" label="Track" hint="5U / 4U / 3U / 2U / ModuleA…" />
          </VCol>
          <VCol cols="12" md="4">
            <VTextField
              v-model="academicYear"
              label="Academic year"
              hint="e.g. תשפ״ו or 2026-2027"
            />
          </VCol>
          <VCol cols="12" md="2">
            <VSelect
              v-model="season"
              :items="['Summer', 'Winter']"
              label="Season"
            />
          </VCol>
          <VCol cols="12" md="2">
            <VSelect
              v-model="moed"
              :items="['A', 'B', 'C', 'Special']"
              label="Moed"
            />
          </VCol>
          <VCol cols="12" md="4">
            <VTextField
              v-model.number="weeklyHours"
              type="number"
              min="1"
              max="40"
              label="Default weekly hours"
            />
          </VCol>
          <VCol cols="12" md="8">
            <VTextField
              v-model="papersRaw"
              label="Question paper codes"
              hint="Comma-separated שאלון codes (Bagrut only), e.g. 035581, 035582"
            />
          </VCol>
          <VCol cols="12">
            <VBtn :loading="submitting" color="primary" @click="submit">
              Assign to classroom roster
            </VBtn>
          </VCol>
        </VRow>
      </VCardText>
    </VCard>

    <VAlert v-if="errorMessage" type="error" class="mb-4">
      {{ errorMessage }}
    </VAlert>

    <VCard v-if="lastResult" class="mb-6">
      <VCardTitle>Last assignment result</VCardTitle>
      <VCardText>
        <VAlert
          v-if="lastResult.warning === 'roster-empty'"
          type="warning"
          class="mb-4"
          text="Roster is empty for this classroom. No students were assigned."
        />
        <div class="d-flex gap-4 mb-4 flex-wrap">
          <VChip>Roster: <strong class="ml-1">{{ lastResult.rosterSize }}</strong></VChip>
          <VChip color="success">
            Assigned: <strong class="ml-1">{{ lastResult.studentsAssigned }}</strong>
          </VChip>
          <VChip color="info">
            Already assigned:
            <strong class="ml-1">{{ lastResult.studentsAlreadyAssigned }}</strong>
          </VChip>
          <VChip v-if="lastResult.studentsFailed > 0" color="error">
            Failed: <strong class="ml-1">{{ lastResult.studentsFailed }}</strong>
          </VChip>
        </div>
        <VTable v-if="lastResult.perStudent.length > 0">
          <thead>
            <tr>
              <th>Student</th>
              <th>Outcome</th>
              <th>Target id / error</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in lastResult.perStudent" :key="row.studentAnonId">
              <td><code>{{ row.studentAnonId }}</code></td>
              <td><VChip size="small" :color="kindColor(row.kind)">{{ row.kind }}</VChip></td>
              <td>
                <code v-if="row.targetId">{{ row.targetId }}</code>
                <span v-else-if="row.error" class="text-error">{{ row.error }}</span>
                <span v-else>—</span>
              </td>
            </tr>
          </tbody>
        </VTable>
      </VCardText>
    </VCard>
  </div>
</template>
