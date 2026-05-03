<script lang="ts" setup>
import { onMounted, ref } from 'vue'
import {
  EmailAuthProvider,
  GoogleAuthProvider,
  OAuthProvider,
  linkWithPopup,
  reauthenticateWithCredential,
  unlink,
  updatePassword,
} from 'firebase/auth'
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
  providers: LinkedProvider[]
  mfaEnrolled: boolean
}

interface SignInHistoryItem {
  timestamp: string
  action: string
  ipAddress?: string | null
  userAgent?: string | null
  succeeded: boolean
}

const profile = ref<MeProfile | null>(null)
const history = ref<SignInHistoryItem[]>([])
const loading = ref(true)
const busy = ref(false)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)

// Password-change form
const pwCurrent = ref('')
const pwNew = ref('')
const pwConfirm = ref('')
const pwDialog = ref(false)
const pwError = ref<string | null>(null)
const pwBusy = ref(false)

async function authedFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const user = firebaseAuth.currentUser
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...((init.headers ?? {}) as Record<string, string>),
  }
  if (user) headers.Authorization = `Bearer ${await user.getIdToken()}`
  return fetch(url, { ...init, headers })
}

async function loadAll() {
  loading.value = true
  errorMessage.value = null
  try {
    const [pRes, hRes] = await Promise.all([
      authedFetch('/api/admin/me/profile'),
      authedFetch('/api/admin/me/sign-in-history?limit=10'),
    ])
    if (!pRes.ok) throw new Error(`Profile load failed: HTTP ${pRes.status}`)
    profile.value = await pRes.json() as MeProfile
    if (hRes.ok) {
      const h = await hRes.json() as { items: SignInHistoryItem[] }
      history.value = h.items ?? []
    }
  }
  catch (err) {
    errorMessage.value = (err as Error).message
  }
  finally {
    loading.value = false
  }
}

// ── Linked providers ──────────────────────────────────────────────────

function providerFromId(providerId: string) {
  switch (providerId) {
    case 'google.com': return new GoogleAuthProvider()
    case 'apple.com': {
      const p = new OAuthProvider('apple.com')
      p.addScope('email'); p.addScope('name')
      return p
    }
    case 'microsoft.com': return new OAuthProvider('microsoft.com')
    default: return null
  }
}

async function linkProvider(providerId: string) {
  const user = firebaseAuth.currentUser
  if (!user) return
  const provider = providerFromId(providerId)
  if (!provider) {
    errorMessage.value = `Linking ${providerId} is not supported in this build.`
    return
  }
  busy.value = true
  errorMessage.value = null; successMessage.value = null
  try {
    await linkWithPopup(user, provider)
    successMessage.value = `Linked ${providerId}.`
    await loadAll()
  }
  catch (err) {
    errorMessage.value = `Failed to link ${providerId}: ${(err as Error).message}`
  }
  finally { busy.value = false }
}

async function unlinkProvider(providerId: string) {
  const user = firebaseAuth.currentUser
  if (!user) return
  if ((profile.value?.providers.length ?? 0) <= 1) {
    errorMessage.value = 'Cannot unlink your last remaining sign-in method.'
    return
  }
  busy.value = true
  errorMessage.value = null; successMessage.value = null
  try {
    await unlink(user, providerId)
    successMessage.value = `Unlinked ${providerId}.`
    await loadAll()
  }
  catch (err) {
    errorMessage.value = `Failed to unlink ${providerId}: ${(err as Error).message}`
  }
  finally { busy.value = false }
}

// ── Password change ──────────────────────────────────────────────────

async function changePassword() {
  const user = firebaseAuth.currentUser
  if (!user || !user.email) return
  pwError.value = null
  if (pwNew.value.length < 8) {
    pwError.value = 'New password must be at least 8 characters.'
    return
  }
  if (pwNew.value !== pwConfirm.value) {
    pwError.value = 'New password confirmation does not match.'
    return
  }
  pwBusy.value = true
  try {
    // Firebase requires a recent sign-in before updating the password —
    // re-authenticate with the current one first so the update doesn't
    // bounce with `auth/requires-recent-login`.
    const cred = EmailAuthProvider.credential(user.email, pwCurrent.value)
    await reauthenticateWithCredential(user, cred)
    await updatePassword(user, pwNew.value)
    pwDialog.value = false
    pwCurrent.value = ''; pwNew.value = ''; pwConfirm.value = ''
    successMessage.value = 'Password updated.'
  }
  catch (err) {
    pwError.value = (err as Error).message
  }
  finally { pwBusy.value = false }
}

// ── Sign out everywhere ──────────────────────────────────────────────

async function signOutEverywhere() {
  busy.value = true
  errorMessage.value = null; successMessage.value = null
  try {
    const r = await authedFetch('/api/admin/me/sign-out-everywhere', { method: 'POST' })
    if (!r.ok) throw new Error(`HTTP ${r.status}`)
    // Current device must also sign out — the existing tokens are no
    // longer valid for refresh.
    await firebaseAuth.signOut()
    window.location.href = '/login'
  }
  catch (err) {
    errorMessage.value = (err as Error).message
  }
  finally { busy.value = false }
}

// Heuristic — client-side indicator whether the current account has an
// email/password provider, which is a prerequisite for the Password
// change dialog.
function hasPasswordProvider(): boolean {
  return profile.value?.providers.some(p => p.providerId === 'password') ?? false
}

onMounted(loadAll)
</script>

