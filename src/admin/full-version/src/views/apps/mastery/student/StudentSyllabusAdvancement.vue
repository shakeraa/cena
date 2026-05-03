<script setup lang="ts">
// =============================================================================
// Teacher view — per-student syllabus advancement with override action
// (RDY-061 Phase 4 admin side).
//
// Loads GET /api/admin/students/{id}/advancement?trackId=... + the track
// syllabus; renders status per chapter with pacing-delta ("X chapters
// behind expected"). Teachers can override a chapter's status with a
// required rationale — audit-logged server-side.
// =============================================================================
import { computed, onMounted, ref, watch } from 'vue'
import { firebaseAuth } from '@/plugins/firebase'

interface Props { studentId: string; trackId?: string }
const props = defineProps<Props>()

const effectiveTrackId = ref<string | null>(props.trackId ?? null)
const syllabus = ref<any | null>(null)
const advancement = ref<any | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const overrideOpen = ref(false)
const overrideChapterId = ref('')
const overrideNewStatus = ref('Unlocked')
const overrideRationale = ref('')
const overrideBusy = ref(false)
const overrideError = ref<string | null>(null)

async function authedFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const user = firebaseAuth.currentUser
  const headers: Record<string, string> = {
    'Accept': 'application/json',
    'Content-Type': 'application/json',
    ...((init.headers ?? {}) as Record<string, string>),
  }
  if (user) headers.Authorization = `Bearer ${await user.getIdToken()}`
  return fetch(url, { ...init, headers })
}

async function load() {
  loading.value = true
  error.value = null
  try {
    if (!effectiveTrackId.value) {
      error.value = 'No track selected.'
      return
    }
    const [sylRes, advRes] = await Promise.all([
      authedFetch(`/api/admin/tracks/${encodeURIComponent(effectiveTrackId.value)}/syllabus`),
      authedFetch(`/api/admin/students/${encodeURIComponent(props.studentId)}/advancement?trackId=${encodeURIComponent(effectiveTrackId.value)}`),
    ])
    if (!sylRes.ok) throw new Error(`syllabus ${sylRes.status}`)
    syllabus.value = await sylRes.json()
    if (advRes.ok) advancement.value = await advRes.json()
    else if (advRes.status === 404) advancement.value = null
    else throw new Error(`advancement ${advRes.status}`)
  }
  catch (e: unknown) {
    error.value = (e as Error).message
  }
  finally {
    loading.value = false
  }
}

async function ensureStarted() {
  overrideBusy.value = true
  try {
    const r = await authedFetch(
      `/api/admin/students/${encodeURIComponent(props.studentId)}/advancement/ensure-started`,
      { method: 'POST', body: JSON.stringify({ trackId: effectiveTrackId.value }) },
    )
    if (!r.ok) throw new Error(`ensure-started ${r.status}`)
    await load()
  }
  finally { overrideBusy.value = false }
}

async function applyOverride() {
  overrideError.value = null
  if (overrideRationale.value.trim().length < 10) {
    overrideError.value = 'Rationale must be at least 10 characters.'
    return
  }
  overrideBusy.value = true
  try {
    const r = await authedFetch(
      `/api/admin/students/${encodeURIComponent(props.studentId)}/advancement/override`,
      {
        method: 'POST',
        body: JSON.stringify({
          trackId: effectiveTrackId.value,
          chapterId: overrideChapterId.value,
          newStatus: overrideNewStatus.value,
          rationale: overrideRationale.value.trim(),
        }),
      },
    )
    if (!r.ok) {
      const body = await r.json().catch(() => ({}))
      throw new Error(body?.message ?? `HTTP ${r.status}`)
    }
    overrideOpen.value = false
    overrideRationale.value = ''
    await load()
  }
  catch (e: unknown) {
    overrideError.value = (e as Error).message
  }
  finally { overrideBusy.value = false }
}

function openOverride(chapterId: string, currentStatus: string) {
  overrideChapterId.value = chapterId
  overrideNewStatus.value = currentStatus === 'Locked' ? 'Unlocked' : currentStatus
  overrideRationale.value = ''
  overrideError.value = null
  overrideOpen.value = true
}

