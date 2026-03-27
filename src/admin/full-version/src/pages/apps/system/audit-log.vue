<script setup lang="ts">
import { $api } from '@/utils/api'

definePage({ meta: { action: 'read', subject: 'AuditLog' } })

interface AuditEntry {
  id: string
  timestamp: string
  userName: string
  userAvatar: string
  action: string
  target: string
  details: string
}

interface AuditResponse {
  entries: AuditEntry[]
  total: number
}

const loading = ref(false)
const entries = ref<AuditEntry[]>([])
const totalEntries = ref(0)
const page = ref(1)
const itemsPerPage = ref(20)
const sortBy = ref('timestamp')
const orderBy = ref('desc')

// Filters
const userSearch = ref('')
const selectedAction = ref<string>('all')
const dateRange = ref<[string, string] | null>(null)
const dateRangeMenu = ref(false)

const actionOptions = [
  { title: 'All Actions', value: 'all' },
  { title: 'Create', value: 'create' },
  { title: 'Update', value: 'update' },
  { title: 'Delete', value: 'delete' },
  { title: 'Login', value: 'login' },
  { title: 'Logout', value: 'logout' },
  { title: 'Export', value: 'export' },
  { title: 'Import', value: 'import' },
  { title: 'Approve', value: 'approve' },
  { title: 'Reject', value: 'reject' },
]

const headers = [
  { title: 'Timestamp', key: 'timestamp', width: '180px' },
  { title: 'User', key: 'userName', width: '200px' },
  { title: 'Action', key: 'action', width: '120px' },
  { title: 'Target', key: 'target', width: '200px' },
  { title: 'Details', key: 'details' },
]

const actionColor = (action: string): string => {
  const map: Record<string, string> = {
    create: 'success',
    update: 'info',
    delete: 'error',
    login: 'primary',
    logout: 'secondary',
    export: 'warning',
    import: 'warning',
    approve: 'success',
    reject: 'error',
  }

  return map[action] ?? 'secondary'
}