<template>
  <div v-if="loading">
    <VSkeletonLoader type="card" class="mb-4" />
    <VSkeletonLoader type="list-item-three-line" />
  </div>

  <div v-else>
    <VAlert v-if="successMessage" type="success" variant="tonal" class="mb-4" closable @click:close="successMessage = null">
      {{ successMessage }}
    </VAlert>
    <VAlert v-if="errorMessage" type="error" variant="tonal" class="mb-4" closable @click:close="errorMessage = null">
      {{ errorMessage }}
    </VAlert>

    <!-- Linked providers ─────────────────────────────────────────── -->
    <VCard class="mb-4">
      <VCardTitle>Linked sign-in methods</VCardTitle>
      <VCardSubtitle>
        Manage how you sign in. At least one method must remain linked.
      </VCardSubtitle>
      <VCardText>
        <VList>
          <VListItem
            v-for="p in profile?.providers ?? []"
            :key="p.providerId"
            :prepend-icon="p.providerId === 'google.com' ? 'tabler-brand-google'
              : p.providerId === 'apple.com' ? 'tabler-brand-apple'
              : p.providerId === 'microsoft.com' ? 'tabler-brand-windows'
              : p.providerId === 'phone' ? 'tabler-phone'
              : 'tabler-mail'"
            :title="p.displayName"
            :subtitle="p.email ?? undefined"
          >
            <template #append>
              <VBtn
                size="small"
                variant="text"
                color="error"
                :disabled="busy || (profile?.providers.length ?? 0) <= 1"
                @click="unlinkProvider(p.providerId)"
              >
                Unlink
              </VBtn>
            </template>
          </VListItem>
        </VList>

        <div class="mt-4 d-flex flex-wrap gap-2">
          <VBtn
            v-if="!profile?.providers.some(p => p.providerId === 'google.com')"
            prepend-icon="tabler-brand-google"
            variant="outlined"
            :disabled="busy"
            @click="linkProvider('google.com')"
          >Link Google</VBtn>
          <VBtn
            v-if="!profile?.providers.some(p => p.providerId === 'apple.com')"
            prepend-icon="tabler-brand-apple"
            variant="outlined"
            :disabled="busy"
            @click="linkProvider('apple.com')"
          >Link Apple</VBtn>
          <VBtn
            v-if="!profile?.providers.some(p => p.providerId === 'microsoft.com')"
            prepend-icon="tabler-brand-windows"
            variant="outlined"
            :disabled="busy"
            @click="linkProvider('microsoft.com')"
          >Link Microsoft</VBtn>
        </div>
      </VCardText>
    </VCard>

    <!-- Password ─────────────────────────────────────────────────── -->
    <VCard class="mb-4">
      <VCardTitle>Password</VCardTitle>
      <VCardSubtitle v-if="hasPasswordProvider()">
        Update the password you use to sign in with email.
      </VCardSubtitle>
      <VCardSubtitle v-else>
        Add an email/password sign-in method to enable password changes.
      </VCardSubtitle>
      <VCardText>
        <VBtn
          :disabled="!hasPasswordProvider()"
          prepend-icon="tabler-lock"
          @click="pwDialog = true"
        >
          Change password
        </VBtn>
      </VCardText>
    </VCard>

    <!-- Active sessions ──────────────────────────────────────────── -->
    <VCard class="mb-4">
      <VCardTitle>Active sessions</VCardTitle>
      <VCardSubtitle>
        Sign out on every device, including this one. Useful if your account was
        accessed somewhere you didn't recognise.
      </VCardSubtitle>
      <VCardText>
        <VBtn
          color="warning"
          prepend-icon="tabler-logout-2"
          :loading="busy"
          @click="signOutEverywhere"
        >
          Sign out everywhere
        </VBtn>
      </VCardText>
    </VCard>

    <!-- Sign-in history ──────────────────────────────────────────── -->
    <VCard>
      <VCardTitle>Recent sign-in activity</VCardTitle>
      <VCardSubtitle>Last 10 events from your audit trail.</VCardSubtitle>
      <VCardText>
        <VAlert v-if="!history.length" type="info" variant="tonal">
          No sign-in events recorded yet.
        </VAlert>
        <VTable v-else density="compact">
          <thead>
            <tr>
              <th>When</th>
              <th>Event</th>
              <th>IP</th>
              <th>User agent</th>
              <th class="text-end">Result</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="h in history" :key="`${h.timestamp}-${h.action}`">
              <td>{{ new Date(h.timestamp).toLocaleString() }}</td>
              <td>{{ h.action }}</td>
              <td class="text-caption">{{ h.ipAddress ?? '—' }}</td>
              <td class="text-caption text-truncate" style="max-inline-size: 260px">{{ h.userAgent ?? '—' }}</td>
              <td class="text-end">
                <VChip :color="h.succeeded ? 'success' : 'error'" size="small">
                  {{ h.succeeded ? 'Success' : 'Failed' }}
                </VChip>
              </td>
            </tr>
          </tbody>
        </VTable>
      </VCardText>
    </VCard>

    <!-- Password change dialog -->
    <VDialog v-model="pwDialog" max-width="480">
      <VCard>
        <VCardTitle>Change password</VCardTitle>
        <VCardText>
          <VTextField v-model="pwCurrent" type="password" label="Current password" autocomplete="current-password" />
          <VTextField v-model="pwNew" type="password" label="New password (8+ chars)" autocomplete="new-password" />
          <VTextField v-model="pwConfirm" type="password" label="Confirm new password" autocomplete="new-password" />
          <VAlert v-if="pwError" type="error" variant="tonal" class="mt-2">{{ pwError }}</VAlert>
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn variant="text" :disabled="pwBusy" @click="pwDialog = false">Cancel</VBtn>
          <VBtn color="primary" :loading="pwBusy" @click="changePassword">Update</VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </div>
</template>
