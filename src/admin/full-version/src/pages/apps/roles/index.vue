<script setup lang="ts">
import type { CenaRole } from '@/plugins/casl/ability'
import RoleCards from '@/views/apps/roles/RoleCards.vue'
import UserList from '@/views/apps/roles/UserList.vue'

const selectedRole = ref<CenaRole | undefined>()

const onRoleSelected = (role: CenaRole) => {
  selectedRole.value = role
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <h4 class="text-h4 mb-1">
        Cena Roles
      </h4>
      <p class="text-body-1 mb-0">
        Manage role-based access control for the Cena learning platform
      </p>
    </VCol>

    <!-- Role Cards -->
    <VCol cols="12">
      <RoleCards @role-selected="onRoleSelected" />
    </VCol>

    <VCol cols="12">
      <h4 class="text-h4 mb-1 mt-6">
        Users by Role
      </h4>
      <p class="text-body-1 mb-0">
        View and manage users assigned to each role.
      </p>
    </VCol>

    <VCol cols="12">
      <UserList :selected-role="selectedRole" />
    </VCol>
  </VRow>
</template>
