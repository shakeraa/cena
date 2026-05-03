# REV-006: Sanitize v-html Content & Fix Frontend Auth Bugs

**Priority:** P1 -- HIGH (stored XSS + two auth bugs affecting production behavior)
**Blocked by:** None
**Blocks:** None
**Estimated effort:** 1 day
**Source:** System Review 2026-03-28 -- Cyber Officer 1 (Findings 5, 8), Frontend Senior (Issues 1, 2, 3)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Three distinct frontend issues create a combined attack surface:

1. **Stored XSS**: Question stems rendered via `v-html` in 3+ components. A malicious content author can inject `<script>` or `<img onerror=>` that executes in every admin browser.
2. **Firebase logout bypass**: `UserProfile.vue` clears cookies but never calls `signOut(firebaseAuth)`. Firebase session persists in IndexedDB; user auto-re-logins on refresh.
3. **Divergent RBAC**: `mapRoleToAbilities` is duplicated in `guards.ts` and `useFirebaseAuth.ts` with different permission sets. ADMIN users lose Tutoring/Settings on page refresh.

## Architect's Decision

- **XSS**: Use DOMPurify client-side. Do NOT create a custom sanitizer. DOMPurify is the industry standard with 40M+ weekly downloads and handles all known bypass vectors.
- **Logout**: Call `useFirebaseAuth().logout()` instead of manual cookie clearing. The composable already handles Firebase signOut.
- **RBAC**: Extract `mapRoleToAbilities` to a single module. Both consumers import from there. Zero tolerance for duplicated authorization logic.

## Subtasks

### REV-006.1: Add DOMPurify for v-html Sanitization

**Package to install:**
```bash
cd src/admin/full-version && npm install dompurify && npm install -D @types/dompurify
```

**Create utility:** `src/admin/full-version/src/utils/sanitize.ts`
```typescript
import DOMPurify from 'dompurify'

const purify = DOMPurify(window)

// Allow only safe HTML subset for educational content
purify.setConfig({
  ALLOWED_TAGS: ['b', 'i', 'em', 'strong', 'p', 'br', 'ul', 'ol', 'li',
    'span', 'div', 'sub', 'sup', 'table', 'tr', 'td', 'th', 'thead', 'tbody',
    'img', 'code', 'pre', 'blockquote', 'h1', 'h2', 'h3', 'h4'],
  ALLOWED_ATTR: ['class', 'style', 'src', 'alt', 'width', 'height', 'dir'],
  ALLOW_DATA_ATTR: false,
})

export function sanitizeHtml(dirty: string): string {
  return purify.sanitize(dirty)
}
```

**Files to modify (replace raw v-html with sanitized):**
- `src/pages/apps/questions/edit/[id].vue` -- lines 277, 300
- `src/pages/apps/questions/list/index.vue` -- line 782
- `src/pages/apps/moderation/review/[id].vue` -- lines 309-312

**Pattern:**
```vue
<!-- BEFORE -->
<div v-html="item.questionText" />

<!-- AFTER -->
<div v-html="sanitizeHtml(item.questionText)" />

<script setup>
import { sanitizeHtml } from '@/utils/sanitize'
</script>
```

**Acceptance:**
- [ ] `<script>alert(1)</script>` in question stem is stripped (renders empty)
- [ ] `<img src=x onerror=alert(1)>` has `onerror` attribute removed
- [ ] Legitimate HTML (bold, lists, math formatting) renders correctly
- [ ] All `v-html` usages in Cena-authored pages use `sanitizeHtml`

### REV-006.2: Fix Firebase Logout Bug

**File to modify:** `src/admin/full-version/src/layouts/components/UserProfile.vue`

```typescript
// BEFORE (lines 10-26): manual cookie clearing, no Firebase signOut
const logout = () => {
  useCookie('userData').value = null
  useCookie('accessToken').value = null
  useCookie('userAbilityRules').value = null
  ability.update([])
  router.push('/login')
}

// AFTER: delegate to useFirebaseAuth which properly calls signOut
import { useFirebaseAuth } from '@/composables/useFirebaseAuth'

const { logout } = useFirebaseAuth()
```

**Acceptance:**
- [ ] Clicking logout calls `signOut(firebaseAuth)` (verified in Firebase Auth emulator or network tab)
- [ ] After logout, refreshing the page redirects to `/login` (no auto-re-login)
- [ ] Cookies, CASL abilities, and Firebase IndexedDB state are all cleared

### REV-006.3: Unify mapRoleToAbilities

**File to create:** `src/admin/full-version/src/plugins/casl/role-abilities.ts`

```typescript
import type { Actions, Subjects } from '@/plugins/casl/AppAbility'

interface AbilityRule {
  action: Actions
  subject: Subjects
}

export function mapRoleToAbilities(role: string): AbilityRule[] {
  switch (role) {
    case 'SUPER_ADMIN':
      return [{ action: 'manage', subject: 'all' }]
    case 'ADMIN':
      return [
        { action: 'manage', subject: 'Dashboard' },
        { action: 'manage', subject: 'Users' },
        { action: 'manage', subject: 'Questions' },
        { action: 'manage', subject: 'Moderation' },
        { action: 'manage', subject: 'System' },
        { action: 'manage', subject: 'Pedagogy' },
        { action: 'manage', subject: 'Tutoring' },    // Was missing in guards.ts
        { action: 'manage', subject: 'Settings' },    // Was missing in guards.ts
        { action: 'read', subject: 'Analytics' },
      ]
    case 'MODERATOR':
      return [
        { action: 'read', subject: 'Dashboard' },
        { action: 'manage', subject: 'Questions' },
        { action: 'manage', subject: 'Moderation' },
        { action: 'read', subject: 'Pedagogy' },      // Was missing in guards.ts
        { action: 'read', subject: 'Tutoring' },      // Was missing in guards.ts
      ]
    case 'STUDENT':
      return [{ action: 'read', subject: 'Dashboard' }]
    default:
      return []
  }
}
```

**Files to modify (import from shared module):**
- `src/plugins/1.router/guards.ts` -- delete local `mapRoleToAbilities`, import from `role-abilities.ts`
- `src/composables/useFirebaseAuth.ts` -- delete local `mapRoleToAbilities`, import from `role-abilities.ts`

**Acceptance:**
- [ ] Only ONE definition of `mapRoleToAbilities` exists in the codebase
- [ ] ADMIN role has Tutoring and Settings abilities on both login AND page refresh
- [ ] MODERATOR role has Pedagogy (read) and Tutoring (read) consistently
- [ ] `grep -r "mapRoleToAbilities" src/` returns exactly 3 results: definition + 2 imports

### REV-006.4: Replace Footer Branding

**File to modify:** `src/admin/full-version/src/layouts/components/Footer.vue`

Replace Pixinvent/Vuexy/ThemeForest links with Cena branding.

**Acceptance:**
- [ ] No references to Pixinvent, ThemeForest, or Vuexy in the footer
- [ ] Footer shows Cena platform branding
