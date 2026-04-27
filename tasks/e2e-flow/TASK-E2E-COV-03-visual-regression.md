# TASK-E2E-COV-03: Visual regression baseline per route (Playwright snapshot)

**Status**: Proposed
**Priority**: P2
**Epic**: Coverage matrix
**Tag**: `@coverage @visual-regression @p2`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-X-visual-regression.spec.ts` (new) + `tests/e2e-flow/baselines/visual/` (golden images)

## Why this exists

The responsiveness sweep catches **layout** regressions (overflow, missing heading). It does not catch:

- A button color silently changing because a theme token moved
- A hero image cropping wrong on a specific viewport
- An icon disappearing because its glyph code was renamed in the icon set
- A subtle z-index regression that makes a dialog tail-clip behind a navbar

Visual regression closes the gap. The user explicitly asked for "visual" coverage — without snapshots we can only assert structural properties.

## Journey

Driven by Playwright's built-in `expect(page).toHaveScreenshot()`:

1. Sign in once per role (admin / student).
2. For each route in the smoke matrix × each viewport (mobile / tablet / desktop):
   - Navigate to the route, settle (wait 1 s after `domcontentloaded`)
   - `await expect(page).toHaveScreenshot('{route-slug}-{viewport}.png', { maxDiffPixelRatio: 0.02, animations: 'disabled', mask: [page.locator('[data-testid="dynamic-timestamp"]')] })`
3. First run creates baselines; subsequent runs compare against them.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Pixel | `maxDiffPixelRatio` ≤ 2 % per route × viewport pair |
| Stability | Mask out per-render dynamic content (timestamps, randomized greetings, real-time counters) |
| Locale | Re-run baselines for ar + he (RTL has its own goldens) |

## Regression this catches

- The mcm-graph `v-else-if="errorTypes.length === 0"` branch I just patched: structurally OK but visually different than the populated state. A snapshot would have caught the moment the empty-state ever changed.
- A theme primary-color update that wasn't fully propagated (Vuexy #7367F0 lock per `feedback_primary_color_locked.md`)
- A Vuetify minor upgrade that subtly changes button border radius
- Marketing-led copy / emoji churn on landing pages

## Done when

- [ ] `EPIC-X-visual-regression.spec.ts` covers signed-in admin + student smoke routes × 3 viewports
- [ ] Goldens stored at `tests/e2e-flow/baselines/visual/{role}/{route-slug}-{viewport}.png`
- [ ] CI auto-uploads diffs on failure (Playwright HTML report carries them by default)
- [ ] `npx playwright test --update-snapshots` recipe documented in `tests/e2e-flow/README.md`
- [ ] Tagged `@visual-regression @p2`

## Tradeoff disclosure

Visual regression has a maintenance tax: every legitimate UI change requires re-baselining. We've seen elsewhere it gets disabled the first time someone is in a hurry. Mitigations:

- Tight `maxDiffPixelRatio` (2 %) catches accidents but tolerates anti-aliasing
- Mask-list for known-flaky regions
- Run only on PRs labelled `visual` (not on every push) to keep the developer feedback loop fast