const statusByChapter = computed<Record<string, { status: string; lastUpdated?: string; questionsAttempted?: number; retention?: number }>>(() => {
  const m: Record<string, { status: string; lastUpdated?: string; questionsAttempted?: number; retention?: number }> = {}
  if (advancement.value) {
    for (const c of advancement.value.chapters) {
      m[c.chapterId] = {
        status: c.status,
        lastUpdated: c.lastUpdated,
        questionsAttempted: c.questionsAttempted,
        retention: c.retention,
      }
    }
  }
  return m
})

const statusColor = (s: string) => ({
  Locked: 'default', Unlocked: 'info', InProgress: 'primary',
  Mastered: 'success', NeedsReview: 'warning',
})[s] || 'default'

onMounted(load)
watch(() => props.studentId, load)
</script>

<template>
  <VCard>
    <VCardTitle class="d-flex align-center">
      <VIcon icon="tabler-book-2" start />
      Syllabus Advancement
      <VSpacer />
      <span v-if="effectiveTrackId" class="text-caption text-medium-emphasis">
        {{ effectiveTrackId }}
      </span>
    </VCardTitle>

    <VCardText>
      <div v-if="loading" class="d-flex justify-center pa-6">
        <VProgressCircular indeterminate />
      </div>

      <VAlert v-else-if="error" type="error" variant="tonal">{{ error }}</VAlert>

      <div v-else-if="syllabus && !advancement">
        <VAlert type="info" variant="tonal" class="mb-3">
          Advancement not started for this student on this track.
        </VAlert>
        <VBtn color="primary" :loading="overrideBusy" @click="ensureStarted">
          Start advancement
        </VBtn>
      </div>

      <VTable v-else-if="syllabus && advancement" density="compact">
        <thead>
          <tr>
            <th>#</th>
            <th>Chapter</th>
            <th>Ministry</th>
            <th>Status</th>
            <th class="text-end">Qs</th>
            <th class="text-end">Retention</th>
            <th>Last update</th>
            <th class="text-end">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="ch in syllabus.chapters" :key="ch.id">
            <td>{{ ch.order }}</td>
            <td>{{ ch.titleByLocale.en || ch.slug }}</td>
            <td class="text-caption">{{ ch.ministryCode ?? '—' }}</td>
            <td>
              <VChip :color="statusColor(statusByChapter[ch.id]?.status || 'Locked')" size="small">
                {{ statusByChapter[ch.id]?.status || 'Locked' }}
              </VChip>
              <VChip
                v-if="ch.id === advancement.currentChapterId"
                size="x-small"
                color="primary"
                variant="outlined"
                class="ms-1"
              >current</VChip>
            </td>
            <td class="text-end">{{ statusByChapter[ch.id]?.questionsAttempted ?? 0 }}</td>
            <td class="text-end">
              {{ statusByChapter[ch.id]?.retention != null ? Math.round((statusByChapter[ch.id]?.retention || 0) * 100) + '%' : '—' }}
            </td>
            <td class="text-caption">
              {{ statusByChapter[ch.id]?.lastUpdated
                ? new Date(statusByChapter[ch.id]!.lastUpdated!).toLocaleDateString()
                : '—' }}
            </td>
            <td class="text-end">
              <VBtn
                size="small"
                variant="text"
                @click="openOverride(ch.id, statusByChapter[ch.id]?.status || 'Locked')"
              >Override</VBtn>
            </td>
          </tr>
        </tbody>
      </VTable>
    </VCardText>

    <VDialog v-model="overrideOpen" max-width="540">
      <VCard>
        <VCardTitle>Override chapter status</VCardTitle>
        <VCardText>
          <VSelect
            v-model="overrideNewStatus"
            :items="['Locked', 'Unlocked', 'InProgress', 'Mastered', 'NeedsReview']"
            label="New status"
          />
          <VTextarea
            v-model="overrideRationale"
            label="Rationale (audit-logged, min 10 chars)"
            :counter="200"
            rows="3"
          />
          <VAlert v-if="overrideError" type="error" variant="tonal" class="mt-2">
            {{ overrideError }}
          </VAlert>
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn variant="text" :disabled="overrideBusy" @click="overrideOpen = false">Cancel</VBtn>
          <VBtn color="error" :loading="overrideBusy" @click="applyOverride">Apply override</VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </VCard>
</template>
