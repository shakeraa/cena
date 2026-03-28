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

// Create question dialog
const isCreateDialogOpen = ref(false)
const isCreating = ref(false)
const createForm = ref({
  sourceType: 'authored',
  stem: '',
  subject: 'Math',
  topic: '',
  grade: '4 Units',
  bloomsLevel: 3,
  difficultyRange: [0.3, 0.7] as [number, number],
  language: 'he',
  conceptIds: [] as string[],
  count: 3,
  options: [
    { label: 'A', text: '', isCorrect: true, distractorRationale: '' },
    { label: 'B', text: '', isCorrect: false, distractorRationale: '' },
    { label: 'C', text: '', isCorrect: false, distractorRationale: '' },
    { label: 'D', text: '', isCorrect: false, distractorRationale: '' },
  ],
  // AI fields
  promptText: '',
  modelId: 'claude-sonnet-4-6',
  modelTemperature: 0.7,
  rawModelOutput: '',
  // Ingestion fields
  sourceDocId: '',
  sourceUrl: '',
  sourceFilename: '',
  originalText: '',
})

const resetCreateForm = () => {
  createForm.value = {
    sourceType: 'authored',
    stem: '',
    subject: 'Math',
    topic: '',
    grade: '4 Units',
    bloomsLevel: 3,
    difficultyRange: [0.3, 0.7],
    language: 'he',
    conceptIds: [],
    count: 3,
    options: [
      { label: 'A', text: '', isCorrect: true, distractorRationale: '' },
      { label: 'B', text: '', isCorrect: false, distractorRationale: '' },
      { label: 'C', text: '', isCorrect: false, distractorRationale: '' },
      { label: 'D', text: '', isCorrect: false, distractorRationale: '' },
    ],
    promptText: '',
    modelId: 'claude-sonnet-4-6',
    modelTemperature: 0.7,
    rawModelOutput: '',
    sourceDocId: '',
    sourceUrl: '',
    sourceFilename: '',
    originalText: '',
  }
}

const setCorrectOption = (idx: number) => {
  createForm.value.options.forEach((opt, i) => {
    opt.isCorrect = i === idx
  })
}

const submitCreateQuestion = async () => {
  isCreating.value = true
  try {
    const [minD, maxD] = createForm.value.difficultyRange
    const body: Record<string, any> = {
      sourceType: createForm.value.sourceType,
      stem: createForm.value.stem,
      subject: createForm.value.subject,
      topic: createForm.value.topic,
      grade: createForm.value.grade,
      bloomsLevel: createForm.value.bloomsLevel,
      difficulty: (minD + maxD) / 2, // midpoint for the created question
      language: createForm.value.language,
      conceptIds: createForm.value.conceptIds,
      options: createForm.value.options,
    }

    if (createForm.value.sourceType === 'ai-generated') {
      body.promptText = createForm.value.promptText
      body.modelId = createForm.value.modelId
      body.modelTemperature = createForm.value.modelTemperature
      body.rawModelOutput = createForm.value.rawModelOutput
    }
    else if (createForm.value.sourceType === 'ingested') {
      body.sourceDocId = createForm.value.sourceDocId
      body.sourceUrl = createForm.value.sourceUrl
      body.sourceFilename = createForm.value.sourceFilename
      body.originalText = createForm.value.originalText
    }

    await $api('/admin/questions', { method: 'POST', body })
    isCreateDialogOpen.value = false
    resetCreateForm()
    fetchQuestions()
  }
  catch (err) {
    console.error('Failed to create question', err)
  }
  finally {
    isCreating.value = false
  }
}

// AI generation
const aiInputMode = ref<'text' | 'photo' | 'file'>('text')
const aiPromptInput = ref('')
const aiQuestionFile = ref<File | null>(null)
const aiQuestionPreviewUrl = ref<string | null>(null)
const aiStyleFile = ref<File | null>(null)
const aiStylePreviewUrl = ref<string | null>(null)
const aiStyleText = ref('')
const isGenerating = ref(false)
const aiGeneratedPreview = ref<any>(null)
const aiGeneratedQuestions = ref<any[]>([])

const fileToBase64 = (file: File): Promise<string> => {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const result = reader.result as string
      // Strip the data:...;base64, prefix
      resolve(result.split(',')[1])
    }
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

