<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'Questions' } })

interface QuestionRow {
  id: string
  stem: string
  subject: string
  status: string
  language: string
  languageVersions: Array<{ language: string }>
}

const router = useRouter()

// Filters
const searchQuery = ref('')
const filterMissingLang = ref<string>('')
const filterHasLang = ref<string>('')

// Table state
const itemsPerPage = ref(20)
const page = ref(1)
const totalQuestions = ref(0)
const questions = ref<QuestionRow[]>([])
const loading = ref(false)

const headers = [
  { title: 'Question', key: 'stem', sortable: false },
  { title: 'Subject', key: 'subject', width: 110 },
  { title: 'Status', key: 'status', width: 100 },
  { title: 'Primary Language', key: 'language', width: 130 },
  { title: 'Translations', key: 'translations', sortable: false, width: 180 },
  { title: '', key: 'actions', sortable: false, width: 80 },
]

const allLanguages = [
  { code: 'en', label: 'English', color: 'primary' },
  { code: 'he', label: 'Hebrew', color: 'info' },
  { code: 'ar', label: 'Arabic', color: 'warning' },
]

const langLabel = (code: string) =>
  allLanguages.find(l => l.code === code)?.label ?? code

const langColor = (code: string) =>
  allLanguages.find(l => l.code === code)?.color ?? 'default'

const missingLangOptions = [
  { title: 'Any missing', value: '' },
  { title: 'Missing Hebrew', value: 'he' },
  { title: 'Missing Arabic', value: 'ar' },
  { title: 'Missing English', value: 'en' },
]

const hasLangOptions = [
  { title: 'Any', value: '' },
  { title: 'Has Hebrew', value: 'he' },
  { title: 'Has Arabic', value: 'ar' },
]

const fetchQuestions = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams()
    if (searchQuery.value) params.set('q', searchQuery.value)
    if (filterHasLang.value) params.set('language', filterHasLang.value)
    params.set('page', page.value.toString())
    params.set('itemsPerPage', itemsPerPage.value.toString())

    const data = await $api<{ questions: QuestionRow[]; total?: number; totalQuestions?: number }>(
      `/admin/questions?${params}`,
    )

    let rows: QuestionRow[] = data.questions ?? []

    // Client-side filter: missing a specific language version
    if (filterMissingLang.value) {
      rows = rows.filter(q => {
        const versions = new Set(['en', ...(q.languageVersions ?? []).map((v: any) => v.language)])
        return !versions.has(filterMissingLang.value)
      })
    }

    questions.value = rows
    totalQuestions.value = data.total ?? data.totalQuestions ?? rows.length
  }
  catch (err) {
    console.error('Failed to fetch questions', err)
  }
  finally {
    loading.value = false
  }
}

const onUpdateOptions = (opts: { page: number; itemsPerPage: number }) => {
  page.value = opts.page
  itemsPerPage.value = opts.itemsPerPage
  fetchQuestions()
}

watch([searchQuery, filterMissingLang, filterHasLang], () => {
  page.value = 1
  fetchQuestions()
})

const getTranslations = (row: QuestionRow): string[] => {
  const versions = new Set<string>(
    (row.languageVersions ?? []).map((v: any) => v.language),
  )
  // Filter out primary language (it's not a translation)
  return allLanguages
    .filter(l => l.code !== 'en' && versions.has(l.code))
    .map(l => l.code)
}

const getMissingTranslations = (row: QuestionRow): string[] => {
  const versions = new Set<string>(
    (row.languageVersions ?? []).map((v: any) => v.language),
  )
  return allLanguages
    .filter(l => l.code !== 'en' && !versions.has(l.code))
    .map(l => l.code)
}

const truncate = (text: string, max = 80) =>
  text?.length > max ? `${text.slice(0, max)}...` : text

onMounted(fetchQuestions)
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">Language Versions</h4>
        <div class="text-body-1">Manage question translations for Hebrew and Arabic</div>
      </div>
    </div>

    <!-- Filters -->
    <VCard class="mb-6">
      <VCardText>
        <VRow>
          <VCol cols="12" sm="5">
            <AppTextField
              v-model="searchQuery"
              placeholder="Search questions..."
              prepend-inner-icon="tabler-search"
              density="compact"
              clearable
            />
          </VCol>
          <VCol cols="12" sm="3.5">
            <AppSelect
              v-model="filterMissingLang"
              :items="missingLangOptions"
              label="Filter by missing"
              density="compact"
              clearable
            />
          </VCol>
          <VCol cols="12" sm="3.5">
            <AppSelect
              v-model="filterHasLang"
              :items="hasLangOptions"
              label="Filter by has language"
              density="compact"
              clearable
            />
          </VCol>
        </VRow>
      </VCardText>
    </VCard>

    <!-- Table -->
    <VCard>
      <VDataTableServer
        :headers="headers"
        :items="questions"
        :items-length="totalQuestions"
        :items-per-page="itemsPerPage"
        :page="page"
        :loading="loading"
        @update:options="onUpdateOptions"
      >
        <template #item.stem="{ item }">
          <span :title="item.stem" class="text-body-2">
            {{ truncate(item.stem) }}
          </span>
        </template>

        <template #item.subject="{ item }">
          <VChip size="small" label color="primary">{{ item.subject }}</VChip>
        </template>

        <template #item.status="{ item }">
          <VChip
            size="small"
            label
            :color="item.status === 'Published' ? 'success' : item.status === 'Approved' ? 'info' : 'secondary'"
          >
            {{ item.status }}
          </VChip>
        </template>

        <template #item.language="{ item }">
          <VChip size="small" label :color="langColor(item.language ?? 'en')">
            {{ langLabel(item.language ?? 'en') }}
          </VChip>
        </template>

        <template #item.translations="{ item }">
          <div class="d-flex gap-1 flex-wrap">
            <VChip
              v-for="lang in getTranslations(item)"
              :key="lang"
              size="x-small"
              label
              :color="langColor(lang)"
            >
              {{ langLabel(lang) }}
            </VChip>
            <VChip
              v-for="lang in getMissingTranslations(item)"
              :key="lang"
              size="x-small"
              label
              variant="outlined"
              color="default"
            >
              <VIcon icon="tabler-plus" size="12" class="me-1" />
              {{ langLabel(lang) }}
            </VChip>
          </div>
        </template>

        <template #item.actions="{ item }">
          <VBtn
            size="x-small"
            variant="tonal"
            color="primary"
            icon="tabler-pencil"
            :to="{ name: 'apps-questions-edit-id', params: { id: item.id } }"
          />
        </template>

        <template #no-data>
          <div class="text-center py-8 text-disabled">
            No questions found
          </div>
        </template>
      </VDataTableServer>
    </VCard>
  </div>
</template>
