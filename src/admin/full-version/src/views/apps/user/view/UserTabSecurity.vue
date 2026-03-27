<script setup lang="ts">
interface Props {
  userId: string
}

const props = defineProps<Props>()

const isForceResetting = ref(false)
const forceResetSuccess = ref(false)
const forceResetError = ref('')
const isTwoFactorDialogOpen = ref(false)

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
