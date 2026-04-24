<script setup lang="ts">
import type { CenaUserRole, CenaUserStatus } from '@db/apps/users/types'

interface UserData {
  id: string
  uid: string
  fullName: string
  email: string
  role: CenaUserRole
  status: CenaUserStatus
  school: string
  grade: string
  avatar: string
  locale: string
  createdAt: string
  lastLoginAt: string | null
}

interface Props {
  userData?: any
  isDialogVisible: boolean
}

interface Emit {
  (e: 'submit', value: UserData): void
  (e: 'update:isDialogVisible', val: boolean): void
}

const props = withDefaults(defineProps<Props>(), {
  userData: () => ({
    id: '',
    uid: '',
    fullName: '',
    email: '',
    role: 'STUDENT' as CenaUserRole,
    status: 'active' as CenaUserStatus,
    school: '',
    grade: '',
    avatar: '',
    locale: 'en',
    createdAt: '',
    lastLoginAt: null,
  }),
})

const emit = defineEmits<Emit>()

const userData = ref<UserData>(structuredClone(toRaw(props.userData)) as UserData)

watch(() => props, () => {
  userData.value = structuredClone(toRaw(props.userData)) as UserData
})

const onFormSubmit = () => {
  emit('update:isDialogVisible', false)
  emit('submit', userData.value)
}

const onFormReset = () => {
  userData.value = structuredClone(toRaw(props.userData))
  emit('update:isDialogVisible', false)
}

const dialogModelValueUpdate = (val: boolean) => {
  emit('update:isDialogVisible', val)
}

const roles: CenaUserRole[] = ['STUDENT', 'TEACHER', 'PARENT', 'MODERATOR', 'ADMIN', 'SUPER_ADMIN']
const statuses: CenaUserStatus[] = ['active', 'suspended', 'pending']
const locales = [
  { title: 'English', value: 'en' },
  { title: 'עברית', value: 'he' },
  { title: 'العربية', value: 'ar' },
]
</script>

<template>
  <VDialog
    :width="$vuetify.display.smAndDown ? 'auto' : 900"
    :model-value="props.isDialogVisible"
    @update:model-value="dialogModelValueUpdate"
  >
    <DialogCloseBtn @click="dialogModelValueUpdate(false)" />

    <VCard class="pa-sm-10 pa-2">
      <VCardText>
        <h4 class="text-h4 text-center mb-2">
          Edit User Information
        </h4>
        <p class="text-body-1 text-center mb-6">
          Update user details for the Cena platform.
        </p>

        <VForm
          class="mt-6"
          @submit.prevent="onFormSubmit"
        >
          <VRow>
            <VCol
              cols="12"
              md="6"
            >
              <AppTextField
                v-model="userData.fullName"
                label="Full Name"
                placeholder="Ahmad Khalil"
              />
            </VCol>

            <VCol
              cols="12"
              md="6"
            >
              <AppTextField
                v-model="userData.email"
                label="Email"
                placeholder="user@cena.edu"
              />
            </VCol>

            <VCol
              cols="12"
              md="6"
            >
              <AppSelect
                v-model="userData.role"
                label="Role"
                :items="roles"
              />
            </VCol>

            <VCol
              cols="12"
              md="6"
            >
              <AppSelect
                v-model="userData.status"
                label="Status"
                :items="statuses"
              />
            </VCol>

            <VCol
              cols="12"
              md="6"
            >
              <AppTextField
                v-model="userData.school"
                label="School"
                placeholder="Al-Quds Academy"
              />
            </VCol>

            <VCol
              cols="12"
              md="6"
            >
              <AppSelect
                v-model="userData.grade"
                label="Grade"
                :items="['1','2','3','4','5','6','7','8','9','10','11','12']"
                clearable
              />
            </VCol>

            <VCol
              cols="12"
              md="6"
            >
              <AppSelect
                v-model="userData.locale"
                label="Language"
                :items="locales"
              />
            </VCol>

            <VCol
              cols="12"
              class="d-flex flex-wrap justify-center gap-4"
            >
              <VBtn type="submit">
                Submit
              </VBtn>

              <VBtn
                color="secondary"
                variant="tonal"
                @click="onFormReset"
              >
                Cancel
              </VBtn>
            </VCol>
          </VRow>
        </VForm>
      </VCardText>
    </VCard>
  </VDialog>
</template>
