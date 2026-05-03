<script setup lang="ts">
import type { CenaRole } from '@/plugins/casl/ability'

interface RoleUser {
  id: string
  fullName: string
  email: string
  role: CenaRole
  status: 'active' | 'inactive' | 'suspended'
  school: string
  lastLogin: string
  avatar?: string
}

interface Props {
  selectedRole?: CenaRole
}

const props = defineProps<Props>()

const searchQuery = ref('')
const itemsPerPage = ref(10)
const page = ref(1)
const sortBy = ref<string>()
const orderBy = ref<string>()

const updateOptions = (options: any) => {
  sortBy.value = options.sortBy[0]?.key
  orderBy.value = options.sortBy[0]?.order
}

const headers = [
  { title: 'User', key: 'user' },
  { title: 'Role', key: 'role' },
  { title: 'Status', key: 'status' },
  { title: 'School', key: 'school' },
  { title: 'Last Login', key: 'lastLogin' },
  { title: 'Actions', key: 'actions', sortable: false },
]

const roleFilterOptions = [
  { title: 'Super Admin', value: 'SUPER_ADMIN' },
  { title: 'Admin', value: 'ADMIN' },
  { title: 'Moderator', value: 'MODERATOR' },
  { title: 'Teacher', value: 'TEACHER' },
  { title: 'Student', value: 'STUDENT' },
  { title: 'Parent', value: 'PARENT' },
]

const roleFilter = ref<CenaRole | undefined>(props.selectedRole)

// Sync prop changes to local filter
watch(() => props.selectedRole, val => {
  roleFilter.value = val
  page.value = 1
})

const { data: usersData, execute: fetchUsers } = await useApi<any>(createUrl('/admin/users', {
  query: {
    q: searchQuery,
    role: roleFilter,
    itemsPerPage,
    page,
    sortBy,
    orderBy,
  },
}))

const users = computed((): RoleUser[] => usersData.value?.users ?? [])
const totalUsers = computed(() => usersData.value?.totalUsers ?? 0)

const roleColorMap: Record<CenaRole, string> = {
  SUPER_ADMIN: 'error',
  ADMIN: 'error',
  MODERATOR: 'warning',
  TEACHER: 'info',
  STUDENT: 'success',
  PARENT: 'secondary',
}

const roleIconMap: Record<CenaRole, string> = {
  SUPER_ADMIN: 'tabler-shield-lock',
  ADMIN: 'tabler-shield-check',
  MODERATOR: 'tabler-eye-check',
  TEACHER: 'tabler-school',
  STUDENT: 'tabler-user',
  PARENT: 'tabler-users-group',
}

const roleLabelMap: Record<CenaRole, string> = {
  SUPER_ADMIN: 'Super Admin',
  ADMIN: 'Admin',
  MODERATOR: 'Moderator',
  TEACHER: 'Teacher',
  STUDENT: 'Student',
  PARENT: 'Parent',
}

const resolveStatusVariant = (status: string): string => {
  if (status === 'active')
    return 'success'
  if (status === 'suspended')
    return 'error'
  if (status === 'inactive')
    return 'secondary'
  return 'primary'
}

const formatDate = (dateStr: string): string => {
  if (!dateStr)
    return 'Never'
  const date = new Date(dateStr)
  return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' })
}

const deleteUser = async (userId: string) => {
  await $api(`/admin/users/${userId}`, { method: 'DELETE' })
  fetchUsers()
}
</script>

<template>
  <section>
    <VCard>
      <VCardText class="d-flex flex-wrap gap-4">
        <div class="d-flex gap-2 align-center">
          <p class="text-body-1 mb-0">
            Show
          </p>
          <AppSelect
            :model-value="itemsPerPage"
            :items="[
              { value: 10, title: '10' },
              { value: 25, title: '25' },
              { value: 50, title: '50' },
              { value: 100, title: '100' },
              { value: -1, title: 'All' },
            ]"
            style="inline-size: 5.5rem;"
            @update:model-value="itemsPerPage = parseInt($event, 10)"
          />
        </div>

        <VSpacer />

        <div class="d-flex align-center flex-wrap gap-4">
          <AppTextField
            v-model="searchQuery"
            placeholder="Search User"
            style="inline-size: 15.625rem;"
          />

          <AppSelect
            v-model="roleFilter"
            placeholder="Select Role"
            :items="roleFilterOptions"
            clearable
            clear-icon="tabler-x"
            style="inline-size: 10rem;"
          />
        </div>
      </VCardText>

      <VDivider />

      <VDataTableServer
        v-model:items-per-page="itemsPerPage"
        v-model:page="page"
        :items-per-page-options="[
          { value: 10, title: '10' },
          { value: 25, title: '25' },
          { value: 50, title: '50' },
          { value: -1, title: '$vuetify.dataFooter.itemsPerPageAll' },
        ]"
        :items="users"
        :items-length="totalUsers"
        :headers="headers"
        class="text-no-wrap"
        @update:options="updateOptions"
      >
        <!-- User -->
        <template #item.user="{ item }">
          <div class="d-flex align-center gap-x-4">
            <VAvatar
              size="34"
              :variant="!item.avatar ? 'tonal' : undefined"
              :color="!item.avatar ? (roleColorMap[item.role] || 'primary') : undefined"
            >
              <VImg
                v-if="item.avatar"
                :src="item.avatar"
              />
              <span v-else>{{ avatarText(item.fullName) }}</span>
            </VAvatar>
            <div class="d-flex flex-column">
              <h6 class="text-base font-weight-medium">
                {{ item.fullName }}
              </h6>
              <div class="text-sm text-medium-emphasis">
                {{ item.email }}
              </div>
            </div>
          </div>
        </template>

        <!-- Role -->
        <template #item.role="{ item }">
          <div class="d-flex align-center gap-x-2">
            <VIcon
              :size="22"
              :icon="roleIconMap[item.role] || 'tabler-user'"
              :color="roleColorMap[item.role] || 'primary'"
            />
            <div class="text-body-1 text-high-emphasis">
              {{ roleLabelMap[item.role] || item.role }}
            </div>
          </div>
        </template>

        <!-- Status -->
        <template #item.status="{ item }">
          <VChip
            :color="resolveStatusVariant(item.status)"
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
            {{ item.school || '—' }}
          </div>
        </template>

        <!-- Last Login -->
        <template #item.lastLogin="{ item }">
          <div class="text-body-1 text-high-emphasis">
            {{ formatDate(item.lastLogin) }}
          </div>
        </template>

        <!-- Actions -->
        <template #item.actions="{ item }">
          <IconBtn>
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
                <VListItem link>
                  <template #prepend>
                    <VIcon icon="tabler-pencil" />
                  </template>
                  <VListItemTitle>Edit</VListItemTitle>
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

        <template #bottom>
          <TablePagination
            v-model:page="page"
            :items-per-page="itemsPerPage"
            :total-items="totalUsers"
          />
        </template>
      </VDataTableServer>
    </VCard>
  </section>
</template>

<style lang="scss">
.text-capitalize {
  text-transform: capitalize;
}
</style>