const formatTimestamp = (ts: string): string => {
  if (!ts)
    return ''

  return new Date(ts).toLocaleString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

const fetchAuditLog = async () => {
  loading.value = true
  try {
    const params: Record<string, string | number> = {
      page: page.value,
      itemsPerPage: itemsPerPage.value,
      sortBy: sortBy.value,
      orderBy: orderBy.value,
    }

    if (userSearch.value)
      params.user = userSearch.value

    if (selectedAction.value !== 'all')
      params.action = selectedAction.value

    if (dateRange.value && dateRange.value.length === 2) {
      params.startDate = dateRange.value[0]
      params.endDate = dateRange.value[1]
    }

    const data = await $api<AuditResponse>('/admin/audit-log', { query: params })

    entries.value = (data.entries ?? []).map(e => ({
      id: e.id ?? '',
      timestamp: e.timestamp ?? '',
      userName: e.userName ?? '',
      userAvatar: e.userAvatar ?? '',
      action: e.action ?? '',
      target: e.target ?? '',
      details: e.details ?? '',
    }))

    totalEntries.value = data.total ?? 0
  }
  catch (err: any) {
    console.error('Failed to fetch audit log:', err)
    entries.value = []
    totalEntries.value = 0
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
    sortBy.value = 'timestamp'
    orderBy.value = 'desc'
  }

  fetchAuditLog()
}

const exportCsv = async () => {
  try {
    const params: Record<string, string> = { format: 'csv' }

    if (userSearch.value)
      params.user = userSearch.value
    if (selectedAction.value !== 'all')
      params.action = selectedAction.value
    if (dateRange.value && dateRange.value.length === 2) {
      params.startDate = dateRange.value[0]
      params.endDate = dateRange.value[1]
    }

    const csvData = await $api<string>('/admin/audit-log', {
      query: params,
      headers: { Accept: 'text/csv' },
    })

    const blob = new Blob([csvData], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')

    link.href = url
    link.download = `audit-log-${new Date().toISOString().slice(0, 10)}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }
  catch (err: any) {
    console.error('Failed to export audit log:', err)
  }
}

const clearDateRange = () => {
  dateRange.value = null
  page.value = 1
  fetchAuditLog()
}

watch([userSearch, selectedAction], () => {
  page.value = 1
  fetchAuditLog()
})

watch([page, itemsPerPage], () => {
  fetchAuditLog()
})

onMounted(fetchAuditLog)
</script>

<template>
  <div>
    <div class="d-flex justify-space-between align-center flex-wrap gap-y-4 mb-6">
      <div>
        <h4 class="text-h4 mb-1">
          Audit Log
        </h4>
        <div class="text-body-1">
          Track all administrative actions and system changes
        </div>
      </div>

      <VBtn
        color="secondary"
        variant="tonal"
        prepend-icon="tabler-download"
        @click="exportCsv"
      >
        Export CSV
      </VBtn>
    </div>

    <VCard>
      <!-- Filters -->
      <VCardText>
        <div class="d-flex justify-space-between flex-wrap gap-4">
          <div class="d-flex gap-4 align-center flex-wrap">
            <AppTextField
              v-model="userSearch"
              placeholder="Search by user"
              prepend-inner-icon="tabler-search"
              style="max-inline-size: 220px; min-inline-size: 180px;"
            />
            <AppSelect
              v-model="selectedAction"
              :items="actionOptions"
              style="max-inline-size: 10rem; min-inline-size: 10rem;"
            />

            <!-- Date Range Picker -->
            <VMenu
              v-model="dateRangeMenu"
              :close-on-content-click="false"
            >
              <template #activator="{ props: menuProps }">
                <AppTextField
                  v-bind="menuProps"
                  :model-value="dateRange ? `${dateRange[0]} - ${dateRange[1]}` : ''"
                  placeholder="Date range"
                  prepend-inner-icon="tabler-calendar"
                  readonly
                  style="max-inline-size: 260px; min-inline-size: 220px;"
                >
                  <template
                    v-if="dateRange"
                    #append-inner
                  >
                    <VIcon
                      icon="tabler-x"
                      size="18"
                      class="cursor-pointer"
                      @click.stop="clearDateRange"
                    />
                  </template>
                </AppTextField>
              </template>

              <VDatePicker
                v-model="dateRange"
                multiple="range"
                @update:model-value="() => { dateRangeMenu = false; page = 1; fetchAuditLog() }"
              />
            </VMenu>
          </div>

          <div class="d-flex gap-x-4 align-center flex-wrap">
            <AppSelect
              v-model="itemsPerPage"
              :items="[10, 20, 50, 100]"
              style="min-inline-size: 6.25rem;"
            />
          </div>
        </div>
      </VCardText>

      <VDivider />

      <!-- Audit Log Table -->
      <VDataTableServer
        v-model:items-per-page="itemsPerPage"
        v-model:page="page"
        :headers="headers"
        :items="entries"
        :items-length="totalEntries"
        :loading="loading"
        item-value="id"
        class="text-no-wrap"
        @update:options="updateOptions"
      >
        <!-- Timestamp -->
        <template #item.timestamp="{ item }">
          <span class="text-body-2">
            {{ formatTimestamp(item.timestamp) }}
          </span>
        </template>

        <!-- User -->
        <template #item.userName="{ item }">
          <div class="d-flex align-center gap-x-3">
            <VAvatar
              size="30"
              :color="item.userAvatar ? undefined : 'primary'"
              variant="tonal"
            >
              <VImg
                v-if="item.userAvatar"
                :src="item.userAvatar"
              />
              <span
                v-else
                class="text-caption"
              >{{ item.userName.charAt(0).toUpperCase() }}</span>
            </VAvatar>
            <span class="text-body-1 font-weight-medium">{{ item.userName }}</span>
          </div>
        </template>

        <!-- Action -->
        <template #item.action="{ item }">
          <VChip
            :color="actionColor(item.action)"
            label
            size="small"
          >
            {{ item.action }}
          </VChip>
        </template>

        <!-- Target -->
        <template #item.target="{ item }">
          <span class="text-body-1">{{ item.target }}</span>
        </template>

        <!-- Details -->
        <template #item.details="{ item }">
          <span class="text-body-2 text-medium-emphasis">{{ item.details }}</span>
        </template>

        <!-- Pagination -->
        <template #bottom>
          <TablePagination
            v-model:page="page"
            :items-per-page="itemsPerPage"
            :total-items="totalEntries"
          />
        </template>
      </VDataTableServer>
    </VCard>
  </div>
</template>
