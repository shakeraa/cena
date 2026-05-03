<script setup lang="ts">
/**
 * OnboardingCatalogPicker.vue — Platform catalog picker for student onboarding V2 (TENANCY-P2e)
 *
 * Shows available curriculum tracks during onboarding. Student picks a track
 * (or enters a join code) to create their enrollment. Supports join-code
 * entry for classroom enrollment.
 */

import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

interface CurriculumTrack {
  trackId: string
  code: string
  title: string
  subject: string
  targetExam?: string
  status: 'draft' | 'seeding' | 'ready'
}

const props = defineProps<{
  tracks: CurriculumTrack[]
}>()

const emit = defineEmits<{
  select: [trackId: string]
  joinCode: [code: string]
}>()

const { t } = useI18n()
const selectedTrack = ref<string>('')
const joinCodeInput = ref('')
const showJoinCode = ref(false)

const availableTracks = computed(() =>
  props.tracks.filter(t => t.status === 'ready'),
)

function handleSelectTrack() {
  if (selectedTrack.value)
    emit('select', selectedTrack.value)
}

function handleJoinCode() {
  const code = joinCodeInput.value.trim().toUpperCase()
  if (code.length >= 4)
    emit('joinCode', code)
}
</script>

<template>
  <VCard
    class="catalog-picker pa-6"
    variant="flat"
  >
    <h2 class="text-h5 mb-2">
      {{ t('onboarding.catalog.title', 'Choose your learning track') }}
    </h2>
    <p class="text-body-2 text-medium-emphasis mb-6">
      {{ t('onboarding.catalog.subtitle', 'Pick the exam you\'re preparing for, or enter a classroom join code.') }}
    </p>

    <!-- Track cards -->
    <div class="catalog-picker__grid">
      <VCard
        v-for="track in availableTracks"
        :key="track.trackId"
        :variant="selectedTrack === track.trackId ? 'elevated' : 'outlined'"
        class="catalog-picker__card"
        :class="{ 'catalog-picker__card--selected': selectedTrack === track.trackId }"
        role="radio"
        :aria-checked="selectedTrack === track.trackId"
        tabindex="0"
        @click="selectedTrack = track.trackId"
        @keydown.enter="selectedTrack = track.trackId"
      >
        <VCardTitle class="text-subtitle-1">
          {{ track.title }}
        </VCardTitle>
        <VCardSubtitle>{{ track.code }}</VCardSubtitle>
        <VCardText
          v-if="track.targetExam"
          class="text-caption"
        >
          {{ t('onboarding.catalog.prepFor', 'Prepares for:') }} {{ track.targetExam }}
        </VCardText>
      </VCard>
    </div>

    <VBtn
      :disabled="!selectedTrack"
      color="primary"
      size="large"
      block
      class="mt-6"
      @click="handleSelectTrack"
    >
      {{ t('onboarding.catalog.startLearning', 'Start learning') }}
    </VBtn>

    <VDivider class="my-6" />

    <!-- Join code entry -->
    <div class="text-center">
      <VBtn
        v-if="!showJoinCode"
        variant="text"
        @click="showJoinCode = true"
      >
        {{ t('onboarding.catalog.haveJoinCode', 'Have a classroom join code?') }}
      </VBtn>

      <div
        v-else
        class="d-flex gap-2 align-center"
      >
        <VTextField
          v-model="joinCodeInput"
          :label="t('onboarding.catalog.joinCodeLabel', 'Join code')"
          variant="outlined"
          density="compact"
          maxlength="8"
          class="flex-grow-1"
          @keydown.enter="handleJoinCode"
        />
        <VBtn
          :disabled="joinCodeInput.trim().length < 4"
          color="secondary"
          @click="handleJoinCode"
        >
          {{ t('onboarding.catalog.join', 'Join') }}
        </VBtn>
      </div>
    </div>
  </VCard>
</template>

<style scoped lang="scss">
.catalog-picker {
  &__grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
    gap: 1rem;
  }

  &__card {
    cursor: pointer;
    transition: border-color 0.2s, box-shadow 0.2s;
    min-height: 44px; // touch target

    &--selected {
      border-color: rgb(var(--v-theme-primary));
      box-shadow: 0 0 0 2px rgb(var(--v-theme-primary) / 0.2);
    }
  }
}

/* RDY-030b: prefers-reduced-motion guard (WCAG 2.3.3).
   Component-local animations/transitions reduced to an imperceptible
   0.01ms so vestibular-sensitive users don't trigger motion-related
   symptoms. Complements the global reset in styles.scss. */
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
</style>
