<script setup lang="ts">
import Shepherd from 'shepherd.js'
import { withQuery } from 'ufo'
import type { RouteLocationRaw } from 'vue-router'
import type { SearchResults } from '@db/app-bar-search/types'
import { useConfigStore } from '@core/stores/config'

interface Suggestion {
  icon: string
  title: string
  url: RouteLocationRaw
}

defineOptions({
  inheritAttrs: false,
})

const configStore = useConfigStore()

interface SuggestionGroup {
  title: string
  content: Suggestion[]
}

// 👉 Is App Search Bar Visible
const isAppSearchBarVisible = ref(false)
const isLoading = ref(false)

// 👉 Default suggestions

const suggestionGroups: SuggestionGroup[] = [
  {
    title: 'Dashboards',
    content: [
      { icon: 'tabler-layout-dashboard', title: 'Platform Overview', url: { name: 'dashboards-admin' } },
      { icon: 'tabler-brain', title: 'Mastery Tracking', url: { name: 'apps-mastery-dashboard' } },
      { icon: 'tabler-focus-2', title: 'Focus & Engagement', url: { name: 'apps-focus-dashboard' } },
      { icon: 'tabler-heart-handshake', title: 'Cultural Responsiveness', url: { name: 'apps-cultural-dashboard' } },
    ],
  },
  {
    title: 'Content & Pedagogy',
    content: [
      { icon: 'tabler-list-check', title: 'Question Bank', url: { name: 'apps-questions-list' } },
      { icon: 'tabler-upload', title: 'Ingestion Pipeline', url: { name: 'apps-ingestion-pipeline' } },
      { icon: 'tabler-git-merge', title: 'Concept Graph (MCM)', url: { name: 'apps-pedagogy-mcm-graph' } },
      { icon: 'tabler-route', title: 'Methodology', url: { name: 'apps-pedagogy-methodology' } },
    ],
  },
  {
    title: 'Student AI',
    content: [
      { icon: 'tabler-message-chatbot', title: 'Tutoring Sessions', url: { name: 'apps-tutoring-sessions' } },
      { icon: 'tabler-flask', title: 'A/B Experiments', url: { name: 'apps-experiments' } },
      { icon: 'tabler-shield-check', title: 'Moderation Queue', url: { name: 'apps-moderation-queue' } },
      { icon: 'tabler-megaphone', title: 'Parent Outreach', url: { name: 'apps-outreach-dashboard' } },
    ],
  },
  {
    title: 'System',
    content: [
      { icon: 'tabler-heart-rate-monitor', title: 'System Health', url: { name: 'apps-system-health' } },
      { icon: 'tabler-cpu', title: 'Actor Explorer', url: { name: 'apps-system-actors' } },
      { icon: 'tabler-database', title: 'Event Store', url: { name: 'apps-system-events' } },
      { icon: 'tabler-robot', title: 'AI Settings', url: { name: 'apps-system-ai-settings' } },
    ],
  },
]

// 👉 No Data suggestion
const noDataSuggestions: Suggestion[] = [
  {
    title: 'Platform Overview',
    icon: 'tabler-layout-dashboard',
    url: { name: 'dashboards-admin' },
  },
  {
    title: 'Question Bank',
    icon: 'tabler-list-check',
    url: { name: 'apps-questions-list' },
  },
  {
    title: 'System Health',
    icon: 'tabler-heart-rate-monitor',
    url: { name: 'apps-system-health' },
  },
]

const searchQuery = ref('')

const router = useRouter()
const searchResult = ref<SearchResults[]>([])

const fetchResults = async () => {
  isLoading.value = true

  const { data } = await useApi<any>(withQuery('/app-bar/search', { q: searchQuery.value }))

  searchResult.value = data.value

  // ℹ️ simulate loading: we have used setTimeout for better user experience your can remove it
  setTimeout(() => {
    isLoading.value = false
  }, 500)
}

watch(searchQuery, fetchResults)

const closeSearchBar = () => {
  isAppSearchBarVisible.value = false
  searchQuery.value = ''
}

// 👉 redirect the selected page
const redirectToSuggestedPage = (selected: Suggestion) => {
  router.push(selected.url as string)
  closeSearchBar()
}

