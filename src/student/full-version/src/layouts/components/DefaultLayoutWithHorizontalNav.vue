<script lang="ts" setup>
import { computed } from 'vue'
import navItems from '@/navigation/horizontal'

import { themeConfig } from '@themeConfig'

// Components
import Footer from '@/layouts/components/Footer.vue'
import NavBarNotifications from '@/layouts/components/NavBarNotifications.vue'
import NavSearchBar from '@/layouts/components/NavSearchBar.vue'
import NavbarShortcuts from '@/layouts/components/NavbarShortcuts.vue'
import NavbarThemeSwitcher from '@/layouts/components/NavbarThemeSwitcher.vue'
import UserProfile from '@/layouts/components/UserProfile.vue'
import NavBarI18n from '@core/components/I18n.vue'
import { HorizontalNavLayout } from '@layouts'
import { VNodeRenderer } from '@layouts/components/VNodeRenderer'

// FIND-ux-014: gate the Hebrew locale in the navbar switcher. See the
// sibling DefaultLayoutWithVerticalNav for the rationale.
import { useAvailableLocales } from '@/composables/useAvailableLocales'

const { codes: availableCodes } = useAvailableLocales()

const visibleLangConfig = computed(() =>
  (themeConfig.app.i18n.langConfig ?? []).filter(lang =>
    availableCodes.value.includes(lang.i18nLang as 'en' | 'ar' | 'he'),
  ),
)
</script>

<template>
  <HorizontalNavLayout :nav-items="navItems">
    <!-- 👉 navbar -->
    <template #navbar>
      <RouterLink
        to="/"
        class="app-logo d-flex align-center gap-x-3"
      >
        <VNodeRenderer :nodes="themeConfig.app.logo" />

        <h1 class="app-title font-weight-bold leading-normal text-xl text-capitalize">
          {{ themeConfig.app.brandTitle ?? themeConfig.app.title }}
        </h1>
      </RouterLink>
      <VSpacer />

      <NavSearchBar trigger-btn-class="ms-lg-n3" />

      <NavBarI18n
        v-if="themeConfig.app.i18n.enable && visibleLangConfig.length"
        :languages="visibleLangConfig"
      />

      <NavbarThemeSwitcher />
      <NavbarShortcuts />
      <NavBarNotifications class="me-2" />
      <UserProfile />
    </template>

    <!-- 👉 Pages -->
    <slot />

    <!-- 👉 Footer -->
    <template #footer>
      <Footer />
    </template>

    <!-- 👉 Customizer -->
    <TheCustomizer />
  </HorizontalNavLayout>
</template>
