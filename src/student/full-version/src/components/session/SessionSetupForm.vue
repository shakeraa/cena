<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SessionMode, SessionStartRequest } from '@/api/types/common'

interface Props {
  loading?: boolean
}

withDefaults(defineProps<Props>(), { loading: false })
const emit = defineEmits<{
  submit: [payload: SessionStartRequest]
}>()

const { t } = useI18n()

const SUBJECTS = ['math', 'physics', 'chemistry', 'biology', 'english', 'history']
const DURATIONS = [5, 10, 15, 30, 45, 60]
const MODES: SessionMode[] = ['practice', 'challenge', 'review', 'diagnostic']

const selectedSubjects = ref<string[]>(['math'])
const durationMinutes = ref<number>(15)
const mode = ref<SessionMode>('practice')

function toggleSubject(subject: string) {
  const idx = selectedSubjects.value.indexOf(subject)
  if (idx >= 0)
    selectedSubjects.value.splice(idx, 1)
  else
    selectedSubjects.value.push(subject)
}

function handleSubmit() {
  if (selectedSubjects.value.length === 0)
    return

  emit('submit', {
    subjects: selectedSubjects.value,
    durationMinutes: durationMinutes.value,
    mode: mode.value,
  })
}
</script>

<template>
  <form
    class="session-setup-form"
    data-testid="session-setup-form"
    @submit.prevent="handleSubmit"
  >
    <section class="mb-6">
      <div class="text-subtitle-1 mb-3">
        {{ t('session.setup.subjectsLabel') }}
      </div>
      <div
        class="d-flex flex-wrap ga-2"
        data-testid="setup-subjects"
      >
        <VChip
          v-for="s in SUBJECTS"
          :key="s"
          :color="selectedSubjects.includes(s) ? 'primary' : undefined"
          :variant="selectedSubjects.includes(s) ? 'flat' : 'outlined'"
          :data-testid="`setup-subject-${s}`"
          size="default"
          @click="toggleSubject(s)"
        >
          {{ t(`session.setup.subjects.${s}`) }}
        </VChip>
      </div>
    </section>

    <section class="mb-6">
      <div class="text-subtitle-1 mb-3">
        {{ t('session.setup.durationLabel') }}
      </div>
      <VBtnToggle
        v-model="durationMinutes"
        mandatory
        color="primary"
        variant="outlined"
        divided
        data-testid="setup-duration"
      >
        <VBtn
          v-for="d in DURATIONS"
          :key="d"
          :value="d"
          :data-testid="`setup-duration-${d}`"
        >
          {{ t('session.setup.durationMinutes', d, { minutes: d }) }}
        </VBtn>
      </VBtnToggle>
    </section>

    <section class="mb-6">
      <div class="text-subtitle-1 mb-3">
        {{ t('session.setup.modeLabel') }}
      </div>
      <VBtnToggle
        v-model="mode"
        mandatory
        color="primary"
        variant="outlined"
        divided
        data-testid="setup-mode"
      >
        <VBtn
          v-for="m in MODES"
          :key="m"
          :value="m"
          :data-testid="`setup-mode-${m}`"
        >
          {{ t(`session.setup.modes.${m}`) }}
        </VBtn>
      </VBtnToggle>
    </section>

    <VBtn
      type="submit"
      color="primary"
      size="large"
      block
      :loading="loading"
      :disabled="loading || selectedSubjects.length === 0"
      prepend-icon="tabler-player-play"
      data-testid="setup-start"
    >
      {{ t('session.setup.startCta') }}
    </VBtn>
  </form>
</template>
