<script setup lang="ts">
definePage({
  meta: {
    action: 'read',
    subject: 'System',
  },
})

interface DeadLetterItem {
  id: string
  timestamp: string
  source: string
  eventType: string
  errorMessage: string
  retryCount: number
  payload?: Record<string, unknown>
  stackTrace?: string
}

const searchQuery = ref('')
const filterSource = ref<string | null>(null)
const filterType = ref<string | null>(null)
const itemsPerPage = ref(10)
const page = ref(1)
const sortBy = ref()
const orderBy = ref()
const selectedRows = ref<string[]>([])
const isDetailDialogVisible = ref(false)
const selectedItem = ref<DeadLetterItem | null>(null)

const updateOptions = (options: any) => {
  sortBy.value = options.sortBy[0]?.key
  orderBy.value = options.sortBy[0]?.order
}

const headers = [
  { title: 'Timestamp', key: 'timestamp' },
  { title: 'Source', key: 'source' },
  { title: 'Event Type', key: 'eventType' },
  { title: 'Error', key: 'errorMessage' },
  { title: 'Retries', key: 'retryCount' },
  { title: 'Actions', key: 'actions', sortable: false },
]

const { data: dlqData, execute: fetchDlq } = await useApi<any>(createUrl('/admin/events/dead-letters', {
  query: {
    q: searchQuery,
    source: filterSource,
    eventType: filterType,
    itemsPerPage,
    page,
    sortBy,
    orderBy,
  },
}))

const items = computed((): DeadLetterItem[] => dlqData.value?.items ?? [])
const totalItems = computed(() => dlqData.value?.total ?? 0)
const dlqDepth = computed(() => dlqData.value?.total ?? 0)

const openDetail = async (item: DeadLetterItem) => {
  try {
    const detail = await $api(`/admin/events/dead-letters/${item.id}`)
    selectedItem.value = detail as DeadLetterItem
    isDetailDialogVisible.value = true
  }
  catch (e) {
    console.error('Failed to fetch DLQ detail:', e)
  }
}

const retryItem = async (id: string) => {
  await $api(`/admin/events/dead-letters/${id}/retry`, { method: 'POST' })
  fetchDlq()
}

const discardItem = async (id: string) => {
  await $api(`/admin/events/dead-letters/${id}/discard`, { method: 'POST' })
  fetchDlq()
}

const bulkRetry = async () => {
  await $api('/admin/events/dead-letters/bulk-retry', {
    method: 'POST',
    body: { ids: selectedRows.value },
  })
  selectedRows.value = []
  fetchDlq()
}

const formatDate = (ts: string) => new Date(ts).toLocaleString('en-US', {
  month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit',
})
</script>

