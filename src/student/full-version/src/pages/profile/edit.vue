<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApiQuery } from '@/composables/useApiQuery'
import { useApiMutation } from '@/composables/useApiMutation'
import type { ProfileDto, ProfilePatchDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.profileEdit',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()
const router = useRouter()

const profileQuery = useApiQuery<ProfileDto>('/api/me/profile')
const patchMutation = useApiMutation<ProfileDto, ProfilePatchDto>('/api/me/profile', 'PATCH')

const displayName = ref('')
const bio = ref('')
const visibility = ref<'public' | 'class-only' | 'private'>('class-only')

const saved = ref(false)
const saveError = ref<string | null>(null)

watch(
  () => profileQuery.data.value,
  next => {
    if (next) {
      displayName.value = next.displayName
      bio.value = next.bio || ''
      visibility.value = next.visibility
    }
  },
  { immediate: true },
)

async function handleSave() {
  saved.value = false
  saveError.value = null
  try {
    await patchMutation.execute({
      displayName: displayName.value,
      bio: bio.value,
      visibility: visibility.value,
    })
    saved.value = true
  }
  catch (err) {
    saveError.value = (err as Error).message || t('error.serverError')
  }
}

function handleCancel() {
  router.push('/profile')
}
</script>

<template>
  <div
    class="profile-edit-page pa-4"
    data-testid="profile-edit-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('profile.editTitle') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('profile.editSubtitle') }}
    </p>

    <div
      v-if="profileQuery.loading.value && !profileQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="edit-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VCard
      v-else-if="profileQuery.data.value"
      variant="outlined"
      class="pa-6"
    >
      <VAlert
        v-if="saved"
        type="success"
        variant="tonal"
        class="mb-4"
        data-testid="edit-saved"
      >
        {{ t('profile.editSaved') }}
      </VAlert>

      <VAlert
        v-if="saveError"
        type="error"
        variant="tonal"
        class="mb-4"
        data-testid="edit-error"
      >
        {{ saveError }}
      </VAlert>

      <VTextField
        v-model="displayName"
        :label="t('profile.displayNameLabel')"
        variant="outlined"
        class="mb-4"
        data-testid="edit-display-name"
      />

      <VTextarea
        v-model="bio"
        :label="t('profile.bioLabel')"
        :placeholder="t('profile.bioPlaceholder')"
        variant="outlined"
        rows="3"
        class="mb-4"
        data-testid="edit-bio"
      />

      <VSelect
        v-model="visibility"
        :label="t('profile.visibilityLabel')"
        :items="[
          { title: t('profile.visibilityPublic'), value: 'public' },
          { title: t('profile.visibilityClass'), value: 'class-only' },
          { title: t('profile.visibilityPrivate'), value: 'private' },
        ]"
        variant="outlined"
        class="mb-4"
        data-testid="edit-visibility"
      />

      <div class="d-flex justify-end ga-3">
        <VBtn
          variant="text"
          data-testid="edit-cancel"
          @click="handleCancel"
        >
          {{ t('common.cancel') }}
        </VBtn>
        <VBtn
          color="primary"
          :loading="patchMutation.loading.value"
          prepend-icon="tabler-device-floppy"
          data-testid="edit-save"
          @click="handleSave"
        >
          {{ t('common.save') }}
        </VBtn>
      </div>
    </VCard>
  </div>
</template>

<style scoped>
.profile-edit-page {
  max-inline-size: 700px;
  margin-inline: auto;
}
</style>
