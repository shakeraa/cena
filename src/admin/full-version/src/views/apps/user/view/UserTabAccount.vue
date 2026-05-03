<script lang="ts" setup>
import type { CenaUserProperties, CenaUserRole } from '@db/apps/users/types'

interface Props {
  userData: CenaUserProperties
}

interface Emit {
  (e: 'userUpdated'): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emit>()

const isSaving = ref(false)
const saveSuccess = ref(false)
const saveError = ref('')

// Editable fields
const fullName = ref(props.userData.fullName)
const email = ref(props.userData.email)
const school = ref(props.userData.school)
const grade = ref(props.userData.grade)
const locale = ref(props.userData.locale)
const role = ref<CenaUserRole>(props.userData.role)

// Watch for prop changes (e.g. after refetch)
watch(() => props.userData, (newData) => {
  fullName.value = newData.fullName
  email.value = newData.email
  school.value = newData.school
  grade.value = newData.grade
  locale.value = newData.locale
  role.value = newData.role
}, { deep: true })

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

const saveUserInfo = async () => {
  isSaving.value = true
  saveSuccess.value = false
  saveError.value = ''

  try {
    await $api(`/admin/users/${props.userData.id}`, {
      method: 'PUT',
      body: {
        fullName: fullName.value,
        email: email.value,
        school: school.value,
        grade: isStudentRole.value ? grade.value : '',
        locale: locale.value,
        role: role.value,
      },
    })

    saveSuccess.value = true
    emit('userUpdated')

    setTimeout(() => {
      saveSuccess.value = false
    }, 3000)
  }
  catch (e: any) {
    saveError.value = e?.data?.message ?? 'Failed to save user information'
    console.error('Failed to update user', e)
  }
  finally {
    isSaving.value = false
  }
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard title="Personal Information">
        <VCardText>
          <VAlert
            v-if="saveSuccess"
            type="success"
            variant="tonal"
            closable
            class="mb-4"
            text="User information updated successfully."
          />

          <VAlert
            v-if="saveError"
            type="error"
            variant="tonal"
            closable
            class="mb-4"
            :text="saveError"
            @click:close="saveError = ''"
          />

          <VForm @submit.prevent="saveUserInfo">
            <VRow>
              <VCol
                cols="12"
                md="6"
              >
                <AppTextField
                  v-model="fullName"
                  label="Full Name"
                  :rules="[requiredValidator]"
                  placeholder="Full Name"
                />
              </VCol>

              <VCol
                cols="12"
                md="6"
              >
                <AppTextField
                  v-model="email"
                  label="Email"
                  :rules="[requiredValidator, emailValidator]"
                  placeholder="Email"
                />
              </VCol>

              <VCol
                cols="12"
                md="6"
              >
                <AppSelect
                  v-model="role"
                  label="Role"
                  :items="roleOptions"
                  :rules="[requiredValidator]"
                />
              </VCol>

              <VCol
                cols="12"
                md="6"
              >
                <AppTextField
                  v-model="school"
                  label="School"
                  placeholder="School name"
                />
              </VCol>

              <VCol
                v-if="isStudentRole"
                cols="12"
                md="6"
              >
                <AppSelect
                  v-model="grade"
                  label="Grade"
                  :items="gradeOptions"
                />
              </VCol>

              <VCol
                cols="12"
                md="6"
              >
                <AppSelect
                  v-model="locale"
                  label="Language"
                  :items="localeOptions"
                  :rules="[requiredValidator]"
                />
              </VCol>

              <VCol cols="12">
                <VBtn
                  type="submit"
                  :loading="isSaving"
                >
                  Save Changes
                </VBtn>
              </VCol>
            </VRow>
          </VForm>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>
