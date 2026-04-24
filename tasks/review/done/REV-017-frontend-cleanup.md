# REV-017: Frontend Cleanup (Dead Vuexy Code, TypeScript any, Debounce, Unused Deps)

**Priority:** P2 -- MEDIUM (60-70% dead code, 61 `any` types, keystroke-level API calls, inflated bundle)
**Blocked by:** None
**Blocks:** None (improves maintainability and performance)
**Estimated effort:** 2 days
**Source:** System Review 2026-03-28 -- Frontend Senior (Issues 4, 5, 6, 7), Lead Architect (Dashboard section)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The Vuexy template contributes ~100+ pages, ~50+ views, and ~30+ fake-API handlers that are never used by Cena. This dead code inflates the route table, confuses developers, and increases build time. Additionally, several code quality issues need cleanup.

## Architect's Decision

- **Delete aggressively**: Remove ALL Vuexy demo pages not linked from Cena navigation. If a page is not in `apps-and-pages.ts` navigation items, it goes.
- **Keep Vuexy core**: `@core/`, `@layouts/`, Vuetify config, theme system -- these are actively used.
- **Fix TypeScript**: Replace `any` with proper types in Cena-authored code. Leave Vuexy template `any` usage alone (it's their code).
- **Add debounce**: Use `useDebounceFn` from VueUse (already a dependency).

## Subtasks

### REV-017.1: Remove Dead Vuexy Demo Pages

**Directories to delete:**
```
src/pages/apps/ecommerce/
src/pages/apps/invoice/
src/pages/apps/email.vue
src/pages/apps/chat.vue
src/pages/apps/calendar.vue
src/pages/apps/kanban/
src/pages/apps/logistics/
src/pages/apps/academy/
src/pages/apps/permissions/
src/pages/dashboards/analytics.vue
src/pages/dashboards/crm.vue
src/pages/dashboards/ecommerce.vue
src/pages/charts/
src/pages/components/
src/pages/forms/
src/pages/extensions/
src/pages/front-pages/
src/pages/pages/       (account settings, pricing, FAQ, misc demos)
src/pages/wizard-examples/
```

**Associated views to delete:**
```
src/views/dashboards/   (except admin/)
src/views/apps/         (ecommerce, invoice, email, chat, calendar, kanban, logistics, academy, permissions)
src/views/demos/
src/views/front-pages/
src/views/pages/
src/views/wizard-examples/
```

**Fake API handlers to delete:**
```
src/plugins/fake-api/handlers/apps/  (non-Cena handlers)
src/plugins/fake-api/handlers/dashboard/  (non-Cena handlers)
src/plugins/fake-api/handlers/pages/
```

**Acceptance:**
- [ ] Only Cena-specific pages remain under `src/pages/apps/`
- [ ] `npm run build` succeeds with no import errors
- [ ] Navigation menu works (no broken links)
- [ ] Route table is ~70% smaller
- [ ] File count in `src/pages/` drops by at least 60%

### REV-017.2: Fix TypeScript `any` in Cena-Authored Code

**Priority targets (Cena code only, not Vuexy template):**

```typescript
// BEFORE
useCookie<any>('userData')

// AFTER (CenaUser interface already exists in useFirebaseAuth.ts)
import type { CenaUser } from '@/composables/useFirebaseAuth'
useCookie<CenaUser | null>('userData')
```

```typescript
// BEFORE (NavBarNotifications.vue)
events.value = data.map((e: any) => ({ ... }))

// AFTER
interface NotificationEvent {
  type: string
  studentId?: string
  conceptId?: string
  timestamp: string
  details?: Record<string, unknown>
}
events.value = data.map((e: NotificationEvent) => ({ ... }))
```

**Files to fix:**
- `NavBarNotifications.vue` -- type event data
- `NavSearchBar.vue` -- type search results
- `useFirebaseAuth.ts` -- ensure CenaUser type exported
- Question bank pages -- type table options callback
- Cultural dashboard -- type API responses
- MCM graph -- replace `getCurrentInstance()!` with `useAbility()`

**Acceptance:**
- [ ] `grep -r ": any" src/pages/apps/ src/layouts/ src/composables/` returns zero matches in Cena-authored files
- [ ] `getCurrentInstance()` no longer used in Cena code
- [ ] `vue-tsc --noEmit` passes with zero errors

### REV-017.3: Add Debounce to Search Inputs

**Files to modify:**
- `src/layouts/components/NavSearchBar.vue` (line 107)
- `src/pages/apps/questions/list/index.vue` (filter watchers)
- `src/pages/apps/tutoring/sessions.vue` (student search + status filter)

**Pattern:**
```typescript
import { useDebounceFn } from '@vueuse/core'

const debouncedSearch = useDebounceFn(fetchResults, 300)
watch(searchQuery, debouncedSearch)
```

**Acceptance:**
- [ ] Search inputs wait 300ms after last keystroke before API call
- [ ] Rapid typing generates 1 API call, not N calls
- [ ] Search still works correctly (results match final query)

### REV-017.4: Reset Vite Chunk Size Warning Limit

**File to modify:** `src/admin/full-version/vite.config.ts`

```typescript
// BEFORE
chunkSizeWarningLimit: 5000  // 5MB! Silences all warnings

// AFTER (Vite default is 500KB -- use a reasonable 1MB after dead code removal)
chunkSizeWarningLimit: 1000
```

**Acceptance:**
- [ ] `npm run build` completes with no chunks > 1MB (after dead code removal)
- [ ] If chunks exceed 1MB, investigate and code-split further
