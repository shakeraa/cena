<script setup lang="ts">
import QuestionDetail from '@/views/apps/questions/QuestionDetail.vue'

definePage({ meta: { action: 'read', subject: 'Questions' } })

// Filters
const searchQuery = ref('')
const selectedSubject = ref<string>()
const selectedBloom = ref<number>()
const selectedDifficulty = ref<string>()
const selectedBagrut = ref<number>()
const selectedStatus = ref<string>()
const selectedLanguage = ref<string>()

// Data table options
const itemsPerPage = ref(10)
const page = ref(1)
const sortBy = ref()
const orderBy = ref()
const selectedRows = ref<string[]>([])

const updateOptions = (options: any) => {
  sortBy.value = options.sortBy[0]?.key
  orderBy.value = options.sortBy[0]?.order
}

// Headers
const headers = [
  { title: 'ID', key: 'id', width: 100 },
  { title: 'Stem', key: 'stem', sortable: false },
  { title: 'Subject', key: 'subject', width: 110 },
  { title: 'Concepts', key: 'concepts', sortable: false, width: 200 },
  { title: 'Bloom\'s', key: 'bloomLevel', width: 100 },
  { title: 'Difficulty', key: 'difficulty', width: 130 },
  { title: 'Status', key: 'status', width: 110 },
  { title: 'Quality', key: 'qualityScore', width: 90 },
  { title: 'Usage', key: 'usageCount', width: 80 },
  { title: 'Success %', key: 'successRate', width: 100 },
]

// Fetch questions via useApi + createUrl (reactive server-side query)
const { data: questionsData, execute: fetchQuestions } = await useApi<any>(createUrl('/admin/questions', {
  query: {
    q: searchQuery,
    subject: selectedSubject,
    bloomLevel: selectedBloom,
    difficulty: selectedDifficulty,
    bagrutLevel: selectedBagrut,
    status: selectedStatus,
    language: selectedLanguage,
    itemsPerPage,
    page,
    sortBy,
    orderBy,
  },
}))

const questions = computed(() => questionsData.value?.questions ?? [])
const totalQuestions = computed(() => questionsData.value?.total ?? questionsData.value?.totalQuestions ?? 0)

// Filter options
const subjects = [
  { title: 'Math', value: 'Math' },
  { title: 'Physics', value: 'Physics' },
  { title: 'Chemistry', value: 'Chemistry' },
  { title: 'Biology', value: 'Biology' },
  { title: 'Computer Science', value: 'Computer Science' },
  { title: 'English', value: 'English' },
]

const bloomLevels = [
  { title: '1 - Remember', value: 1 },
  { title: '2 - Understand', value: 2 },
  { title: '3 - Apply', value: 3 },
  { title: '4 - Analyze', value: 4 },
  { title: '5 - Evaluate', value: 5 },
  { title: '6 - Create', value: 6 },
]

const difficulties = [
  { title: 'Easy', value: 'easy' },
  { title: 'Medium', value: 'medium' },
  { title: 'Hard', value: 'hard' },
]

const bagrutLevels = [
  { title: '3 Units', value: 3 },
  { title: '4 Units', value: 4 },
  { title: '5 Units', value: 5 },
]

const statuses = [
  { title: 'Draft', value: 'draft' },
  { title: 'In Review', value: 'in-review' },
  { title: 'Approved', value: 'approved' },
  { title: 'Published', value: 'published' },
  { title: 'Deprecated', value: 'deprecated' },
]

const languages = [
  { title: 'Hebrew', value: 'he' },
  { title: 'Arabic', value: 'ar' },
  { title: 'Bilingual', value: 'bilingual' },
]

// Resolvers
const resolveStatusColor = (status: string) => {
  const map: Record<string, string> = {
    'draft': 'secondary',
    'in-review': 'warning',
    'approved': 'info',
    'published': 'success',
    'deprecated': 'error',
  }

  return map[status] ?? 'primary'
}

const resolveSubjectColor = (subject: string) => {
  const map: Record<string, string> = {
    'Math': 'primary',
    'Physics': 'info',
    'Chemistry': 'warning',
    'Biology': 'success',
    'Computer Science': 'secondary',
    'English': 'error',
  }

  return map[subject] ?? 'secondary'
}

const resolveDifficultyColor = (difficulty: string) => {
  const map: Record<string, string> = {
    easy: 'success',
    medium: 'warning',
    hard: 'error',
  }

  return map[difficulty] ?? 'secondary'
}

const resolveDifficultyPercent = (difficulty: string) => {
  const map: Record<string, number> = {
    easy: 33,
    medium: 66,
    hard: 100,
  }

  return map[difficulty] ?? 50
}