const handleQuestionFileUpload = (event: Event) => {
  const target = event.target as HTMLInputElement
  if (target.files?.length) {
    aiQuestionFile.value = target.files[0]
    if (target.files[0].type.startsWith('image/')) {
      aiQuestionPreviewUrl.value = URL.createObjectURL(target.files[0])
    }
    else {
      aiQuestionPreviewUrl.value = null
    }
  }
}

const handleStyleFileUpload = (event: Event) => {
  const target = event.target as HTMLInputElement
  if (target.files?.length) {
    aiStyleFile.value = target.files[0]
    if (target.files[0].type.startsWith('image/')) {
      aiStylePreviewUrl.value = URL.createObjectURL(target.files[0])
    }
    else {
      aiStylePreviewUrl.value = null
    }
  }
}

const generateWithAi = async () => {
  isGenerating.value = true
  aiGeneratedPreview.value = null
  aiGeneratedQuestions.value = []

  try {
    let context = ''
    let imageBase64: string | undefined
    let fileName: string | undefined
    let styleImageBase64: string | undefined
    let styleFileName: string | undefined

    if (aiInputMode.value === 'text') {
      context = aiPromptInput.value
    }
    else if ((aiInputMode.value === 'photo' || aiInputMode.value === 'file') && aiQuestionFile.value) {
      fileName = aiQuestionFile.value.name
      if (aiQuestionFile.value.type.startsWith('image/')) {
        imageBase64 = await fileToBase64(aiQuestionFile.value)
        context = `[Image: ${fileName}] Generate questions based on this image.`
      }
      else {
        context = `[File: ${fileName}] Extract and generate questions from this document.`
      }
    }

    // Style reference
    if (aiStyleFile.value) {
      styleFileName = aiStyleFile.value.name
      if (aiStyleFile.value.type.startsWith('image/')) {
        styleImageBase64 = await fileToBase64(aiStyleFile.value)
      }
    }

    const [minD, maxD] = createForm.value.difficultyRange

    const result = await $api<any>('/admin/ai/generate', {
      method: 'POST',
      body: {
        subject: createForm.value.subject,
        topic: createForm.value.topic,
        grade: createForm.value.grade,
        bloomsLevel: createForm.value.bloomsLevel,
        minDifficulty: minD,
        maxDifficulty: maxD,
        language: createForm.value.language,
        context,
        imageBase64,
        fileName,
        styleContext: aiStyleText.value || undefined,
        styleImageBase64,
        styleFileName,
        count: createForm.value.count,
      },
    })

    if (result.success && result.questions?.length > 0) {
      aiGeneratedQuestions.value = result.questions

      // Auto-fill with the first question for the form preview
      const q = result.questions[0]
      createForm.value.sourceType = 'ai-generated'
      createForm.value.stem = q.stem
      createForm.value.topic = q.topic || createForm.value.topic
      createForm.value.promptText = result.promptUsed
      createForm.value.modelId = result.modelUsed
      createForm.value.modelTemperature = result.temperatureUsed
      createForm.value.rawModelOutput = result.rawOutput || ''

      if (q.options?.length) {
        createForm.value.options = q.options.map((o: any) => ({
          label: o.label,
          text: o.text,
          isCorrect: o.isCorrect,
          distractorRationale: o.distractorRationale || '',
        }))
      }

      aiGeneratedPreview.value = {
        status: 'success',
        message: `Generated ${result.questions.length} question(s) by ${result.modelUsed} across difficulty ${minD.toFixed(2)}–${maxD.toFixed(2)}. Review below and click "Create & Evaluate" to save.`,
      }
    }
    else {
      aiGeneratedPreview.value = {
        status: 'error',
        message: result.error || 'No questions generated. Check your AI provider settings.',
      }
    }
  }
  catch (err: any) {
    console.error('AI generation failed', err)
    aiGeneratedPreview.value = {
      status: 'error',
      message: err.message || 'Generation failed. Check Settings > AI Providers for API key.',
    }
  }
  finally {
    isGenerating.value = false
  }
}

