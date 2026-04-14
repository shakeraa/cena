<script setup lang="ts">
/**
 * FigureThumbnail.vue — Mini figure preview for mobile step input (FIG-MOBILE-001)
 *
 * Shows a collapsed thumbnail of the question figure during step-by-step solving.
 * Tapping expands to full-size overlay. Keeps the figure visible without
 * competing for screen space with the math input keyboard.
 */

import { ref } from 'vue'

interface FigureSpec {
  type: string
  ariaLabel: string
  [key: string]: unknown
}

defineProps<{
  spec: FigureSpec

  /** Thumbnail height in px (default 80) */
  thumbnailHeight?: number
}>()

const expanded = ref(false)

function toggleExpand() {
  expanded.value = !expanded.value
}
</script>

<template>
  <div class="figure-thumbnail">
    <!-- Collapsed thumbnail -->
    <button
      v-if="!expanded"
      class="figure-thumbnail__button"
      :style="{ height: `${thumbnailHeight ?? 80}px` }"
      :aria-label="`${spec.ariaLabel} — tap to expand`"
      @click="toggleExpand"
    >
      <div class="figure-thumbnail__preview">
        <VIcon
          icon="tabler-chart-line"
          size="24"
        />
        <span class="figure-thumbnail__label">View figure</span>
      </div>
    </button>

    <!-- Expanded overlay -->
    <Teleport to="body">
      <Transition name="fade">
        <div
          v-if="expanded"
          class="figure-thumbnail__overlay"
          role="dialog"
          :aria-label="spec.ariaLabel"
          @click.self="toggleExpand"
        >
          <div class="figure-thumbnail__expanded">
            <button
              class="figure-thumbnail__close"
              aria-label="Close figure"
              @click="toggleExpand"
            >
              <VIcon icon="tabler-x" />
            </button>
            <!-- Render the actual QuestionFigure component -->
            <slot />
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped lang="scss">
.figure-thumbnail {
  &__button {
    width: 100%;
    display: flex;
    align-items: center;
    justify-content: center;
    border: 1px dashed var(--cena-muted, #B4B7BD);
    border-radius: 8px;
    background: var(--cena-card-bg, #f8f8f8);
    cursor: pointer;
    min-height: 44px; // touch target
    transition: background 0.15s;

    &:hover { background: var(--cena-hover-bg, #f0f0f0); }
  }

  &__preview {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--cena-primary, #7367F0);
  }

  &__label {
    font-size: 0.8125rem;
    font-weight: 500;
  }

  &__overlay {
    position: fixed;
    inset: 0;
    z-index: 9999;
    background: rgba(0, 0, 0, 0.7);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 1rem;
  }

  &__expanded {
    position: relative;
    background: var(--cena-card-bg, #fff);
    border-radius: 12px;
    padding: 1rem;
    max-width: 95vw;
    max-height: 80vh;
    overflow: auto;
  }

  &__close {
    position: absolute;
    top: 0.5rem;
    inset-inline-end: 0.5rem;
    z-index: 1;
    width: 36px;
    height: 36px;
    border-radius: 50%;
    border: none;
    background: var(--cena-card-bg, #f0f0f0);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
  }
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
