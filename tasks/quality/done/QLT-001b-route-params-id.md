# QLT-001b: Fix route.params.id Union Type in Dynamic Pages

**Priority:** P1
**Files:**
- `src/admin/full-version/src/pages/apps/experiments/[id].vue` (line 34)
- `src/admin/full-version/src/pages/apps/tutoring/sessions/[id].vue` (line 33)
**Errors:** 2 × TS2339

## Problem
`route.params` is a union type from unplugin-vue-router. Direct `.id` access fails because `Record<never, never>` is in the union.

## Fix
Replace:
```ts
const x = computed(() => route.params.id as string)
```
With:
```ts
const x = computed(() => String((route.params as Record<string, string>).id ?? ''))
```

## Verify
```bash
npx vue-tsc --noEmit 2>&1 | grep -E "experiments|tutoring.*\[id\]"
# Should return nothing
```
