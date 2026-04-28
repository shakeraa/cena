<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import FriendRow from '@/components/social/FriendRow.vue'
import ReportDialog from '@/components/social/ReportDialog.vue'
import BlockUserDialog from '@/components/social/BlockUserDialog.vue'
import { useApiQuery } from '@/composables/useApiQuery'
import { $api } from '@/api/$api'
import type { FriendsListDto, ReportContentType } from '@/api/types/common'

definePage({
  meta: {
    layout: 'default',
    requiresAuth: true,
    requiresOnboarded: true,
    public: false,
    title: 'nav.friends',
    hideSidebar: false,
    breadcrumbs: true,
  },
})

const { t } = useI18n()

const friendsQuery = useApiQuery<FriendsListDto>('/api/social/friends')

const acceptError = ref<string | null>(null)
const acceptErrorVisible = ref(false)

// FIND-privacy-018: Report dialog state (for friend requests)
const reportDialogOpen = ref(false)
const reportContentId = ref('')
const reportContentType = ref<ReportContentType>('friend-request')

// FIND-privacy-018: Block dialog state
const blockDialogOpen = ref(false)
const blockTargetId = ref('')
const blockTargetName = ref('')

const reportSuccessVisible = ref(false)
const blockSuccessVisible = ref(false)

function handleReport(contentId: string, contentType: ReportContentType) {
  reportContentId.value = contentId
  reportContentType.value = contentType
  reportDialogOpen.value = true
}

function handleBlock(studentId: string, displayName: string) {
  blockTargetId.value = studentId
  blockTargetName.value = displayName
  blockDialogOpen.value = true
}

function onReported() {
  reportSuccessVisible.value = true
}

function onBlocked() {
  blockSuccessVisible.value = true
  friendsQuery.refresh()
}

async function handleAccept(requestId: string) {
  try {
    await $api(`/api/social/friends/${requestId}/accept`, { method: 'POST' as any, body: {} as any })
    friendsQuery.refresh()
  }
  catch (err) {
    console.error('[FIND-ux-024] friend-accept failed', { requestId, error: err })
    acceptError.value = err instanceof Error ? err.message : t('social.friends.acceptError')
    acceptErrorVisible.value = true
  }
}
</script>

<template>
  <div
    class="friends-page pa-4"
    data-testid="friends-page"
  >
    <h1 class="text-h4 mb-1">
      {{ t('social.friends.title') }}
    </h1>
    <p class="text-body-1 text-medium-emphasis mb-6">
      {{ t('social.friends.subtitle') }}
    </p>

    <div
      v-if="friendsQuery.loading.value && !friendsQuery.data.value"
      class="d-flex justify-center py-12"
      data-testid="friends-loading"
    >
      <VProgressCircular indeterminate />
    </div>

    <VAlert
      v-else-if="friendsQuery.error.value"
      type="error"
      variant="tonal"
      data-testid="friends-error"
    >
      {{ t(friendsQuery.error.value.i18nKey ?? 'social.friendsUnavailable') }}
    </VAlert>

    <template v-else-if="friendsQuery.data.value">
      <section
        v-if="friendsQuery.data.value.pendingRequests.length > 0"
        class="mb-6"
        data-testid="pending-requests-section"
      >
        <h2 class="text-h6 mb-3">
          {{ t('social.friends.pendingTitle') }}
        </h2>
        <VCard
          v-for="req in friendsQuery.data.value.pendingRequests"
          :key="req.requestId"
          variant="outlined"
          class="pa-3 mb-2"
          :data-testid="`request-${req.requestId}`"
        >
          <div class="d-flex align-center">
            <VAvatar
              color="warning"
              size="40"
              class="me-3"
            >
              <VIcon
                icon="tabler-user-plus"
                size="20"
                color="white"
                aria-hidden="true"
              />
            </VAvatar>
            <div class="flex-grow-1 min-w-0">
              <div class="text-subtitle-1 font-weight-medium text-truncate">
                {{ req.fromDisplayName }}
              </div>
              <div class="text-caption text-medium-emphasis">
                {{ t('social.friends.wantsToConnect') }}
              </div>
            </div>
            <!-- FIND-privacy-018: Report button on friend requests -->
            <VBtn
              variant="text"
              size="small"
              icon="tabler-flag"
              color="error"
              :aria-label="t('social.report.ariaLabel')"
              :data-testid="`report-${req.requestId}`"
              class="me-1"
              @click="handleReport(req.requestId, 'friend-request')"
            />
            <VBtn
              color="primary"
              size="small"
              :data-testid="`accept-${req.requestId}`"
              @click="handleAccept(req.requestId)"
            >
              {{ t('social.friends.accept') }}
            </VBtn>
          </div>
        </VCard>
      </section>

      <section data-testid="friends-list-section">
        <h2 class="text-h6 mb-3">
          {{ t('social.friends.listTitle', { count: friendsQuery.data.value.friends.length }, { plural: friendsQuery.data.value.friends.length }) }}
        </h2>
        <FriendRow
          v-for="f in friendsQuery.data.value.friends"
          :key="f.studentId"
          :friend="f"
          @block="handleBlock"
        />
      </section>
    </template>

    <!-- FIND-privacy-018: Report & Block dialogs -->
    <ReportDialog
      v-model="reportDialogOpen"
      :content-type="reportContentType"
      :content-id="reportContentId"
      @reported="onReported"
    />
    <BlockUserDialog
      v-model="blockDialogOpen"
      :target-student-id="blockTargetId"
      :target-display-name="blockTargetName"
      @blocked="onBlocked"
    />

    <VSnackbar
      v-model="acceptErrorVisible"
      color="error"
      timeout="4000"
      data-testid="accept-error-snackbar"
    >
      {{ acceptError ?? t('social.friends.acceptError') }}
    </VSnackbar>
    <VSnackbar
      v-model="reportSuccessVisible"
      color="success"
      timeout="4000"
      data-testid="report-success-snackbar"
    >
      {{ t('social.report.success') }}
    </VSnackbar>
    <VSnackbar
      v-model="blockSuccessVisible"
      color="success"
      timeout="4000"
      data-testid="block-success-snackbar"
    >
      {{ t('social.block.success') }}
    </VSnackbar>
  </div>
</template>

<style scoped>
.friends-page {
  max-inline-size: 800px;
  margin-inline: auto;
}
</style>
