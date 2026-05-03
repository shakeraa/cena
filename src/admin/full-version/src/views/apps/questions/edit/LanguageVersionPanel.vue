<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  questionId: string
  question: any
}

const props = defineProps<Props>()
const emit = defineEmits<{ versionAdded: [] }>()

const activeTab = ref('en')
const sideBySide = ref(false)
const sideBySideLang = ref<string | null>(null)
const showAddDialog = ref(false)
const isSubmitting = ref(false)
const errorMsg = ref<string | null>(null)

// Inline edit state per language
const editingLang = ref<string | null>(null)
const editForm = ref<{
  stem: string
  options: Array<{ id: string; text: string; isCorrect: boolean }>
  explanation: string
}>({ stem: '', options: [], explanation: '' })
const isSavingEdit = ref(false)
const editError = ref<string | null>(null)

const languages = [
  { code: 'en', label: 'English', dir: 'ltr' },
  { code: 'he', label: 'Hebrew', dir: 'rtl' },
  { code: 'ar', label: 'Arabic', dir: 'rtl' },
]

// Check which language versions exist
const existingVersions = computed(() => {
  const versions = new Set(['en']) // English always exists
  if (props.question?.languageVersions) {
    for (const v of props.question.languageVersions)
      versions.add(v.language)
  }
  return versions
})

const getVersion = (langCode: string) => {
  if (langCode === 'en') {
    return {
      stem: props.question?.stem ?? '',
      options: props.question?.options ?? [],
      explanation: props.question?.explanation ?? '',
    }
  }
  return props.question?.languageVersions?.find((v: any) => v.language === langCode)
}

const getLangDir = (langCode: string): string =>
  languages.find(l => l.code === langCode)?.dir ?? 'ltr'

const getLangLabel = (langCode: string): string =>
  languages.find(l => l.code === langCode)?.label ?? langCode

// ─── Add new version ───
const newVersionLang = ref('he')
const newVersionForm = ref({
  stem: '',
  options: [
    { id: 'A', text: '', isCorrect: false },
    { id: 'B', text: '', isCorrect: false },
    { id: 'C', text: '', isCorrect: false },
    { id: 'D', text: '', isCorrect: false },
  ],
  explanation: '',
})

const resetNewVersionForm = () => {
  newVersionForm.value = {
    stem: '',
    options: [
      { id: 'A', text: '', isCorrect: false },
      { id: 'B', text: '', isCorrect: false },
      { id: 'C', text: '', isCorrect: false },
      { id: 'D', text: '', isCorrect: false },
    ],
    explanation: '',
  }
  errorMsg.value = null
}

const openAddDialog = (langCode: string) => {
  newVersionLang.value = langCode
  resetNewVersionForm()

  // Pre-populate isCorrect from English version
  const enVersion = getVersion('en')
  if (enVersion?.options) {
    newVersionForm.value.options = enVersion.options.map((o: any, i: number) => ({
      id: newVersionForm.value.options[i]?.id ?? String.fromCharCode(65 + i),
      text: '',
      isCorrect: o.isCorrect ?? false,
    }))
  }

  showAddDialog.value = true
}

const submitNewVersion = async () => {
  isSubmitting.value = true
  errorMsg.value = null
  try {
    await $api(`/admin/questions/${props.questionId}/language-versions`, {
      method: 'POST',
      body: {
        language: newVersionLang.value,
        stem: newVersionForm.value.stem,
        options: newVersionForm.value.options,
        explanation: newVersionForm.value.explanation,
      },
    })
    showAddDialog.value = false
    emit('versionAdded')
  }
  catch (err: any) {
    errorMsg.value = err.data?.message ?? err.message ?? 'Failed to add language version'
  }
  finally {
    isSubmitting.value = false
  }
}

// ─── Inline edit existing version ───
const startEditing = (langCode: string) => {
  const version = getVersion(langCode)
  editForm.value = {
    stem: version?.stem ?? '',
    options: (version?.options ?? []).map((o: any) => ({
      id: o.id ?? o.label,
      text: o.text ?? '',
      isCorrect: o.isCorrect ?? false,
    })),
    explanation: version?.explanation ?? '',
  }
  editError.value = null
  editingLang.value = langCode
}

const cancelEditing = () => {
  editingLang.value = null
  editError.value = null
}

const saveEditing = async () => {
  if (!editingLang.value) return
  isSavingEdit.value = true
  editError.value = null
  try {
    await $api(`/admin/questions/${props.questionId}/language-versions`, {
      method: 'POST',
      body: {
        language: editingLang.value,
        stem: editForm.value.stem,
        options: editForm.value.options,
        explanation: editForm.value.explanation,
      },
    })
    editingLang.value = null
    emit('versionAdded')
  }
  catch (err: any) {
    editError.value = err.data?.message ?? err.message ?? 'Failed to save'
  }
  finally {
    isSavingEdit.value = false
  }
}

