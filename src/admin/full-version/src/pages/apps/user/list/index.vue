<script setup lang="ts">
import AddNewUserDrawer from '@/views/apps/user/list/AddNewUserDrawer.vue'
import type { CenaUserProperties, CenaUserRole, CenaUserStatus } from '@db/apps/users/types'

// Store
const searchQuery = ref('')
const selectedRole = ref<CenaUserRole>()
const selectedStatus = ref<CenaUserStatus>()
const selectedSchool = ref('')
const selectedGrade = ref('')

// Data table options
const itemsPerPage = ref(10)
const page = ref(1)
const sortBy = ref()
const orderBy = ref()
const selectedRows = ref<string[]>([])

const updateOptions = (options: any) => {
  sortBy.value = options.sortBy[0]?.key
  orderBy.value = options.sortBy[0]?.order
}

// Headers
const headers = [
  { title: 'User', key: 'user' },
  { title: 'Role', key: 'role' },
  { title: 'Status', key: 'status' },
  { title: 'School', key: 'school' },
  { title: 'Grade', key: 'grade' },
  { title: 'Created', key: 'createdAt' },
  { title: 'Actions', key: 'actions', sortable: false },
]

// Fetching users
const { data: usersData, execute: fetchUsers } = await useApi<any>(createUrl('/admin/users', {
  query: {
    q: searchQuery,
    status: selectedStatus,
    role: selectedRole,
    school: selectedSchool,
    grade: selectedGrade,
    itemsPerPage,
    page,
    sortBy,
    orderBy,
  },
}))

const users = computed((): CenaUserProperties[] => usersData.value?.users ?? [])
const totalUsers = computed(() => usersData.value?.totalUsers ?? 0)

// Filter options
const roles: { title: string; value: CenaUserRole }[] = [
  { title: 'Student', value: 'STUDENT' },
  { title: 'Teacher', value: 'TEACHER' },
  { title: 'Parent', value: 'PARENT' },
  { title: 'Moderator', value: 'MODERATOR' },
  { title: 'Admin', value: 'ADMIN' },
  { title: 'Super Admin', value: 'SUPER_ADMIN' },
]

const statuses: { title: string; value: CenaUserStatus }[] = [
  { title: 'Active', value: 'active' },
  { title: 'Suspended', value: 'suspended' },
  { title: 'Pending', value: 'pending' },
]

const grades = Array.from({ length: 12 }, (_, i) => ({
  title: `Grade ${i + 1}`,
  value: String(i + 1),
}))

// Role badge resolver
const resolveUserRoleVariant = (role: CenaUserRole) => {
  const map: Record<CenaUserRole, { color: string; icon: string }> = {
    SUPER_ADMIN: { color: 'error', icon: 'tabler-crown' },
    ADMIN: { color: 'error', icon: 'tabler-shield' },
    MODERATOR: { color: 'warning', icon: 'tabler-eye-check' },
    TEACHER: { color: 'info', icon: 'tabler-chalkboard' },
    STUDENT: { color: 'success', icon: 'tabler-school' },
    PARENT: { color: 'secondary', icon: 'tabler-users' },
  }

  return map[role] ?? { color: 'primary', icon: 'tabler-user' }
}

const resolveUserStatusVariant = (status: CenaUserStatus) => {
  const map: Record<CenaUserStatus, string> = {
    active: 'success',
    suspended: 'error',
    pending: 'warning',
  }

  return map[status] ?? 'primary'
}

const formatRoleLabel = (role: CenaUserRole) => {
  return role.replace('_', ' ')
}

