<script setup lang="ts">
// =============================================================================
// Student Syllabus Map (RDY-061 Phase 4)
//
// Renders the student's current position on the syllabus: locked /
// unlocked / in-progress / mastered / needs-review per chapter. No
// comparative pacing signal — "you're here" only.
// =============================================================================
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { getFirebaseAuth } from '@/plugins/firebase'

interface ChapterAdvancement { chapterId: string; status: string }
interface AdvancementResponse {
  trackId?: string | null
  syllabusId?: string
  currentChapterId?: string | null
  chapters: ChapterAdvancement[]
  started?: boolean
}
interface SyllabusChapter {
  id: string
  order: number
  slug: string
  titleByLocale: Record<string, string>
  prerequisiteChapterIds: string[]
  expectedWeeks: number
  ministryCode?: string
}
interface SyllabusResponse {
  syllabusId: string
  trackId: string
  chapters: SyllabusChapter[]
}

const { locale } = useI18n()
const advancement = ref<AdvancementResponse | null>(null)
const syllabus = ref<SyllabusResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

async function authedFetch(url: string): Promise<Response> {
  const auth = getFirebaseAuth()
  const token = await auth.currentUser?.getIdToken()
  return fetch(url, {
    headers: {
      Accept: 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  })
}

async function load() {
  loading.value = true
  error.value = null
  try {
    const advRes = await authedFetch('/api/me/advancement')
    if (!advRes.ok) throw new Error(`advancement ${advRes.status}`)
    advancement.value = await advRes.json()
    const trackId = advancement.value?.trackId
    if (trackId) {
      const sylRes = await authedFetch(`/api/admin/tracks/${encodeURIComponent(trackId)}/syllabus`)
      // Admin endpoint — student may not have permission. Fall back to empty.
      if (sylRes.ok) syllabus.value = await sylRes.json()
    }
  }
  catch (e: unknown) {
    error.value = (e as Error).message
  }
  finally {
    loading.value = false
  }
}

const statusByChapter = computed<Map<string, string>>(() => {
  const m = new Map<string, string>()
  if (advancement.value) {
    for (const c of advancement.value.chapters) m.set(c.chapterId, c.status)
  }
  return m
})

function chapterTitle(ch: SyllabusChapter): string {
  const lang = locale.value
  return ch.titleByLocale[lang] || ch.titleByLocale.en || ch.slug
}

function statusLabel(s: string): string {
  switch (s) {
    case 'Locked': return '🔒'
    case 'Unlocked': return '✨'
    case 'InProgress': return '📝'
    case 'Mastered': return '✅'
    case 'NeedsReview': return '🔁'
    default: return ''
  }
}

function statusColor(s: string): string {
  switch (s) {
    case 'Mastered': return 'success'
    case 'InProgress': return 'primary'
    case 'Unlocked': return 'info'
    case 'NeedsReview': return 'warning'
    default: return 'default'
  }
}

onMounted(load)
</script>

<template>
  <div data-testid="syllabus-map">
    <div v-if="loading" class="d-flex justify-center pa-6">
      <VProgressCircular indeterminate />
    </div>

    <VAlert v-else-if="error" type="error" variant="tonal">
      {{ error }}
    </VAlert>

    <VAlert
      v-else-if="!advancement?.started || !syllabus"
      type="info"
      variant="tonal"
      data-testid="syllabus-not-started"
    >
      Your syllabus will appear after you start your first session.
    </VAlert>

    <div v-else class="syllabus-grid">
      <div
        v-for="ch in syllabus.chapters"
        :key="ch.id"
        class="chapter-card"
        :data-chapter-id="ch.id"
        :data-status="statusByChapter.get(ch.id) || 'Locked'"
      >
        <VCard
          :color="statusByChapter.get(ch.id) === 'Locked' ? undefined : statusColor(statusByChapter.get(ch.id) || 'Locked')"
          :variant="statusByChapter.get(ch.id) === 'Locked' ? 'outlined' : 'tonal'"
          class="chapter"
          :class="{ current: ch.id === advancement.currentChapterId }"
        >
          <VCardTitle class="d-flex align-center">
            <span class="text-overline text-medium-emphasis me-2">{{ ch.order }}</span>
            <span>{{ chapterTitle(ch) }}</span>
            <VSpacer />
            <span class="text-h5">{{ statusLabel(statusByChapter.get(ch.id) || 'Locked') }}</span>
          </VCardTitle>
          <VCardSubtitle v-if="ch.ministryCode">{{ ch.ministryCode }}</VCardSubtitle>
        </VCard>
      </div>
    </div>
  </div>
</template>

<style scoped>
.syllabus-grid {
  display: grid;
  gap: 12px;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
}
.chapter.current {
  outline: 2px solid rgb(var(--v-theme-primary));
}
</style>