const LazyAppBarSearch = defineAsyncComponent(() => import('@core/components/AppBarSearch.vue'))
</script>

<template>
  <div
    class="d-flex align-center cursor-pointer"
    v-bind="$attrs"
    style="user-select: none;"
    @click="isAppSearchBarVisible = !isAppSearchBarVisible"
  >
    <!-- 👉 Search Trigger button -->
    <!-- close active tour while opening search bar using icon -->
    <IconBtn @click="Shepherd.activeTour?.cancel()">
      <VIcon icon="tabler-search" />
    </IconBtn>

    <span
      v-if="configStore.appContentLayoutNav === 'vertical'"
      class="d-none d-md-flex align-center text-disabled ms-2"
      @click="Shepherd.activeTour?.cancel()"
    >
      <span class="me-2">Search</span>
      <span class="meta-key">&#8984;K</span>
    </span>
  </div>

  <!-- 👉 App Bar Search -->
  <LazyAppBarSearch
    v-model:is-dialog-visible="isAppSearchBarVisible"
    :search-results="searchResult"
    :is-loading="isLoading"
    @search="searchQuery = $event"
  >
    <!-- suggestion -->
    <template #suggestions>
      <VCardText class="app-bar-search-suggestions pa-12">
        <VRow v-if="suggestionGroups">
          <VCol
            v-for="suggestion in suggestionGroups"
            :key="suggestion.title"
            cols="12"
            sm="6"
          >
            <p
              class="custom-letter-spacing text-disabled text-uppercase py-2 px-4 mb-0"
              style="font-size: 0.75rem; line-height: 0.875rem;"
            >
              {{ suggestion.title }}
            </p>
            <VList class="card-list">
              <VListItem
                v-for="item in suggestion.content"
                :key="item.title"
                class="app-bar-search-suggestion mx-4 mt-2"
                @click="redirectToSuggestedPage(item)"
              >
                <VListItemTitle>{{ item.title }}</VListItemTitle>
                <template #prepend>
                  <VIcon
                    :icon="item.icon"
                    size="20"
                    class="me-n1"
                  />
                </template>
              </VListItem>
            </VList>
          </VCol>
        </VRow>
      </VCardText>
    </template>

    <!-- no data suggestion -->
    <template #noDataSuggestion>
      <div class="mt-9">
        <span class="d-flex justify-center text-disabled mb-2">Try searching for</span>
        <h6
          v-for="suggestion in noDataSuggestions"
          :key="suggestion.title"
          class="app-bar-search-suggestion text-h6 font-weight-regular cursor-pointer py-2 px-4"
          @click="redirectToSuggestedPage(suggestion)"
        >
          <VIcon
            size="20"
            :icon="suggestion.icon"
            class="me-2"
          />
          <span>{{ suggestion.title }}</span>
        </h6>
      </div>
    </template>

    <!-- search result -->
    <template #searchResult="{ item }">
      <VListSubheader class="text-disabled custom-letter-spacing font-weight-regular ps-4">
        {{ item.title }}
      </VListSubheader>
      <VListItem
        v-for="list in item.children"
        :key="list.title"
        :to="list.url"
        @click="closeSearchBar"
      >
        <template #prepend>
          <VIcon
            size="20"
            :icon="list.icon"
            class="me-n1"
          />
        </template>
        <template #append>
          <VIcon
            size="20"
            icon="tabler-corner-down-left"
            class="enter-icon flip-in-rtl"
          />
        </template>
        <VListItemTitle>
          {{ list.title }}
        </VListItemTitle>
      </VListItem>
    </template>
  </LazyAppBarSearch>
</template>

<style lang="scss">
@use "@styles/variables/vuetify.scss";

.meta-key {
  border: thin solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 6px;
  block-size: 1.5625rem;
  font-size: 0.8125rem;
  line-height: 1.3125rem;
  padding-block: 0.125rem;
  padding-inline: 0.25rem;
}

.app-bar-search-dialog {
  .custom-letter-spacing {
    letter-spacing: 0.8px;
  }

  .card-list {
    --v-card-list-gap: 8px;
  }
}
</style>
