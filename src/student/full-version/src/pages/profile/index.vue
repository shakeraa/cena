<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useApiQuery } from '@/composables/useApiQuery'
import type { MeBootstrapDto, ProfileDto } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.profile',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const meQuery = useApiQuery<MeBootstrapDto>('/api/me')
const profileQuery = useApiQuery<ProfileDto>('/api/me/profile')
</script>

<template>
  <div
    class="profile-page pa-4"
    data-testid="profile-page"
  >
    <div
      v-if="(meQuery.loading.value && !meQuery.data.value) || (profileQuery.loading.value && !profileQuery.data.value)"
      class="d-flex justify-center py-12"
      data-testid="profile-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="meQuery.error.value || profileQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="profile-error"
    >
      {{ (meQuery.error.value || profileQuery.error.value)?.message }}
    </VAlert>

    <template v-else-if="meQuery.data.value && profileQuery.data.value">
      <VCard
        variant="flat"
        color="primary"
        class="pa-6 mb-4"
        data-testid="profile-hero"
      >
        <div class="d-flex align-center">
          <VAvatar
            size="96"
            color="surface"
            class="me-4"
          >
            <VIcon
              icon="tabler-user"
              size="48"
              aria-hidden="true"
            />
          </VAvatar>
          <div>
            <div
              class="text-h4 text-white"
              data-testid="profile-name"
            >
              {{ profileQuery.data.value.displayName }}
            </div>
            <div
              class="text-body-1 text-white opacity-90"
              data-testid="profile-email"
            >
              {{ profileQuery.data.value.email }}
            </div>
            <div class="d-flex align-center ga-2 mt-2">
              <VChip
                size="small"
                variant="flat"
                color="white"
                text-color="primary"
              >
                {{ t('profile.levelLabel', { level: meQuery.data.value.level }) }}
              </VChip>
              <VChip
                size="small"
                variant="flat"
                color="white"
                text-color="primary"
              >
                {{ t('profile.streakLabel', meQuery.data.value.streakDays) }}
              </VChip>
            </div>
          </div>
          <VSpacer />
          <VBtn
            color="white"
            variant="flat"
            to="/profile/edit"
            prepend-icon="tabler-edit"
            data-testid="profile-edit-btn"
          >
            {{ t('profile.editProfile') }}
          </VBtn>
        </div>
      </VCard>

      <VCard
        variant="outlined"
        class="pa-5 mb-4"
        data-testid="profile-bio"
      >
        <div class="text-h6 mb-2">
          {{ t('profile.aboutTitle') }}
        </div>
        <p
          v-if="profileQuery.data.value.bio"
          class="text-body-1"
        >
          {{ profileQuery.data.value.bio }}
        </p>
        <p
          v-else
          class="text-body-2 text-medium-emphasis fst-italic"
        >
          {{ t('profile.bioEmpty') }}
        </p>
      </VCard>

      <VCard
        variant="outlined"
        class="pa-5"
        data-testid="profile-subjects"
      >
        <div class="text-h6 mb-3">
          {{ t('profile.favoriteSubjectsTitle') }}
        </div>
        <div
          v-if="profileQuery.data.value.favoriteSubjects.length > 0"
          class="d-flex flex-wrap ga-2"
        >
          <VChip
            v-for="s in profileQuery.data.value.favoriteSubjects"
            :key="s"
            variant="tonal"
            color="primary"
          >
            {{ t(`session.setup.subjects.${s}`, s) }}
          </VChip>
        </div>
        <p
          v-else
          class="text-body-2 text-medium-emphasis fst-italic"
        >
          {{ t('profile.subjectsEmpty') }}
        </p>
      </VCard>
    </template>
  </div>
</template>

<style scoped>
.profile-page {
  max-inline-size: 900px;
  margin-inline: auto;
}
</style>
