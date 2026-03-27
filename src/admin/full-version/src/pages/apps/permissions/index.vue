<script setup lang="ts">
import type { CenaRole } from '@/plugins/casl/ability'

definePage({
  meta: {
    action: 'read',
    subject: 'Users',
  },
})

interface PermissionCategory {
  name: string
  actions: string[]
}

interface RolePermissions {
  roleId: string
  roleName: string
  role: CenaRole
  permissions: Record<string, string[]>
}

const search = ref('')
const isLoading = ref(true)
const isSaving = ref(false)
const fetchError = ref<string | null>(null)
const expandedCategories = ref<string[]>([])

const permissionCategories = ref<PermissionCategory[]>([])
const rolePermissions = ref<RolePermissions[]>([])

const roleColorMap: Record<CenaRole, string> = {
  SUPER_ADMIN: 'error',
  ADMIN: 'error',
  MODERATOR: 'warning',
  TEACHER: 'info',
  STUDENT: 'success',
  PARENT: 'secondary',
}

const roleLabelMap: Record<CenaRole, string> = {
  SUPER_ADMIN: 'Super Admin',
  ADMIN: 'Admin',
  MODERATOR: 'Moderator',
  TEACHER: 'Teacher',
  STUDENT: 'Student',
  PARENT: 'Parent',
}

const roleDisplayOrder: CenaRole[] = ['SUPER_ADMIN', 'ADMIN', 'MODERATOR', 'TEACHER', 'STUDENT', 'PARENT']

const orderedRoles = computed(() => {
  return roleDisplayOrder
    .map(role => rolePermissions.value.find(rp => rp.role === role))
    .filter((rp): rp is RolePermissions => rp !== undefined)
})

const filteredCategories = computed(() => {
  if (!search.value.trim())
    return permissionCategories.value

  const query = search.value.toLowerCase()
  return permissionCategories.value.filter(cat => {
    const categoryMatch = cat.name.toLowerCase().includes(query)
    const actionMatch = cat.actions.some(a => a.toLowerCase().includes(query))
    return categoryMatch || actionMatch
  })
})

const filteredActions = (category: PermissionCategory): string[] => {
  if (!search.value.trim())
    return category.actions

  const query = search.value.toLowerCase()
  if (category.name.toLowerCase().includes(query))
    return category.actions

  return category.actions.filter(a => a.toLowerCase().includes(query))
}

const toggleCategory = (categoryName: string) => {
  const index = expandedCategories.value.indexOf(categoryName)
  if (index === -1)
    expandedCategories.value.push(categoryName)
  else
    expandedCategories.value.splice(index, 1)
}

const isCategoryExpanded = (categoryName: string): boolean => {
  return expandedCategories.value.includes(categoryName)
}

const hasPermission = (rp: RolePermissions, category: string, action: string): boolean => {
  return rp.permissions[category]?.includes(action) ?? false
}

const togglePermission = async (rp: RolePermissions, category: string, action: string) => {
  const current = rp.permissions[category] ?? []
  const actionIndex = current.indexOf(action)

  // Optimistic update
  if (actionIndex === -1) {
    if (!rp.permissions[category])
      rp.permissions[category] = []
    rp.permissions[category].push(action)
  }
  else {
    rp.permissions[category].splice(actionIndex, 1)
    if (rp.permissions[category].length === 0)
      delete rp.permissions[category]
  }

  try {
    await $api(`/admin/roles/${rp.roleId}/permissions`, {
      method: 'PUT',
      body: { permissions: rp.permissions },
    })
  }
  catch (error) {
    console.error('Failed to update permission:', error)
    // Revert on failure
    if (actionIndex === -1) {
      const idx = rp.permissions[category]?.indexOf(action) ?? -1
      if (idx !== -1)
        rp.permissions[category].splice(idx, 1)
    }
    else {
      if (!rp.permissions[category])
        rp.permissions[category] = []
      rp.permissions[category].push(action)
    }
  }
}

const categoryPermissionCount = (rp: RolePermissions, category: PermissionCategory): number => {
  return category.actions.filter(a => hasPermission(rp, category.name, a)).length
}

const isCategoryAllSelected = (rp: RolePermissions, category: PermissionCategory): boolean => {
  return category.actions.every(a => hasPermission(rp, category.name, a))
}

const isCategoryIndeterminate = (rp: RolePermissions, category: PermissionCategory): boolean => {
  const count = categoryPermissionCount(rp, category)
  return count > 0 && count < category.actions.length
}

const toggleCategoryAll = async (rp: RolePermissions, category: PermissionCategory) => {
  const allSelected = isCategoryAllSelected(rp, category)

  // Optimistic: toggle all actions for this category
  if (allSelected) {
    delete rp.permissions[category.name]
  }
  else {
    rp.permissions[category.name] = [...category.actions]
  }

  try {
    await $api(`/admin/roles/${rp.roleId}/permissions`, {
      method: 'PUT',
      body: { permissions: rp.permissions },
    })
  }
  catch (error) {
    console.error('Failed to bulk update permissions:', error)
    // Revert: re-fetch
    await fetchData()
  }
}

