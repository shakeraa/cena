<script setup lang="ts">
interface PipelineConfig {
  maxConcurrentIngestions: number
  maxFileSizeMb: number
  autoClassify: boolean
  autoDedup: boolean
  minQualityScore: number
  allowedFileTypes: string[]
  defaultLanguage: string
  defaultSubject: string
}

const props = defineProps<{
  modelValue: PipelineConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [config: PipelineConfig]
}>()

const update = <K extends keyof PipelineConfig>(key: K, val: PipelineConfig[K]) => {
  emit('update:modelValue', { ...props.modelValue, [key]: val })
}

const allFileTypes = ['pdf', 'png', 'jpg', 'jpeg', 'webp', 'csv', 'xlsx', 'docx', 'txt']

const languageOptions = [
  { title: 'Hebrew', value: 'he' },
  { title: 'Arabic', value: 'ar' },
  { title: 'English', value: 'en' },
]

const subjectOptions = [
  { title: 'Math', value: 'math' },
  { title: 'Physics', value: 'physics' },
  { title: 'Chemistry', value: 'chemistry' },
  { title: 'Biology', value: 'biology' },
  { title: 'Computer Science', value: 'cs' },
]

const toggleFileType = (ft: string) => {
  const current = [...props.modelValue.allowedFileTypes]
  const idx = current.indexOf(ft)
  if (idx >= 0)
    current.splice(idx, 1)
  else
    current.push(ft)
  update('allowedFileTypes', current)
}
</script>

<template>
  <div>
    <VRow>
      <!-- Max concurrent -->
      <VCol cols="12">
        <div class="d-flex align-center justify-space-between mb-1">
          <span class="text-body-1">Max Concurrent Ingestions</span>
          <VChip
            size="small"
            variant="tonal"
          >
            {{ modelValue.maxConcurrentIngestions }}
          </VChip>
        </div>
        <VSlider
          :model-value="modelValue.maxConcurrentIngestions"
          :min="1"
          :max="20"
          :step="1"
          color="primary"
          thumb-label
          hide-details
          @update:model-value="update('maxConcurrentIngestions', $event as number)"
        />
      </VCol>

      <!-- Max file size -->
      <VCol cols="12">
        <div class="d-flex align-center justify-space-between mb-1">
          <span class="text-body-1">Max File Size (MB)</span>
          <VChip
            size="small"
            variant="tonal"
          >
            {{ modelValue.maxFileSizeMb }} MB
          </VChip>
        </div>
        <VSlider
          :model-value="modelValue.maxFileSizeMb"
          :min="1"
          :max="50"
          :step="1"
          color="primary"
          thumb-label
          hide-details
          @update:model-value="update('maxFileSizeMb', $event as number)"
        />
      </VCol>

      <!-- Min quality score -->
      <VCol cols="12">
        <div class="d-flex align-center justify-space-between mb-1">
          <span class="text-body-1">Minimum Quality Score</span>
          <VChip
            size="small"
            variant="tonal"
          >
            {{ (modelValue.minQualityScore * 100).toFixed(0) }}%
          </VChip>
        </div>
        <VSlider
          :model-value="modelValue.minQualityScore"
          :min="0"
          :max="1"
          :step="0.05"
          color="primary"
          thumb-label
          hide-details
          @update:model-value="update('minQualityScore', $event as number)"
        />
      </VCol>

      <!-- Toggles -->
      <VCol
        cols="12"
        sm="6"
      >
        <VSwitch
          :model-value="modelValue.autoClassify"
          label="Auto-Classify Content"
          color="success"
          hide-details
          @update:model-value="update('autoClassify', $event as boolean)"
        />
      </VCol>
      <VCol
        cols="12"
        sm="6"
      >
        <VSwitch
          :model-value="modelValue.autoDedup"
          label="Auto-Deduplicate"
          color="success"
          hide-details
          @update:model-value="update('autoDedup', $event as boolean)"
        />
      </VCol>

      <!-- Allowed file types -->
      <VCol cols="12">
        <div class="text-body-1 mb-2">
          Allowed File Types
        </div>
        <div class="d-flex flex-wrap gap-2">
          <VChip
            v-for="ft in allFileTypes"
            :key="ft"
            :color="modelValue.allowedFileTypes.includes(ft) ? 'primary' : 'default'"
            :variant="modelValue.allowedFileTypes.includes(ft) ? 'flat' : 'outlined'"
            label
            class="cursor-pointer"
            @click="toggleFileType(ft)"
          >
            .{{ ft }}
          </VChip>
        </div>
      </VCol>

      <!-- Default language -->
      <VCol
        cols="12"
        sm="6"
      >
        <VSelect
          :model-value="modelValue.defaultLanguage"
          :items="languageOptions"
          label="Default Language"
          @update:model-value="update('defaultLanguage', $event)"
        />
      </VCol>

      <!-- Default subject -->
      <VCol
        cols="12"
        sm="6"
      >
        <VSelect
          :model-value="modelValue.defaultSubject"
          :items="subjectOptions"
          label="Default Subject"
          @update:model-value="update('defaultSubject', $event)"
        />
      </VCol>
    </VRow>
  </div>
</template>
