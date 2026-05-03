<script setup lang="ts">
import type { CenaRole } from '@/plugins/casl/ability'

interface RolePermissionEntry {
  category: string
  actions: string[]
}

interface CenaRoleData {
  id: string
  name: string
  role: CenaRole
  description: string
  userCount: number
  permissionCount: number
  permissions: Record<string, string[]>
  isSystem: boolean
}

interface EditablePermission {
  category: string
  actions: { name: string; enabled: boolean }[]
}

const emit = defineEmits<{
  (e: 'role-selected', role: CenaRole): void
}>()

const roles = ref<CenaRoleData[]>([])
const isLoading = ref(true)
const isEditDialogVisible = ref(false)
const isAddDialogVisible = ref(false)
const editingRole = ref<CenaRoleData | null>(null)
const editablePermissions = ref<EditablePermission[]>([])
const newRoleName = ref('')
const isSaving = ref(false)

const permissionCategories: RolePermissionEntry[] = [
  { category: 'Users', actions: ['list', 'view', 'create', 'edit', 'delete', 'suspend', 'impersonate'] },
  { category: 'Content', actions: ['list', 'view', 'create', 'edit', 'delete', 'approve', 'reject', 'publish'] },
  { category: 'Questions', actions: ['list', 'view', 'create', 'edit', 'delete', 'review', 'approve'] },
  { category: 'Analytics', actions: ['view-own', 'view-class', 'view-school', 'view-platform', 'export'] },
  { category: 'Focus Data', actions: ['view-own', 'view-class', 'view-aggregated', 'configure-alerts'] },
  { category: 'Mastery Data', actions: ['view-own', 'view-class', 'view-school', 'configure-thresholds'] },
  { category: 'Settings', actions: ['view', 'edit-own', 'edit-org', 'edit-platform'] },
  { category: 'System', actions: ['view-health', 'manage-actors', 'view-logs', 'manage-config'] },
]

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

const fetchRoles = async () => {
  isLoading.value = true
  try {
    const response = await $api('/admin/roles', { method: 'GET' })
    roles.value = response as CenaRoleData[]
  }
  catch (error) {
    console.error('Failed to fetch roles:', error)
  }
  finally {
    isLoading.value = false
  }
}

const countPermissions = (permissions: Record<string, string[]>): number => {
  let count = 0
  for (const actions of Object.values(permissions)) {
    count += actions.length
  }
  return count
}

const openEditDialog = (role: CenaRoleData) => {
  editingRole.value = role
  editablePermissions.value = permissionCategories.map(cat => ({
    category: cat.category,
    actions: cat.actions.map(action => ({
      name: action,
      enabled: role.permissions[cat.category]?.includes(action) ?? false,
    })),
  }))
  isEditDialogVisible.value = true
}

const savePermissions = async () => {
  if (!editingRole.value)
    return

  isSaving.value = true
  try {
    const updatedPermissions: Record<string, string[]> = {}
    for (const category of editablePermissions.value) {
      const enabledActions = category.actions.filter(a => a.enabled).map(a => a.name)
      if (enabledActions.length > 0) {
        updatedPermissions[category.category] = enabledActions
      }
    }

    await $api(`/admin/roles/${editingRole.value.id}/permissions`, {
      method: 'PUT',
      body: { permissions: updatedPermissions },
    })

    // Update the local role data
    const roleIndex = roles.value.findIndex(r => r.id === editingRole.value!.id)
    if (roleIndex !== -1) {
      roles.value[roleIndex].permissions = updatedPermissions
      roles.value[roleIndex].permissionCount = countPermissions(updatedPermissions)
    }

    isEditDialogVisible.value = false
  }
  catch (error) {
    console.error('Failed to save permissions:', error)
  }
  finally {
    isSaving.value = false
  }
}

const createCustomRole = async () => {
  if (!newRoleName.value.trim())
    return

  isSaving.value = true
  try {
    const response = await $api('/admin/roles', {
      method: 'POST',
      body: { name: newRoleName.value.trim() },
    })

    roles.value.push(response as CenaRoleData)
    isAddDialogVisible.value = false
    newRoleName.value = ''
  }
  catch (error) {
    console.error('Failed to create role:', error)
  }
  finally {
    isSaving.value = false
  }
}

const toggleCategoryAll = (catIndex: number, value: boolean) => {
  editablePermissions.value[catIndex].actions.forEach(action => {
    action.enabled = value
  })
}

const isCategoryAllSelected = (catIndex: number): boolean => {
  return editablePermissions.value[catIndex].actions.every(a => a.enabled)
}

const isCategoryIndeterminate = (catIndex: number): boolean => {
  const cat = editablePermissions.value[catIndex]
  const checkedCount = cat.actions.filter(a => a.enabled).length
  return checkedCount > 0 && checkedCount < cat.actions.length
}

onMounted(() => {
  fetchRoles()
})
</script>

