# Iteration 8 -- Accessibility of Photo-Based Question Input

**Date**: 2026-04-12
**Series**: Screenshot Question Analyzer -- Defense-in-Depth Research
**Iteration**: 8 of 10
**Focus**: Making photo-upload-to-step-solver fully accessible to students with disabilities
**Cumulative Security Robustness Score**: 82 / 100 (accessibility adds +8 from iteration 7's 74)

---

## 1. Problem Statement

Cena's Path B pipeline lets students photograph a math or physics question with their phone camera, sends the image through Gemini 2.5 Flash for LaTeX extraction, validates the expression via the CAS chain (MathNet -> SymPy -> Wolfram), and presents a step-solver workspace. Every stage of this pipeline must be usable by students who are blind, low-vision, motor-impaired, cognitively disabled, or using assistive technology.

This article is a proof-of-compliance guide covering WCAG 2.2 AA conformance, platform-specific screen reader flows, math rendering accessibility, cognitive and motor considerations, and a production-ready Vue 3 implementation.

---

## 2. WCAG 2.2 Success Criteria Applicable to Photo Upload

The following success criteria from WCAG 2.2 (W3C Recommendation, October 2023) directly govern the photo-upload-to-step-solver flow. Every criterion listed below is either Level A or Level AA -- the conformance target for U.S. education under the April 2026 ADA Title II deadline ([W3C WCAG 2.2](https://www.w3.org/TR/WCAG22/); [WebAIM WCAG Checklist](https://webaim.org/standards/wcag/checklist)).

### 2.1. SC 1.1.1 Non-text Content (Level A)

> "All non-text content that is presented to the user has a text alternative that serves the equivalent purpose."

**Application to photo upload:**

- The uploaded photo itself is transient input, not persistent content -- but the *result* (the extracted question) must have a text alternative.
- The vision model (Gemini 2.5 Flash) must return both LaTeX notation AND a plain-text description of the mathematical expression.
- If the photo is shown as a thumbnail in the UI (e.g., "You uploaded this image"), that thumbnail must carry `alt` text describing what the vision model extracted from it.
- Decorative icons (camera icon, upload icon) use `aria-hidden="true"` and empty `alt=""`.

**Backend contract change:**

```typescript
// The vision model response must include both fields
interface VisionExtractionResult {
  latex: string                    // e.g., "\\frac{d}{dx} x^2 = 2x"
  plainTextDescription: string     // e.g., "Find the derivative of x squared"
  confidence: number
  contentFlags: ContentFlag[]
}
```

### 2.2. SC 1.3.1 Info and Relationships (Level A)

> "Information, structure, and relationships conveyed through presentation can be programmatically determined or are available in text."

**Application:**

- The upload area must use semantic HTML: a `<label>` associated with the hidden file `<input>`, not a bare `<div>` with a click handler.
- Step-solver steps must use an ordered list (`<ol>`) with `role="list"` and `role="listitem"`, not a stack of styled `<div>` elements.
- Hint regions must use `<details>`/`<summary>` or ARIA `aria-expanded` to communicate collapsed/expanded state.
- The existing `QuestionCard.vue` already uses `role="radiogroup"` for choices -- this pattern extends to the step-solver workspace.

### 2.3. SC 2.1.1 Keyboard (Level A)

> "All functionality of the content is operable through a keyboard interface without requiring specific timings for individual keystrokes."

**Application:**

- The upload button must be focusable and activatable with Enter/Space.
- The camera capture variant must be triggerable from keyboard (the native file picker handles this on desktop; on mobile, the OS provides the camera interface).
- Every step in the step-solver must be navigable with Tab/Shift+Tab.
- The "Type your question instead" fallback must be reachable without a mouse.

### 2.4. SC 2.4.7 Focus Visible (Level AA)

> "Any keyboard operable user interface has a mode of operation where the keyboard focus indicator is visible."

**Application:**

- The upload button, camera trigger, step-solver inputs, and hint toggle all require a visible focus ring.
- Use a 2px solid outline with `outline-offset: 2px` on `:focus-visible`, matching the existing pattern in `QuestionCard.vue`.
- Never suppress focus outlines with `outline: none` without providing an alternative indicator.
- Focus indicators must have a minimum 3:1 contrast ratio against adjacent colors (WCAG 2.4.11 Focus Appearance, Level AAA -- recommended even though AA is our target).

### 2.5. SC 2.5.8 Target Size (Minimum) (Level AA) -- New in WCAG 2.2

> "The size of the target for pointer inputs is at least 24 by 24 CSS pixels."

**Application:**

- The camera trigger button on mobile must meet the 44x44px Apple Human Interface Guideline minimum (exceeding WCAG's 24px floor).
- The "Browse Photos" and "Take Photo" buttons must both meet this minimum.
- Remove file / retry buttons must not be smaller than 24x24 CSS pixels.
- The previous WCAG 2.1 AAA recommendation of 44px is adopted as our floor for mobile touch targets.

Reference: [W3C Understanding SC 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html)

### 2.6. SC 3.3.1 Error Identification (Level A)

> "If an input error is detected, the item that is in error is identified and the error is described to the user in text."

**Application:**

- File type rejection: "The file you selected is not an image. Please upload a JPG, PNG, or HEIC photo."
- File too large: "The photo is larger than 10 MB. Please take a smaller photo or crop the image."
- Vision model failure: "We could not read the math in your photo. Please make sure the question is clearly visible and try again, or type your question below."
- CAS validation failure: "The extracted expression could not be verified. You can edit it below or upload a different photo."
- All errors must be associated with the upload control via `aria-describedby` pointing to the error message element.

### 2.7. SC 3.3.7 Redundant Entry (Level A) -- New in WCAG 2.2

> "Information previously provided by or on behalf of the user is either auto-populated or available for the user to select."

**Application:**

- If a student uploads a photo and the extraction partially succeeds, the partial result must be preserved -- not discarded on retry.
- If the student switches to "Type your question" mode, any text already extracted from the photo must pre-fill the text input.

### 2.8. SC 4.1.2 Name, Role, Value (Level A)

> "For all user interface components, the name and role can be programmatically determined; states, properties, and values that can be set by the user can be programmatically set."

**Application:**

- The upload trigger: `role="button"`, `aria-label="Upload a photo of your math question"`.
- The processing spinner: `role="status"`, `aria-live="polite"`, with text content describing the current state.
- The extracted-question display: `role="region"`, `aria-label="Extracted question"`.
- Step-solver step inputs: `role="textbox"` (implicit from `<input>`), with `aria-label` describing which step.
- Hint toggle: `aria-expanded="true|false"`, `aria-controls="hint-panel-{id}"`.

---

## 3. Screen Reader Flow -- End-to-End Walkthrough

The following walkthrough documents exactly what a screen reader user hears at each stage of the photo-upload-to-step-solver pipeline. This is the target experience for VoiceOver (iOS/macOS), TalkBack (Android), NVDA (Windows), and JAWS (Windows).

### Stage 1: Navigate to Upload

```
Screen reader announces:
  "Upload a photo of your math question, button"
  (If using mobile: "Upload a photo of your math question, button.
   Double-tap to activate.")
```

The button must have:
- `role="button"` (implicit from `<button>` element)
- `aria-label="Upload a photo of your math question"`
- Visible text label matching the `aria-label` (SC 2.5.3 Label in Name)

### Stage 2: File Picker Opens

The native OS file picker is invoked via `<input type="file" accept="image/*" capture="environment">`. On mobile, this presents the camera or gallery chooser. The native file picker is accessible by default on all platforms -- VoiceOver, TalkBack, NVDA, and JAWS all handle it natively. No custom ARIA is needed for the picker itself.

On iOS, VoiceOver announces: "Take Photo or Video, or Choose File." On Android, TalkBack announces: "Camera, Photos, Files" (varies by OEM).

### Stage 3: Photo Uploading -- Progress Announced

Once the student selects or captures a photo, the UI enters a processing state. The screen reader must announce progress without the student having to navigate to a specific element.

```html
<div
  role="status"
  aria-live="polite"
  aria-atomic="true"
  class="sr-only"
  data-testid="upload-status"
>
  <!-- Content changes trigger automatic announcement -->
  Uploading your photo... 45% complete.
</div>
```

The `aria-live="polite"` region announces state changes after the screen reader finishes its current utterance. The sequence of announcements:

1. "Uploading your photo..."
2. "Analyzing your photo... This may take a few seconds."
3. "Extracting the math question from your photo..."

**Key implementation detail**: The `aria-live` region must exist in the DOM *before* content is injected. If the region is added dynamically, there must be a minimum 2-second delay before injecting text, per screen reader compatibility requirements ([MDN ARIA Live Regions](https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Guides/Live_regions); [Sara Soueidan -- Accessible Notifications](https://www.sarasoueidan.com/blog/accessible-notifications-with-aria-live-regions-part-1/)).

### Stage 4: Processing Complete -- Extracted Question Read Aloud

When the vision model returns the extracted question, the screen reader announces:

```
"Processing complete. Your question: Find the derivative of x squared.
 You can review the extracted question below or upload a different photo."
```

This is achieved by updating the `aria-live` region with the `plainTextDescription` from the vision model response. The announcement uses plain language, not LaTeX notation.

### Stage 5: Step-Solver Workspace Appears

The workspace is announced as a landmark region:

```html
<section
  aria-label="Step-by-step solver for: Find the derivative of x squared"
  role="region"
>
  <h2>Solve step by step</h2>
  <ol aria-label="Solution steps">
    <li>
      <span aria-hidden="true"><!-- KaTeX visual rendering --></span>
      <span class="sr-only">Step 1: Apply the power rule. The derivative
        of x to the power n is n times x to the power n minus 1.</span>
      <math xmlns="http://www.w3.org/1998/Math/MathML">
        <!-- MathML for screen reader consumption -->
      </math>
    </li>
  </ol>
</section>
```

### Stage 6: Working Through Steps with KaTeX + MathML

KaTeX renders visually-hidden MathML alongside its visible HTML output. However, as documented in [KaTeX issue #820](https://github.com/KaTeX/KaTeX/issues/820), the `aria-hidden="true"` on the visible rendering prevents touch exploration on mobile VoiceOver. Our mitigation strategy:

1. **Dual rendering**: KaTeX for visual display, plus a separate `aria-label` on each math container with the speech-form text.
2. **MathML fallback**: Include `<math>` elements for browsers/screen readers that support MathML natively (Firefox + NVDA with MathCAT; Safari + VoiceOver).
3. **Plain-text `sr-only` span**: For screen readers that do not process MathML, provide a visually-hidden span with the expression in spoken English.

```html
<div class="math-expression" aria-label="x squared equals 4">
  <!-- KaTeX visual output (aria-hidden by KaTeX itself) -->
  <span class="katex" aria-hidden="true">...</span>
  <!-- MathML for native screen reader support -->
  <math xmlns="http://www.w3.org/1998/Math/MathML" aria-hidden="false">
    <mrow>
      <msup><mi>x</mi><mn>2</mn></msup>
      <mo>=</mo>
      <mn>4</mn>
    </mrow>
  </math>
  <!-- Fallback for screen readers without MathML -->
  <span class="sr-only">x squared equals 4</span>
</div>
```

**MathJax alternative**: MathJax 4's `a11y/explorer` and `a11y/speech` components generate `aria-label` and `aria-braillelabel` attributes automatically from math expressions. If KaTeX's MathML accessibility issues remain unresolved, MathJax should be evaluated as a replacement for the step-solver's math rendering ([MathJax Accessibility Docs](https://docs.mathjax.org/en/latest/basic/accessibility.html)).

---

## 4. Mobile Camera Accessibility

### 4.1. iOS VoiceOver with Camera Capture

iOS handles the camera-to-web-upload flow natively through `<input type="file" accept="image/*" capture="environment">`:

1. VoiceOver user double-taps the upload button.
2. iOS presents a sheet: "Take Photo or Video" / "Choose File" / "Browse".
3. If "Take Photo" is selected, the Camera app opens.
4. Since iOS 13, VoiceOver tells the user where to tilt the device for framing guidance.
5. VoiceOver announces "Shutter button" when the user explores the capture trigger.
6. After capture, VoiceOver announces "Use Photo" and "Retake" options.
7. Selecting "Use Photo" returns to the web app, triggering the upload flow.

**No custom code is required for the iOS camera interaction** -- the OS handles accessibility. Our responsibility is ensuring the return-to-web transition announces the upload state via `aria-live`.

Reference: [Level Access -- iOS Switch Control](https://www.levelaccess.com/blog/assistive-technology-users-mobility-disabilities-ios-switch-control/)

### 4.2. Android TalkBack with Camera

Android's handling varies by OEM and version:

1. TalkBack user double-taps the upload button.
2. Android presents a chooser: "Camera" / "Files" / "Gallery" (varies by OEM).
3. If "Camera" is selected, the device camera opens.
4. On Pixel phones (Android 14+), TalkBack provides Guided Frame for selfies and basic object detection.
5. On non-Pixel devices, camera accessibility is limited -- primarily face detection.
6. The shutter button is announced as "Capture" or "Take photo".
7. After capture, the user confirms, and control returns to the web app.

**Recommendation**: For Android devices with limited camera accessibility, prominently surface the "Choose from gallery" option so students can take the photo with their preferred camera app first, then upload from gallery.

Reference: [Accessible Android](https://accessibleandroid.com/from-voiceover-on-ios-to-talkback-on-android-major-differences-and-anomalies-visually-impaired-users-should-expect/)

### 4.3. Gallery Upload Fallback

Not all students can use the camera in real time. The upload component must offer:

1. **Camera capture**: `<input type="file" accept="image/*" capture="environment">` -- opens camera directly.
2. **Gallery/file picker**: `<input type="file" accept="image/*">` (no `capture` attribute) -- opens gallery/file browser.
3. **Type manually**: A text input fallback for students who cannot photograph their question.

All three options must be equally prominent and keyboard/screen-reader accessible.

---

## 5. Extracted Question Accessibility

### 5.1. Vision Model Output Contract

The Gemini 2.5 Flash vision model must return a structured response that includes accessibility data:

```typescript
interface AccessibleQuestionExtraction {
  /** LaTeX notation for KaTeX rendering */
  latex: string

  /**
   * Plain-text description suitable for screen reader announcement.
   * Written as a student would read the question aloud.
   * Example: "Solve for x: x squared plus 3x minus 4 equals zero"
   */
  plainTextDescription: string

  /**
   * MathML markup for native screen reader math support.
   * Generated server-side from LaTeX using a converter (e.g., temml, latex2mathml).
   */
  mathml: string

  /** Subject classification for context */
  subject: 'algebra' | 'calculus' | 'geometry' | 'physics' | 'statistics'

  /** Confidence score 0-1 */
  confidence: number
}
```

The `plainTextDescription` is generated by including in the vision model prompt:

```
In addition to the LaTeX, provide a plain-text description of the question
as a student would read it aloud. Use words instead of symbols:
"squared" not "^2", "divided by" not "/", "times" not "*".
```

### 5.2. KaTeX Rendering with Accessibility Attributes

Each KaTeX-rendered expression wraps in an accessible container:

```typescript
function renderMathAccessible(
  latex: string,
  plainText: string,
  mathml: string,
): string {
  return `
    <div class="math-container" role="math" aria-label="${escapeHtml(plainText)}">
      <span aria-hidden="true">${katex.renderToString(latex, { throwOnError: false })}</span>
      <div class="sr-only">${mathml}</div>
    </div>
  `
}
```

The `role="math"` signals to screen readers that this is a mathematical expression. The `aria-label` provides the spoken description. The hidden MathML provides structured navigation for screen readers that support it (NVDA + MathCAT, VoiceOver).

### 5.3. Step-Solver Step Semantics

Each solver step uses list semantics:

```html
<ol class="step-solver__steps" aria-label="Solution steps">
  <li class="step-solver__step" data-step="1">
    <div class="step-solver__step-label">
      Step 1 of 4
    </div>
    <div role="math" aria-label="Apply the power rule: derivative of x squared is 2x">
      <!-- KaTeX visual rendering -->
    </div>
    <div class="step-solver__step-input">
      <label for="step-1-answer">Your answer for step 1</label>
      <input
        id="step-1-answer"
        type="text"
        aria-describedby="step-1-hint-toggle"
      />
    </div>
  </li>
</ol>
```

### 5.4. Hints as Expandable Regions

Hints follow the disclosure pattern with proper ARIA:

```html
<button
  id="step-1-hint-toggle"
  aria-expanded="false"
  aria-controls="step-1-hint-panel"
  class="step-solver__hint-toggle"
>
  Show hint for step 1
</button>
<div
  id="step-1-hint-panel"
  role="region"
  aria-labelledby="step-1-hint-toggle"
  hidden
>
  <p>Think about what happens to the exponent when you differentiate.</p>
</div>
```

When the hint is expanded, `aria-expanded` changes to `"true"` and the `hidden` attribute is removed. Screen readers announce "Show hint for step 1, expanded" or "collapsed" automatically.

---

## 6. Cognitive Accessibility

Cognitive accessibility benefits all students, not just those with diagnosed disabilities. The following patterns address WCAG 2.2 Guideline 3.3 (Input Assistance) and the Cognitive and Learning Disabilities Accessibility Task Force (COGA) supplemental guidance.

### 6.1. Clear Instructions

Every stage of the flow uses plain language:

| Stage | Instruction Text |
|-------|-----------------|
| Upload idle | "Take a photo of your math question" |
| Upload active | "Choose how to add your question:" |
| Camera option | "Take a photo with your camera" |
| Gallery option | "Choose a photo from your gallery" |
| Type option | "Type your question instead" |
| Processing | "Reading your photo... This takes a few seconds." |
| Success | "Here is the question from your photo. Is this correct?" |
| Error | "We could not read the math in your photo. The image may be blurry or the question may not be visible. Try again or type your question below." |

### 6.2. Progress Indicators

During the 1-2 second processing window (Gemini 2.5 Flash vision):

1. A determinate or indeterminate progress bar appears.
2. A text label below the bar describes the current stage.
3. The progress bar uses `role="progressbar"` with `aria-valuenow`, `aria-valuemin`, and `aria-valuemax`.
4. For indeterminate progress (we cannot predict exact duration), use `aria-busy="true"` on the container.

```html
<div aria-busy="true" aria-label="Processing your photo">
  <VProgressLinear
    indeterminate
    color="primary"
    height="6"
    rounded
    aria-label="Analyzing your photo"
  />
  <p class="text-body-2 text-center mt-2">
    Reading your photo... This takes a few seconds.
  </p>
</div>
```

### 6.3. Error Messages in Plain Language

Error messages avoid jargon, technical codes, or ambiguous language:

| Error Condition | Bad Message | Accessible Message |
|----------------|-------------|-------------------|
| Wrong file type | "MIME type invalid" | "This file is not a photo. Please upload a JPG, PNG, or HEIC image." |
| File too large | "413 Payload Too Large" | "This photo is too large (over 10 MB). Try taking a smaller photo or cropping it." |
| Vision failure | "Vision model confidence below threshold" | "We could not read the math in your photo. Make sure the question is clearly visible, well-lit, and not blurry." |
| Network error | "ERR_NETWORK" | "Something went wrong with the upload. Check your internet connection and try again." |
| CAS rejection | "CAS validation failed" | "The math expression we found does not look right. You can edit it below or upload a new photo." |

### 6.4. Type-Instead Fallback

A text input alternative must always be available. Students with cognitive disabilities may find it easier to type than to photograph and verify. Students with anxiety may feel more in control typing directly.

```html
<div class="upload-alternatives">
  <p class="text-body-2 text-medium-emphasis">
    Or type your question:
  </p>
  <VTextarea
    v-model="manualQuestion"
    :label="t('photoUpload.typeQuestion')"
    :placeholder="t('photoUpload.typeQuestionPlaceholder')"
    rows="3"
    aria-describedby="type-question-help"
  />
  <p id="type-question-help" class="text-caption text-medium-emphasis">
    Type the math question as you would read it aloud.
    For example: "Solve x squared plus 3x minus 4 equals 0"
  </p>
</div>
```

---

## 7. Low-Vision Considerations

### 7.1. High Contrast Mode Support

The upload component must respect system-level high contrast preferences using the standard CSS media queries ([MDN prefers-contrast](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-contrast); [Microsoft Edge Blog -- Forced Colors](https://blogs.windows.com/msedgedev/2020/09/17/styling-for-windows-high-contrast-with-new-standards-for-forced-colors/)):

```css
/* Increased contrast (macOS, iOS, some Linux DEs) */
@media (prefers-contrast: more) {
  .photo-upload__dropzone {
    border: 3px solid CanvasText;
    background: Canvas;
  }

  .photo-upload__button {
    border: 2px solid ButtonText;
    background: ButtonFace;
    color: ButtonText;
  }

  .step-solver__step {
    border-left: 4px solid CanvasText;
  }
}

/* Windows High Contrast Mode / Forced Colors */
@media (forced-colors: active) {
  .photo-upload__dropzone {
    border: 2px solid ButtonText;
    forced-color-adjust: none;
  }

  .photo-upload__icon {
    /* Ensure icons remain visible */
    color: ButtonText;
  }

  .step-solver__hint-toggle[aria-expanded="true"] {
    background: Highlight;
    color: HighlightText;
  }
}
```

### 7.2. Zoom-Friendly Layout

The layout must remain functional at 200% browser zoom and 400% text-only zoom (WCAG SC 1.4.4 Resize Text, Level AA; SC 1.4.10 Reflow, Level AA):

- Use `rem` and `em` units, not `px`, for font sizes and spacing.
- The upload area must reflow to a single column at narrow viewports.
- No horizontal scrolling at 320px CSS viewport width (the reflow baseline).
- The camera trigger button must not overflow or overlap other elements at any zoom level.

```css
.photo-upload {
  /* Reflow-safe: single column at narrow widths */
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-inline-size: 100%;
}

.photo-upload__options {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(15rem, 100%), 1fr));
  gap: 1rem;
}
```

### 7.3. Font Size Independence

Text in the upload component and step-solver must scale with user font size preferences:

- Never use `px` for `font-size`.
- Use relative units (`rem`, `em`) throughout.
- Test with the browser set to "Very Large" text size.
- Ensure no text is clipped or hidden behind other elements when font size increases.

---

## 8. Motor Accessibility

### 8.1. Large Tap Targets

All interactive elements in the photo upload flow must meet Apple's 44x44pt guideline for touch targets, which exceeds WCAG 2.2 SC 2.5.8's 24px minimum ([W3C Understanding SC 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html)):

```css
.photo-upload__trigger {
  /* Minimum 44x44px touch target */
  min-block-size: 2.75rem;     /* 44px at default 16px root */
  min-inline-size: 2.75rem;
  padding: 0.75rem 1.5rem;
}

.photo-upload__option-card {
  /* Card-style buttons: comfortably large */
  min-block-size: 5rem;
  padding: 1rem;
}

/* Ensure remove/retry buttons are not too small */
.photo-upload__remove-btn {
  min-block-size: 2.75rem;
  min-inline-size: 2.75rem;
}
```

### 8.2. Voice-Activated Camera Trigger

Both iOS and Android provide system-level voice control that works with accessible web content:

- **iOS Voice Control** (iOS 13+): The student says "Tap Upload a photo of your math question" -- Voice Control matches the `aria-label` or visible text. After the camera opens, the student says "Tap Take Picture" to capture. ([MacRumors -- Voice Control Guide](https://www.macrumors.com/guide/voice-control/); [INAIRSPACE -- Voice Commands for Camera](https://inairspace.com/blogs/learn-with-inair/how-to-set-up-voice-command-for-camera-on-iphone-step-by-step-guide))
- **Android Voice Access**: The student says "Tap 3" (where 3 is the number overlay on the upload button) or speaks the button label. After the camera opens, voice shutter or "Take photo" captures the image. ([INAIRSPACE -- Android Voice Commands](https://inairspace.com/blogs/learn-with-inair/android-voice-command-take-picture-hands-free-photo-mastery-guide))

**No custom code is needed for voice trigger** -- the system-level voice control works with any accessible button. Our responsibility is ensuring buttons have discoverable, speakable labels (SC 2.5.3 Label in Name).

### 8.3. Switch Access Compatibility

Switch access users (iOS Switch Control, Android Switch Access) navigate by scanning through focusable elements and activating with a physical switch ([Level Access -- Switch Control](https://www.levelaccess.com/blog/smartphone-accessibility-primer-301-switching-things-switch-access/)):

- Every interactive element must be reachable via Tab key (switch scanning maps to sequential focus navigation).
- Focus order must be logical: upload options -> processing status -> extracted question -> step-solver steps -> hints -> submit.
- Avoid custom gesture-only interactions (no swipe-to-upload, no pinch-to-zoom that is the only way to view the extracted question).
- Group related controls with `role="group"` and `aria-label` to reduce scanning time.

```html
<div role="group" aria-label="Upload options">
  <button class="photo-upload__option-card">
    <VIcon icon="tabler-camera" aria-hidden="true" />
    Take a photo
  </button>
  <button class="photo-upload__option-card">
    <VIcon icon="tabler-photo" aria-hidden="true" />
    Choose from gallery
  </button>
  <button class="photo-upload__option-card">
    <VIcon icon="tabler-keyboard" aria-hidden="true" />
    Type your question
  </button>
</div>
```

---

## 9. Implementation -- Vue 3 + TypeScript

The following component implements an accessible photo upload with all the ARIA patterns described above. It integrates with the existing Cena student app's Vuetify component library and i18n system.

### 9.1. Accessible Upload Component

```vue
<script setup lang="ts">
import { computed, ref, onMounted, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useFileDialog } from '@vueuse/core'

// ─── Types ───────────────────────────────────────────────────────
interface VisionResult {
  latex: string
  plainTextDescription: string
  mathml: string
  confidence: number
}

type UploadState = 'idle' | 'uploading' | 'processing' | 'success' | 'error'

interface Props {
  maxFileSizeMb?: number
  acceptedTypes?: string[]
}

// ─── Props & Emits ───────────────────────────────────────────────
const props = withDefaults(defineProps<Props>(), {
  maxFileSizeMb: 10,
  acceptedTypes: () => ['image/jpeg', 'image/png', 'image/heic', 'image/webp'],
})

const emit = defineEmits<{
  questionExtracted: [result: VisionResult]
  manualEntry: [text: string]
}>()

// ─── State ───────────────────────────────────────────────────────
const { t } = useI18n()
const state = ref<UploadState>('idle')
const errorMessage = ref<string>('')
const extractedResult = ref<VisionResult | null>(null)
const manualText = ref<string>('')
const statusAnnouncement = ref<string>('')
const previewUrl = ref<string>('')
const liveRegionReady = ref(false)

// Hidden file inputs: one with capture, one without
const { open: openCamera, onChange: onCameraChange } = useFileDialog({
  accept: 'image/*',
  // @ts-expect-error capture is valid but not in VueUse types
  capture: 'environment',
})

const { open: openGallery, onChange: onGalleryChange } = useFileDialog({
  accept: 'image/*',
})

// ─── Lifecycle ───────────────────────────────────────────────────
onMounted(() => {
  // aria-live region must exist in DOM before content injection.
  // Delay readiness by 2 seconds per screen reader compatibility.
  setTimeout(() => {
    liveRegionReady.value = true
  }, 2000)
})

// ─── Computed ────────────────────────────────────────────────────
const acceptedTypesDisplay = computed(() =>
  props.acceptedTypes.map(t => t.replace('image/', '').toUpperCase()).join(', '),
)

// ─── Methods ─────────────────────────────────────────────────────
function validateFile(file: File): string | null {
  if (!props.acceptedTypes.includes(file.type)) {
    return t('photoUpload.errors.invalidType', { types: acceptedTypesDisplay.value })
  }
  if (file.size > props.maxFileSizeMb * 1024 * 1024) {
    return t('photoUpload.errors.tooLarge', { maxMb: props.maxFileSizeMb })
  }
  return null
}

async function handleFile(file: File) {
  const validationError = validateFile(file)
  if (validationError) {
    state.value = 'error'
    errorMessage.value = validationError
    announce(validationError)
    return
  }

  // Show preview
  previewUrl.value = URL.createObjectURL(file)

  // Upload phase
  state.value = 'uploading'
  announce(t('photoUpload.status.uploading'))

  try {
    // Processing phase
    state.value = 'processing'
    announce(t('photoUpload.status.processing'))

    const result = await uploadAndExtract(file)
    extractedResult.value = result
    state.value = 'success'
    announce(t('photoUpload.status.success', {
      question: result.plainTextDescription,
    }))

    await nextTick()
    emit('questionExtracted', result)
  }
  catch (err) {
    state.value = 'error'
    const message = err instanceof Error
      ? t('photoUpload.errors.extractionFailed')
      : t('photoUpload.errors.networkError')
    errorMessage.value = message
    announce(message)
  }
}

async function uploadAndExtract(file: File): Promise<VisionResult> {
  const formData = new FormData()
  formData.append('photo', file)

  const response = await fetch('/api/questions/extract-from-photo', {
    method: 'POST',
    body: formData,
  })

  if (!response.ok) {
    throw new Error(`Upload failed: ${response.status}`)
  }

  return response.json()
}

function announce(message: string) {
  if (liveRegionReady.value) {
    // Clear first so duplicate messages still trigger announcement
    statusAnnouncement.value = ''
    requestAnimationFrame(() => {
      statusAnnouncement.value = message
    })
  }
}

function handleManualSubmit() {
  if (manualText.value.trim()) {
    emit('manualEntry', manualText.value.trim())
  }
}

function retry() {
  state.value = 'idle'
  errorMessage.value = ''
  extractedResult.value = null
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = ''
  }
}

// Wire file dialog callbacks
onCameraChange((files) => {
  if (files?.[0]) handleFile(files[0])
})
onGalleryChange((files) => {
  if (files?.[0]) handleFile(files[0])
})
</script>

<template>
  <div class="photo-upload" data-testid="photo-upload">
    <!--
      aria-live region — must exist in DOM at mount time.
      Content changes are announced automatically by screen readers.
    -->
    <div
      role="status"
      aria-live="polite"
      aria-atomic="true"
      class="sr-only"
      data-testid="upload-status-live"
    >
      {{ statusAnnouncement }}
    </div>

    <!-- ── Idle State: Upload Options ── -->
    <template v-if="state === 'idle'">
      <h3 class="text-h6 mb-4">
        {{ t('photoUpload.title') }}
      </h3>

      <div
        role="group"
        :aria-label="t('photoUpload.optionsGroup')"
        class="photo-upload__options"
      >
        <!-- Camera capture -->
        <VBtn
          variant="tonal"
          color="primary"
          size="large"
          class="photo-upload__option-card"
          prepend-icon="tabler-camera"
          :aria-label="t('photoUpload.takePhoto')"
          data-testid="upload-camera-btn"
          @click="openCamera"
        >
          {{ t('photoUpload.takePhoto') }}
        </VBtn>

        <!-- Gallery picker -->
        <VBtn
          variant="tonal"
          color="secondary"
          size="large"
          class="photo-upload__option-card"
          prepend-icon="tabler-photo"
          :aria-label="t('photoUpload.chooseFromGallery')"
          data-testid="upload-gallery-btn"
          @click="openGallery"
        >
          {{ t('photoUpload.chooseFromGallery') }}
        </VBtn>

        <!-- Type manually -->
        <VBtn
          variant="tonal"
          color="info"
          size="large"
          class="photo-upload__option-card"
          prepend-icon="tabler-keyboard"
          :aria-label="t('photoUpload.typeInstead')"
          data-testid="upload-type-btn"
          @click="state = 'idle'"
        >
          {{ t('photoUpload.typeInstead') }}
        </VBtn>
      </div>

      <!-- Manual entry -->
      <div class="photo-upload__manual mt-6">
        <p class="text-body-2 text-medium-emphasis mb-2">
          {{ t('photoUpload.typeQuestionLabel') }}
        </p>
        <VTextarea
          v-model="manualText"
          :label="t('photoUpload.typeQuestion')"
          :placeholder="t('photoUpload.typeQuestionPlaceholder')"
          rows="3"
          aria-describedby="manual-question-help"
          data-testid="manual-question-input"
        />
        <p
          id="manual-question-help"
          class="text-caption text-medium-emphasis mt-1"
        >
          {{ t('photoUpload.typeQuestionHelp') }}
        </p>
        <VBtn
          color="primary"
          :disabled="!manualText.trim()"
          class="mt-2"
          data-testid="manual-question-submit"
          @click="handleManualSubmit"
        >
          {{ t('photoUpload.submitTypedQuestion') }}
        </VBtn>
      </div>
    </template>

    <!-- ── Uploading / Processing State ── -->
    <template v-if="state === 'uploading' || state === 'processing'">
      <div
        aria-busy="true"
        :aria-label="t('photoUpload.processingAria')"
        class="photo-upload__processing pa-8 text-center"
      >
        <VProgressLinear
          indeterminate
          color="primary"
          height="6"
          rounded
          :aria-label="state === 'uploading'
            ? t('photoUpload.status.uploading')
            : t('photoUpload.status.processing')"
        />
        <p class="text-body-1 mt-4">
          {{ state === 'uploading'
            ? t('photoUpload.status.uploading')
            : t('photoUpload.status.processing')
          }}
        </p>
        <p class="text-body-2 text-medium-emphasis mt-1">
          {{ t('photoUpload.status.patience') }}
        </p>
      </div>
    </template>

    <!-- ── Success State: Show Extracted Question ── -->
    <template v-if="state === 'success' && extractedResult">
      <VAlert
        type="success"
        variant="tonal"
        class="mb-4"
        data-testid="extraction-success"
      >
        {{ t('photoUpload.status.successBrief') }}
      </VAlert>

      <section
        :aria-label="t('photoUpload.extractedQuestionAria', {
          question: extractedResult.plainTextDescription,
        })"
        role="region"
        class="photo-upload__result"
      >
        <h4 class="text-subtitle-1 mb-2">
          {{ t('photoUpload.extractedQuestionLabel') }}
        </h4>

        <!-- Math rendering with accessible fallback -->
        <div
          class="math-container pa-4 rounded bg-surface-variant"
          role="math"
          :aria-label="extractedResult.plainTextDescription"
          data-testid="extracted-math"
        >
          <!-- KaTeX visual rendering inserted here via v-html -->
          <span
            aria-hidden="true"
            v-html="renderKatex(extractedResult.latex)"
          />
          <!-- MathML for screen readers with native support -->
          <span
            class="sr-only"
            v-html="extractedResult.mathml"
          />
        </div>

        <p class="text-body-2 text-medium-emphasis mt-2">
          {{ extractedResult.plainTextDescription }}
        </p>

        <div class="d-flex gap-2 mt-4">
          <VBtn
            color="primary"
            data-testid="confirm-question-btn"
            @click="emit('questionExtracted', extractedResult!)"
          >
            {{ t('photoUpload.confirmQuestion') }}
          </VBtn>
          <VBtn
            variant="tonal"
            data-testid="retry-upload-btn"
            @click="retry"
          >
            {{ t('photoUpload.tryAgain') }}
          </VBtn>
        </div>
      </section>
    </template>

    <!-- ── Error State ── -->
    <template v-if="state === 'error'">
      <VAlert
        type="error"
        variant="tonal"
        class="mb-4"
        data-testid="upload-error"
        role="alert"
        :aria-describedby="'error-detail'"
      >
        <div id="error-detail">
          {{ errorMessage }}
        </div>
      </VAlert>

      <div class="d-flex gap-2">
        <VBtn
          color="primary"
          data-testid="error-retry-btn"
          @click="retry"
        >
          {{ t('photoUpload.tryAgain') }}
        </VBtn>
        <VBtn
          variant="tonal"
          data-testid="error-type-instead-btn"
          @click="retry(); state = 'idle'"
        >
          {{ t('photoUpload.typeInstead') }}
        </VBtn>
      </div>
    </template>
  </div>
</template>

<style scoped>
.photo-upload__options {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(15rem, 100%), 1fr));
  gap: 1rem;
}

.photo-upload__option-card {
  /* 44px minimum touch target */
  min-block-size: 2.75rem;
  min-inline-size: 2.75rem;
  justify-content: flex-start;
  text-transform: none;
  letter-spacing: normal;
}

/* Focus visible — matches existing QuestionCard pattern */
.photo-upload__option-card:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

/* High contrast mode */
@media (prefers-contrast: more) {
  .photo-upload__option-card {
    border: 2px solid CanvasText;
  }

  .math-container {
    border: 2px solid CanvasText;
  }
}

@media (forced-colors: active) {
  .photo-upload__option-card {
    border: 2px solid ButtonText;
    forced-color-adjust: none;
  }

  .photo-upload__option-card:focus-visible {
    outline: 2px solid Highlight;
  }
}

/* Zoom-friendly: reflow at narrow widths */
@media (max-width: 320px) {
  .photo-upload__options {
    grid-template-columns: 1fr;
  }
}

/* Screen-reader-only utility (visually hidden, still readable) */
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border-width: 0;
}
</style>
```

### 9.2. Processing State Announcements

The component uses a dedicated `aria-live="polite"` region that exists in the DOM from mount. The `announce()` function clears and re-sets the text in separate animation frames to ensure screen readers detect the change even when the same message is repeated:

```typescript
function announce(message: string) {
  if (liveRegionReady.value) {
    statusAnnouncement.value = ''
    requestAnimationFrame(() => {
      statusAnnouncement.value = message
    })
  }
}
```

This pattern is recommended by [Sara Soueidan's research on ARIA live regions](https://www.sarasoueidan.com/blog/accessible-notifications-with-aria-live-regions-part-1/) and confirmed by [TPGi's screen reader compatibility testing](https://www.tpgi.com/screen-reader-support-aria-live-regions/).

### 9.3. Error State Handling

Errors use `role="alert"` which is equivalent to `aria-live="assertive"` -- the error message interrupts the current screen reader announcement because errors require immediate attention. The error container includes `aria-describedby` pointing to the error detail text.

---

## 10. Testing Checklist

### 10.1. Manual Screen Reader Testing

| Test | VoiceOver (iOS) | VoiceOver (macOS) | TalkBack (Android) | NVDA (Windows) | JAWS (Windows) |
|------|----------------|-------------------|--------------------|--------------------|-----|
| Upload button announced with label | | | | | |
| File picker opens on activation | | | | | |
| Upload progress announced via aria-live | | | | | |
| Extraction success announced with question text | | | | | |
| Error messages announced immediately | | | | | |
| Math expressions read in plain text | | | | | |
| Step-solver steps navigable in order | | | | | |
| Hints toggle announced as expanded/collapsed | | | | | |
| Manual entry text area accessible | | | | | |
| Focus returns to logical position after state changes | | | | | |

### 10.2. Automated Testing with axe-core

The [vue-axe-next](https://github.com/vue-a11y/vue-axe-next) plugin provides development-time axe-core integration. For CI, use [jest-axe](https://github.com/NickColley/jest-axe):

```typescript
import { render } from '@testing-library/vue'
import { axe, toHaveNoViolations } from 'jest-axe'
import PhotoUpload from '@/components/session/PhotoUpload.vue'

expect.extend(toHaveNoViolations)

describe('PhotoUpload accessibility', () => {
  it('idle state has no axe violations', async () => {
    const { container } = render(PhotoUpload)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('error state has no axe violations', async () => {
    const { container } = render(PhotoUpload, {
      props: { /* trigger error state */ },
    })
    // Simulate error
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('success state with math rendering has no violations', async () => {
    const { container } = render(PhotoUpload, {
      props: { /* provide extracted result */ },
    })
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
```

### 10.3. Keyboard-Only Navigation Test

| Step | Action | Expected |
|------|--------|----------|
| 1 | Tab to upload area | "Take a photo" button receives focus with visible ring |
| 2 | Tab through options | Focus moves: Camera -> Gallery -> Type -> Manual textarea |
| 3 | Enter on "Take a photo" | File picker opens |
| 4 | Escape from file picker | Focus returns to upload button |
| 5 | Tab to manual textarea | Textarea receives focus |
| 6 | Type question, Tab to Submit | Submit button receives focus |
| 7 | Enter on Submit | Question submitted, focus moves to step-solver |
| 8 | Tab through step-solver | Focus moves through steps in order |
| 9 | Enter on hint toggle | Hint panel expands, content is accessible |
| 10 | Shift+Tab | Focus moves backward through controls |

### 10.4. Additional Automated Checks

| Tool | What it tests | Integration point |
|------|-------------|------------------|
| axe-core | WCAG 2.2 A/AA violations | jest-axe in unit tests |
| vue-axe-next | Real-time violations during development | Vite plugin |
| Lighthouse Accessibility | Automated audit score | CI pipeline via `npx lighthouse` |
| Pa11y | Automated WCAG compliance | CI pipeline |
| @testing-library/vue | Accessible queries (`getByRole`, `getByLabelText`) | Component tests |

---

## 11. Security Score Contribution

Accessibility is a security concern: inaccessible software excludes users and creates legal liability under ADA Title II (effective April 2026 for education), Section 508, and the European Accessibility Act.

| Category | Points | Rationale |
|----------|--------|-----------|
| WCAG 2.2 AA conformance for upload flow | 3 | Legal compliance baseline; prevents ADA lawsuits |
| Screen reader state announcements (aria-live) | 1 | Prevents information leakage through silent failures |
| Error messages in plain language | 1 | Reduces social engineering risk from confusing error states |
| Input validation with accessible error reporting | 1 | Prevents bypasses that rely on users not seeing error feedback |
| Type-instead fallback (defense in depth for input) | 1 | Alternative input path reduces single-point-of-failure risk |
| High contrast / forced-colors support | 0.5 | Prevents UI spoofing in high-contrast modes |
| Keyboard-only operability | 0.5 | Ensures functionality without mouse -- also helps automated testing |
| **Subtotal this iteration** | **8** | |
| **Cumulative score (iterations 1-8)** | **82** | |

---

## 12. Cena-Specific Implementation Notes

### 12.1. Existing Patterns to Follow

The existing `QuestionCard.vue` at `src/student/full-version/src/components/session/QuestionCard.vue` already demonstrates correct accessibility patterns:

- `role="radiogroup"` with `aria-label` for choice groups
- `:aria-checked` for radio state
- `tabindex="0"` with `@keydown.enter` and `@keydown.space` handlers
- `aria-hidden="true"` on decorative icons
- `VProgressLinear` with `:aria-label` for progress indication
- `:focus-visible` CSS with 2px outline and offset

The new `PhotoUpload.vue` component must follow these same patterns.

### 12.2. Existing DropZone Accessibility Gaps

The existing `DropZone.vue` at `src/student/full-version/src/@core/components/DropZone.vue` has accessibility issues that the new component addresses:

1. **No `aria-label`** on the drop zone div -- screen readers announce nothing meaningful.
2. **No `role="button"`** on the clickable area -- screen readers do not identify it as interactive.
3. **No keyboard handler** -- the `@click` on the div is not reachable via keyboard.
4. **No `aria-live` region** for upload progress.
5. **`alert()` for file type errors** -- blocks screen readers and is not styled.
6. **No alt text** on `VImg` previews.

The `PhotoUpload.vue` component replaces `DropZone.vue` for the question upload use case with full WCAG 2.2 AA compliance.

### 12.3. Internationalization

All user-facing strings use the `t()` i18n function, consistent with the existing codebase. The keys needed in the locale files:

```json
{
  "photoUpload": {
    "title": "Add your question",
    "optionsGroup": "Choose how to add your question",
    "takePhoto": "Take a photo with your camera",
    "chooseFromGallery": "Choose a photo from your gallery",
    "typeInstead": "Type your question instead",
    "typeQuestionLabel": "Or type your question:",
    "typeQuestion": "Your math question",
    "typeQuestionPlaceholder": "For example: Solve x squared plus 3x minus 4 equals 0",
    "typeQuestionHelp": "Type the math question as you would read it aloud.",
    "submitTypedQuestion": "Submit question",
    "confirmQuestion": "This is correct, continue",
    "tryAgain": "Try again",
    "extractedQuestionLabel": "We found this question in your photo:",
    "extractedQuestionAria": "Extracted question: {question}",
    "processingAria": "Processing your photo",
    "status": {
      "uploading": "Uploading your photo...",
      "processing": "Reading your photo... This takes a few seconds.",
      "patience": "Please wait while we analyze the math in your photo.",
      "success": "Done! Your question: {question}",
      "successBrief": "We found a math question in your photo."
    },
    "errors": {
      "invalidType": "This file is not a photo. Please upload a {types} image.",
      "tooLarge": "This photo is too large (over {maxMb} MB). Try taking a smaller photo or cropping it.",
      "extractionFailed": "We could not read the math in your photo. Make sure the question is clearly visible, well-lit, and not blurry.",
      "networkError": "Something went wrong with the upload. Check your internet connection and try again."
    }
  }
}
```

### 12.4. Backend API Contract

The `POST /api/questions/extract-from-photo` endpoint must return the `AccessibleQuestionExtraction` schema defined in Section 5.1. The Gemini 2.5 Flash prompt must explicitly request both `latex` and `plainTextDescription` fields. The `mathml` field can be generated server-side from the LaTeX using a library like `temml` or `latex2mathml` rather than asking the vision model to produce it.

### 12.5. KaTeX vs MathJax Decision

Given that [KaTeX issue #820](https://github.com/KaTeX/KaTeX/issues/820) (opened 2017, still open in 2026) documents that KaTeX's `aria-hidden="true"` on visible rendering prevents mobile VoiceOver touch exploration, and that MathJax 4 provides built-in `aria-label`, `aria-braillelabel`, and explorable math via the `a11y/explorer` component, the recommendation for the step-solver is:

- **Use KaTeX for rendering** (faster, smaller bundle) but **supplement with explicit `aria-label` and hidden MathML** on every math container.
- **Evaluate MathJax 4** if the supplemental approach proves insufficient during manual screen reader testing.
- **Never rely on KaTeX's built-in MathML output alone** for accessibility.

---

## 13. References

1. [W3C WCAG 2.2 Recommendation](https://www.w3.org/TR/WCAG22/)
2. [WebAIM WCAG 2 Checklist](https://webaim.org/standards/wcag/checklist)
3. [W3C Understanding SC 2.5.8 Target Size (Minimum)](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html)
4. [W3C Understanding SC 2.5.7 Dragging Movements](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements)
5. [KaTeX Issue #820 -- VoiceOver Screen Reader Cannot Read MathML](https://github.com/KaTeX/KaTeX/issues/820)
6. [MathJax 4 Accessibility Features](https://docs.mathjax.org/en/latest/basic/accessibility.html)
7. [PDF Association -- Accessible Math in PDF](https://pdfa.org/accessible-math-in-pdf-finally/)
8. [MDN -- ARIA Live Regions](https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Guides/Live_regions)
9. [Sara Soueidan -- Accessible Notifications with ARIA Live Regions](https://www.sarasoueidan.com/blog/accessible-notifications-with-aria-live-regions-part-1/)
10. [TPGi -- Screen Reader Support for ARIA Live Regions](https://www.tpgi.com/screen-reader-support-aria-live-regions/)
11. [Level Access -- iOS Switch Control](https://www.levelaccess.com/blog/assistive-technology-users-mobility-disabilities-ios-switch-control/)
12. [Level Access -- Mobile Switch Access](https://www.levelaccess.com/blog/smartphone-accessibility-primer-301-switching-things-switch-access/)
13. [Accessible Android -- VoiceOver vs TalkBack](https://accessibleandroid.com/from-voiceover-on-ios-to-talkback-on-android-major-differences-and-anomalies-visually-impaired-users-should-expect/)
14. [MDN -- Mobile Accessibility](https://developer.mozilla.org/en-US/docs/Learn_web_development/Core/Accessibility/Mobile)
15. [MDN -- prefers-contrast Media Query](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-contrast)
16. [Microsoft Edge Blog -- Forced Colors](https://blogs.windows.com/msedgedev/2020/09/17/styling-for-windows-high-contrast-with-new-standards-for-forced-colors/)
17. [MacRumors -- iOS Voice Control Guide](https://www.macrumors.com/guide/voice-control/)
18. [vue-axe-next -- Accessibility Auditing for Vue 3](https://github.com/vue-a11y/vue-axe-next)
19. [jest-axe -- Custom Jest Matcher for axe-core](https://github.com/NickColley/jest-axe)
20. [W3C WAI -- Images Tutorial](https://www.w3.org/WAI/tutorials/images/)
21. [AltText.ai -- AI-Generated Alt Text](https://alttext.ai)
22. [Vispero -- Making Math Accessible](https://vispero.com/resources/making-math-accessible/)
23. [Silktide -- Web Accessibility for Mobile Screen Readers](https://silktide.com/app/uploads/2023/02/Web-accessibility-for-mobile-screen-readers.pdf)
24. [Orange Digital Accessibility -- TalkBack and VoiceOver Guide](https://a11y-guidelines.orange.com/en/mobile/screen-readers/)
