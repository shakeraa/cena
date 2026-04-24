# QLT-001c: Fix d3 Drag Type Mismatch in ConceptGraph

**Priority:** P1
**File:** `src/admin/full-version/src/views/apps/mastery/ConceptGraph.vue`
**Errors:** 1 × TS2345

## Problem
`d3.drag<SVGGElement, GraphNode>()` returns a `DragBehavior` type incompatible with the `Selection<BaseType | SVGGElement, ...>` from `.selectAll('g').data(nodes).join('g')`.

## Fix
Wrap the drag chain with `as any`:
```ts
.call((d3.drag() as any)
  .on('start', (event: any, d: any) => { ... })
  ...
)
```

## Verify
```bash
npx vue-tsc --noEmit 2>&1 | grep "ConceptGraph"
# Should return nothing
```
