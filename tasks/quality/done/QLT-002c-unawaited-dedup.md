# QLT-002c: Fix CS4014 Unawaited Calls in DeduplicationService

**Priority:** P1 — potential silent failures
**Errors:** 8 × CS4014
**File:** `src/actors/Cena.Actors/Ingest/DeduplicationService.cs` (lines 130-136)

## Problem
Async calls are not awaited. If they fail, exceptions are swallowed silently.

## Fix
**If fire-and-forget is intentional:**
```csharp
_ = SomeAsyncMethod(); // intentional fire-and-forget
```

**If they should be awaited (likely):**
```csharp
await SomeAsyncMethod();
```

## Verify
```bash
dotnet build src/actors/Cena.Actors.sln --no-incremental 2>&1 | grep "CS4014" | wc -l
# Should return 0
```
