<script lang="ts" setup>
import { computed, onMounted, ref } from 'vue'
import { firebaseAuth } from '@/plugins/firebase'

interface LinkedProvider {
  providerId: string
  displayName: string
  email?: string | null
  linkedAt?: string | null
}

interface MeProfile {
  uid: string
  email: string
  emailVerified: boolean
  displayName: string
  photoUrl?: string | null
  role: string
  locale: string
  timezone: string
  school?: string | null
  createdAt: string
  lastLoginAt?: string | null
  providers: LinkedProvider[]
  mfaEnrolled: boolean
}

const profile = ref<MeProfile | null>(null)
const loading = ref(true)
const saving = ref(false)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)

// Form fields (editable)
const displayNameInput = ref('')
const localeInput = ref('en')
const timezoneInput = ref('UTC')

// Locale options drive what Firebase custom claim we write on save. Match
// the platform-wide language set (en / he / ar) defined in i18n/locales/.
const localeOptions = [
  { value: 'en', title: 'English' },
  { value: 'he', title: 'עברית' },
  { value: 'ar', title: 'العربية' },
]

// A reasonable set of common timezones. The backend is free to accept any
// IANA zone; this list is for the picker UX only.
const timezoneOptions = [
  'UTC', 'Asia/Jerusalem', 'Asia/Beirut', 'Asia/Riyadh', 'Asia/Dubai',
  'Europe/London', 'Europe/Berlin', 'Europe/Paris',
  'America/New_York', 'America/Los_Angeles', 'America/Sao_Paulo',
].map(z => ({ value: z, title: z }))

// Confirmation dialog for GDPR Art. 17 self-delete.
const showDeleteDialog = ref(false)
const deleteConfirmation = ref('')
const deleteInFlight = ref(false)
const deleteError = ref<string | null>(null)
const deleteConfirmationMatches = computed(() => deleteConfirmation.value === 'DELETE MY ACCOUNT')

async function authedFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const user = firebaseAuth.currentUser
  const headers: Record<string, string> = {
    'Accept': 'application/json',
    'Content-Type': 'application/json',
    ...((init.headers ?? {}) as Record<string, string>),
  }
  if (user) {
    const token = await user.getIdToken()
    headers.Authorization = `Bearer ${token}`
  }
  return fetch(url, { ...init, headers })
}

async function loadProfile() {
  loading.value = true
  errorMessage.value = null
  try {
    const r = await authedFetch('/api/admin/me/profile')
    if (!r.ok) throw new Error(`HTTP ${r.status}`)
    const data = await r.json() as MeProfile
    profile.value = data
    displayNameInput.value = data.displayName
    localeInput.value = data.locale
    timezoneInput.value = data.timezone
  }
  catch (err) {
    errorMessage.value = `Failed to load profile: ${(err as Error).message}`
  }
  finally {
    loading.value = false
  }
}

async function saveProfile() {
  if (!profile.value) return
  saving.value = true
  errorMessage.value = null
  successMessage.value = null
  try {
    const r = await authedFetch('/api/admin/me/profile', {
      method: 'PATCH',
      body: JSON.stringify({
        displayName: displayNameInput.value,
        locale: localeInput.value,
        timezone: timezoneInput.value,
      }),
    })
    if (!r.ok) {
      const body = await r.json().catch(() => ({}))
      throw new Error(body?.message ?? `HTTP ${r.status}`)
    }
    successMessage.value = 'Profile saved.'
    // Refresh the ID token so updated claims propagate immediately.
    await firebaseAuth.currentUser?.getIdToken(true)
    await loadProfile()
  }
  catch (err) {
    errorMessage.value = `Save failed: ${(err as Error).message}`
  }
  finally {
    saving.value = false
  }
}

async function confirmDeleteAccount() {
  if (!deleteConfirmationMatches.value) return
  deleteInFlight.value = true
  deleteError.value = null
  try {
    const r = await authedFetch('/api/admin/me/', {
      method: 'DELETE',
      body: JSON.stringify({ confirmation: 'DELETE MY ACCOUNT' }),
    })
    if (!r.ok) {
      const body = await r.json().catch(() => ({}))
      throw new Error(body?.message ?? `HTTP ${r.status}`)
    }
    // Sign out client-side. The refresh token is already revoked server-side,
    // and the AdminUser row is flagged for erasure by the retention worker.
    await firebaseAuth.signOut()
    // Hard-reload to the sign-in page so no stale SPA state remains.
    window.location.href = '/login'
  }
  catch (err) {
    deleteError.value = (err as Error).message
  }
  finally {
    deleteInFlight.value = false
  }
}

onMounted(loadProfile)
</script>

