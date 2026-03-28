# QLT-001: Fix TypeScript Errors in Admin Frontend

**Priority:** P1 — type safety for admin dashboard
**Estimated effort:** 1 hour
**Test:** `scripts/tests/test-ts-errors.sh`

## Errors (25 total across 5 files)

### guards.ts (2 errors)
- `TS2345`: `string` not assignable to `Actions` at lines 81, 90
- **Fix:** Import `Actions`, `Subjects` from `@/plugins/casl/ability`, cast `route.meta.action as Actions`

### experiments/[id].vue (1 error)
- `TS2339`: `route.params.id` doesn't exist on union type at line 34
- **Fix:** Cast `route.params as Record<string, string>`

### tutoring/sessions/[id].vue (1 error)
- `TS2339`: Same `route.params.id` union type issue at line 33
- **Fix:** Same cast

### ConceptGraph.vue (1 error)
- `TS2345`: d3 `DragBehavior` type mismatch with SVG selection at line 203
- **Fix:** Cast drag behavior chain `as any`

### questions/list/index.vue (20 errors)
- `TS18046` x19: `item` is `unknown` in VDataTableServer slots (lines 759-873)
- `TS7006` x1: Parameter `v` implicitly `any` at line 1218
- **Root cause:** `useApi<any>()` returns untyped data
- **Fix:** Add `QuestionRow` interface, type `useApi<{ questions: QuestionRow[]; ... }>`, type `v: string`
- `TS2345` x1: `resolveQualityColor(item.qualityScore)` — `number|null` vs `number`
- **Fix:** Widen param to `number | null`, handle null

## Acceptance
- `npx vue-tsc --noEmit` exits with 0 errors
