<script setup lang="ts">
/**
 * FIND-privacy-018: ReportDialog
 * ICO Children's Code Std 11 — in-app content reporting for minors.
 * Opens when a student clicks "Report" on any social UGC surface.
 * Submits to POST /api/social/report.
 */
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { $api } from '@/api/$api'
import type { ReportCategory, ReportContentType, SubmitReportResponse } from '@/api/types/common'

interface Props {
  modelValue: boolean
  contentType: ReportContentType
  contentId: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  reported: [response: SubmitReportResponse]
}>()

const { t } = useI18n()

const categories: ReportCategory[] = ['bullying', 'inappropriate', 'spam', 'self-harm-risk', 'other']
const selectedCategory = ref<ReportCategory | null>(null)
const reasonText = ref('')
const submitting = ref(false)
const errorMessage = ref<string | null>(null)

const canSubmit = computed(() => selectedCategory.value !== null && !submitting.value)

function close() {
  emit('update:modelValue', false)

  // Reset form state after close
  selectedCategory.value = null
  reasonText.value = ''
  errorMessage.value = null
}

async function submit() {
  if (!selectedCategory.value)
    return

  submitting.value = true
  errorMessage.value = null

  try {
    const response = await $api<SubmitReportResponse>('/api/social/report', {
      method: 'POST' as any,
      body: {
        contentType: props.contentType,
        contentId: props.contentId,
        category: selectedCategory.value,
        reason: reasonText.value || undefined,
      } as any,
    })

    emit('reported', response)
    close()
  }
  catch (err) {
    console.error('[FIND-privacy-018] report submission failed', {
      contentType: props.contentType,
      contentId: props.contentId,
      error: err,
    })
    errorMessage.value = err instanceof Error ? err.message : t('social.report.submitError')
  }
  finally {
    submitting.value = false
  }
}
</script>

<template>
  <VDialog
    :model-value="modelValue"
    max-width="480"
    persistent
    data-testid="report-dialog"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <VCard>
      <VCardTitle class="d-flex align-center pa-4">
        <VIcon
          icon="tabler-flag"
          class="me-2"
          color="error"
          aria-hidden="true"
        />
        {{ t('social.report.title') }}
      </VCardTitle>

      <VCardText class="pt-0">
        <p class="text-body-2 text-medium-emphasis mb-4">
          {{ t('social.report.description') }}
        </p>

        <div
          class="mb-4"
          role="radiogroup"
          :aria-label="t('social.report.categoryLabel')"
        >
          <div class="text-subtitle-2 mb-2">
            {{ t('social.report.categoryLabel') }}
          </div>
          <VRadioGroup
            v-model="selectedCategory"
            data-testid="report-category-group"
          >
            <VRadio
              v-for="cat in categories"
              :key="cat"
              :value="cat"
              :label="t(`social.report.category.${cat}`)"
              :data-testid="`report-category-${cat}`"
            />
          </VRadioGroup>
        </div>

        <VTextarea
          v-model="reasonText"
          :label="t('social.report.reasonLabel')"
          :placeholder="t('social.report.reasonPlaceholder')"
          rows="3"
          counter="500"
          maxlength="500"
          data-testid="report-reason-textarea"
        />

        <VAlert
          v-if="errorMessage"
          type="error"
          variant="tonal"
          class="mt-3"
          data-testid="report-error-alert"
        >
          {{ errorMessage }}
        </VAlert>
      </VCardText>

      <VCardActions class="pa-4 pt-0">
        <VSpacer />
        <VBtn
          variant="text"
          data-testid="report-cancel-btn"
          @click="close"
        >
          {{ t('common.cancel') }}
        </VBtn>
        <VBtn
          color="error"
          variant="elevated"
          :disabled="!canSubmit"
          :loading="submitting"
          data-testid="report-submit-btn"
          @click="submit"
        >
          {{ t('social.report.submitBtn') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