<template>
  <VCard>
    <!-- Hardcoded English: the admin SPA is English-only per the
         language-strategy memory. Student SPA handles he/ar/en i18n. -->
    <VCardTitle>Profile</VCardTitle>
    <VCardSubtitle>
      Your account information and preferences. Changes are saved to Firebase and the admin directory.
    </VCardSubtitle>

    <VCardText v-if="loading">
      <VSkeletonLoader type="card" />
    </VCardText>

    <VCardText v-else-if="errorMessage && !profile">
      <VAlert type="error" variant="tonal" data-testid="profile-load-error">{{ errorMessage }}</VAlert>
    </VCardText>

    <VCardText v-else-if="profile">
      <VRow>
        <VCol cols="12" md="4" class="text-center">
          <VAvatar size="120" color="primary" variant="tonal">
            <VImg v-if="profile.photoUrl" :src="profile.photoUrl" :alt="profile.displayName" />
            <span v-else class="text-h3">{{ (profile.displayName || profile.email).slice(0, 2).toUpperCase() }}</span>
          </VAvatar>
          <div class="text-h6 mt-4">{{ profile.displayName || profile.email }}</div>
          <VChip size="small" color="info" class="mt-1">{{ profile.role }}</VChip>
          <div class="text-caption text-medium-emphasis mt-3">
            {{ profile.email }}
            <VIcon
              v-if="profile.emailVerified"
              icon="tabler-circle-check"
              size="16"
              color="success"
              class="ms-1"
            />
          </div>
          <div v-if="profile.school" class="text-caption text-medium-emphasis mt-1">
            {{ profile.school }}
          </div>
          <div class="text-caption text-medium-emphasis mt-1">
            Member since {{ new Date(profile.createdAt).toLocaleDateString() }}
          </div>
        </VCol>

        <VCol cols="12" md="8">
          <VForm @submit.prevent="saveProfile">
            <VRow>
              <VCol cols="12">
                <VTextField
                  v-model="displayNameInput"
                  label="Display name"
                  :maxlength="120"
                  counter
                  data-testid="profile-display-name"
                />
              </VCol>
              <VCol cols="12" md="6">
                <VSelect
                  v-model="localeInput"
                  :items="localeOptions"
                  label="Interface language"
                  item-title="title"
                  item-value="value"
                  data-testid="profile-locale"
                />
              </VCol>
              <VCol cols="12" md="6">
                <VSelect
                  v-model="timezoneInput"
                  :items="timezoneOptions"
                  label="Timezone"
                  item-title="title"
                  item-value="value"
                  data-testid="profile-timezone"
                />
              </VCol>
              <VCol cols="12">
                <VTextField
                  :model-value="profile.email"
                  label="Email (contact your admin to change)"
                  readonly
                  density="compact"
                  variant="outlined"
                />
              </VCol>
              <VCol cols="12" class="d-flex gap-3">
                <VBtn color="primary" type="submit" :loading="saving" data-testid="profile-save">
                  Save changes
                </VBtn>
                <VBtn variant="text" @click="loadProfile">Reset</VBtn>
              </VCol>
              <VCol v-if="errorMessage" cols="12">
                <VAlert type="error" variant="tonal">{{ errorMessage }}</VAlert>
              </VCol>
              <VCol v-if="successMessage" cols="12">
                <VAlert type="success" variant="tonal">{{ successMessage }}</VAlert>
              </VCol>
            </VRow>
          </VForm>
        </VCol>
      </VRow>
    </VCardText>

    <VDivider class="my-4" />

    <!-- GDPR Article 17 — self-erasure. Locked behind a typed confirmation -->
    <VCardText>
      <div class="text-h6 mb-2 text-error">Danger zone</div>
      <div class="text-body-2 text-medium-emphasis mb-4">
        Deleting your account revokes all active sessions, removes your Firebase
        credentials, and schedules your admin directory record for erasure under
        GDPR Article 17. This cannot be undone.
      </div>
      <VBtn color="error" variant="outlined" @click="showDeleteDialog = true" data-testid="delete-account-open">
        <VIcon icon="tabler-trash" start />
        Delete my account
      </VBtn>
    </VCardText>

    <VDialog v-model="showDeleteDialog" max-width="540">
      <VCard>
        <VCardTitle class="text-error">Delete your account</VCardTitle>
        <VCardText>
          <p class="mb-3">
            This permanently deletes <strong>{{ profile?.email }}</strong> from Firebase
            and schedules your admin directory record for GDPR Article 17 erasure.
            Active sessions will be signed out immediately.
          </p>
          <p class="mb-3">
            Type <code>DELETE MY ACCOUNT</code> below to confirm.
          </p>
          <VTextField
            v-model="deleteConfirmation"
            label="Type DELETE MY ACCOUNT"
            :rules="[v => v === 'DELETE MY ACCOUNT' || 'Must match exactly']"
            data-testid="delete-account-confirm"
          />
          <VAlert v-if="deleteError" type="error" variant="tonal" class="mt-3">
            {{ deleteError }}
          </VAlert>
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn variant="text" @click="showDeleteDialog = false">Cancel</VBtn>
          <VBtn
            color="error"
            :disabled="!deleteConfirmationMatches || deleteInFlight"
            :loading="deleteInFlight"
            data-testid="delete-account-confirm-btn"
            @click="confirmDeleteAccount"
          >
            Delete permanently
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </VCard>
</template>
