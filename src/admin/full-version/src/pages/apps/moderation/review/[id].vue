<script setup lang="ts">
import { sanitizeHtml } from '@/utils/sanitize'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Content',
  },
})

interface AnswerOption {
  label: string
  text: string
  isCorrect: boolean
}

interface ModerationComment {
  id: string
  author: string
  text: string
  createdAt: string
}

interface HistoryEntry {
  action: string
  actor: string
  timestamp: string
  details: string
}

interface ModerationItemDetail {
  id: string
  questionText: string
  answerOptions: AnswerOption[]
  subject: string
  topic: string
  difficulty: number
  gradeLevel: string
  conceptTags: string[]
  aiQualityScore: number
  qualityGate?: {
    compositeScore: number
    gateDecision: string
    factualAccuracy: number
    languageQuality: number
    pedagogicalQuality: number
    distractorQuality: number
    stemClarity: number
    bloomAlignment: number
    structuralValidity: number
    culturalSensitivity: number
    violationCount: number
    evaluatedAt?: string
  }
  status: string
  author: string
  submittedAt: string
  comments: ModerationComment[]
  history: HistoryEntry[]
}

const route = useRoute('apps-moderation-review-id')
const router = useRouter()

const item = ref<ModerationItemDetail | null>(null)
const loading = ref(true)
const actionLoading = ref(false)

const isApproveDialogVisible = ref(false)
const isRejectDialogVisible = ref(false)
const rejectReason = ref('')

const newComment = ref('')
const commentLoading = ref(false)

const fetchItem = async () => {
  loading.value = true
  try {
    const data = await $api<ModerationItemDetail>(`/admin/moderation/items/${route.params.id}`)

    item.value = data
  }
  catch (error) {
    console.error('Failed to fetch moderation item:', error)
    item.value = null
  }
  finally {
    loading.value = false
  }
}

const approveItem = async () => {
  if (!item.value)
    return

  actionLoading.value = true
  try {
    await $api(`/admin/moderation/items/${item.value.id}/approve`, { method: 'POST' })
    await fetchItem()
  }
  catch (error) {
    console.error('Failed to approve item:', error)
  }
  finally {
    actionLoading.value = false
  }
}

const rejectItem = async () => {
  if (!item.value || !rejectReason.value.trim())
    return

  actionLoading.value = true
  try {
    await $api(`/admin/moderation/items/${item.value.id}/reject`, {
      method: 'POST',
      body: { reason: rejectReason.value },
    })

    isRejectDialogVisible.value = false
    rejectReason.value = ''
    await fetchItem()
  }
  catch (error) {
    console.error('Failed to reject item:', error)
  }
  finally {
    actionLoading.value = false
  }
}

const flagItem = async () => {
  if (!item.value)
    return

  actionLoading.value = true
  try {
    await $api(`/admin/moderation/items/${item.value.id}/flag`, { method: 'POST' })
    await fetchItem()
  }
  catch (error) {
    console.error('Failed to flag item:', error)
  }
  finally {
    actionLoading.value = false
  }
}

const addComment = async () => {
  if (!item.value || !newComment.value.trim())
    return

  commentLoading.value = true
  try {
    await $api(`/admin/moderation/items/${item.value.id}/comments`, {
      method: 'POST',
      body: { text: newComment.value },
    })

    newComment.value = ''
    await fetchItem()
  }
  catch (error) {
    console.error('Failed to add comment:', error)
  }
  finally {
    commentLoading.value = false
  }
}

const resolveStatusColor = (status: string): string => {
  const map: Record<string, string> = {
    'pending': 'warning',
    'in-review': 'info',
    'approved': 'success',
    'rejected': 'error',
    'flagged': 'primary',
  }

  return map[status] ?? 'secondary'
}

const resolveStatusLabel = (status: string): string => {
  const map: Record<string, string> = {
    'pending': 'Pending',
    'in-review': 'In Review',
    'approved': 'Approved',
    'rejected': 'Rejected',
    'flagged': 'Flagged',
  }

  return map[status] ?? status
}

const resolveQualityScoreColor = (score: number): string => {
  if (score >= 80)
    return 'success'
  if (score >= 60)
    return 'info'
  if (score >= 40)
    return 'warning'

  return 'error'
}

