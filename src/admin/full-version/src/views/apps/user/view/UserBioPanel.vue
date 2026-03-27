<script setup lang="ts">
import type { CenaUserRole, CenaUserStatus } from '@db/apps/users/types'

interface Props {
  userData: {
    id: string
    uid: string
    fullName: string
    email: string
    role: CenaUserRole
    status: CenaUserStatus
    school: string
    grade: string
    avatar: string
    locale: string
    createdAt: string
    lastLoginAt: string | null
  }
}

interface Emit {
  (e: 'userUpdated'): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

const isUserInfoEditDialogVisible = ref(false)
const isSuspending = ref(false)

const resolveUserRoleVariant = (role: CenaUserRole) => {
  const map: Record<CenaUserRole, { color: string; icon: string }> = {
    SUPER_ADMIN: { color: 'error', icon: 'tabler-crown' },
    ADMIN: { color: 'error', icon: 'tabler-shield' },
    MODERATOR: { color: 'warning', icon: 'tabler-eye-check' },
    TEACHER: { color: 'info', icon: 'tabler-chalkboard' },
    STUDENT: { color: 'success', icon: 'tabler-school' },
    PARENT: { color: 'secondary', icon: 'tabler-users' },
  }

  return map[role] ?? { color: 'primary', icon: 'tabler-user' }
}

const resolveStatusVariant = (status: CenaUserStatus) => {
  const map: Record<CenaUserStatus, string> = {
    active: 'success',
    suspended: 'error',
    pending: 'warning',
  }

  return map[status] ?? 'primary'
}

const formatRoleLabel = (role: CenaUserRole) => {
  return role.replace('_', ' ')
}

const formatLocale = (locale: string) => {
  const map: Record<string, string> = {
    en: 'English',
    he: 'Hebrew',
    ar: 'Arabic',
  }

  return map[locale] ?? locale
}

const formatDate = (dateStr: string | null) => {
  if (!dateStr) return 'Never'

  return new Date(dateStr).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const toggleSuspend = async () => {
  isSuspending.value = true

  try {
    if (props.userData.status === 'suspended') {
      await $api(`/admin/users/${props.userData.id}/activate`, {
        method: 'POST',
      })
    }
    else {
      await $api(`/admin/users/${props.userData.id}/suspend`, {
        method: 'POST',
      })
    }

    emit('userUpdated')
  }
  catch (e) {
    console.error('Failed to toggle suspend', e)
  }
  finally {
    isSuspending.value = false
  }
}
</script>

<template>
  <VRow>
    <!-- User Details -->
    <VCol cols="12">
      <VCard v-if="props.userData">
        <VCardText class="text-center pt-12">
          <!-- Avatar -->
          <VAvatar
            rounded
            :size="100"
            :color="!props.userData.avatar ? 'primary' : undefined"
            :variant="!props.userData.avatar ? 'tonal' : undefined"
          >
            <VImg
              v-if="props.userData.avatar"
              :src="props.userData.avatar"
            />
            <span
              v-else
              class="text-5xl font-weight-medium"
            >
              {{ avatarText(props.userData.fullName) }}
            </span>
          </VAvatar>

          <!-- User fullName -->
          <h5 class="text-h5 mt-4">
            {{ props.userData.fullName }}
          </h5>

          <!-- Email -->
          <div class="text-body-1 text-medium-emphasis mt-1">
            {{ props.userData.email }}
          </div>

          <!-- Role chip -->
          <VChip
            label
            :color="resolveUserRoleVariant(props.userData.role).color"
            size="small"
            class="mt-4"
          >
            <VIcon
              start
              :size="16"
              :icon="resolveUserRoleVariant(props.userData.role).icon"
            />
            {{ formatRoleLabel(props.userData.role) }}
          </VChip>

          <!-- Status chip -->
          <VChip
            label
            :color="resolveStatusVariant(props.userData.status)"
            size="small"
            class="text-capitalize mt-2 ms-2"
          >
            {{ props.userData.status }}
          </VChip>
        </VCardText>

        <VCardText>
          <!-- Details -->
          <h5 class="text-h5">
            Details
          </h5>

          <VDivider class="my-4" />

          <!-- User Details list -->
          <VList class="card-list mt-2">
            <VListItem>
              <VListItemTitle>
                <h6 class="text-h6">
                  School:
                  <span class="text-body-1">
                    {{ props.userData.school || '--' }}
                  </span>
                </h6>
              </VListItemTitle>
            </VListItem>

            <VListItem>
              <VListItemTitle>
                <h6 class="text-h6">
                  Grade:
                  <span class="text-body-1">
                    {{ props.userData.grade ? `Grade ${props.userData.grade}` : '--' }}
                  </span>
                </h6>
              </VListItemTitle>
            </VListItem>

            <VListItem>
              <VListItemTitle>
                <h6 class="text-h6">
                  Language:
                  <span class="text-body-1">
                    {{ formatLocale(props.userData.locale) }}
                  </span>
                </h6>
              </VListItemTitle>
            </VListItem>

            <VListItem>
              <VListItemTitle>
                <h6 class="text-h6">
                  Member Since:
                  <span class="text-body-1">
                    {{ formatDate(props.userData.createdAt) }}
                  </span>
                </h6>
              </VListItemTitle>
            </VListItem>

            <VListItem>
              <VListItemTitle>
                <h6 class="text-h6">
                  Last Login:
                  <span class="text-body-1">
                    {{ formatDate(props.userData.lastLoginAt) }}
                  </span>
                </h6>
              </VListItemTitle>
            </VListItem>
          </VList>
        </VCardText>

        <!-- Edit and Suspend button -->
        <VCardText class="d-flex justify-center gap-x-4">
          <VBtn
            variant="elevated"
            @click="isUserInfoEditDialogVisible = true"
          >
            Edit
          </VBtn>

          <VBtn
            variant="tonal"
            :color="props.userData.status === 'suspended' ? 'success' : 'error'"
            :loading="isSuspending"
            @click="toggleSuspend"
          >
            {{ props.userData.status === 'suspended' ? 'Activate' : 'Suspend' }}
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>

  <!-- Edit user info dialog -->
  <UserInfoEditDialog
    v-model:is-dialog-visible="isUserInfoEditDialogVisible"
    :user-data="props.userData"
  />
</template>

<style lang="scss" scoped>
.card-list {
  --v-card-list-gap: 0.5rem;
}
</style>
