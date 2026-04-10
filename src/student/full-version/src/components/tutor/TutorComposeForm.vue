<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

interface Props {
  disabled?: boolean
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), { disabled: false, loading: false })
const emit = defineEmits<{
  submit: [content: string]
}>()

const { t } = useI18n()
const content = ref('')

function handleSubmit() {
  const trimmed = content.value.trim()

  if (!trimmed || props.disabled || props.loading)
    return

  emit('submit', trimmed)
  content.value = ''
}
</script>

<template>
  <form
    class="tutor-compose-form d-flex align-end"
    data-testid="tutor-compose-form"
    @submit.prevent="handleSubmit"
  >
    <VTextarea
      v-model="content"
      :placeholder="t('tutor.compose.placeholder')"
      :disabled="disabled"
      rows="1"
      auto-grow
      max-rows="5"
      variant="outlined"
      hide-details
      density="comfortable"
      class="flex-grow-1 me-2"
      data-testid="tutor-compose-input"
      @keydown.enter.exact.prevent="handleSubmit"
    />
    <VBtn
      type="submit"
      color="primary"
      icon
      :loading="loading"
      :disabled="disabled || loading || !content.trim()"
      :aria-label="t('tutor.compose.sendAria')"
      data-testid="tutor-compose-submit"
    >
      <VIcon
        icon="tabler-send"
        aria-hidden="true"
      />
    </VBtn>
  </form>
</template>
