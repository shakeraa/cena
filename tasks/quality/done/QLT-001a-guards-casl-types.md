# QLT-001a: Fix CASL Type Casts in Router Guards

**Priority:** P1
**File:** `src/admin/full-version/src/plugins/1.router/guards.ts`
**Errors:** 2 × TS2345

## Problem
`ability.can()` expects `Actions` and `Subjects` types, but route meta values are cast to `string`.

## Lines
- Line 81: `ability.can(targetRoute.meta.action as string, targetRoute.meta.subject as string)`
- Line 90: `ability.can(route.meta.action as string, route.meta.subject as string)`

## Fix
1. Add import: `import type { Actions, Subjects } from '@/plugins/casl/ability'`
2. Replace `as string` with `as Actions` / `as Subjects` on both lines

## Verify
```bash
npx vue-tsc --noEmit 2>&1 | grep "guards.ts"
# Should return nothing
```
