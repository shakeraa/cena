// =============================================================================
// A11y store — user-adjustable accessibility preferences (IL Equal-Rights
// for Persons with Disabilities Law, 5758-1998 + Reg 5773-2013).
//
// Persists to localStorage under `cena-student-a11y-prefs`. Applies the
// preferences as `data-a11y-*` attributes on <html> so the SCSS rules in
// assets/styles/styles.scss cascade. Reactive — components call
// useA11yStore() and the setters automatically re-render.
//
// Extended 2026-04-21 (PRR-A11Y-EXPANDED-CONTROLS): line-height slider,
// color-blind filter (protanopia/deuteranopia/tritanopia), underline-all-
// links toggle. Each new preference round-trips through the store +
// localStorage + DOM attribute contract so the SCSS cascade can apply it.
// =============================================================================
import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export type A11yTextSize = 0 | 1 | 2 | 3 | 4 | 5
export type A11yContrast = 'normal' | 'high'
export type A11yMotion = 'normal' | 'reduced'
export type A11yDyslexiaFont = 'off' | 'on'
// Five discrete steps mapped to CSS line-height values 1.2..2.0 (0.2 steps).
export type A11yLineHeight = 0 | 1 | 2 | 3 | 4
// Filter simulates what a color-blind viewer sees. "off" means no filter.
export type A11yColorBlind = 'off' | 'protanopia' | 'deuteranopia' | 'tritanopia'
export type A11yUnderlineLinks = 'off' | 'on'

interface A11yPrefs {
  textSize: A11yTextSize
  contrast: A11yContrast
  motion: A11yMotion
  dyslexiaFont: A11yDyslexiaFont
  lineHeight: A11yLineHeight
  colorBlind: A11yColorBlind
  underlineLinks: A11yUnderlineLinks
}

const STORAGE_KEY = 'cena-student-a11y-prefs'

const DEFAULTS: A11yPrefs = {
  textSize: 1,
  contrast: 'normal',
  motion: 'normal',
  dyslexiaFont: 'off',
  lineHeight: 2, // middle step → line-height: 1.6
  colorBlind: 'off',
  underlineLinks: 'off',
}

// Map a line-height step to its CSS multiplier. Exported so tests and the
// toolbar component can show a human-readable label ("1.6×").
export const LINE_HEIGHT_STEPS: readonly number[] = Object.freeze([1.2, 1.4, 1.6, 1.8, 2.0])

function loadFromStorage(): A11yPrefs {
  if (typeof localStorage === 'undefined') return DEFAULTS
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return DEFAULTS
    const parsed = JSON.parse(raw)
    return {
      textSize: clampSize(parsed.textSize),
      contrast: parsed.contrast === 'high' ? 'high' : 'normal',
      motion: parsed.motion === 'reduced' ? 'reduced' : 'normal',
      dyslexiaFont: parsed.dyslexiaFont === 'on' ? 'on' : 'off',
      lineHeight: clampLineHeight(parsed.lineHeight),
      colorBlind: sanitizeColorBlind(parsed.colorBlind),
      underlineLinks: parsed.underlineLinks === 'on' ? 'on' : 'off',
    }
  }
  catch {
    return DEFAULTS
  }
}

function clampSize(v: unknown): A11yTextSize {
  const n = Number(v)
  if (!Number.isInteger(n) || n < 0 || n > 5) return DEFAULTS.textSize
  return n as A11yTextSize
}

function clampLineHeight(v: unknown): A11yLineHeight {
  const n = Number(v)
  if (!Number.isInteger(n) || n < 0 || n > 4) return DEFAULTS.lineHeight
  return n as A11yLineHeight
}

function sanitizeColorBlind(v: unknown): A11yColorBlind {
  if (v === 'protanopia' || v === 'deuteranopia' || v === 'tritanopia')
    return v
  return 'off'
}

function applyToDocument(prefs: A11yPrefs) {
  if (typeof document === 'undefined') return
  const html = document.documentElement
  html.setAttribute('data-a11y-text-size', String(prefs.textSize))
  html.setAttribute('data-a11y-contrast', prefs.contrast)
  html.setAttribute('data-a11y-motion', prefs.motion)
  html.setAttribute('data-a11y-dyslexia-font', prefs.dyslexiaFont)
  html.setAttribute('data-a11y-line-height', String(prefs.lineHeight))
  html.setAttribute('data-a11y-color-blind', prefs.colorBlind)
  html.setAttribute('data-a11y-underline-links', prefs.underlineLinks)
}

export const useA11yStore = defineStore('a11y', () => {
  const prefs = ref<A11yPrefs>(loadFromStorage())

  applyToDocument(prefs.value)

  watch(prefs, next => {
    applyToDocument(next)
    if (typeof localStorage !== 'undefined') {
      try { localStorage.setItem(STORAGE_KEY, JSON.stringify(next)) }
      catch { /* storage disabled; preferences are session-only */ }
    }
  }, { deep: true })

  function setTextSize(size: A11yTextSize) { prefs.value.textSize = clampSize(size) }
  function increaseTextSize() {
    if (prefs.value.textSize < 5) prefs.value.textSize = (prefs.value.textSize + 1) as A11yTextSize
  }
  function decreaseTextSize() {
    if (prefs.value.textSize > 0) prefs.value.textSize = (prefs.value.textSize - 1) as A11yTextSize
  }
  function toggleContrast() {
    prefs.value.contrast = prefs.value.contrast === 'high' ? 'normal' : 'high'
  }
  function toggleMotion() {
    prefs.value.motion = prefs.value.motion === 'reduced' ? 'normal' : 'reduced'
  }
  function toggleDyslexiaFont() {
    prefs.value.dyslexiaFont = prefs.value.dyslexiaFont === 'on' ? 'off' : 'on'
  }
  function setLineHeight(step: A11yLineHeight) {
    prefs.value.lineHeight = clampLineHeight(step)
  }
  function setColorBlind(mode: A11yColorBlind) {
    prefs.value.colorBlind = sanitizeColorBlind(mode)
  }
  function toggleUnderlineLinks() {
    prefs.value.underlineLinks = prefs.value.underlineLinks === 'on' ? 'off' : 'on'
  }
  function resetToDefaults() {
    prefs.value = { ...DEFAULTS }
  }

  return {
    prefs,
    setTextSize,
    increaseTextSize,
    decreaseTextSize,
    toggleContrast,
    toggleMotion,
    toggleDyslexiaFont,
    setLineHeight,
    setColorBlind,
    toggleUnderlineLinks,
    resetToDefaults,
  }
})
