<script lang="ts" setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import navItems from '@/navigation/vertical'
import { themeConfig } from '@themeConfig'

// Student-web layout navbar. STU-W-UI-POLISH stripped five Vuexy admin
// leftovers (NavSearchBar, NavbarShortcuts, TheCustomizer, Footer slot,
// and admin-specific menu items in UserProfile). Kept:
//   - NavBarI18n: student language switcher
//   - NavbarThemeSwitcher: dark/light toggle
//   - NavBarNotifications: placeholder notifications bell — will be wired
//     by STU-W-14 against STB-07's notification endpoint
//   - UserProfile: simplified in STU-W-UI-POLISH to use authStore + meStore
import NavBarNotifications from '@/layouts/components/NavBarNotifications.vue'
import NavbarThemeSwitcher from '@/layouts/components/NavbarThemeSwitcher.vue'
import UserProfile from '@/layouts/components/UserProfile.vue'
import NavBarI18n from '@core/components/I18n.vue'

// @layouts plugin
import { VerticalNavLayout } from '@layouts'

// FIND-ux-014: gate the Hebrew locale in the navbar switcher. We filter
// themeConfig.app.i18n.langConfig through the same composable the
// standalone LanguageSwitcher uses so both surfaces stay in lockstep.
import { useAvailableLocales } from '@/composables/useAvailableLocales'

const { t } = useI18n()
const { codes: availableCodes } = useAvailableLocales()

const visibleLangConfig = computed(() =>
  (themeConfig.app.i18n.langConfig ?? []).filter(lang =>
    availableCodes.value.includes(lang.i18nLang as 'en' | 'ar' | 'he'),
  ),
)
</script>

<template>
  <VerticalNavLayout :nav-items="navItems">
    <!-- 👉 navbar -->
    <template #navbar="{ toggleVerticalOverlayNavActive }">
      <div class="d-flex h-100 align-center">
        <IconBtn
          id="vertical-nav-toggle-btn"
          class="ms-n3 d-lg-none"
          :aria-label="t('aria.sidebarToggle')"
          @click="toggleVerticalOverlayNavActive(true)"
        >
          <VIcon
            size="26"
            icon="tabler-menu-2"
          />
        </IconBtn>

        <VSpacer />

        <NavBarI18n
          v-if="themeConfig.app.i18n.enable && visibleLangConfig.length"
          :languages="visibleLangConfig"
        />
        <NavbarThemeSwitcher />
        <NavBarNotifications class="me-1" />
        <UserProfile />
      </div>
    </template>

    <!-- 👉 Pages -->
    <slot />

    <!--
      STU-W-UI-POLISH: removed <Footer /> slot (Vuexy admin footer) and
      <TheCustomizer /> (admin theme-picker panel). Student app ships
      without a footer per docs/student/01-navigation-and-ia.md §Layouts.
    -->
  </VerticalNavLayout>
</template>
