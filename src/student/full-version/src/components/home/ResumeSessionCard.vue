<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  sessionId: string
  subject: string
  startedAt: string
  progressPercent: number
}

const props = defineProps<Props>()
const { t } = useI18n()

const resumeUrl = computed(() => `/session/${props.sessionId}`)

const startedAtRelative = computed(() => {
  const mins = Math.max(1, Math.floor((Date.now() - new Date(props.startedAt).getTime()) / 60_000))
  if (mins < 60)
    return t('home.resume.minutesAgo', { count: mins })
  const hours = Math.floor(mins / 60)

  return t('home.resume.hoursAgo', { count: hours })
})
</script>

<template>
  <VCard
    class="resume-session-card pa-5"
    color="primary"
    variant="flat"
    data-testid="resume-session-card"
  >
    <div class="d-flex align-center gap-4">
      <VIcon
        icon="tabler-player-play-filled"
        size="48"
        color="on-primary"
        aria-hidden="true"
      />
      <div class="flex-grow-1">
        <div class="text-caption text-on-primary text-uppercase">
          {{ t('home.resume.label') }}
        </div>
        <div class="text-h6 text-on-primary">
          {{ subject }}
        </div>
        <div class="text-body-2 text-on-primary opacity-80">
          {{ startedAtRelative }}
        </div>
      </div>
      <VBtn
        :to="resumeUrl"
        color="on-primary"
        variant="flat"
        data-testid="resume-session-cta"
      >
        {{ t('home.resume.cta') }}
      </VBtn>
    </div>
    <VProgressLinear
      :model-value="progressPercent"
      color="on-primary"
      bg-color="on-primary"
      bg-opacity="0.2"
      class="mt-3"
      height="6"
      rounded
      :aria-label="t('home.resume.progressAria', { percent: progressPercent })"
    />
  </VCard>
</template>
