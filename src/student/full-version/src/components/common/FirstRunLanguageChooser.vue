<script setup lang="ts">
/**
 * FirstRunLanguageChooser — full-screen first-visit locale picker.
 *
 * Mounts from root App.vue with `v-if="!localeStore.locked"`. Three
 * large tiles (English / العربية / עברית), native-script labels, 44×44 min
 * touch target, arrow-key cycling, Enter commits, Esc disabled.
 *
 * WHY full-screen + blocking: first-language Arabic and Hebrew readers
 * landing on a shared device default to English and cannot reach the
 * in-toolbar language switcher without keyboard nav through English UI
 * — an access barrier per IL Equal Rights for Persons with Disabilities
 * Law (5758-1998) "independent control" principle.
 *
 * WHY Esc disabled: Esc dismissal would let the student skip into
 * English-default onboarding without a language choice, defeating the
 * accessibility purpose of the chooser.
 *
 * Hebrew gate: VITE_ENABLE_HEBREW=false removes the Hebrew tile from
 * the list via useAvailableLocales.
 *
 * Upcast path: users who had already picked a locale before this chooser
 * shipped have their legacy `cena-student-locale` string value upcast to
 * `{ code, locked: true }` by `localeStore.readPersisted()`, so they do
 * not see the chooser.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useAvailableLocales } from '@/composables/useAvailableLocales'
import { useLocaleSideEffects } from '@/composables/useLocaleSideEffects'
import { useLocaleStore, type SupportedLocaleCode } from '@/stores/localeStore'

const { locales: availableLocales } = useAvailableLocales()
const { apply } = useLocaleSideEffects()
const localeStore = useLocaleStore()

/**
 * Each tile has: code, native-script headline, hint-in-that-language
 * caption. Hints are intentionally written in the target locale so a
 * reader who cannot parse the app shell's current locale can still
 * recognise their own tile from the caption. Keys are pre-localised at
 * the i18n layer under `firstRun.tile*Hint` and live in ALL three locale
 * files — but we read them via the raw i18n messages rather than t() so
 * the Arabic hint reads in Arabic regardless of current shell locale.
 */
const tiles = computed(() => {
  return availableLocales.value.map(loc => {
    const code = loc.code as SupportedLocaleCode
    const headline = loc.label // native script — already in useAvailableLocales
    // Map each locale code to a caption that reads in that target locale.
    // Sourced from firstRun.tile*Hint in the same locale's JSON (native
    // script for whichever tile). The mapping is fixed at build time.
    const captionByCode: Record<SupportedLocaleCode, string> = {
      en: 'Continue in English',
      ar: 'متابعة بالعربية',
      he: 'המשך בעברית',
    }

    return {
      code,
      headline,
      caption: captionByCode[code],
      dir: loc.dir,
    }
  })
})

const selectedIndex = ref(0)
const tileRefs = ref<HTMLButtonElement[]>([])

function focusSelected() {
  const el = tileRefs.value[selectedIndex.value]
  if (el && typeof el.focus === 'function')
    el.focus()
}

function commit(code: SupportedLocaleCode) {
  // Apply side-effects (html.lang, html.dir, vue-i18n, vuetify) BEFORE
  // persisting — ensures the onboarding screen that appears after close
  // renders in the chosen locale on first paint.
  apply(code)
  localeStore.setLocale(code, { lock: true })
}

function commitSelected() {
  const tile = tiles.value[selectedIndex.value]
  if (tile)
    commit(tile.code)
}

function onKeydown(e: KeyboardEvent) {
  // Esc is disabled per task spec — swallow it so nothing up the
  // container handles it either.
  if (e.key === 'Escape') {
    e.preventDefault()
    e.stopPropagation()

    return
  }

  if (e.key === 'ArrowDown' || e.key === 'ArrowRight') {
    e.preventDefault()
    selectedIndex.value = (selectedIndex.value + 1) % tiles.value.length
    focusSelected()

    return
  }

  if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') {
    e.preventDefault()
    selectedIndex.value
      = (selectedIndex.value - 1 + tiles.value.length) % tiles.value.length
    focusSelected()

    return
  }

  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    commitSelected()
  }
}

