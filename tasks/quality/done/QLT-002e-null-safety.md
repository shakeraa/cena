# QLT-002e: Fix Null Reference Warnings in Router and Endpoints

**Priority:** P1 — potential NullReferenceException at runtime
**Errors:** 2 × CS8602, 2 × CS8604

### LlmClientRouter.cs (CS8602, line 24)
Dereferencing a possibly-null reference.
**Fix:** Add null check or use `!` if null is impossible by contract.

### MasteryEndpoints.cs (CS8604, line 187)
Passing possibly-null `message` to `RequestAsync<TResult>`.
**Fix:** Add null guard:
```csharp
if (message is null) return Results.BadRequest("Message required");
```

## Verify
```bash
dotnet build src/actors/Cena.Actors.sln --no-incremental 2>&1 | grep -E "CS8602|CS8604" | wc -l
# Should return 0
```