<template>
  <VRow>
    <!-- Role Cards -->
    <VCol
      v-for="item in roles"
      :key="item.id"
      cols="12"
      sm="6"
      lg="4"
    >
      <VCard>
        <VCardText class="d-flex align-center pb-4">
          <div class="text-body-1">
            Total {{ item.userCount }} users
          </div>

          <VSpacer />

          <VAvatar
            :color="roleColorMap[item.role] || 'primary'"
            variant="tonal"
            size="40"
          >
            <VIcon
              :icon="roleIconMap[item.role] || 'tabler-user'"
              size="24"
            />
          </VAvatar>
        </VCardText>

        <VCardText class="pb-5">
          <h5 class="text-h5 mb-1">
            {{ item.name }}
          </h5>
          <p class="text-body-2 text-medium-emphasis mb-3">
            {{ item.description }}
          </p>
          <div class="d-flex align-center gap-2 mb-3">
            <VChip
              :color="roleColorMap[item.role] || 'primary'"
              size="small"
              label
            >
              {{ item.permissionCount }} permissions
            </VChip>
          </div>

          <div class="d-flex justify-space-between align-center">
            <a
              href="javascript:void(0)"
              class="font-weight-medium"
              @click="openEditDialog(item)"
            >
              Edit Role
            </a>
            <a
              href="javascript:void(0)"
              class="text-body-2"
              @click="emit('role-selected', item.role)"
            >
              View Users
            </a>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Add New Role Card -->
    <VCol
      cols="12"
      sm="6"
      lg="4"
    >
      <VCard
        class="h-100 d-flex align-center justify-center"
        :ripple="false"
        min-height="200"
      >
        <VCardText class="d-flex flex-column align-center justify-center text-center">
          <VAvatar
            color="primary"
            variant="tonal"
            size="48"
            class="mb-4"
          >
            <VIcon
              icon="tabler-plus"
              size="24"
            />
          </VAvatar>
          <VBtn
            size="small"
            @click="isAddDialogVisible = true"
          >
            Add New Role
          </VBtn>
          <p class="text-body-2 text-medium-emphasis mt-3 mb-0">
            Add a custom role if the predefined ones don't fit.
          </p>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Loading overlay -->
    <VCol
      v-if="isLoading"
      cols="12"
      class="text-center"
    >
      <VProgressCircular indeterminate />
    </VCol>
  </VRow>

  <!-- Edit Role Permissions Dialog -->
  <VDialog
    :width="$vuetify.display.smAndDown ? 'auto' : 900"
    :model-value="isEditDialogVisible"
    @update:model-value="isEditDialogVisible = $event"
  >
    <DialogCloseBtn @click="isEditDialogVisible = false" />

    <VCard class="pa-sm-10 pa-2">
      <VCardText>
        <h4 class="text-h4 text-center mb-2">
          Edit Role — {{ editingRole?.name }}
        </h4>
        <p class="text-body-1 text-center mb-6">
          Configure permissions for this role
        </p>

        <VTable class="permission-table text-no-wrap mb-6">
          <template
            v-for="(cat, catIndex) in editablePermissions"
            :key="cat.category"
          >
            <!-- Category header row -->
            <tr>
              <td>
                <h6 class="text-h6">
                  {{ cat.category }}
                </h6>
              </td>
              <td colspan="2">
                <div class="d-flex justify-end">
                  <VCheckbox
                    :model-value="isCategoryAllSelected(catIndex)"
                    :indeterminate="isCategoryIndeterminate(catIndex)"
                    label="Select All"
                    @update:model-value="toggleCategoryAll(catIndex, $event as boolean)"
                  />
                </div>
              </td>
            </tr>
            <!-- Individual action rows -->
            <tr
              v-for="action in cat.actions"
              :key="`${cat.category}-${action.name}`"
            >
              <td class="ps-8">
                <span class="text-body-1">{{ action.name }}</span>
              </td>
              <td colspan="2">
                <div class="d-flex justify-end">
                  <VCheckbox
                    v-model="action.enabled"
                  />
                </div>
              </td>
            </tr>
          </template>
        </VTable>

        <div class="d-flex align-center justify-center gap-4">
          <VBtn
            :loading="isSaving"
            @click="savePermissions"
          >
            Save Changes
          </VBtn>
          <VBtn
            color="secondary"
            variant="tonal"
            @click="isEditDialogVisible = false"
          >
            Cancel
          </VBtn>
        </div>
      </VCardText>
    </VCard>
  </VDialog>

  <!-- Add New Role Dialog -->
  <VDialog
    :width="$vuetify.display.smAndDown ? 'auto' : 500"
    :model-value="isAddDialogVisible"
    @update:model-value="isAddDialogVisible = $event"
  >
    <DialogCloseBtn @click="isAddDialogVisible = false" />

    <VCard class="pa-sm-10 pa-2">
      <VCardText>
        <h4 class="text-h4 text-center mb-2">
          Add New Role
        </h4>
        <p class="text-body-1 text-center mb-6">
          Create a custom role with specific permissions
        </p>

        <AppTextField
          v-model="newRoleName"
          label="Role Name"
          placeholder="Enter a name for the new role"
          class="mb-6"
        />

        <div class="d-flex align-center justify-center gap-4">
          <VBtn
            :loading="isSaving"
            :disabled="!newRoleName.trim()"
            @click="createCustomRole"
          >
            Create Role
          </VBtn>
          <VBtn
            color="secondary"
            variant="tonal"
            @click="isAddDialogVisible = false"
          >
            Cancel
          </VBtn>
        </div>
      </VCardText>
    </VCard>
  </VDialog>
</template>

<style lang="scss">
.permission-table {
  td {
    border-block-end: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    padding-block: 0.5rem;

    .v-checkbox {
      min-inline-size: 4.75rem;
    }

    &:not(:first-child) {
      padding-inline: 0.5rem;
    }

    .v-label {
      white-space: nowrap;
    }
  }
}
</style>