// ─── Side-by-side ───
const toggleSideBySide = () => {
  if (sideBySide.value) {
    sideBySide.value = false
    sideBySideLang.value = null
  }
  else {
    // Pick the first non-English existing version, or the first non-English language
    const nonEn = languages.filter(l => l.code !== 'en')
    const existing = nonEn.find(l => existingVersions.value.has(l.code))
    sideBySideLang.value = existing?.code ?? nonEn[0].code
    sideBySide.value = true
  }
}

const dialogLangDir = computed(() => getLangDir(newVersionLang.value))
const dialogLangLabel = computed(() => getLangLabel(newVersionLang.value))
</script>

<template>
  <VCard>
    <VCardItem>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon icon="tabler-language" size="20" />
        Language Versions
      </VCardTitle>
      <template #append>
        <VBtn
          size="small"
          variant="tonal"
          :color="sideBySide ? 'primary' : 'secondary'"
          prepend-icon="tabler-layout-columns"
          @click="toggleSideBySide"
        >
          {{ sideBySide ? 'Single View' : 'Side by Side' }}
        </VBtn>
      </template>
    </VCardItem>

    <VCardText>
      <!-- Side-by-side view -->
      <template v-if="sideBySide && sideBySideLang">
        <div class="d-flex gap-2 mb-4">
          <VChip
            v-for="lang in languages.filter(l => l.code !== 'en')"
            :key="lang.code"
            :variant="sideBySideLang === lang.code ? 'tonal' : 'outlined'"
            :color="sideBySideLang === lang.code ? 'primary' : 'default'"
            size="small"
            clickable
            @click="sideBySideLang = lang.code"
          >
            {{ lang.label }}
            <VIcon
              v-if="existingVersions.has(lang.code)"
              icon="tabler-check"
              size="14"
              class="ms-1"
            />
          </VChip>
        </div>

        <VRow>
          <!-- English (left) -->
          <VCol cols="12" md="6">
            <div class="text-caption text-medium-emphasis font-weight-medium mb-3 d-flex align-center gap-1">
              <VIcon icon="tabler-flag" size="14" />
              English (source)
            </div>
            <div dir="ltr">
              <div class="mb-3">
                <label class="text-body-2 text-medium-emphasis d-block mb-1">Stem</label>
                <div class="pa-3 rounded bg-surface-variant text-body-2">
                  {{ getVersion('en')?.stem || 'No stem text' }}
                </div>
              </div>
              <div class="mb-3">
                <label class="text-body-2 text-medium-emphasis d-block mb-1">Options</label>
                <div
                  v-for="opt in (getVersion('en')?.options ?? [])"
                  :key="opt.id"
                  class="d-flex align-center gap-2 pa-2 rounded mb-1"
                  :class="opt.isCorrect ? 'bg-success-lighten' : ''"
                >
                  <VChip size="small" :color="opt.isCorrect ? 'success' : 'default'" label>
                    {{ opt.label ?? opt.id }}
                  </VChip>
                  <span class="text-body-2 flex-grow-1">{{ opt.text }}</span>
                </div>
              </div>
              <div>
                <label class="text-body-2 text-medium-emphasis d-block mb-1">Explanation</label>
                <div class="pa-3 rounded bg-surface-variant text-body-2">
                  {{ getVersion('en')?.explanation || 'No explanation' }}
                </div>
              </div>
            </div>
          </VCol>

          <!-- Selected language (right) -->
          <VCol cols="12" md="6">
            <div class="text-caption text-medium-emphasis font-weight-medium mb-3 d-flex align-center gap-2">
              <VIcon icon="tabler-language" size="14" />
              {{ getLangLabel(sideBySideLang) }}
              <VBtn
                v-if="!existingVersions.has(sideBySideLang)"
                size="x-small"
                color="primary"
                variant="tonal"
                prepend-icon="tabler-plus"
                @click="openAddDialog(sideBySideLang)"
              >
                Add
              </VBtn>
            </div>

            <template v-if="existingVersions.has(sideBySideLang)">
              <div :dir="getLangDir(sideBySideLang)">
                <div class="mb-3">
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">Stem</label>
                  <div class="pa-3 rounded bg-surface-variant text-body-2">
                    {{ getVersion(sideBySideLang)?.stem || 'No stem text' }}
                  </div>
                </div>
                <div class="mb-3">
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">Options</label>
                  <div
                    v-for="opt in (getVersion(sideBySideLang)?.options ?? [])"
                    :key="opt.id"
                    class="d-flex align-center gap-2 pa-2 rounded mb-1"
                    :class="opt.isCorrect ? 'bg-success-lighten' : ''"
                  >
                    <VChip size="small" :color="opt.isCorrect ? 'success' : 'default'" label>
                      {{ opt.label ?? opt.id }}
                    </VChip>
                    <span class="text-body-2 flex-grow-1">{{ opt.text }}</span>
                  </div>
                </div>
                <div>
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">Explanation</label>
                  <div class="pa-3 rounded bg-surface-variant text-body-2">
                    {{ getVersion(sideBySideLang)?.explanation || 'No explanation' }}
                  </div>
                </div>
              </div>
            </template>
            <template v-else>
              <div class="text-center py-8">
                <VIcon icon="tabler-language-off" size="36" color="disabled" class="mb-2" />
                <div class="text-body-2 text-disabled mb-3">
                  No {{ getLangLabel(sideBySideLang) }} version yet
                </div>
                <VBtn
                  size="small"
                  color="primary"
                  prepend-icon="tabler-plus"
                  @click="openAddDialog(sideBySideLang)"
                >
                  Add {{ getLangLabel(sideBySideLang) }} Version
                </VBtn>
              </div>
            </template>
          </VCol>
        </VRow>
      </template>

      <!-- Single tab view -->
      <template v-else>
        <VTabs v-model="activeTab" class="mb-4">
          <VTab
            v-for="lang in languages"
            :key="lang.code"
            :value="lang.code"
          >
            {{ lang.label }}
            <VIcon
              v-if="existingVersions.has(lang.code)"
              icon="tabler-check"
              size="16"
              color="success"
              class="ms-1"
            />
          </VTab>
        </VTabs>

        <VWindow v-model="activeTab">
          <VWindowItem
            v-for="lang in languages"
            :key="lang.code"
            :value="lang.code"
          >
            <!-- English: read-only (edit via main Edit tab) -->
            <template v-if="lang.code === 'en'">
              <div dir="ltr">
                <div class="mb-4">
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">Stem</label>
                  <div class="pa-3 rounded bg-surface-variant text-body-1">
                    {{ getVersion('en')?.stem || 'No stem text' }}
                  </div>
                </div>
                <div class="mb-4">
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">Options</label>
                  <div
                    v-for="opt in (getVersion('en')?.options ?? [])"
                    :key="opt.id"
                    class="d-flex align-center gap-3 pa-2 rounded mb-1"
                    :class="opt.isCorrect ? 'bg-success-lighten' : ''"
                  >
                    <VChip size="small" :color="opt.isCorrect ? 'success' : 'default'" label>
                      {{ opt.label ?? opt.id }}
                    </VChip>
                    <span class="text-body-2 flex-grow-1">{{ opt.text }}</span>
                    <VIcon v-if="opt.isCorrect" icon="tabler-check" color="success" size="18" />
                  </div>
                </div>
                <div>
                  <label class="text-body-2 text-medium-emphasis d-block mb-1">Explanation</label>
                  <div class="pa-3 rounded bg-surface-variant text-body-2">
                    {{ getVersion('en')?.explanation || 'No explanation' }}
                  </div>
                </div>
                <div class="text-caption text-disabled mt-3">
                  Edit English content via the Edit tab above.
                </div>
              </div>
            </template>

            <!-- Non-English: exists → show content with inline edit -->
            <template v-else-if="existingVersions.has(lang.code)">
              <!-- Inline editing mode -->
              <template v-if="editingLang === lang.code">
                <VAlert
                  v-if="editError"
                  type="error"
                  variant="tonal"
                  class="mb-4"
                  closable
                  @click:close="editError = null"
                >
                  {{ editError }}
                </VAlert>

                <div :dir="lang.dir">
                  <AppTextarea
                    v-model="editForm.stem"
                    label="Question Stem"
                    rows="3"
                    class="mb-4"
                    :dir="lang.dir"
                  />

                  <label class="text-body-2 text-medium-emphasis d-block mb-2">Answer Options</label>
                  <div
                    v-for="(opt) in editForm.options"
                    :key="opt.id"
                    class="d-flex align-center gap-3 mb-3"
                  >
                    <VChip size="small" :color="opt.isCorrect ? 'success' : 'default'" label>
                      {{ opt.id }}
                    </VChip>
                    <AppTextField
                      v-model="opt.text"
                      :placeholder="`Option ${opt.id}`"
                      density="compact"
                      class="flex-grow-1"
                      :dir="lang.dir"
                    />
                    <VIcon v-if="opt.isCorrect" icon="tabler-check" color="success" size="18" />
                  </div>

                  <AppTextarea
                    v-model="editForm.explanation"
                    label="Explanation"
                    rows="3"
                    class="mt-2"
                    :dir="lang.dir"
                  />
                </div>

                <div class="d-flex gap-2 justify-end mt-4">
                  <VBtn variant="tonal" color="secondary" @click="cancelEditing">
                    Cancel
                  </VBtn>
                  <VBtn color="primary" :loading="isSavingEdit" @click="saveEditing">
                    Save
                  </VBtn>
                </div>
              </template>

              <!-- Read-only mode -->
              <template v-else>
                <div :dir="lang.dir">
                  <div class="mb-4">
                    <label class="text-body-2 text-medium-emphasis d-block mb-1">Stem</label>
                    <div class="pa-3 rounded bg-surface-variant text-body-1">
                      {{ getVersion(lang.code)?.stem || 'No stem text' }}
                    </div>
                  </div>
                  <div class="mb-4">
                    <label class="text-body-2 text-medium-emphasis d-block mb-1">Options</label>
                    <div
                      v-for="opt in (getVersion(lang.code)?.options ?? [])"
                      :key="opt.id"
                      class="d-flex align-center gap-3 pa-2 rounded mb-1"
                      :class="opt.isCorrect ? 'bg-success-lighten' : ''"
                    >
                      <VChip size="small" :color="opt.isCorrect ? 'success' : 'default'" label>
                        {{ opt.label ?? opt.id }}
                      </VChip>
                      <span class="text-body-2 flex-grow-1">{{ opt.text }}</span>
                      <VIcon v-if="opt.isCorrect" icon="tabler-check" color="success" size="18" />
                    </div>
                  </div>
                  <div>
                    <label class="text-body-2 text-medium-emphasis d-block mb-1">Explanation</label>
                    <div class="pa-3 rounded bg-surface-variant text-body-2">
                      {{ getVersion(lang.code)?.explanation || 'No explanation' }}
                    </div>
                  </div>
                </div>
                <div class="d-flex justify-end mt-4">
                  <VBtn
                    size="small"
                    variant="tonal"
                    color="primary"
                    prepend-icon="tabler-pencil"
                    @click="startEditing(lang.code)"
                  >
                    Edit {{ lang.label }} Version
                  </VBtn>
                </div>
              </template>
            </template>

            <!-- Non-English: does not exist -->
            <template v-else>
              <div class="text-center py-8">
                <VIcon icon="tabler-language-off" size="48" color="disabled" class="mb-3" />
                <div class="text-body-1 text-disabled mb-4">
                  No {{ lang.label }} version available
                </div>
                <VBtn
                  color="primary"
                  prepend-icon="tabler-plus"
                  @click="openAddDialog(lang.code)"
                >
                  Add {{ lang.label }} Version
                </VBtn>
              </div>
            </template>
          </VWindowItem>
        </VWindow>
      </template>
    </VCardText>
  </VCard>

  <!-- Add language version dialog -->
  <VDialog
    v-model="showAddDialog"
    max-width="640"
    persistent
  >
    <VCard>
      <VCardTitle>
        Add {{ dialogLangLabel }} Version
      </VCardTitle>

      <VCardText :dir="dialogLangDir">
        <VAlert
          v-if="errorMsg"
          type="error"
          class="mb-4"
          closable
          @click:close="errorMsg = null"
        >
          {{ errorMsg }}
        </VAlert>

        <AppTextarea
          v-model="newVersionForm.stem"
          label="Question Stem"
          rows="3"
          class="mb-4"
          :dir="dialogLangDir"
        />

        <label class="text-body-2 text-medium-emphasis d-block mb-2">Answer Options</label>
        <div
          v-for="(opt) in newVersionForm.options"
          :key="opt.id"
          class="d-flex align-center gap-3 mb-3"
        >
          <VChip size="small" :color="opt.isCorrect ? 'success' : 'default'" label>
            {{ opt.id }}
          </VChip>
          <AppTextField
            v-model="opt.text"
            :placeholder="`Option ${opt.id}`"
            density="compact"
            class="flex-grow-1"
            :dir="dialogLangDir"
          />
          <VIcon v-if="opt.isCorrect" icon="tabler-check" color="success" size="18" />
        </div>

        <AppTextarea
          v-model="newVersionForm.explanation"
          label="Explanation"
          rows="3"
          class="mt-4"
          :dir="dialogLangDir"
        />
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn variant="tonal" @click="showAddDialog = false">
          Cancel
        </VBtn>
        <VBtn color="primary" :loading="isSubmitting" @click="submitNewVersion">
          Add Version
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