<template>
  <div>
    <div class="d-flex align-center justify-space-between mb-6">
      <div>
        <h4 class="text-h4">
          Dead Letter Queue
        </h4>
        <p class="text-body-1 mb-0">
          Failed messages awaiting retry or investigation
        </p>
      </div>
      <VChip
        v-if="dlqDepth > 50"
        color="error"
        variant="flat"
      >
        {{ dlqDepth }} items in DLQ
      </VChip>
    </div>

    <VAlert
      v-if="dlqDepth > 100"
      color="error"
      variant="tonal"
      class="mb-6"
    >
      DLQ depth is {{ dlqDepth }} — above the 100-item threshold. Investigate and resolve failing messages.
    </VAlert>

    <VCard>
      <VCardText class="d-flex flex-wrap gap-4">
        <AppTextField
          v-model="searchQuery"
          placeholder="Search errors..."
          style="inline-size: 15rem;"
        />
        <AppSelect
          v-model="filterSource"
          placeholder="Source"
          :items="['nats', 'marten', 'actor-system', 'ingestion']"
          clearable
          style="inline-size: 10rem;"
        />
        <AppSelect
          v-model="filterType"
          placeholder="Event Type"
          :items="['ConceptAttempted', 'ConceptMastered', 'StagnationDetected', 'MethodologySwitched']"
          clearable
          style="inline-size: 12rem;"
        />
        <VSpacer />
        <VBtn
          v-if="selectedRows.length > 0"
          color="primary"
          variant="tonal"
          prepend-icon="tabler-refresh"
          @click="bulkRetry"
        >
          Retry Selected ({{ selectedRows.length }})
        </VBtn>
      </VCardText>

      <VDivider />

      <VDataTableServer
        v-model:items-per-page="itemsPerPage"
        v-model:model-value="selectedRows"
        v-model:page="page"
        :items="items"
        item-value="id"
        :items-length="totalItems"
        :headers="headers"
        class="text-no-wrap"
        show-select
        @update:options="updateOptions"
      >
        <template #item.timestamp="{ item }">
          <span class="text-caption">{{ formatDate(item.timestamp) }}</span>
        </template>

        <template #item.source="{ item }">
          <VChip
            size="x-small"
            label
          >
            {{ item.source }}
          </VChip>
        </template>

        <template #item.eventType="{ item }">
          <span class="text-body-2">{{ item.eventType }}</span>
        </template>

        <template #item.errorMessage="{ item }">
          <span class="text-body-2 text-error">{{ item.errorMessage?.substring(0, 60) }}{{ (item.errorMessage?.length ?? 0) > 60 ? '...' : '' }}</span>
        </template>

        <template #item.retryCount="{ item }">
          <VChip
            :color="item.retryCount >= 3 ? 'error' : 'warning'"
            size="x-small"
          >
            {{ item.retryCount }}
          </VChip>
        </template>

        <template #item.actions="{ item }">
          <div class="d-flex gap-1">
            <IconBtn
              size="small"
              @click.stop="openDetail(item)"
            >
              <VIcon
                icon="tabler-eye"
                size="18"
              />
            </IconBtn>
            <IconBtn
              size="small"
              @click.stop="retryItem(item.id)"
            >
              <VIcon
                icon="tabler-refresh"
                size="18"
              />
            </IconBtn>
            <IconBtn
              size="small"
              color="error"
              @click.stop="discardItem(item.id)"
            >
              <VIcon
                icon="tabler-trash"
                size="18"
              />
            </IconBtn>
          </div>
        </template>

        <template #bottom>
          <TablePagination
            v-model:page="page"
            :items-per-page="itemsPerPage"
            :total-items="totalItems"
          />
        </template>
      </VDataTableServer>
    </VCard>

    <!-- Detail Dialog -->
    <VDialog
      v-model="isDetailDialogVisible"
      max-width="700"
    >
      <VCard v-if="selectedItem">
        <VCardItem>
          <VCardTitle>Dead Letter Detail</VCardTitle>
        </VCardItem>
        <VCardText>
          <VList density="compact">
            <VListItem>
              <strong>ID:</strong> {{ selectedItem.id }}
            </VListItem>
            <VListItem>
              <strong>Source:</strong> {{ selectedItem.source }}
            </VListItem>
            <VListItem>
              <strong>Event Type:</strong> {{ selectedItem.eventType }}
            </VListItem>
            <VListItem>
              <strong>Retry Count:</strong> {{ selectedItem.retryCount }}
            </VListItem>
            <VListItem>
              <strong>Error:</strong>
              <span class="text-error">{{ selectedItem.errorMessage }}</span>
            </VListItem>
          </VList>

          <div
            v-if="selectedItem.payload"
            class="mt-4"
          >
            <h6 class="text-h6 mb-2">
              Payload
            </h6>
            <pre class="pa-3 bg-grey-lighten-4 rounded text-caption" style="overflow-x: auto; max-height: 200px;">{{ JSON.stringify(selectedItem.payload, null, 2) }}</pre>
          </div>

          <div
            v-if="selectedItem.stackTrace"
            class="mt-4"
          >
            <h6 class="text-h6 mb-2">
              Stack Trace
            </h6>
            <pre class="pa-3 bg-error-lighten-5 rounded text-caption" style="overflow-x: auto; max-height: 200px;">{{ selectedItem.stackTrace }}</pre>
          </div>
        </VCardText>
        <VCardActions>
          <VSpacer />
          <VBtn
            color="primary"
            @click="retryItem(selectedItem.id); isDetailDialogVisible = false"
          >
            Retry
          </VBtn>
          <VBtn
            color="secondary"
            variant="tonal"
            @click="isDetailDialogVisible = false"
          >
            Close
          </VBtn>
        </VCardActions>
      </VCard>
    </VDialog>
  </div>
</template>
