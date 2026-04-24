<script setup lang="ts">
interface Props {
  userId: string
  userStatus: string
}

const props = defineProps<Props>()
const emit = defineEmits<{ (e: 'userUpdated'): void }>()

const isForceResetting = ref(false)
const forceResetSuccess = ref(false)
const forceResetError = ref('')
const isTwoFactorDialogOpen = ref(false)

// Account status
const isTogglingStatus = ref(false)
const statusToggleError = ref('')

// Revoke all sessions
const isRevokingAll = ref(false)
const revokeAllSuccess = ref(false)
const revokeAllError = ref('')

// 2FA status
const twoFactorEnabled = ref(false)
const isLoading2FA = ref(true)

// API keys (for admin users)
const apiKeys = ref<Array<{ id: string; name: string; key: string; createdAt: string }>>([])
const isLoadingKeys = ref(true)

const apiKeyHeaders = [
  { title: 'Name', key: 'name' },
  { title: 'Key', key: 'key' },
  { title: 'Created', key: 'createdAt' },
  { title: 'Actions', key: 'actions', sortable: false },
]

// Fetch security info
const fetchSecurityInfo = async () => {
  try {
    const data = await $api(`/admin/users/${props.userId}/security`)

    twoFactorEnabled.value = data.twoFactorEnabled ?? false
    apiKeys.value = data.apiKeys ?? []
  }
  catch (e) {
    console.error('Failed to fetch security info', e)
  }
  finally {
    isLoading2FA.value = false
    isLoadingKeys.value = false
  }
}

fetchSecurityInfo()

// Suspend / activate account
const toggleAccountStatus = async () => {
  const isSuspended = props.userStatus === 'suspended'
  const action = isSuspended ? 'activate' : 'suspend'
  const confirmMsg = isSuspended
    ? 'Activate this account? The user will regain access immediately.'
    : 'Suspend this account? The user will lose access immediately.'

  if (!confirm(confirmMsg))
    return

  isTogglingStatus.value = true
  statusToggleError.value = ''

  try {
    await $api(`/admin/users/${props.userId}/${action}`, { method: 'POST' })
    emit('userUpdated')
  }
  catch (e: any) {
    statusToggleError.value = e?.data?.message ?? `Failed to ${action} account`
    console.error(`Failed to ${action} account`, e)
  }
  finally {
    isTogglingStatus.value = false
  }
}

// Revoke all sessions
const revokeAllSessions = async () => {
  if (!confirm('This will force the user to re-login on all devices. Continue?'))
    return

  isRevokingAll.value = true
  revokeAllSuccess.value = false
  revokeAllError.value = ''

  try {
    const data = await $api(`/admin/users/${props.userId}/sessions`)
    const activeSessions: Array<{ id: string }> = data.sessions ?? []

    await Promise.all(
      activeSessions.map(s =>
        $api(`/admin/users/${props.userId}/sessions/${s.id}`, { method: 'DELETE' }),
      ),
    )

    revokeAllSuccess.value = true
    setTimeout(() => { revokeAllSuccess.value = false }, 5000)
  }
  catch (e: any) {
    revokeAllError.value = e?.data?.message ?? 'Failed to revoke sessions'
    console.error('Failed to revoke all sessions', e)
  }
  finally {
    isRevokingAll.value = false
  }
}

// Force password reset
const forcePasswordReset = async () => {
  isForceResetting.value = true
  forceResetSuccess.value = false
  forceResetError.value = ''

  try {
    await $api(`/admin/users/${props.userId}/force-reset`, {
      method: 'POST',
    })

    forceResetSuccess.value = true

    setTimeout(() => {
      forceResetSuccess.value = false
    }, 5000)
  }
  catch (e: any) {
    forceResetError.value = e?.data?.message ?? 'Failed to send password reset'
    console.error('Failed to force reset', e)
  }
  finally {
    isForceResetting.value = false
  }
}

// Delete API key
const deleteApiKey = async (keyId: string) => {
  try {
    await $api(`/admin/users/${props.userId}/api-keys/${keyId}`, {
      method: 'DELETE',
    })

    apiKeys.value = apiKeys.value.filter(k => k.id !== keyId)
  }
  catch (e) {
    console.error('Failed to delete API key', e)
  }
}

