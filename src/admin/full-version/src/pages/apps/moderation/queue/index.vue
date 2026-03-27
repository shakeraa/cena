<script setup lang="ts">
import ModerationStats from '@/views/apps/moderation/ModerationStats.vue'
import { $api } from '@/utils/api'

definePage({
  meta: {
    action: 'read',
    subject: 'Content',
  },
})

interface ModerationItem {
  id: string
  questionText: string
  subject: string
  grade: string
  author: string
  submittedAt: string
  status: 'pending' | 'in-review' | 'approved' | 'rejected' | 'flagged'
}

interface QueueResponse {
  items: ModerationItem[]
  total: number
}

const searchQuery = ref('')
const selectedStatus = ref<string>('all')
const itemsPerPage = ref(10)
const page = ref(1)
const sortBy = ref<string>('submittedAt')
const orderBy = ref<string>('asc')
const selectedRows = ref<string[]>([])
const loading = ref(false)
const items = ref<ModerationItem[]>([])
const totalItems = ref(0)
const statsRef = ref<InstanceType<typeof ModerationStats> | null>(null)

const statusOptions = [
  { title: 'All', value: 'all' },
  { title: 'Pending', value: 'pending' },
  { title: 'In Review', value: 'in-review' },
  { title: 'Approved', value: 'approved' },
  { title: 'Rejected', value: 'rejected' },
  { title: 'Flagged', value: 'flagged' },
]

const headers = [
  { title: 'Question', key: 'questionText' },
  { title: 'Subject', key: 'subject' },
  { title: 'Grade', key: 'grade' },
  { title: 'Author', key: 'author' },
  { title: 'Submitted', key: 'submittedAt' },
  { title: 'Status', key: 'status' },
  { title: 'Actions', key: 'actions', sortable: false },
]

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

const truncateText = (text: string, maxLength: number = 80): string => {
  if (!text)
    return ''
  if (text.length <= maxLength)
    return text

  return `${text.slice(0, maxLength)}...`
}

const fetchQueue = async () => {
  loading.value = true
  try {
    const params: Record<string, string | number> = {
      page: page.value,
      itemsPerPage: itemsPerPage.value,
      sortBy: sortBy.value,
      orderBy: orderBy.value,
    }

    if (searchQuery.value)
      params.q = searchQuery.value

    if (selectedStatus.value !== 'all')
      params.status = selectedStatus.value

    const data = await $api<QueueResponse>('/admin/moderation/queue', { query: params })

    items.value = data.items ?? []
    totalItems.value = data.total ?? 0
  }
  catch (error) {
    console.error('Failed to fetch moderation queue:', error)
    items.value = []
    totalItems.value = 0
  }
  finally {
    loading.value = false
  }
}

const updateOptions = (options: any) => {
  if (options.sortBy?.length) {
    sortBy.value = options.sortBy[0].key
    orderBy.value = options.sortBy[0].order
  }
  else {
    sortBy.value = 'submittedAt'
    orderBy.value = 'asc'
  }

  fetchQueue()
}

const claimItem = async (id: string) => {
  try {
    await $api(`/admin/moderation/items/${id}/claim`, { method: 'POST' })
    await fetchQueue()
    statsRef.value?.refresh()
  }
  catch (error) {
    console.error('Failed to claim item:', error)
  }
}

const bulkApprove = async () => {
  if (!selectedRows.value.length)
    return

  try {
    await $api('/admin/moderation/bulk', {
      method: 'POST',
      body: { action: 'approve', itemIds: selectedRows.value },
    })

    selectedRows.value = []
    await fetchQueue()
    statsRef.value?.refresh()
  }
  catch (error) {
    console.error('Failed to bulk approve:', error)
  }
}

const bulkReject = async () => {
  if (!selectedRows.value.length)
    return

  try {
    await $api('/admin/moderation/bulk', {
      method: 'POST',
      body: { action: 'reject', itemIds: selectedRows.value },
    })

    selectedRows.value = []
    await fetchQueue()
    statsRef.value?.refresh()
  }
  catch (error) {
    console.error('Failed to bulk reject:', error)
  }
}

