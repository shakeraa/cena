# ADM-015: Navigation Structure & Cena Branding

**Priority:** P0 — foundational UI setup
**Blocked by:** None
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Replace Vuexy's default demo navigation, branding, and theme with Cena-specific structure. This is the foundational UI task that all other admin pages depend on for navigation.

## Subtasks

### ADM-015.1: Cena Branding

**Files to modify:**

- `src/admin/full-version/themeConfig.ts` — app title, colors, logo
- `src/admin/full-version/src/assets/images/` — Cena logo files
- `src/admin/full-version/src/plugins/vuetify/theme.ts` — Cena color palette

**Acceptance:**

- [ ] App title: "Cena Admin"
- [ ] Cena logo in sidebar header and login page
- [ ] Primary color matches Cena brand
- [ ] Dark mode variant configured
- [ ] Favicon updated

### ADM-015.2: Navigation Structure

**Files to modify:**

- `src/admin/full-version/src/navigation/vertical/index.ts` — replace all sections
- `src/admin/full-version/src/navigation/vertical/dashboard.ts` — Cena dashboards
- `src/admin/full-version/src/navigation/vertical/apps-and-pages.ts` — Cena apps

**Navigation Sections:**

```
DASHBOARDS
  - Platform Overview          (ADM-004)
  - Focus & Attention           (ADM-006)
  - Mastery Progress            (ADM-007)
  - Outreach & Engagement       (ADM-014)

CONTENT
  - Ingestion Pipeline          (ADM-009)
  - Moderation Queue            (ADM-005)
  - Question Bank               (ADM-010)

USERS
  - All Users                   (ADM-002)
  - Roles & Permissions         (ADM-003)

PEDAGOGY
  - Methodology Analytics       (ADM-011)
  - Cultural Context            (ADM-012)

SYSTEM (Super Admin only)
  - Actor Health                (ADM-008)
  - Event Stream                (ADM-013)
  - Settings                    (ADM-008)
  - Audit Log                   (ADM-008)
```

**Acceptance:**

- [ ] All demo navigation items removed
- [ ] Navigation sections match the list above
- [ ] CASL-based visibility: sections hidden per role
- [ ] SYSTEM section visible only to SUPER_ADMIN
- [ ] CONTENT section visible to MODERATOR and above
- [ ] Active route highlighted in nav
- [ ] Collapsible sections

### ADM-015.3: Remove Demo Pages

**Acceptance:**

- [ ] Remove or hide all Vuexy demo pages not used by Cena (e-commerce, logistics, invoice, etc.)
- [ ] Keep component library pages available in dev mode only (for reference)
- [ ] 404 page customized with Cena branding
- [ ] Default redirect after login goes to Platform Overview dashboard

### ADM-015.4: i18n Setup for Admin

**Files to modify:**

- `src/admin/full-version/src/plugins/i18n/` — Cena-specific translations

**Acceptance:**

- [ ] Admin UI supports English, Hebrew, Arabic
- [ ] All navigation labels translated
- [ ] RTL layout works correctly for Hebrew and Arabic
- [ ] Language switcher in navbar
- [ ] Admin's locale preference persisted (Firebase `locale` claim or local storage)

## Test

- [ ] Navigation renders all Cena sections correctly
- [ ] Demo pages no longer accessible via routes
- [ ] MODERATOR sees only Content + limited Dashboards
- [ ] SUPER_ADMIN sees everything including System
- [ ] RTL layout correct in Hebrew and Arabic
- [ ] Dark mode works across all Cena-branded pages
- [ ] Logo and colors consistent across login, sidebar, and navbar