const resolveHistoryActionColor = (action: string): string => {
  const map: Record<string, string> = {
    'submitted': 'info',
    'claimed': 'warning',
    'approved': 'success',
    'rejected': 'error',
    'flagged': 'primary',
    'commented': 'secondary',
  }

  return map[action] ?? 'secondary'
}

onMounted(fetchItem)
</script>

<template>
  <div v-if="loading">
    <VCard>
      <VCardText class="d-flex justify-center py-12">
        <VProgressCircular
          indeterminate
          size="48"
        />
      </VCardText>
    </VCard>
  </div>

  <div v-else-if="item">
    <!-- Header -->
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <div class="d-flex gap-2 align-center mb-2 flex-wrap">
          <VBtn
            icon="tabler-arrow-left"
            variant="text"
            size="small"
            @click="router.push({ name: 'apps-moderation-queue' })"
          />
          <h5 class="text-h5">
            Review Item #{{ item.id }}
          </h5>
          <VChip
            :color="resolveStatusColor(item.status)"
            label
            size="small"
          >
            {{ resolveStatusLabel(item.status) }}
          </VChip>
        </div>
        <div class="text-body-1 ms-9">
          Submitted by {{ item.author }} on {{ new Date(item.submittedAt).toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' }) }}
        </div>
      </div>

      <div class="d-flex gap-x-3">
        <VBtn
          color="success"
          prepend-icon="tabler-check"
          :loading="actionLoading"
          :disabled="item.status === 'approved'"
          @click="isApproveDialogVisible = true"
        >
          Approve
        </VBtn>
        <VBtn
          color="error"
          variant="tonal"
          prepend-icon="tabler-x"
          :disabled="item.status === 'rejected'"
          @click="isRejectDialogVisible = true"
        >
          Reject
        </VBtn>
        <VBtn
          color="primary"
          variant="tonal"
          prepend-icon="tabler-flag"
          :disabled="item.status === 'flagged'"
          @click="flagItem"
        >
          Flag
        </VBtn>
      </div>
    </div>

    <VRow>
      <!-- Left Column: Question & Answers -->
      <VCol
        cols="12"
        md="8"
      >
        <!-- Question Card -->
        <VCard class="mb-6">
          <VCardItem>
            <template #title>
              <h5 class="text-h5">
                Question
              </h5>
            </template>
          </VCardItem>
          <VDivider />
          <VCardText>
            <!-- eslint-disable vue/no-v-html -->
            <div
              class="text-body-1 question-content"
              v-html="sanitizeHtml(item.questionText ?? '')"
            />
            <!-- eslint-enable vue/no-v-html -->
          </VCardText>
        </VCard>

        <!-- Answer Options Card -->
        <VCard class="mb-6">
          <VCardItem>
            <template #title>
              <h5 class="text-h5">
                Answer Options
              </h5>
            </template>
          </VCardItem>
          <VDivider />
          <VCardText>
            <VList>
              <VListItem
                v-for="(option, index) in item.answerOptions"
                :key="index"
                :class="option.isCorrect ? 'correct-answer' : ''"
                class="mb-2 rounded-lg"
                :style="option.isCorrect ? 'background-color: rgba(var(--v-theme-success), 0.12)' : ''"
              >
                <template #prepend>
                  <VAvatar
                    size="32"
                    :color="option.isCorrect ? 'success' : 'secondary'"
                    variant="tonal"
                    class="me-3"
                  >
                    <span class="text-body-1 font-weight-medium">{{ option.label }}</span>
                  </VAvatar>
                </template>

                <VListItemTitle>
                  {{ option.text }}
                </VListItemTitle>

                <template
                  v-if="option.isCorrect"
                  #append
                >
                  <VIcon
                    icon="tabler-circle-check-filled"
                    color="success"
                    size="24"
                  />
                </template>
              </VListItem>
            </VList>
          </VCardText>
        </VCard>

        <!-- Comments Card -->
        <VCard>
          <VCardItem>
            <template #title>
              <h5 class="text-h5">
                Comments ({{ item.comments.length }})
              </h5>
            </template>
          </VCardItem>
          <VDivider />
          <VCardText>
            <!-- Existing Comments -->
            <div
              v-if="item.comments.length"
              class="mb-6"
            >
              <div
                v-for="comment in item.comments"
                :key="comment.id"
                class="d-flex gap-x-3 mb-4"
              >
                <VAvatar
                  size="34"
                  color="primary"
                  variant="tonal"
                >
                  <span class="text-body-2 font-weight-medium">{{ comment.author.charAt(0).toUpperCase() }}</span>
                </VAvatar>
                <div class="flex-grow-1">
                  <div class="d-flex justify-space-between align-center mb-1">
                    <h6 class="text-h6">
                      {{ comment.author }}
                    </h6>
                    <span class="text-body-2 text-disabled">
                      {{ new Date(comment.createdAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' }) }}
                    </span>
                  </div>
                  <p class="text-body-1 mb-0">
                    {{ comment.text }}
                  </p>
                </div>
              </div>
            </div>
            <div
              v-else
              class="text-body-2 text-disabled mb-6"
            >
              No comments yet.
            </div>

            <!-- Add Comment -->
            <div class="d-flex gap-x-3 align-end">
              <AppTextField
                v-model="newComment"
                placeholder="Add a comment..."
                class="flex-grow-1"
                @keyup.enter="addComment"
              />
              <VBtn
                :loading="commentLoading"
                :disabled="!newComment.trim()"
                @click="addComment"
              >
                Send
              </VBtn>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Right Column: Metadata & History -->
      <VCol
        cols="12"
        md="4"
      >
        <!-- Metadata Card -->
        <VCard class="mb-6">
          <VCardText class="d-flex flex-column gap-y-4">
            <h5 class="text-h5">
              Metadata
            </h5>

            <div class="d-flex flex-column gap-y-3">
              <div class="d-flex justify-space-between">
                <span class="text-body-1 text-high-emphasis">Subject</span>
                <span class="text-body-1 font-weight-medium">{{ item.subject }}</span>
              </div>

              <VDivider />

              <div class="d-flex justify-space-between">
                <span class="text-body-1 text-high-emphasis">Topic</span>
                <span class="text-body-1 font-weight-medium">{{ item.topic }}</span>
              </div>

              <VDivider />

              <div class="d-flex justify-space-between align-center">
                <span class="text-body-1 text-high-emphasis">Difficulty</span>
                <VRating
                  :model-value="item.difficulty"
                  readonly
                  density="compact"
                  size="20"
                  color="warning"
                  active-color="warning"
                />
              </div>

              <VDivider />

              <div class="d-flex justify-space-between">
                <span class="text-body-1 text-high-emphasis">Grade Level</span>
                <span class="text-body-1 font-weight-medium">{{ item.gradeLevel }}</span>
              </div>

              <VDivider />

              <div>
                <span class="text-body-1 text-high-emphasis d-block mb-2">Concept Tags</span>
                <div class="d-flex flex-wrap gap-2">
                  <VChip
                    v-for="tag in item.conceptTags"
                    :key="tag"
                    label
                    size="small"
                    color="secondary"
                  >
                    {{ tag }}
                  </VChip>
                </div>
              </div>
            </div>
          </VCardText>
        </VCard>

        <!-- Quality Gate Card (8-dimension breakdown) -->
        <VCard class="mb-6">
          <VCardText>
            <div class="d-flex align-center gap-2 mb-4">
              <h5 class="text-h5">
                Quality Gate
              </h5>
              <VChip
                v-if="item.qualityGate?.gateDecision"
                size="x-small"
                :color="item.qualityGate.gateDecision === 'AutoApproved' ? 'success' : item.qualityGate.gateDecision === 'AutoRejected' ? 'error' : 'warning'"
                label
              >
                {{ item.qualityGate.gateDecision }}
              </VChip>
            </div>

            <!-- Composite score -->
            <div class="d-flex align-center gap-x-4 mb-2">
              <h3 class="text-h3">
                {{ item.qualityGate ? Math.round(item.qualityGate.compositeScore) : item.aiQualityScore }}
              </h3>
              <span class="text-body-1 text-disabled">/ 100</span>
              <VSpacer />
              <span
                v-if="item.qualityGate?.violationCount"
                class="text-body-2 text-error"
              >
                {{ item.qualityGate.violationCount }} violation{{ item.qualityGate.violationCount !== 1 ? 's' : '' }}
              </span>
            </div>
            <VProgressLinear
              :model-value="item.qualityGate ? item.qualityGate.compositeScore : item.aiQualityScore"
              :color="resolveQualityScoreColor(item.qualityGate ? Math.round(item.qualityGate.compositeScore) : item.aiQualityScore)"
              height="10"
              rounded
              class="mb-4"
            />

            <!-- 8-dimension bars (only when quality gate data available) -->
            <template v-if="item.qualityGate">
              <div class="d-flex flex-column gap-2">
                <div
                  v-for="dim in [
                    { key: 'structuralValidity', label: 'Structural' },
                    { key: 'stemClarity', label: 'Stem Clarity' },
                    { key: 'distractorQuality', label: 'Distractors' },
                    { key: 'bloomAlignment', label: 'Bloom\'s' },
                    { key: 'factualAccuracy', label: 'Factual' },
                    { key: 'languageQuality', label: 'Language' },
                    { key: 'pedagogicalQuality', label: 'Pedagogy' },
                    { key: 'culturalSensitivity', label: 'Cultural' },
                  ]"
                  :key="dim.key"
                  class="d-flex align-center gap-2"
                >
                  <span class="text-body-2 text-medium-emphasis" style="min-inline-size: 90px;">{{ dim.label }}</span>
                  <VProgressLinear
                    :model-value="(item.qualityGate as any)[dim.key]"
                    :color="resolveQualityScoreColor((item.qualityGate as any)[dim.key])"
                    height="6"
                    rounded
                    style="max-inline-size: 120px;"
                  />
                  <span class="text-body-2 font-weight-medium" style="min-inline-size: 28px;">
                    {{ (item.qualityGate as any)[dim.key] }}
                  </span>
                </div>
              </div>
            </template>
          </VCardText>
        </VCard>

        <!-- History Timeline Card -->
        <VCard>
          <VCardItem>
            <template #title>
              <h5 class="text-h5">
                Review History
              </h5>
            </template>
          </VCardItem>
          <VDivider />
          <VCardText>
            <VTimeline
              truncate-line="both"
              line-inset="9"
              align="start"
              side="end"
              line-color="primary"
              density="compact"
            >
              <VTimelineItem
                v-for="(entry, index) in item.history"
                :key="index"
                :dot-color="resolveHistoryActionColor(entry.action)"
                size="x-small"
              >
                <div class="d-flex justify-space-between align-center">
                  <span class="app-timeline-title">{{ entry.details }}</span>
                </div>
                <div class="d-flex justify-space-between mt-1">
                  <span class="app-timeline-text text-body-2">{{ entry.actor }}</span>
                  <span class="app-timeline-meta text-body-2 text-disabled">
                    {{ new Date(entry.timestamp).toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) }}
                  </span>
                </div>
              </VTimelineItem>
            </VTimeline>
          </VCardText>
        </VCard>
      </VCol>
    </VRow>

    <!-- Approve Confirmation Dialog -->
    <VDialog v-model="isApproveDialogVisible" max-width="480">
      <VCard>
        <VCardTitle>Confirm Approval</VCardTitle>
        <VCardText>
          <p class="text-body-1">
            Are you sure you want to <strong>approve</strong> this question?
          </p>
          <p class="text-body-2 text-disabled">
            The question will be eligible for publishing and student delivery.
          </p>
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn variant="tonal" @click="isApproveDialogVisible = false">Cancel</VBtn>
          <VBtn color="success" :loading="actionLoading" @click="isApproveDialogVisible = false; approveItem()">
            Approve
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>

    <!-- Reject Reason Dialog -->
    <VDialog
      v-model="isRejectDialogVisible"
      max-width="500"
    >
      <VCard title="Reject Item">
        <VCardText>
          <p class="text-body-1 mb-4">
            Please provide a reason for rejecting this content.
          </p>
          <AppTextarea
            v-model="rejectReason"
            label="Rejection Reason"
            placeholder="Explain why this content is being rejected..."
            rows="4"
          />
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            variant="tonal"
            color="secondary"
            @click="isRejectDialogVisible = false"
          >
            Cancel
          </VBtn>
          <VBtn
            color="error"
            :loading="actionLoading"
            :disabled="!rejectReason.trim()"
            @click="rejectItem"
          >
            Reject
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </div>

  <!-- Not Found -->
  <div v-else>
    <VAlert
      type="error"
      variant="tonal"
    >
      Moderation item with ID #{{ route.params.id }} is not available or could not be found.
    </VAlert>
  </div>
</template>

<style lang="scss" scoped>
.question-content {
  :deep(img) {
    max-inline-size: 100%;
    block-size: auto;
  }
}
</style>
