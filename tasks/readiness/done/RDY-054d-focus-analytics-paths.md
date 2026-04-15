# RDY-054d: Focus Analytics Path Resolution

- **Priority**: Medium
- **Complexity**: Low
- **Effort**: 2-3 hours

## Hypothesis

Commit `adf14d0` fixed 5→4 "ups" for most path-resolution tests but left focus-analytics variants untouched. The remaining `DirectoryNotFoundException`s in `FocusAnalyticsService*` test paths indicate stale path arithmetic.

## Scope

- Sweep `src/api/Cena.Admin.Api.Tests/Focus*Tests.cs` for hard-coded `../../../..` paths
- Normalize to `AppContext.BaseDirectory`-relative + the same 4-ups pattern used elsewhere

## Acceptance

- [ ] All focus-analytics tests green
