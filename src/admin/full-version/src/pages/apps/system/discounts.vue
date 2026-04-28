<script setup lang="ts">
// =============================================================================
// Cena Platform — Admin discounts page (per-user discount-codes feature)
//
// Super-admin issues / lists / revokes per-user personal discounts.
// Endpoints (all AdminOnly):
//   POST   /api/admin/discounts                  — issue
//   GET    /api/admin/discounts[?email=...]      — list / search
//   DELETE /api/admin/discounts/{assignmentId}   — revoke
//
// Date-statement copy only (no time-pressure framing) per Cena ship-gate
// banned-terms rules.
// =============================================================================

import { $api } from '@/utils/api'

definePage({ meta: { action: 'manage', subject: 'Settings' } })

interface DiscountAssignment {
  assignmentId: string
  targetEmailNormalized: string
  status: 'Issued' | 'Redeemed' | 'Revoked'
  discountKind: 'PercentOff' | 'AmountOff'
  discountValue: number
  durationMonths: number
  reason: string
  issuedAt: string
  redeemedAt: string | null
  revokedAt: string | null
}

interface IssueResponse {
  assignmentId: string
  targetEmailNormalized: string
  promotionCodeString: string
  status: string
}

interface ApiError {
  code: string
  message: string
  category: string
  details?: Record<string, string> | null
}

// Form state
const form = reactive({
  targetEmail: '',
  discountKind: 'PercentOff' as 'PercentOff' | 'AmountOff',
  // For PercentOff this is whole percent (UI-friendly); we convert to basis points on submit.
  discountPercent: 50,
  // For AmountOff this is whole shekels; we convert to agorot on submit.
  discountAmountShekels: 50,
  durationMonths: 3,
  reason: '',
})

const issuing = ref(false)
const issueError = ref<string | null>(null)
const issueSuccess = ref<{ promoCode: string; email: string } | null>(null)

// List state
const listing = ref(false)
const listError = ref<string | null>(null)
const searchEmail = ref('')
const assignments = ref<DiscountAssignment[]>([])

// Per-row revoke loading state.
const revokingIds = reactive<Record<string, boolean>>({})

const isFormValid = computed(() => {
  if (!form.targetEmail.trim()) return false
  if (!form.reason.trim()) return false
  if (form.durationMonths < 1 || form.durationMonths > 36) return false
  if (form.discountKind === 'PercentOff') {
    return form.discountPercent >= 1 && form.discountPercent <= 100
  }
  return form.discountAmountShekels > 0
})

async function fetchAssignments() {
  listing.value = true
  listError.value = null
  try {
    const url = searchEmail.value.trim()
      ? `/admin/discounts?email=${encodeURIComponent(searchEmail.value.trim())}`
      : '/admin/discounts'
    const data = await $api<DiscountAssignment[]>(url)
    assignments.value = Array.isArray(data) ? data : []
  }
  catch (err: any) {
    listError.value = err?.data?.message ?? err?.message ?? 'Failed to load discounts'
  }
  finally {
    listing.value = false
  }
}

async function submitIssue() {
  if (!isFormValid.value) return
  issuing.value = true
  issueError.value = null
  issueSuccess.value = null

  // Convert UI units to wire units.
  const discountValue = form.discountKind === 'PercentOff'
    ? Math.round(form.discountPercent * 100)             // % → basis points
    : Math.round(form.discountAmountShekels * 100)        // shekels → agorot

  try {
    const result = await $api<IssueResponse>('/admin/discounts', {
      method: 'POST',
      body: {
        targetEmail: form.targetEmail.trim(),
        discountKind: form.discountKind,
        discountValue,
        durationMonths: form.durationMonths,
        reason: form.reason.trim(),
      },
    })
    issueSuccess.value = {
      promoCode: result.promotionCodeString,
      email: result.targetEmailNormalized,
    }
    // Reset form (keep duration/kind to make repeated issues easier).
    form.targetEmail = ''
    form.reason = ''
    await fetchAssignments()
  }
  catch (err: any) {
    const apiErr: ApiError | undefined = err?.data ?? err?.body
    issueError.value = apiErr?.message ?? err?.message ?? 'Failed to issue discount'
  }
  finally {
    issuing.value = false
  }
}

