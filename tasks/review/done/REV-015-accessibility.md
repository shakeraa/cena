# REV-015: WCAG 2.1 AA Accessibility Baseline for Admin Dashboard

**Priority:** P2 -- MEDIUM (zero WCAG testing, no screen reader support, no math accessibility)
**Blocked by:** None
**Blocks:** Inclusive deployment, Israeli Equal Rights for People with Disabilities Law compliance
**Estimated effort:** 5 days
**Source:** System Review 2026-03-28 -- Pedagogy Professor 1 (Accessibility section, rated D+), Frontend Senior (Issue #9)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The admin dashboard has near-zero accessibility implementation beyond what Vuetify provides by default. Only 1 Cena-authored page uses `aria-` attributes. No custom keyboard navigation. No math content accessibility (MathML/aria-math). No high-contrast mode. No IEP accommodation profiles.

For an education platform, accessibility is both a legal requirement (Israeli Equal Rights for People with Disabilities Law, WCAG as referenced standard) and a pedagogical imperative (teachers and moderators with disabilities must be able to use the admin interface).

## Architect's Decision

This task focuses on the **admin dashboard** (teacher/moderator interface), not the student-facing experience (which requires a separate, larger effort with MathJax and accommodations modeling).

Use **Vuetify's built-in a11y** as the foundation and add targeted enhancements for Cena-specific components. Do not attempt to retrofit accessibility into Vuexy demo pages -- those should be deleted (REV-017).

## Subtasks

### REV-015.1: Audit & Fix ARIA Attributes on Cena Pages

**Files to audit and modify (all pages under `src/pages/apps/`):**
- Data table rows with `@click:row` need `role="button"` and `tabindex="0"`
- MCM graph interactive cells need `role="gridcell"` with `aria-label`
- Status chips need text labels (not color-only indicators)
- Chart components need `aria-label` describing the data trend
- Notification items need `role="listitem"` with descriptive `aria-label`

**Pattern for clickable table rows:**
```vue
<VDataTableServer
  ...
  @click:row="(_, { item }) => navigateToDetail(item)"
  :row-props="{ role: 'link', tabindex: 0, 'aria-label': (item) => `View ${item.name}` }"
/>
```

**Acceptance:**
- [ ] All interactive elements have appropriate ARIA roles
- [ ] All data visualizations have descriptive aria-labels
- [ ] No color-only status indicators (all have text labels)
- [ ] `axe-core` audit on main admin pages returns zero critical violations

### REV-015.2: Add Keyboard Navigation to Custom Components

**Files to modify:**
- `src/pages/apps/pedagogy/mcm-graph.vue` -- arrow keys navigate cells, Enter edits
- `src/layouts/components/NavBarNotifications.vue` -- arrow keys navigate items, Enter opens
- `src/pages/apps/questions/list/index.vue` -- Enter on row opens detail

**Pattern for MCM grid:**
```vue
<td
  v-for="(col, colIdx) in columns"
  :key="col"
  tabindex="0"
  role="gridcell"
  :aria-label="`${row} to ${col}: confidence ${getValue(row, col)}`"
  @keydown.enter="startEdit(row, col)"
  @keydown.arrow-right="focusCell(rowIdx, colIdx + 1)"
  @keydown.arrow-down="focusCell(rowIdx + 1, colIdx)"
/>
```

**Acceptance:**
- [ ] MCM graph is fully navigable via keyboard (arrows + Enter)
- [ ] Notification dropdown navigable via arrow keys
- [ ] Focus trap works correctly in modal dialogs
- [ ] Tab order follows logical document flow on all Cena pages

### REV-015.3: Add Skip Links and Landmark Roles

**File to modify:** `src/layouts/default.vue` or `DefaultLayoutWithVerticalNav.vue`

```vue
<!-- Skip navigation for screen readers -->
<a href="#main-content" class="skip-link">Skip to main content</a>

<nav role="navigation" aria-label="Main navigation">
  <!-- sidebar nav -->
</nav>

<main id="main-content" role="main">
  <RouterView />
</main>

<footer role="contentinfo">
  <Footer />
</footer>
```

```css
.skip-link {
  position: absolute;
  top: -40px;
  left: 0;
  z-index: 9999;
  padding: 8px 16px;
  background: #1e1e1e;
  color: white;
}
.skip-link:focus {
  top: 0;
}
```

**Acceptance:**
- [ ] Skip link visible on Tab from page load
- [ ] Skip link jumps focus to main content area
- [ ] All major layout sections have landmark roles
- [ ] Screen reader announces page structure correctly

### REV-015.4: Add axe-core CI Check

**File to modify:** `.github/workflows/frontend.yml` (created in REV-009)

Add an accessibility audit step using `@axe-core/cli` or `pa11y-ci`:

```yaml
- name: Accessibility audit
  run: |
    npm install -g @axe-core/cli
    npx serve -s dist -l 3000 &
    sleep 3
    axe http://localhost:3000/login --exit
```

**Acceptance:**
- [ ] axe-core runs on CI for at least the login page and main dashboard
- [ ] Zero critical or serious violations
- [ ] Moderate violations logged as warnings (not blocking initially)
