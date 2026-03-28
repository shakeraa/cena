<script setup lang="ts">
import { $api } from '@/utils/api'

interface Props {
  questionId: string
  question: any
}

const props = defineProps<Props>()
const emit = defineEmits<{ versionAdded: [] }>()

const activeTab = ref('en')
const showAddDialog = ref(false)
const isSubmitting = ref(false)
const errorMsg = ref<string | null>(null)

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

  // Pre-populate isCorrect from the English version
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

const currentLangDir = computed(() =>
  languages.find(l => l.code === activeTab.value)?.dir ?? 'ltr',
)

const dialogLangDir = computed(() =>
  languages.find(l => l.code === newVersionLang.value)?.dir ?? 'ltr',
)

const dialogLangLabel = computed(() =>
  languages.find(l => l.code === newVersionLang.value)?.label ?? newVersionLang.value,
)
</script>

<template>
  <VCard>
    <VCardItem>
      <VCardTitle class="d-flex align-center gap-2">
        <VIcon
          icon="tabler-language"
          size="20"
        />
        Language Versions
      </VCardTitle>
    </VCardItem>

    <VCardText>
      <VTabs
        v-model="activeTab"
        class="mb-4"
      >
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
          <!-- Version exists: show read-only content -->
          <template v-if="existingVersions.has(lang.code)">
            <div :dir="lang.dir">
              <!-- Stem -->
              <div class="mb-4">
                <label class="text-body-2 text-medium-emphasis d-block mb-1">Stem</label>
                <div class="pa-3 rounded bg-surface-variant text-body-1">
                  {{ getVersion(lang.code)?.stem || 'No stem text' }}
                </div>
              </div>

              <!-- Options -->
              <div class="mb-4">
                <label class="text-body-2 text-medium-emphasis d-block mb-1">Options</label>
                <div
                  v-for="opt in (getVersion(lang.code)?.options ?? [])"
                  :key="opt.id"
                  class="d-flex align-center gap-3 pa-2 rounded mb-1"
                  :class="opt.isCorrect ? 'bg-success-lighten' : ''"
                >
                  <VChip
                    size="small"
                    :color="opt.isCorrect ? 'success' : 'default'"
                    label
                  >
                    {{ opt.label ?? opt.id }}
                  </VChip>
                  <span class="text-body-2 flex-grow-1">{{ opt.text }}</span>
                  <VIcon
                    v-if="opt.isCorrect"
                    icon="tabler-check"
                    color="success"
                    size="18"
                  />
                </div>
              </div>

              <!-- Explanation -->
              <div>
                <label class="text-body-2 text-medium-emphasis d-block mb-1">Explanation</label>
                <div class="pa-3 rounded bg-surface-variant text-body-2">
                  {{ getVersion(lang.code)?.explanation || 'No explanation' }}
                </div>
              </div>
            </div>
          </template>

          <!-- Version does not exist: show add button -->
          <template v-else>
            <div class="text-center py-8">
              <VIcon
                icon="tabler-language-off"
                size="48"
                color="disabled"
                class="mb-3"
              />
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

        <!-- Stem -->
        <AppTextarea
          v-model="newVersionForm.stem"
          label="Question Stem"
          rows="3"
          class="mb-4"
          :dir="dialogLangDir"
        />

        <!-- Options -->
        <label class="text-body-2 text-medium-emphasis d-block mb-2">Answer Options</label>
        <div
          v-for="(opt, idx) in newVersionForm.options"
          :key="opt.id"
          class="d-flex align-center gap-3 mb-3"
        >
          <VChip
            size="small"
            :color="opt.isCorrect ? 'success' : 'default'"
            label
          >
            {{ opt.id }}
          </VChip>
          <AppTextField
            v-model="opt.text"
            :placeholder="`Option ${opt.id}`"
            density="compact"
            class="flex-grow-1"
            :dir="dialogLangDir"
          />
          <VIcon
            v-if="opt.isCorrect"
            icon="tabler-check"
            color="success"
            size="18"
          />
        </div>

        <!-- Explanation -->
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
        <VBtn
          variant="tonal"
          @click="showAddDialog = false"
        >
          Cancel
        </VBtn>
        <VBtn
          color="primary"
          :loading="isSubmitting"
          @click="submitNewVersion"
        >
          Add Version
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