async function revoke(row: DiscountAssignment) {
  if (row.status !== 'Issued') return
  // Native confirm — admin pages elsewhere use the same simple gate.
  // eslint-disable-next-line no-alert
  if (!confirm(`Revoke discount for ${row.targetEmailNormalized}? This cannot be undone.`)) return

  revokingIds[row.assignmentId] = true
  try {
    await $api(`/admin/discounts/${encodeURIComponent(row.assignmentId)}`, {
      method: 'DELETE',
    })
    await fetchAssignments()
  }
  catch (err: any) {
    listError.value = err?.data?.message ?? err?.message ?? 'Failed to revoke'
  }
  finally {
    revokingIds[row.assignmentId] = false
  }
}

function statusColor(status: string): string {
  switch (status) {
    case 'Issued': return 'success'
    case 'Redeemed': return 'info'
    case 'Revoked': return 'default'
    default: return 'default'
  }
}

function formatDiscount(row: DiscountAssignment): string {
  if (row.discountKind === 'PercentOff') {
    // basis points → percent
    return `${(row.discountValue / 100).toFixed(2).replace(/\.00$/, '')}% off`
  }
  // agorot → shekels
  return `₪${(row.discountValue / 100).toFixed(2).replace(/\.00$/, '')} off`
}

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  try {
    return new Date(iso).toISOString().slice(0, 10)
  }
  catch {
    return '—'
  }
}