const formatDate = (dateStr: string) => {
  if (!dateStr) return '--'

  return new Date(dateStr).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

const maskKey = (key: string) => {
  if (!key || key.length < 8) return key

  return `${key.slice(0, 4)}${'*'.repeat(key.length - 8)}${key.slice(-4)}`
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <!-- Account Status -->
      <VCard
        title="Account Status"
        class="mb-6"
      >
        <VCardText>
          <VAlert
            v-if="statusToggleError"
            type="error"
            variant="tonal"
            closable
            class="mb-4"
            :text="statusToggleError"
            @click:close="statusToggleError = ''"
          />

          <div class="d-flex align-center justify-space-between flex-wrap gap-4">
            <div class="d-flex align-center gap-3">
              <span class="text-body-1">Current status:</span>
              <VChip
                :color="userStatus === 'active' ? 'success' : 'error'"
                size="small"
                label
                class="text-capitalize"
              >
                {{ userStatus }}
              </VChip>
            </div>

            <VBtn
              v-if="userStatus === 'suspended'"
              color="success"
              variant="outlined"
              :loading="isTogglingStatus"
              prepend-icon="tabler-user-check"
              @click="toggleAccountStatus"
            >
              Activate Account
            </VBtn>

            <VBtn
              v-else
              color="warning"
              variant="outlined"
              :loading="isTogglingStatus"
              prepend-icon="tabler-user-off"
              @click="toggleAccountStatus"
            >
              Suspend Account
            </VBtn>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12">
      <!-- Revoke All Sessions -->
      <VCard
        title="Sessions"
        class="mb-6"
      >
        <VCardText>
          <VAlert
            v-if="revokeAllSuccess"
            type="success"
            variant="tonal"
            closable
            class="mb-4"
            text="All sessions have been revoked. The user must re-login on all devices."
          />

          <VAlert
            v-if="revokeAllError"
            type="error"
            variant="tonal"
            closable
            class="mb-4"
            :text="revokeAllError"
            @click:close="revokeAllError = ''"
          />

          <p class="text-body-1 mb-4">
            Revoking all sessions will immediately sign the user out of every device and browser. They will need to log in again.
          </p>

          <VBtn
            color="error"
            variant="outlined"
            :loading="isRevokingAll"
            prepend-icon="tabler-logout"
            @click="revokeAllSessions"
          >
            Revoke All Sessions
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12">
      <!-- Force Password Reset -->
      <VCard title="Password Reset">
        <VCardText>
          <VAlert
            v-if="forceResetSuccess"
            type="success"
            variant="tonal"
            closable
            class="mb-4"
            text="Password reset email sent to the user."
          />

          <VAlert
            v-if="forceResetError"
            type="error"
            variant="tonal"
            closable
            class="mb-4"
            :text="forceResetError"
            @click:close="forceResetError = ''"
          />

          <p class="text-body-1 mb-4">
            Sending a password reset will email the user a link to set a new password. Their current password will remain valid until they complete the reset.
          </p>

          <VBtn
            color="warning"
            :loading="isForceResetting"
            prepend-icon="tabler-lock-open"
            @click="forcePasswordReset"
          >
            Force Password Reset
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12">
      <!-- Two-factor authentication -->
      <VCard
        title="Two-Factor Authentication"
        subtitle="Additional security layer for this account."
      >
        <VCardText>
          <div
            v-if="isLoading2FA"
            class="d-flex justify-center pa-4"
          >
            <VProgressCircular indeterminate />
          </div>

          <template v-else>
            <div class="d-flex align-center gap-4">
              <VChip
                :color="twoFactorEnabled ? 'success' : 'warning'"
                label
                size="small"
              >
                {{ twoFactorEnabled ? 'Enabled' : 'Disabled' }}
              </VChip>

              <span class="text-body-1">
                Two-factor authentication is currently
                <strong>{{ twoFactorEnabled ? 'enabled' : 'disabled' }}</strong>
                for this user.
              </span>
            </div>

            <p class="mb-0 mt-4 text-body-2">
              Two-factor authentication adds an additional layer of security to the account by requiring more than just a password to log in.
            </p>
          </template>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12">
      <!-- API Keys -->
      <VCard title="API Keys">
        <VCardText>
          <div
            v-if="isLoadingKeys"
            class="d-flex justify-center pa-4"
          >
            <VProgressCircular indeterminate />
          </div>

          <template v-else-if="apiKeys.length === 0">
            <p class="text-body-1 text-medium-emphasis mb-0">
              No API keys have been created for this user.
            </p>
          </template>

          <template v-else>
            <VDataTable
              :items="apiKeys"
              :headers="apiKeyHeaders"
              hide-default-footer
              class="text-no-wrap"
            >
              <template #item.key="{ item }">
                <code>{{ maskKey(item.key) }}</code>
              </template>

              <template #item.createdAt="{ item }">
                {{ formatDate(item.createdAt) }}
              </template>

              <template #item.actions="{ item }">
                <IconBtn
                  color="error"
                  @click="deleteApiKey(item.id)"
                >
                  <VIcon icon="tabler-trash" />
                </IconBtn>
              </template>

              <template #bottom />
            </VDataTable>
          </template>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>