const fetchData = async () => {
  isLoading.value = true
  try {
    const [permResponse, rolesResponse] = await Promise.all([
      $api('/admin/permissions', { method: 'GET' }),
      $api('/admin/roles', { method: 'GET' }),
    ])

    permissionCategories.value = permResponse as PermissionCategory[]

    rolePermissions.value = (rolesResponse as any[]).map(role => ({
      roleId: role.id,
      roleName: role.name,
      role: role.role as CenaRole,
      permissions: role.permissions ?? {},
    }))
  }
  catch (error: any) {
    console.error('Failed to fetch permission data:', error)
    fetchError.value = error.message ?? 'Failed to load permission data'
  }
  finally {
    isLoading.value = false
  }
}

onMounted(() => {
  fetchData()
})
</script>

<template>
  <VRow>
    <VCol cols="12">
      <h4 class="text-h4 mb-1">
        Permission Matrix
      </h4>
      <p class="text-body-1 mb-0">
        Configure which actions each role can perform across all Cena platform areas.
      </p>
    </VCol>

    <VCol cols="12">
      <VCard>
        <VCardText class="d-flex align-center justify-space-between flex-wrap gap-4">
          <AppTextField
            v-model="search"
            placeholder="Search permissions..."
            prepend-inner-icon="tabler-search"
            style="inline-size: 20rem;"
          />
        </VCardText>

        <VDivider />

        <VAlert
          v-if="fetchError"
          type="error"
          variant="tonal"
          class="ma-4"
        >
          {{ fetchError }}
        </VAlert>

        <VAlert
          v-if="!isLoading && !fetchError && orderedRoles.length === 0 && permissionCategories.length > 0"
          type="info"
          variant="tonal"
          class="ma-4"
        >
          No roles loaded. Role columns will appear once role data is available.
        </VAlert>

        <VProgressLinear
          v-if="isLoading"
          indeterminate
        />

        <div
          v-if="!isLoading"
          class="permission-matrix"
        >
          <!-- Header row: role columns -->
          <VTable
            density="comfortable"
            class="text-no-wrap"
          >
            <thead>
              <tr>
                <th
                  class="permission-label-col"
                  style="min-inline-size: 200px;"
                >
                  Resource / Action
                </th>
                <th
                  v-for="rp in orderedRoles"
                  :key="rp.roleId"
                  class="text-center"
                  style="min-inline-size: 100px;"
                >
                  <VChip
                    :color="roleColorMap[rp.role]"
                    size="small"
                    label
                  >
                    {{ roleLabelMap[rp.role] || rp.roleName }}
                  </VChip>
                </th>
              </tr>
            </thead>

            <tbody>
              <template
                v-for="category in filteredCategories"
                :key="category.name"
              >
                <!-- Category header row -->
                <tr class="category-row">
                  <td>
                    <div
                      class="d-flex align-center gap-2 cursor-pointer"
                      @click="toggleCategory(category.name)"
                    >
                      <VIcon
                        :icon="isCategoryExpanded(category.name) ? 'tabler-chevron-down' : 'tabler-chevron-right'"
                        size="18"
                      />
                      <h6 class="text-h6">
                        {{ category.name }}
                      </h6>
                      <VChip
                        size="x-small"
                        variant="tonal"
                        color="default"
                      >
                        {{ category.actions.length }} actions
                      </VChip>
                    </div>
                  </td>
                  <td
                    v-for="rp in orderedRoles"
                    :key="`${category.name}-${rp.roleId}-all`"
                    class="text-center"
                  >
                    <VCheckbox
                      :model-value="isCategoryAllSelected(rp, category)"
                      :indeterminate="isCategoryIndeterminate(rp, category)"
                      hide-details
                      density="compact"
                      class="d-inline-flex"
                      @update:model-value="toggleCategoryAll(rp, category)"
                    />
                  </td>
                </tr>

                <!-- Individual action rows (expanded) -->
                <template v-if="isCategoryExpanded(category.name)">
                  <tr
                    v-for="action in filteredActions(category)"
                    :key="`${category.name}-${action}`"
                    class="action-row"
                  >
                    <td class="ps-10">
                      <span class="text-body-2 text-medium-emphasis">{{ category.name }}</span>
                      <span class="text-body-2 text-medium-emphasis mx-1">&rsaquo;</span>
                      <span class="text-body-2">{{ action }}</span>
                    </td>
                    <td
                      v-for="rp in orderedRoles"
                      :key="`${category.name}-${action}-${rp.roleId}`"
                      class="text-center"
                    >
                      <VCheckbox
                        :model-value="hasPermission(rp, category.name, action)"
                        hide-details
                        density="compact"
                        class="d-inline-flex"
                        @update:model-value="togglePermission(rp, category.name, action)"
                      />
                    </td>
                  </tr>
                </template>
              </template>
            </tbody>
          </VTable>
        </div>

        <VCardText
          v-if="!isLoading && filteredCategories.length === 0"
          class="text-center text-medium-emphasis"
        >
          No permissions match your search.
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>

<style lang="scss">
.permission-matrix {
  .category-row {
    td {
      background: rgba(var(--v-theme-on-surface), 0.04);
      font-weight: 600;
    }
  }

  .action-row {
    td {
      border-block-end: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    }
  }

  .v-checkbox {
    .v-selection-control {
      min-block-size: unset;
    }
  }

  .cursor-pointer {
    cursor: pointer;
  }
}
</style>
