# QLT-002b: Fix CS8625 Null Literals in NATS Publisher Tests

**Priority:** P2
**Errors:** 10 × CS8625
**File:** `src/actors/Cena.Actors.Tests/Messaging/MessagingNatsPublisherTests.cs`

## Problem
Tests intentionally pass `null` to non-nullable params to verify null-guard behavior, but the compiler warns.

## Fix
Use null-forgiving operator `null!` for intentional null-testing:
```csharp
// Before
var sut = new NatsPublisher(null, logger);
// After
var sut = new NatsPublisher(null!, logger);
```

## Verify
```bash
dotnet build src/actors/Cena.Actors.sln --no-incremental 2>&1 | grep "CS8625" | wc -l
# Should return 0
```
