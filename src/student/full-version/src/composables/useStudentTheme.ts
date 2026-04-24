import type { ComputedRef } from 'vue'
import { computed } from 'vue'
import { useTheme } from 'vuetify'
import type { StudentThemeExtension } from '@/plugins/vuetify/theme'
import { getStudentTokens } from '@/plugins/vuetify/theme'

/**
 * Returns the strongly-typed student tokens (flow + mastery) for the
 * currently active Vuetify theme. Reactive — consumers do not need to
 * manually watch theme changes.
 */
export function useStudentTheme(): ComputedRef<StudentThemeExtension> {
  const theme = useTheme()

  return computed(() => getStudentTokens(theme.global.name.value))
}