onMounted(fetchAssignments)
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Personal Discounts
        </h4>
        <div class="text-body-1">
          Issue a personal Stripe discount tied to a single user email.
          Discount auto-applies at checkout for the matched user.
          Note: a personal discount replaces the sibling discount for the assigned user.
        </div>
      </div>
    </div>

    <VRow>
      <!-- Issue form -->
      <VCol
        cols="12"
        md="5"
      >
        <VCard>
          <VCardItem>
            <template #prepend>
              <VAvatar
                color="primary"
                variant="tonal"
                rounded
              >
                <VIcon icon="tabler-discount-2" />
              </VAvatar>
            </template>
            <VCardTitle>Issue discount</VCardTitle>
            <VCardSubtitle>Pre-bound to a user email; auto-applied at first checkout</VCardSubtitle>
          </VCardItem>

          <VCardText>
            <VAlert
              v-if="issueError"
              type="error"
              variant="tonal"
              class="mb-4"
              closable
              @click:close="issueError = null"
            >
              {{ issueError }}
            </VAlert>

            <VAlert
              v-if="issueSuccess"
              type="success"
              variant="tonal"
              class="mb-4"
              closable
              @click:close="issueSuccess = null"
            >
              <div class="font-weight-medium">
                Discount issued for {{ issueSuccess.email }}.
              </div>
              <div class="text-body-2 mt-1">
                Promotion code (for reference): <code>{{ issueSuccess.promoCode }}</code>
              </div>
            </VAlert>

            <VRow>
              <VCol cols="12">
                <AppTextField
                  v-model="form.targetEmail"
                  label="Target email"
                  placeholder="user@example.com"
                  type="email"
                  data-testid="discount-form-email"
                  hint="Server normalises Gmail dot/+ aliases"
                  persistent-hint
                />
              </VCol>

              <VCol cols="12">
                <VRadioGroup
                  v-model="form.discountKind"
                  inline
                  label="Kind"
                  data-testid="discount-form-kind"
                >
                  <VRadio
                    value="PercentOff"
                    label="Percent off"
                  />
                  <VRadio
                    value="AmountOff"
                    label="Amount off (₪)"
                  />
                </VRadioGroup>
              </VCol>

              <VCol
                v-if="form.discountKind === 'PercentOff'"
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="form.discountPercent"
                  label="Percent (%)"
                  type="number"
                  :min="1"
                  :max="100"
                  data-testid="discount-form-percent"
                />
              </VCol>

              <VCol
                v-else
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="form.discountAmountShekels"
                  label="Amount (₪)"
                  type="number"
                  :min="1"
                  data-testid="discount-form-amount"
                />
              </VCol>

              <VCol
                cols="12"
                sm="6"
              >
                <AppTextField
                  v-model.number="form.durationMonths"
                  label="Duration (months)"
                  type="number"
                  :min="1"
                  :max="36"
                  data-testid="discount-form-duration"
                  hint="Discount applies to first paid invoice + N-1 renewals"
                  persistent-hint
                />
              </VCol>

              <VCol cols="12">
                <AppTextarea
                  v-model="form.reason"
                  label="Reason (audit log)"
                  rows="3"
                  data-testid="discount-form-reason"
                  placeholder="e.g. Loyalty discount, founding-member thanks, partner referral"
                />
              </VCol>

              <VCol cols="12">
                <VBtn
                  color="primary"
                  block
                  :loading="issuing"
                  :disabled="!isFormValid || issuing"
                  prepend-icon="tabler-send"
                  data-testid="discount-form-submit"
                  @click="submitIssue"
                >
                  Issue discount
                </VBtn>
              </VCol>
            </VRow>
          </VCardText>
        </VCard>
      </VCol>

      <!-- List -->
      <VCol
        cols="12"
        md="7"
      >
        <VCard>
          <VCardItem>
            <template #prepend>
              <VAvatar
                color="info"
                variant="tonal"
                rounded
              >
                <VIcon icon="tabler-list-check" />
              </VAvatar>
            </template>
            <VCardTitle>Issued discounts</VCardTitle>
            <VCardSubtitle>Recent issuances; search by email to filter</VCardSubtitle>
          </VCardItem>

          <VCardText>
            <div class="d-flex flex-wrap gap-3 mb-4">
              <AppTextField
                v-model="searchEmail"
                label="Filter by email"
                density="compact"
                clearable
                style="max-inline-size: 24rem"
                data-testid="discount-search-email"
                @keyup.enter="fetchAssignments"
                @click:clear="() => { searchEmail = ''; fetchAssignments() }"
              />
              <VBtn
                variant="tonal"
                :loading="listing"
                prepend-icon="tabler-refresh"
                data-testid="discount-search-go"
                @click="fetchAssignments"
              >
                Refresh
              </VBtn>
            </div>

            <VAlert
              v-if="listError"
              type="error"
              variant="tonal"
              class="mb-4"
              closable
              @click:close="listError = null"
            >
              {{ listError }}
            </VAlert>

            <div
              v-if="listing"
              class="d-flex justify-center my-6"
            >
              <VProgressCircular indeterminate />
            </div>

            <div
              v-else-if="assignments.length === 0"
              class="text-body-2 text-disabled text-center py-6"
              data-testid="discount-list-empty"
            >
              No discount assignments to show.
            </div>

            <VTable
              v-else
              hover
              data-testid="discount-list-table"
            >
              <thead>
                <tr>
                  <th>Email</th>
                  <th>Status</th>
                  <th>Discount</th>
                  <th>Duration</th>
                  <th>Issued</th>
                  <th>Reason</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in assignments"
                  :key="row.assignmentId"
                  :data-testid="`discount-row-${row.assignmentId}`"
                >
                  <td>{{ row.targetEmailNormalized }}</td>
                  <td>
                    <VChip
                      :color="statusColor(row.status)"
                      size="small"
                    >
                      {{ row.status }}
                    </VChip>
                  </td>
                  <td>{{ formatDiscount(row) }}</td>
                  <td>{{ row.durationMonths }} mo</td>
                  <td>{{ formatDate(row.issuedAt) }}</td>
                  <td
                    class="text-truncate"
                    style="max-inline-size: 14rem"
                    :title="row.reason"
                  >
                    {{ row.reason }}
                  </td>
                  <td>
                    <VBtn
                      v-if="row.status === 'Issued'"
                      size="small"
                      variant="tonal"
                      color="error"
                      :loading="revokingIds[row.assignmentId]"
                      prepend-icon="tabler-x"
                      :data-testid="`discount-revoke-${row.assignmentId}`"
                      @click="revoke(row)"
                    >
                      Revoke
                    </VBtn>
                    <span
                      v-else
                      class="text-body-2 text-disabled"
                    >—</span>
                  </td>
                </tr>
              </tbody>
            </VTable>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>
  </div>
</template>
