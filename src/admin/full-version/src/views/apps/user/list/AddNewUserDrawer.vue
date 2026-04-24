<script setup lang="ts">
import { PerfectScrollbar } from 'vue3-perfect-scrollbar'

import type { VForm } from 'vuetify/components/VForm'

import type { CenaUserProperties, CenaUserRole } from '@db/apps/users/types'

interface Emit {
  (e: 'update:isDrawerOpen', value: boolean): void
  (e: 'userData', value: Partial<CenaUserProperties>): void
  (e: 'inviteData', value: Partial<CenaUserProperties>): void
}

interface Props {
  isDrawerOpen: boolean
}

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

const isFormValid = ref(false)
const refForm = ref<VForm>()
const fullName = ref('')
const email = ref('')
const role = ref<CenaUserRole>('STUDENT')
const school = ref('')
const grade = ref('')
const locale = ref('en')

const roleOptions: { title: string; value: CenaUserRole }[] = [
  { title: 'Student', value: 'STUDENT' },
  { title: 'Teacher', value: 'TEACHER' },
  { title: 'Parent', value: 'PARENT' },
  { title: 'Moderator', value: 'MODERATOR' },
  { title: 'Admin', value: 'ADMIN' },
  { title: 'Super Admin', value: 'SUPER_ADMIN' },
]

const localeOptions = [
  { title: 'English', value: 'en' },
  { title: 'Hebrew', value: 'he' },
  { title: 'Arabic', value: 'ar' },
]

const gradeOptions = Array.from({ length: 12 }, (_, i) => ({
  title: `Grade ${i + 1}`,
  value: String(i + 1),
}))

const isStudentRole = computed(() => role.value === 'STUDENT')

// Drawer close
const closeNavigationDrawer = () => {
  emit('update:isDrawerOpen', false)

  nextTick(() => {
    refForm.value?.reset()
    refForm.value?.resetValidation()
  })
}

const buildUserData = (): Partial<CenaUserProperties> => ({
  fullName: fullName.value,
  email: email.value,
  role: role.value,
  school: school.value,
  grade: isStudentRole.value ? grade.value : '',
  locale: locale.value,
  avatar: '',
  status: 'pending',
})

const onSubmitCreate = () => {
  refForm.value?.validate().then(({ valid }) => {
    if (valid) {
      emit('userData', buildUserData())
      emit('update:isDrawerOpen', false)
      nextTick(() => {
        refForm.value?.reset()
        refForm.value?.resetValidation()
      })
    }
  })
}

const onSubmitInvite = () => {
  refForm.value?.validate().then(({ valid }) => {
    if (valid) {
      emit('inviteData', buildUserData())
      emit('update:isDrawerOpen', false)
      nextTick(() => {
        refForm.value?.reset()
        refForm.value?.resetValidation()
      })
    }
  })
}

const handleDrawerModelValueUpdate = (val: boolean) => {
  emit('update:isDrawerOpen', val)
}
</script>

<template>
  <VNavigationDrawer
    data-allow-mismatch
    temporary
    :width="400"
    location="end"
    class="scrollable-content"
    :model-value="props.isDrawerOpen"
    @update:model-value="handleDrawerModelValueUpdate"
  >
    <!-- Title -->
    <AppDrawerHeaderSection
      title="Add New User"
      @cancel="closeNavigationDrawer"
    />

    <VDivider />

    <PerfectScrollbar :options="{ wheelPropagation: false }">
      <VCard flat>
        <VCardText>
          <!-- Form -->
          <VForm
            ref="refForm"
            v-model="isFormValid"
            @submit.prevent="onSubmitCreate"
          >
            <VRow>
              <!-- Full Name -->
              <VCol cols="12">
                <AppTextField
                  v-model="fullName"
                  :rules="[requiredValidator]"
                  label="Full Name"
                  placeholder="Ahmed Al-Rashid"
                />
              </VCol>

              <!-- Email -->
              <VCol cols="12">
                <AppTextField
                  v-model="email"
                  :rules="[requiredValidator, emailValidator]"
                  label="Email"
                  placeholder="user@school.edu"
                />
              </VCol>

              <!-- Role -->
              <VCol cols="12">
                <AppSelect
                  v-model="role"
                  label="Role"
                  placeholder="Select Role"
                  :rules="[requiredValidator]"
                  :items="roleOptions"
                />
              </VCol>

              <!-- School -->
              <VCol cols="12">
                <AppTextField
                  v-model="school"
                  label="School"
                  placeholder="Al-Noor Academy"
                />
              </VCol>

              <!-- Grade (only for students) -->
              <VCol
                v-if="isStudentRole"
                cols="12"
              >
                <AppSelect
                  v-model="grade"
                  label="Grade"
                  placeholder="Select Grade"
                  :rules="[requiredValidator]"
                  :items="gradeOptions"
                />
              </VCol>

              <!-- Locale -->
              <VCol cols="12">
                <AppSelect
                  v-model="locale"
                  label="Language"
                  placeholder="Select Language"
                  :rules="[requiredValidator]"
                  :items="localeOptions"
                />
              </VCol>

              <!-- Submit buttons -->
              <VCol cols="12">
                <div class="d-flex gap-4">
                  <VBtn
                    type="submit"
                    class="flex-grow-1"
                  >
                    Create
                  </VBtn>
                  <VBtn
                    color="info"
                    class="flex-grow-1"
                    @click="onSubmitInvite"
                  >
                    Send Invite
                  </VBtn>
                </div>
                <VBtn
                  type="reset"
                  variant="tonal"
                  color="error"
                  block
                  class="mt-3"
                  @click="closeNavigationDrawer"
                >
                  Cancel
                </VBtn>
              </VCol>
            </VRow>
          </VForm>
        </VCardText>
      </VCard>
    </PerfectScrollbar>
  </VNavigationDrawer>
</template>