const selectGeneratedQuestion = (idx: number) => {
  const q = aiGeneratedQuestions.value[idx]
  if (!q) return
  createForm.value.stem = q.stem
  createForm.value.difficultyRange = [q.difficulty, q.difficulty]
  if (q.topic) createForm.value.topic = q.topic
  if (q.options?.length) {
    createForm.value.options = q.options.map((o: any) => ({
      label: o.label,
      text: o.text,
      isCorrect: o.isCorrect,
      distractorRationale: o.distractorRationale || '',
    }))
  }
}

const sourceTypes = [
  { title: 'Authored (Manual)', value: 'authored' },
  { title: 'AI Generated', value: 'ai-generated' },
  { title: 'Ingested (Import)', value: 'ingested' },
]

const languageOptions = [
  { title: 'Hebrew', value: 'he' },
  { title: 'Arabic', value: 'ar' },
  { title: 'English', value: 'en' },
]

const gradeOptions = [
  { title: '3 Units', value: '3 Units' },
  { title: '4 Units', value: '4 Units' },
  { title: '5 Units', value: '5 Units' },
]

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

          <!-- Create Question -->
          <VBtn
            color="primary"
            prepend-icon="tabler-plus"
            @click="isCreateDialogOpen = true"
          >
            Create Question
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

    <!-- Create Question Dialog -->
    <VDialog
      v-model="isCreateDialogOpen"
      max-width="720"
      persistent
    >
      <VCard>
        <VCardTitle class="d-flex align-center pa-6 pb-0">
          <span class="text-h5">Create Question</span>
          <VSpacer />
          <VBtn
            icon
            variant="text"
            size="small"
            @click="isCreateDialogOpen = false"
          >
            <VIcon icon="tabler-x" />
          </VBtn>
        </VCardTitle>

        <VCardText class="pa-6">
          <VRow>
            <!-- Source type -->
            <VCol cols="12" sm="6">
              <AppSelect
                v-model="createForm.sourceType"
                label="Source Type"
                :items="sourceTypes"
              />
            </VCol>

            <!-- Language -->
            <VCol cols="12" sm="6">
              <AppSelect
                v-model="createForm.language"
                label="Language"
                :items="languageOptions"
              />
            </VCol>

            <!-- Subject -->
            <VCol cols="12" sm="4">
              <AppSelect
                v-model="createForm.subject"
                label="Subject"
                :items="subjects"
              />
            </VCol>

            <!-- Grade -->
            <VCol cols="12" sm="4">
              <AppSelect
                v-model="createForm.grade"
                label="Bagrut Level"
                :items="gradeOptions"
              />
            </VCol>

            <!-- Bloom's -->
            <VCol cols="12" sm="4">
              <AppSelect
                v-model="createForm.bloomsLevel"
                label="Bloom's Level"
                :items="bloomLevels"
              />
            </VCol>

            <!-- Topic -->
            <VCol cols="12" sm="6">
              <AppTextField
                v-model="createForm.topic"
                label="Topic"
                placeholder="e.g., Linear Equations"
              />
            </VCol>

            <!-- Difficulty range slider -->
            <VCol cols="12" sm="6">
              <label class="text-body-2 text-medium-emphasis d-block mb-1">
                Difficulty Range: {{ createForm.difficultyRange[0].toFixed(2) }} – {{ createForm.difficultyRange[1].toFixed(2) }}
              </label>
              <VRangeSlider
                v-model="createForm.difficultyRange"
                :min="0"
                :max="1"
                :step="0.05"
                thumb-label
                color="primary"
                strict
              />
            </VCol>

            <!-- Question stem -->
            <VCol cols="12">
              <AppTextarea
                v-model="createForm.stem"
                label="Question Stem"
                placeholder="Enter the question text..."
                rows="3"
                :rules="[v => !!v || 'Stem is required']"
              />
            </VCol>

            <!-- Answer options -->
            <VCol cols="12">
              <label class="text-body-2 text-medium-emphasis d-block mb-2">
                Answer Options (select the correct one)
              </label>
              <div
                v-for="(opt, idx) in createForm.options"
                :key="opt.label"
                class="d-flex align-center gap-3 mb-3"
              >
                <VRadio
                  :model-value="opt.isCorrect"
                  :value="true"
                  color="success"
                  @click="setCorrectOption(idx)"
                />
                <VChip
                  size="small"
                  :color="opt.isCorrect ? 'success' : 'default'"
                  label
                >
                  {{ opt.label }}
                </VChip>
                <AppTextField
                  v-model="opt.text"
                  :placeholder="`Option ${opt.label} text`"
                  density="compact"
                  class="flex-grow-1"
                />
                <AppTextField
                  v-if="!opt.isCorrect"
                  v-model="opt.distractorRationale"
                  placeholder="Why wrong?"
                  density="compact"
                  style="max-inline-size: 180px;"
                />
              </div>
            </VCol>

            <!-- AI Generation Panel -->
            <template v-if="createForm.sourceType === 'ai-generated'">
              <VCol cols="12">
                <VDivider class="mb-2" />
                <span class="text-body-2 font-weight-medium text-primary">AI Generation</span>
              </VCol>

              <!-- Question source: how to provide the question content -->
              <VCol cols="12">
                <label class="text-body-2 text-medium-emphasis d-block mb-2">
                  Question Source
                </label>
                <VBtnToggle
                  v-model="aiInputMode"
                  mandatory
                  color="primary"
                  variant="outlined"
                  density="compact"
                  class="mb-3"
                >
                  <VBtn value="text">
                    <VIcon icon="tabler-text-size" class="me-1" />
                    Text Prompt
                  </VBtn>
                  <VBtn value="photo">
                    <VIcon icon="tabler-camera" class="me-1" />
                    Photo / Image
                  </VBtn>
                  <VBtn value="file">
                    <VIcon icon="tabler-file-upload" class="me-1" />
                    Document
                  </VBtn>
                </VBtnToggle>
              </VCol>

              <!-- Text prompt input -->
              <VCol
                v-if="aiInputMode === 'text'"
                cols="12"
              >
                <AppTextarea
                  v-model="aiPromptInput"
                  label="Describe the question or topic"
                  placeholder="e.g., Questions about quadratic equations, factoring methods, completing the square..."
                  rows="3"
                />
              </VCol>

              <!-- Photo upload with preview -->
              <VCol
                v-if="aiInputMode === 'photo'"
                cols="12"
              >
                <VFileInput
                  label="Upload exam photo or textbook screenshot"
                  accept="image/*"
                  prepend-icon="tabler-camera"
                  show-size
                  @change="handleQuestionFileUpload"
                />
                <span class="text-caption text-disabled">
                  Supports JPG, PNG, HEIC. The image will be sent to the vision model for question extraction.
                </span>
                <VImg
                  v-if="aiQuestionPreviewUrl"
                  :src="aiQuestionPreviewUrl"
                  max-height="200"
                  class="mt-2 rounded border"
                  cover
                />
              </VCol>

              <!-- File upload -->
              <VCol
                v-if="aiInputMode === 'file'"
                cols="12"
              >
                <VFileInput
                  label="Upload document (PDF, Word, Excel)"
                  accept=".pdf,.doc,.docx,.xls,.xlsx,.txt"
                  prepend-icon="tabler-file-upload"
                  show-size
                  @change="handleQuestionFileUpload"
                />
                <span class="text-caption text-disabled">
                  Supports PDF, Word, Excel, plain text. Questions will be extracted and AI-enhanced.
                </span>
              </VCol>

              <!-- Style Reference (optional) -->
              <VCol cols="12">
                <VDivider class="mb-2" />
                <label class="text-body-2 text-medium-emphasis d-block mb-2">
                  Style Reference
                  <VChip size="x-small" variant="tonal" color="secondary" class="ms-2">Optional</VChip>
                </label>
                <span class="text-caption text-disabled d-block mb-2">
                  Upload an example question or describe the style you want the generated questions to follow.
                </span>
              </VCol>

              <VCol cols="12" sm="6">
                <VFileInput
                  label="Style reference image"
                  accept="image/*"
                  prepend-icon="tabler-palette"
                  show-size
                  clearable
                  @change="handleStyleFileUpload"
                />
                <VImg
                  v-if="aiStylePreviewUrl"
                  :src="aiStylePreviewUrl"
                  max-height="150"
                  class="mt-2 rounded border"
                  cover
                />
              </VCol>

              <VCol cols="12" sm="6">
                <AppTextarea
                  v-model="aiStyleText"
                  label="Style description"
                  placeholder="e.g., Bagrut 2024 summer exam style, concise stems, Hebrew formal register..."
                  rows="3"
                />
              </VCol>

              <!-- Count -->
              <VCol cols="12" sm="4">
                <AppSelect
                  v-model="createForm.count"
                  label="Number of Questions"
                  :items="[
                    { title: '1', value: 1 },
                    { title: '3', value: 3 },
                    { title: '5', value: 5 },
                    { title: '10', value: 10 },
                  ]"
                />
              </VCol>

              <!-- Generate button -->
              <VCol cols="12">
                <VBtn
                  color="primary"
                  variant="tonal"
                  prepend-icon="tabler-sparkles"
                  :loading="isGenerating"
                  :disabled="aiInputMode === 'text' ? !aiPromptInput : !aiQuestionFile"
                  @click="generateWithAi"
                >
                  Generate {{ createForm.count }} Question{{ createForm.count > 1 ? 's' : '' }} with AI
                </VBtn>
              </VCol>

              <!-- AI preview result -->
              <VCol
                v-if="aiGeneratedPreview"
                cols="12"
              >
                <VAlert
                  :color="aiGeneratedPreview.status === 'error' ? 'error' : 'success'"
                  variant="tonal"
                  density="compact"
                >
                  {{ aiGeneratedPreview.message }}
                </VAlert>
              </VCol>

              <!-- Generated questions list (pick one to fill the form) -->
              <VCol
                v-if="aiGeneratedQuestions.length > 1"
                cols="12"
              >
                <label class="text-body-2 text-medium-emphasis d-block mb-2">
                  Generated Questions — click to load into the form
                </label>
                <VList density="compact" class="rounded border">
                  <VListItem
                    v-for="(gq, idx) in aiGeneratedQuestions"
                    :key="idx"
                    :title="`#${idx + 1} — Difficulty ${gq.difficulty?.toFixed(2) ?? '?'}`"
                    :subtitle="gq.stem?.slice(0, 120)"
                    @click="selectGeneratedQuestion(idx)"
                  >
                    <template #prepend>
                      <VAvatar color="primary" variant="tonal" size="32">
                        {{ idx + 1 }}
                      </VAvatar>
                    </template>
                  </VListItem>
                </VList>
              </VCol>

              <VCol cols="12">
                <VDivider class="mb-1" />
                <span class="text-caption text-disabled">
                  AI details (stored for reproducibility)
                </span>
              </VCol>
              <VCol cols="12">
                <AppTextarea
                  v-model="createForm.promptText"
                  label="Full Prompt"
                  rows="2"
                />
              </VCol>
              <VCol cols="6">
                <AppTextField
                  v-model="createForm.modelId"
                  label="Model"
                />
              </VCol>
              <VCol cols="6">
                <AppTextField
                  v-model.number="createForm.modelTemperature"
                  label="Temperature"
                  type="number"
                  :step="0.1"
                />
              </VCol>
            </template>

            <!-- Ingestion-specific fields -->
            <template v-if="createForm.sourceType === 'ingested'">
              <VCol cols="12">
                <VDivider class="mb-2" />
                <span class="text-body-2 font-weight-medium text-primary">Ingestion Source</span>
              </VCol>
              <VCol cols="6">
                <AppTextField
                  v-model="createForm.sourceDocId"
                  label="Source Document ID"
                />
              </VCol>
              <VCol cols="6">
                <AppTextField
                  v-model="createForm.sourceFilename"
                  label="Source Filename"
                />
              </VCol>
              <VCol cols="12">
                <AppTextField
                  v-model="createForm.sourceUrl"
                  label="Source URL"
                />
              </VCol>
              <VCol cols="12">
                <AppTextarea
                  v-model="createForm.originalText"
                  label="Original Text (OCR)"
                  rows="2"
                />
              </VCol>
            </template>
          </VRow>
        </VCardText>

        <VDivider />

        <VCardActions class="pa-6">
          <VSpacer />
          <VBtn
            variant="tonal"
            color="secondary"
            @click="isCreateDialogOpen = false; resetCreateForm()"
          >
            Cancel
          </VBtn>
          <VBtn
            color="primary"
            :loading="isCreating"
            :disabled="!createForm.stem || createForm.options.every(o => !o.text)"
            @click="submitCreateQuestion"
          >
            Create & Evaluate
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </section>
</template>