watch([searchQuery, selectedStatus], () => {
  page.value = 1
  fetchQueue()
})

watch([page, itemsPerPage], () => {
  fetchQueue()
})

onMounted(fetchQueue)
</script>

<template>
  <div>
    <!-- Stats Cards -->
    <ModerationStats
      ref="statsRef"
      class="mb-6"
    />

    <VCard>
      <!-- Filters -->
      <VCardText>
        <div class="d-flex justify-space-between flex-wrap gap-4">
          <div class="d-flex gap-4 align-center flex-wrap">
            <AppTextField
              v-model="searchQuery"
              placeholder="Search questions"
              style="max-inline-size: 250px; min-inline-size: 200px;"
            />
            <AppSelect
              v-model="selectedStatus"
              :items="statusOptions"
              style="max-inline-size: 10rem; min-inline-size: 10rem;"
            />
          </div>

          <div class="d-flex gap-x-4 align-center flex-wrap">
            <VBtn
              v-if="selectedRows.length"
              color="success"
              variant="tonal"
              prepend-icon="tabler-checks"
              @click="bulkApprove"
            >
              Approve ({{ selectedRows.length }})
            </VBtn>
            <VBtn
              v-if="selectedRows.length"
              color="error"
              variant="tonal"
              prepend-icon="tabler-x"
              @click="bulkReject"
            >
              Reject ({{ selectedRows.length }})
            </VBtn>
            <AppSelect
              v-model="itemsPerPage"
              :items="[5, 10, 20, 50]"
              style="min-inline-size: 6.25rem;"
            />
          </div>
        </div>
      </VCardText>

      <VDivider />

      <!-- Queue Table -->
      <VDataTableServer
        v-model:items-per-page="itemsPerPage"
        v-model:model-value="selectedRows"
        v-model:page="page"
        :headers="headers"
        :items="items"
        :items-length="totalItems"
        :loading="loading"
        item-value="id"
        show-select
        class="text-no-wrap"
        @update:options="updateOptions"
      >
        <!-- Question Text -->
        <template #item.questionText="{ item }">
          <RouterLink
            :to="{ name: 'apps-moderation-review-id', params: { id: item.id } }"
            class="text-link font-weight-medium"
          >
            {{ truncateText(item.questionText) }}
          </RouterLink>
        </template>

        <!-- Subject -->
        <template #item.subject="{ item }">
          <div class="text-body-1">
            {{ item.subject }}
          </div>
        </template>

        <!-- Grade -->
        <template #item.grade="{ item }">
          <div class="text-body-1">
            {{ item.grade }}
          </div>
        </template>

        <!-- Author -->
        <template #item.author="{ item }">
          <div class="text-body-1">
            {{ item.author }}
          </div>
        </template>

        <!-- Submitted Date -->
        <template #item.submittedAt="{ item }">
          {{ new Date(item.submittedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) }}
        </template>

        <!-- Status -->
        <template #item.status="{ item }">
          <VChip
            :color="resolveStatusColor(item.status)"
            label
            size="small"
          >
            {{ resolveStatusLabel(item.status) }}
          </VChip>
        </template>

        <!-- Actions -->
        <template #item.actions="{ item }">
          <div class="d-flex gap-x-1">
            <VBtn
              v-if="item.status === 'pending'"
              size="small"
              variant="tonal"
              color="info"
              @click="claimItem(item.id)"
            >
              Claim
            </VBtn>
            <IconBtn :to="{ name: 'apps-moderation-review-id', params: { id: item.id } }">
              <VIcon icon="tabler-eye" />
            </IconBtn>
          </div>
        </template>

        <!-- Pagination -->
        <template #bottom>
          <TablePagination
            v-model:page="page"
            :items-per-page="itemsPerPage"
            :total-items="totalItems"
          />
        </template>
      </VDataTableServer>
    </VCard>
  </div>
</template>
