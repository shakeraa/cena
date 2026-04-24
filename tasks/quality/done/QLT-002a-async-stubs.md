# QLT-002a: Fix CS1998 async-without-await in Admin API Stubs

**Priority:** P2
**Errors:** 46 × CS1998
**Files:**
- `src/api/Cena.Admin.Api/EventStreamService.cs` (9 methods)
- `src/api/Cena.Admin.Api/FocusAnalyticsService.cs` (3 methods)
- `src/api/Cena.Admin.Api/MasteryTrackingService.cs` (4 methods)
- `src/api/Cena.Admin.Api/OutreachEngagementService.cs` (4 methods)
- `src/api/Cena.Admin.Api/CulturalContextService.cs` (2 methods)
- `src/api/Cena.Admin.Api/IngestionPipelineService.cs` (1 method)
- `src/api/Cena.Admin.Api/SystemMonitoringService.cs` (1 method)

## Problem
Methods declared `async Task<T>` but contain no `await` — they return stub/mock data synchronously.

## Fix Options
**Option A (preferred):** Remove `async` keyword, wrap return in `Task.FromResult()`:
```csharp
// Before
public async Task<FooDto> GetFoo() { return new FooDto(); }
// After
public Task<FooDto> GetFoo() { return Task.FromResult(new FooDto()); }
```

**Option B (if method will get real async calls soon):** Add `await Task.CompletedTask` at top:
```csharp
public async Task<FooDto> GetFoo() { await Task.CompletedTask; return new FooDto(); }
```

## Verify
```bash
dotnet build src/actors/Cena.Actors.sln --no-incremental 2>&1 | grep "warning CS1998" | wc -l
# Should return 0
```