const resolveQualityColor = (score: number) => {
  if (score >= 80)
    return 'success'
  if (score >= 60)
    return 'warning'

  return 'error'
}

const truncateStem = (html: string, maxLength = 100) => {
  if (!html)
    return ''

  // Strip HTML tags for length check, but keep HTML for display
  const text = html.replace(/<[^>]*>/g, '')

  if (text.length <= maxLength)
    return html

  // Truncate the plain text version and append ellipsis
  return `${text.slice(0, maxLength)}...`
}

const shortId = (id: string) => {
  if (!id)
    return ''

  return id.length > 8 ? id.slice(0, 8) : id
}

// Detail drawer
const selectedQuestionId = ref<string | null>(null)
const isDetailDrawerOpen = ref(false)

const openDetail = (questionId: string) => {
  selectedQuestionId.value = questionId
  isDetailDrawerOpen.value = true
}

// Bulk actions
const bulkApprove = async () => {
  await Promise.all(selectedRows.value.map(id => $api(`/admin/questions/${id}/approve`, { method: 'POST' })))
  selectedRows.value = []
  fetchQuestions()
}

const bulkDeprecate = async () => {
  await Promise.all(selectedRows.value.map(id => $api(`/admin/questions/${id}/deprecate`, { method: 'POST' })))
  selectedRows.value = []
  fetchQuestions()
}