const formatDate = (dateStr: string) => {
  if (!dateStr) return '--'

  return new Date(dateStr).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

const isAddNewUserDrawerVisible = ref(false)

// Add new user
const addNewUser = async (userData: Partial<CenaUserProperties>) => {
  await $api('/admin/users', {
    method: 'POST',
    body: userData,
  })

  fetchUsers()
}

// Invite user
const inviteUser = async (userData: Partial<CenaUserProperties>) => {
  await $api('/admin/users/invite', {
    method: 'POST',
    body: userData,
  })

  fetchUsers()
}

// Delete user
const deleteUser = async (id: string) => {
  await $api(`/admin/users/${id}`, {
    method: 'DELETE',
  })

  const index = selectedRows.value.findIndex(row => row === id)
  if (index !== -1)
    selectedRows.value.splice(index, 1)

  fetchUsers()
}

// Suspend user
const suspendUser = async (id: string) => {
  await $api(`/admin/users/${id}/suspend`, {
    method: 'POST',
  })

  fetchUsers()
}

// Activate user
const activateUser = async (id: string) => {
  await $api(`/admin/users/${id}/activate`, {
    method: 'POST',
  })

  fetchUsers()
}

// Bulk actions
const bulkActivate = async () => {
  await Promise.all(selectedRows.value.map(id => $api(`/admin/users/${id}/activate`, { method: 'POST' })))
  selectedRows.value = []
  fetchUsers()
}

const bulkSuspend = async () => {
  await Promise.all(selectedRows.value.map(id => $api(`/admin/users/${id}/suspend`, { method: 'POST' })))
  selectedRows.value = []
  fetchUsers()
}

const bulkDelete = async () => {
  await Promise.all(selectedRows.value.map(id => $api(`/admin/users/${id}`, { method: 'DELETE' })))
  selectedRows.value = []
  fetchUsers()
}

const exportCsv = () => {
  const csvHeaders = ['ID', 'Name', 'Email', 'Role', 'Status', 'School', 'Grade', 'Created']
  const csvRows = users.value.map(u => [
    u.id,
    u.fullName,
    u.email,
    u.role,
    u.status,
    u.school,
    u.grade,
    u.createdAt,
  ].join(','))

  const csvContent = [csvHeaders.join(','), ...csvRows].join('\n')
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')

  link.setAttribute('href', url)
  link.setAttribute('download', `cena-users-${new Date().toISOString().slice(0, 10)}.csv`)
  link.click()
  URL.revokeObjectURL(url)
}

// Widget data
const widgetData = ref([
  { title: 'Total Users', value: '0', change: 0, desc: 'All registered users', icon: 'tabler-users', iconColor: 'primary' },
  { title: 'New This Week', value: '0', change: 0, desc: 'Registered in last 7 days', icon: 'tabler-user-plus', iconColor: 'info' },
  { title: 'Active Today', value: '0', change: 0, desc: 'Logged in today', icon: 'tabler-user-check', iconColor: 'success' },
  { title: 'Pending Review', value: '0', change: 0, desc: 'Awaiting activation', icon: 'tabler-user-search', iconColor: 'warning' },
])

// Fetch widget stats
const fetchWidgetStats = async () => {
  try {
    const stats = await $api('/admin/users/stats')

    widgetData.value[0].value = String(stats.totalUsers ?? 0)
    widgetData.value[0].change = stats.totalUsersChange ?? 0
    widgetData.value[1].value = String(stats.newThisWeek ?? 0)
    widgetData.value[1].change = stats.newThisWeekChange ?? 0
    widgetData.value[2].value = String(stats.activeToday ?? 0)
    widgetData.value[2].change = stats.activeTodayChange ?? 0
    widgetData.value[3].value = String(stats.pendingReview ?? 0)
    widgetData.value[3].change = stats.pendingReviewChange ?? 0
  }
  catch (e) {
    console.error('Failed to fetch user stats', e)
  }
}

fetchWidgetStats()
</script>

<template>
  <section>
    <!-- Widgets -->
    <div class="d-flex mb-6">
      <VRow>
        <template
          v-for="(data, id) in widgetData"
          :key="id"
        >
          <VCol
            cols="12"
            md="3"
            sm="6"
          >
            <VCard>
              <VCardText>
                <div class="d-flex justify-space-between">
                  <div class="d-flex flex-column gap-y-1">
                    <div class="text-body-1 text-high-emphasis">
                      {{ data.title }}
                    </div>
                    <div class="d-flex gap-x-2 align-center">
                      <h4 class="text-h4">
                        {{ data.value }}
                      </h4>
                      <div
                        v-if="data.change !== 0"
                        class="text-base"
                        :class="data.change > 0 ? 'text-success' : 'text-error'"
                      >
                        ({{ prefixWithPlus(data.change) }}%)
                      </div>
                    </div>
                    <div class="text-sm">
                      {{ data.desc }}
                    </div>
                  </div>
                  <VAvatar
                    :color="data.iconColor"
                    variant="tonal"
                    rounded
                    size="42"
                  >
                    <VIcon
                      :icon="data.icon"
                      size="26"
                    />
                  </VAvatar>
                </div>
              </VCardText>
            </VCard>
          </VCol>
        </template>
      </VRow>
    </div>

    <VCard class="mb-6">
      <VCardItem class="pb-4">
        <VCardTitle>Filters</VCardTitle>
      </VCardItem>

      <VCardText>
        <VRow>
          <!-- Role -->
          <VCol
            cols="12"
            sm="3"
          >
            <AppSelect
              v-model="selectedRole"
              placeholder="Select Role"
              :items="roles"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>
          <!-- Status -->
          <VCol
            cols="12"
            sm="3"
          >
            <AppSelect
              v-model="selectedStatus"
              placeholder="Select Status"
              :items="statuses"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>
          <!-- School -->
          <VCol
            cols="12"
            sm="3"
          >
            <AppTextField
              v-model="selectedSchool"
              placeholder="Filter by School"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>
          <!-- Grade -->
          <VCol
            cols="12"
            sm="3"
          >
            <AppSelect
              v-model="selectedGrade"
              placeholder="Select Grade"
              :items="grades"
              clearable
              clear-icon="tabler-x"
            />
          </VCol>
        </VRow>
      </VCardText>

      <VDivider />

      <VCardText class="d-flex flex-wrap gap-4">
        <div class="me-3 d-flex gap-3">
          <AppSelect
            :model-value="itemsPerPage"
            :items="[
              { value: 10, title: '10' },
              { value: 25, title: '25' },
              { value: 50, title: '50' },
              { value: 100, title: '100' },
              { value: -1, title: 'All' },
            ]"
            style="inline-size: 6.25rem;"
            @update:model-value="itemsPerPage = parseInt($event, 10)"
          />
        </div>
        <VSpacer />

        <div class="app-user-search-filter d-flex align-center flex-wrap gap-4">
          <!-- Search -->
          <div style="inline-size: 15.625rem;">
            <AppTextField
              v-model="searchQuery"
              placeholder="Search by name or email"
            />
          </div>

          <!-- Bulk actions -->
          <VBtn
            v-if="selectedRows.length > 0"
            variant="tonal"
            color="primary"
          >
            Bulk Actions ({{ selectedRows.length }})
            <VMenu activator="parent">
              <VList>
                <VListItem @click="bulkActivate">
                  <template #prepend>
                    <VIcon icon="tabler-check" />
                  </template>
                  <VListItemTitle>Activate</VListItemTitle>
                </VListItem>
                <VListItem @click="bulkSuspend">
                  <template #prepend>
                    <VIcon icon="tabler-ban" />
                  </template>
                  <VListItemTitle>Suspend</VListItemTitle>
                </VListItem>
                <VListItem @click="bulkDelete">
                  <template #prepend>
                    <VIcon icon="tabler-trash" />
                  </template>
                  <VListItemTitle>Delete</VListItemTitle>
                </VListItem>
                <VListItem @click="exportCsv">
                  <template #prepend>
                    <VIcon icon="tabler-download" />
                  </template>
                  <VListItemTitle>Export CSV</VListItemTitle>
                </VListItem>
              </VList>
            </VMenu>
          </VBtn>

          <!-- Export button -->
          <VBtn
            variant="tonal"
            color="secondary"
            prepend-icon="tabler-upload"
            @click="exportCsv"
          >
            Export
          </VBtn>

          <!-- Add user button -->
          <VBtn
            prepend-icon="tabler-plus"
            @click="isAddNewUserDrawerVisible = true"
          >
            Add New User
          </VBtn>
        </div>
      </VCardText>

      <VDivider />

      <!-- Datatable -->
      <VDataTableServer
        v-model:items-per-page="itemsPerPage"
        v-model:model-value="selectedRows"
        v-model:page="page"
        :items="users"
        item-value="id"
        :items-length="totalUsers"
        :headers="headers"
        class="text-no-wrap"
        show-select
        @update:options="updateOptions"
      >
        <!-- User -->
        <template #item.user="{ item }">
          <div class="d-flex align-center gap-x-4">
            <VAvatar
              size="34"
              :variant="!item.avatar ? 'tonal' : undefined"
              :color="!item.avatar ? resolveUserRoleVariant(item.role).color : undefined"
            >
              <VImg
                v-if="item.avatar"
                :src="item.avatar"
              />
              <span v-else>{{ avatarText(item.fullName) }}</span>
            </VAvatar>
            <div class="d-flex flex-column">
              <h6 class="text-base">
                <RouterLink
                  :to="{ name: 'apps-user-view-id', params: { id: item.id } }"
                  class="font-weight-medium text-link"
                >
                  {{ item.fullName }}
                </RouterLink>
              </h6>
              <div class="text-sm">
                {{ item.email }}
              </div>
            </div>
          </div>
        </template>

        <!-- Role -->
        <template #item.role="{ item }">
          <VChip
            label
            size="small"
            :color="resolveUserRoleVariant(item.role).color"
          >
            <VIcon
              start
              :size="16"
              :icon="resolveUserRoleVariant(item.role).icon"
            />
            {{ formatRoleLabel(item.role) }}
          </VChip>
        </template>

        <!-- Status -->
        <template #item.status="{ item }">
          <VChip
            :color="resolveUserStatusVariant(item.status)"
            size="small"
            label
            class="text-capitalize"
          >
            {{ item.status }}
          </VChip>
        </template>

        <!-- School -->
        <template #item.school="{ item }">
          <div class="text-body-1 text-high-emphasis">
            {{ item.school || '--' }}
          </div>
        </template>

        <!-- Grade -->
        <template #item.grade="{ item }">
          <div class="text-body-1 text-high-emphasis">
            {{ item.grade ? `Grade ${item.grade}` : '--' }}
          </div>
        </template>

        <!-- Created -->
        <template #item.createdAt="{ item }">
          <div class="text-body-1 text-high-emphasis">
            {{ formatDate(item.createdAt) }}
          </div>
        </template>

        <!-- Actions -->
        <template #item.actions="{ item }">
          <IconBtn :to="{ name: 'apps-user-view-id', params: { id: item.id } }">
            <VIcon icon="tabler-eye" />
          </IconBtn>

          <VBtn
            icon
            variant="text"
            color="medium-emphasis"
          >
            <VIcon icon="tabler-dots-vertical" />
            <VMenu activator="parent">
              <VList>
                <VListItem :to="{ name: 'apps-user-view-id', params: { id: item.id } }">
                  <template #prepend>
                    <VIcon icon="tabler-eye" />
                  </template>
                  <VListItemTitle>View</VListItemTitle>
                </VListItem>

                <VListItem :to="{ name: 'apps-user-view-id', params: { id: item.id } }">
                  <template #prepend>
                    <VIcon icon="tabler-pencil" />
                  </template>
                  <VListItemTitle>Edit</VListItemTitle>
                </VListItem>

                <VListItem
                  v-if="item.status === 'active'"
                  @click="suspendUser(item.id)"
                >
                  <template #prepend>
                    <VIcon icon="tabler-ban" />
                  </template>
                  <VListItemTitle>Suspend</VListItemTitle>
                </VListItem>

                <VListItem
                  v-if="item.status === 'suspended'"
                  @click="activateUser(item.id)"
                >
                  <template #prepend>
                    <VIcon icon="tabler-check" />
                  </template>
                  <VListItemTitle>Activate</VListItemTitle>
                </VListItem>

                <VListItem @click="deleteUser(item.id)">
                  <template #prepend>
                    <VIcon icon="tabler-trash" />
                  </template>
                  <VListItemTitle>Delete</VListItemTitle>
                </VListItem>
              </VList>
            </VMenu>
          </VBtn>
        </template>

        <!-- Pagination -->
        <template #bottom>
          <TablePagination
            v-model:page="page"
            :items-per-page="itemsPerPage"
            :total-items="totalUsers"
          />
        </template>
      </VDataTableServer>
    </VCard>

    <!-- Add New User -->
    <AddNewUserDrawer
      v-model:is-drawer-open="isAddNewUserDrawerVisible"
      @user-data="addNewUser"
      @invite-data="inviteUser"
    />
  </section>
</template>
