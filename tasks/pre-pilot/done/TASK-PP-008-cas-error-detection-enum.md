# PP-008: Replace CAS Error String Convention with Typed Enum

- **Priority**: Medium — reduces fragility in CAS routing logic
- **Complexity**: Senior engineer — refactor across CAS subsystem
- **Source**: Expert panel review § CAS Engine (Dina)

## Problem

`CasRouterService.VerifyAsync` in `src/actors/Cena.Actors/Cas/CasRouterService.cs:76,88` detects CAS errors by checking `ErrorMessage?.StartsWith("[ERROR]")`. This is a string prefix convention — brittle, not type-safe, and invisible to the compiler.

If a CAS engine returns an error message that happens to not start with `[ERROR]` (e.g., a SymPy Python traceback, or a timeout message), the router treats it as a successful result, potentially delivering incorrect verification to the student.

## Scope

### 1. Add CasVerifyStatus enum

```csharp
public enum CasVerifyStatus
{
    Ok,
    Error,
    Timeout,
    UnsupportedOperation,
    CircuitBreakerOpen
}
```

### 2. Add Status property to CasVerifyResult

```csharp
public record CasVerifyResult(
    CasOperation Operation,
    string Engine,
    bool Verified,
    double LatencyMs,
    CasVerifyStatus Status,  // NEW
    string? ErrorMessage
);
```

### 3. Update router logic

Replace:
```csharp
if (!mathNetResult.ErrorMessage?.StartsWith("[ERROR]", StringComparison.Ordinal) ?? true)
```

With:
```csharp
if (mathNetResult.Status == CasVerifyStatus.Ok)
```

### 4. Update all CAS implementations

- `MathNetVerifier.Verify` — return `Status = Ok` on success, `Status = Error` on failure
- `SymPySidecarClient.VerifyAsync` — return appropriate status for each failure mode
- `CasVerifyResult.Error` factory — set `Status = Error`

## Files to Modify

- `src/actors/Cena.Actors/Cas/CasContracts.cs` — add enum, add property to CasVerifyResult
- `src/actors/Cena.Actors/Cas/CasRouterService.cs` — replace string checks with enum checks
- `src/actors/Cena.Actors/Cas/MathNetVerifier.cs` — return typed status
- `src/actors/Cena.Actors/Cas/SymPySidecarClient.cs` — return typed status
- `src/actors/Cena.Actors/Cas/StepVerifierService.cs` — consume typed status if error checking exists
- `src/actors/Cena.Actors/Cas/CasLlmOutputVerifier.cs` — replace string check at line 113

## Acceptance Criteria

- [ ] `CasVerifyStatus` enum exists with at least: Ok, Error, Timeout, UnsupportedOperation, CircuitBreakerOpen
- [ ] `CasVerifyResult` has a `Status` property
- [ ] No string-based error detection remains in the CAS subsystem
- [ ] All CAS engine implementations return typed status
- [ ] Existing tests pass without behavior change
- [ ] New test verifies that an error result with `Status = Error` is correctly routed to fallback
