<script setup lang="ts">
import { onErrorCaptured, ref } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const error = ref<Error | null>(null)
const errorCode = ref<string>('')
const copied = ref(false)

onErrorCaptured((err: Error) => {
  error.value = err

  // Stable 8-char error code so users can reference it to support.
  errorCode.value = `ERR-${Math.random().toString(36).slice(2, 10).toUpperCase()}`

  console.error('[StudentErrorBoundary]', err)

  return false
})

const copyErrorCode = async () => {
  if (!navigator?.clipboard)
    return
  await navigator.clipboard.writeText(`${errorCode.value}: ${error.value?.message ?? ''}`)
  copied.value = true
  setTimeout(() => {
    copied.value = false
  }, 2000)
}

const reset = () => {
  error.value = null
  errorCode.value = ''
}
</script>

<template>
  <div>
    <template v-if="error">
      <VCard
        class="student-error-boundary pa-6 text-center"
        variant="outlined"
        role="alert"
      >
        <VIcon
          icon="tabler-alert-triangle"
          size="48"
          color="error"
          class="mb-3"
        />
        <div class="text-h5 mb-2">
          {{ t('error.serverError') }}
        </div>
        <div class="text-body-2 text-medium-emphasis mb-4">
          {{ t('common.errorGeneric') }}
        </div>
        <div
          v-if="errorCode"
          class="text-caption mb-4 d-flex align-center justify-center gap-2"
        >
          <span>{{ t('error.errorCode') }}:</span>
          <code class="student-error-code">{{ errorCode }}</code>
          <VBtn
            size="x-small"
            variant="text"
            :aria-label="t('common.copy')"
            @click="copyErrorCode"
          >
            <VIcon
              :icon="copied ? 'tabler-check' : 'tabler-copy'"
              size="16"
            />
          </VBtn>
        </div>
        <div class="d-flex justify-center gap-3">
          <VBtn
            color="primary"
            @click="reset"
          >
            {{ t('error.tryAgain') }}
          </VBtn>
          <VBtn
            variant="text"
            href="mailto:support@cena.app"
          >
            {{ t('error.reportToSupport') }}
          </VBtn>
        </div>
      </VCard>
    </template>
    <slot v-else />
  </div>
</template>

<style scoped>
.student-error-boundary {
  max-inline-size: 520px;
  margin-inline: auto;
}

.student-error-code {
  padding: 2px 6px;
  background: rgb(var(--v-theme-grey-100));
  border-radius: 4px;
  font-family: monospace;
}
</style>