const exportCsv = () => {
  const csvHeaders = ['ID', 'Subject', 'Bloom Level', 'Difficulty', 'Status', 'Quality Score', 'Usage Count', 'Success Rate']
  const csvRows = questions.value.map((q: any) => [
    q.id,
    q.subject,
    q.bloomLevel,
    q.difficulty,
    q.status,
    q.qualityScore,
    q.usageCount,
    q.successRate,
  ].join(','))

  const csvContent = [csvHeaders.join(','), ...csvRows].join('\n')
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')

  link.setAttribute('href', url)
  link.setAttribute('download', `cena-questions-${new Date().toISOString().slice(0, 10)}.csv`)
  link.click()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <section>
    <VCard>
      <VCardItem class="pb-4">
        <VCardTitle>Filters</VCardTitle>
      </VCardItem>

      <VCardText>
        <VRow>
          <!-- Subject -->
          <VCol
            cols="12"
            sm="4"
            md="2"
          >
            <AppSelect
              v-model="selectedSubject"
              placeholder="Subject"
              :items="subjects"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>

          <!-- Bloom's Level -->
          <VCol
            cols="12"
            sm="4"
            md="2"
          >
            <AppSelect
              v-model="selectedBloom"
              placeholder="Bloom's Level"
              :items="bloomLevels"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>

          <!-- Difficulty -->
          <VCol
            cols="12"
            sm="4"
            md="2"
          >
            <AppSelect
              v-model="selectedDifficulty"
              placeholder="Difficulty"
              :items="difficulties"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>

          <!-- Bagrut Level -->
          <VCol
            cols="12"
            sm="4"
            md="2"
          >
            <AppSelect
              v-model="selectedBagrut"
              placeholder="Bagrut Level"
              :items="bagrutLevels"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>

          <!-- Status -->
          <VCol
            cols="12"
            sm="4"
            md="2"
          >
            <AppSelect
              v-model="selectedStatus"
              placeholder="Status"
              :items="statuses"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>

          <!-- Language -->
          <VCol
            cols="12"
            sm="4"
            md="2"
          >
            <AppSelect
              v-model="selectedLanguage"
              placeholder="Language"
              :items="languages"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>
        </VRow>
      </VCardText>

      <VDivider />

      <VCardText class="d-flex flex-wrap gap-4">
        <div class="me-3 d-flex gap-3">
          <AppSelect
            :model-value="itemsPerPage"
            :items="[
              { value: 10, title: '10' },
              { value: 25, title: '25' },
              { value: 50, title: '50' },
              { value: 100, title: '100' },
              { value: -1, title: 'All' },
            ]"
            style="inline-size: 6.25rem;"
            @update:model-value="itemsPerPage = parseInt($event, 10)"
          />
        </div>
        <VSpacer />

        <div class="d-flex align-center flex-wrap gap-4">
          <!-- Search -->
          <div style="inline-size: 15.625rem;">
            <AppTextField
              v-model="searchQuery"
              placeholder="Search question text"
            />
          </div>

          <!-- Bulk actions -->
          <VBtn
            v-if="selectedRows.length > 0"
            variant="tonal"
            color="primary"
          >
            Bulk Actions ({{ selectedRows.length }})
            <VMenu activator="parent">
              <VList>
                <VListItem @click="bulkApprove">
                  <template #prepend>
                    <VIcon icon="tabler-check" />
                  </template>
                  <VListItemTitle>Approve</VListItemTitle>
                </VListItem>
                <VListItem @click="bulkDeprecate">
                  <template #prepend>
                    <VIcon icon="tabler-archive" />
                  </template>
                  <VListItemTitle>Deprecate</VListItemTitle>
                </VListItem>
                <VListItem @click="exportCsv">
                  <template #prepend>
                    <VIcon icon="tabler-download" />
                  </template>
                  <VListItemTitle>Export CSV</VListItemTitle>
                </VListItem>
              </VList>
            </VMenu>
          </VBtn>

          <!-- Export button -->
          <VBtn
            variant="tonal"
            color="secondary"
            prepend-icon="tabler-upload"
            @click="exportCsv"
          >
            Export
          </VBtn>
        </div>
      </VCardText>

      <VDivider />

      <!-- Datatable -->
      <VDataTableServer
        v-model:items-per-page="itemsPerPage"
        v-model:model-value="selectedRows"
        v-model:page="page"
        :items="questions"
        item-value="id"
        :items-length="totalQuestions"
        :headers="headers"
        class="text-no-wrap"
        show-select
        @update:options="updateOptions"
        @click:row="(_event: Event, { item }: any) => openDetail(item.id)"
      >
        <!-- ID -->
        <template #item.id="{ item }">
          <code class="text-body-2">{{ shortId(item.id) }}</code>
        </template>

        <!-- Stem preview -->
        <template #item.stem="{ item }">
          <div
            class="text-body-2 text-truncate"
            style="max-inline-size: 280px;"
            v-html="truncateStem(item.stem)"
          />
        </template>

        <!-- Subject chip -->
        <template #item.subject="{ item }">
          <VChip
            :color="resolveSubjectColor(item.subject)"
            size="small"
            label
            class="text-capitalize"
          >
            {{ item.subject }}
          </VChip>
        </template>

        <!-- Concepts -->
        <template #item.concepts="{ item }">
          <div
            v-if="item.concepts?.length"
            class="d-flex flex-wrap gap-1"
          >
            <VChip
              v-for="concept in item.concepts.slice(0, 3)"
              :key="concept"
              size="x-small"
              variant="tonal"
              color="secondary"
            >
              {{ concept }}
            </VChip>
            <VChip
              v-if="item.concepts.length > 3"
              size="x-small"
              variant="tonal"
              color="default"
            >
              +{{ item.concepts.length - 3 }} more
            </VChip>
          </div>
          <span
            v-else
            class="text-disabled"
          >--</span>
        </template>

        <!-- Bloom's level as rating -->
        <template #item.bloomLevel="{ item }">
          <VRating
            :model-value="item.bloomLevel"
            readonly
            density="compact"
            size="small"
            :length="6"
            color="warning"
            active-color="warning"
          />
        </template>

        <!-- Difficulty as progress bar -->
        <template #item.difficulty="{ item }">
          <div class="d-flex align-center gap-2">
            <VProgressLinear
              :model-value="resolveDifficultyPercent(item.difficulty)"
              :color="resolveDifficultyColor(item.difficulty)"
              height="6"
              rounded
              style="min-inline-size: 60px;"
            />
            <span class="text-body-2 text-capitalize">{{ item.difficulty }}</span>
          </div>
        </template>

        <!-- Status chip -->
        <template #item.status="{ item }">
          <VChip
            :color="resolveStatusColor(item.status)"
            size="small"
            label
            class="text-capitalize"
          >
            {{ item.status }}
          </VChip>
        </template>

        <!-- Quality score -->
        <template #item.qualityScore="{ item }">
          <span
            class="font-weight-medium"
            :class="`text-${resolveQualityColor(item.qualityScore)}`"
          >
            {{ item.qualityScore ?? '--' }}
          </span>
        </template>

        <!-- Usage count -->
        <template #item.usageCount="{ item }">
          <span class="text-body-1">{{ item.usageCount ?? 0 }}</span>
        </template>

        <!-- Success rate -->
        <template #item.successRate="{ item }">
          <span
            v-if="item.successRate != null"
            class="text-body-1"
          >
            {{ item.successRate }}%
          </span>
          <span
            v-else
            class="text-disabled"
          >--</span>
        </template>

        <!-- Pagination -->
        <template #bottom>
          <TablePagination
            v-model:page="page"
            :items-per-page="itemsPerPage"
            :total-items="totalQuestions"
          />
        </template>
      </VDataTableServer>
    </VCard>

    <!-- Question Detail Drawer -->
    <QuestionDetail
      v-model:is-open="isDetailDrawerOpen"
      :question-id="selectedQuestionId"
      @updated="fetchQuestions"
    />
  </section>
</template>