onMounted(() => {
  // Focus the first tile on mount so keyboard users can commit immediately.
  focusSelected()
})

onBeforeUnmount(() => {
  // No global listener installed; nothing to clean up.
})
</script>

<template>
  <div
    class="first-run-chooser"
    role="dialog"
    aria-modal="true"
    aria-labelledby="first-run-title"
    data-testid="first-run-chooser"
    @keydown="onKeydown"
  >
    <div class="first-run-chooser__panel">
      <h1
        id="first-run-title"
        class="text-h4 mb-2"
        data-testid="first-run-title"
      >
        <!-- Headline is rendered in English because i18n is locked to the
             default locale until the user commits. The tile captions below
             cover other readers. -->
        Choose your language · اختر لغتك · בחר את השפה שלך
      </h1>

      <p class="text-body-2 text-medium-emphasis mb-6">
        You can change this later in the accessibility toolbar.
        · يمكنك تغيير ذلك لاحقًا من شريط إمكانية الوصول.
        · ניתן לשנות זאת מאוחר יותר בסרגל הנגישות.
      </p>

      <div
        class="first-run-chooser__tiles"
        role="radiogroup"
        aria-labelledby="first-run-title"
      >
        <button
          v-for="(tile, index) in tiles"
          :key="tile.code"
          ref="tileRefs"
          type="button"
          role="radio"
          :aria-checked="selectedIndex === index"
          :data-testid="`first-run-tile-${tile.code}`"
          :class="[
            'first-run-chooser__tile',
            { 'first-run-chooser__tile--selected': selectedIndex === index },
          ]"
          @click="selectedIndex = index; commit(tile.code)"
          @focus="selectedIndex = index"
        >
          <bdi :dir="tile.dir" class="first-run-chooser__tile-headline">
            {{ tile.headline }}
          </bdi>
          <bdi :dir="tile.dir" class="first-run-chooser__tile-caption">
            {{ tile.caption }}
          </bdi>
          <span class="first-run-chooser__tile-code">{{ tile.code }}</span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.first-run-chooser {
  position: fixed;
  inset: 0;
  z-index: 2147483000; /* above all Vuetify overlays */
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  background: rgb(var(--v-theme-background));
}

.first-run-chooser__panel {
  width: 100%;
  max-width: 760px;
  text-align: center;
}

.first-run-chooser__tiles {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
  margin-block-start: 24px;
}

@media (min-width: 640px) {
  .first-run-chooser__tiles {
    grid-template-columns: repeat(3, 1fr);
  }
}

.first-run-chooser__tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-block-size: 160px;
  min-inline-size: 44px;
  padding: 24px 16px;
  border: 2px solid rgb(var(--v-theme-on-surface), 0.15);
  border-radius: 16px;
  background: rgb(var(--v-theme-surface));
  color: rgb(var(--v-theme-on-surface));
  font: inherit;
  cursor: pointer;
  transition: border-color 120ms ease, transform 120ms ease;
}

.first-run-chooser__tile:focus-visible {
  outline: none;
  border-color: rgb(var(--v-theme-primary));
  box-shadow: 0 0 0 3px rgb(var(--v-theme-primary), 0.35);
}

.first-run-chooser__tile--selected {
  border-color: rgb(var(--v-theme-primary));
}

.first-run-chooser__tile-headline {
  font-size: 1.75rem;
  font-weight: 600;
  line-height: 1.1;
}

.first-run-chooser__tile-caption {
  font-size: 0.95rem;
  color: rgb(var(--v-theme-on-surface), 0.75);
}

.first-run-chooser__tile-code {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: rgb(var(--v-theme-on-surface), 0.55);
}

@media (prefers-reduced-motion: reduce) {
  .first-run-chooser__tile {
    transition: none;
  }
}
</style>
