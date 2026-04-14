<script setup lang="ts">
/**
 * FIND-privacy-018: BlockUserDialog
 * ICO Children's Code Std 11 — block a user from appearing in social feeds.
 * Submits to POST /api/social/block.
 */
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { $api } from '@/api/$api'
import type { BlockUserResponse } from '@/api/types/common'

interface Props {
  modelValue: boolean
  targetStudentId: string
  targetDisplayName: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  blocked: [response: BlockUserResponse]
}>()

const { t } = useI18n()

const submitting = ref(false)
const errorMessage = ref<string | null>(null)

function close() {
  emit('update:modelValue', false)
  errorMessage.value = null
}

async function confirmBlock() {
  submitting.value = true
  errorMessage.value = null

  try {
    const response = await $api<BlockUserResponse>('/api/social/block', {
      method: 'POST' as any,
      body: {
        targetStudentId: props.targetStudentId,
      } as any,
    })

    emit('blocked', response)
    close()
  }
  catch (err) {
    console.error('[FIND-privacy-018] block user failed', {
      targetStudentId: props.targetStudentId,
      error: err,
    })
    errorMessage.value = err instanceof Error ? err.message : t('social.block.error')
  }
  finally {
    submitting.value = false
  }
}
</script>

<template>
  <VDialog
    :model-value="modelValue"
    max-width="400"
    persistent
    data-testid="block-user-dialog"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <VCard>
      <VCardTitle class="d-flex align-center pa-4">
        <VIcon
          icon="tabler-ban"
          class="me-2"
          color="error"
          aria-hidden="true"
        />
        {{ t('social.block.title') }}
      </VCardTitle>

      <VCardText class="pt-0">
        <p class="text-body-2 text-medium-emphasis">
          {{ t('social.block.confirmMessage', { name: targetDisplayName }) }}
        </p>

        <VAlert
          v-if="errorMessage"
          type="error"
          variant="tonal"
          class="mt-3"
          data-testid="block-error-alert"
        >
          {{ errorMessage }}
        </VAlert>
      </VCardText>

      <VCardActions class="pa-4 pt-0">
        <VSpacer />
        <VBtn
          variant="text"
          data-testid="block-cancel-btn"
          @click="close"
        >
          {{ t('common.cancel') }}
        </VBtn>
        <VBtn
          color="error"
          variant="elevated"
          :loading="submitting"
          data-testid="block-confirm-btn"
          @click="confirmBlock"
        >
          {{ t('social.block.confirmBtn') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
