# QLT-001d: Type QuestionRow in Question List Page

**Priority:** P1
**File:** `src/admin/full-version/src/pages/apps/questions/list/index.vue`
**Errors:** 19 × TS18046, 1 × TS7006, 1 × TS2345

## Problem
`useApi<any>()` returns untyped data → VDataTableServer slots receive `item` as `unknown` → 19 errors cascade.
Also: `v` param in `:rules="[v => ...]"` is implicitly `any`, and `resolveQualityColor(number)` gets `number|null`.

## Fix

### Step 1: Add interface (after `definePage`)
```ts
interface QuestionRow {
  id: string
  stem: string
  subject: string
  concepts: string[]
  bloomLevel: number
  difficulty: string
  status: string
  qualityScore: number | null
  usageCount: number
  successRate: number | null
}
```

### Step 2: Type the API call (~line 57)
```ts
const { data: questionsData, execute: fetchQuestions } = await useApi<{
  questions: QuestionRow[]
  total?: number
  totalQuestions?: number
}>(createUrl('/admin/questions', { ... }))
```

### Step 3: Type the computed (~line 73)
```ts
const questions = computed<QuestionRow[]>(() => questionsData.value?.questions ?? [])
```

### Step 4: Fix `v` param (~line 1218)
```ts
:rules="[(v: string) => !!v || 'Stem is required']"
```

### Step 5: Fix resolveQualityColor (~line 167)
```ts
const resolveQualityColor = (score: number | null) => {
  if (score == null) return 'disabled'
  // ... rest unchanged
}
```

## Verify
```bash
npx vue-tsc --noEmit 2>&1 | grep "questions/list"
# Should return nothing
```
