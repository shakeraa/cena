# QLT-002: Fix .NET Build Warnings

**Priority:** P2 — code quality, prevent real bugs hiding behind warning noise
**Estimated effort:** 2 hours
**Test:** `scripts/tests/test-dotnet-warnings.sh`

## Warnings (76 total)

### CS1998: async without await (36 warnings)
- **Files:** EventStreamService.cs, FocusAnalyticsService.cs, MasteryTrackingService.cs, OutreachEngagementService.cs, CulturalContextService.cs, IngestionPipelineService.cs, SystemMonitoringService.cs
- **Fix:** Remove `async` or add `await Task.CompletedTask` for stub methods

### CS8625: null to non-nullable (5 warnings)
- **File:** MessagingNatsPublisherTests.cs lines 35, 50, 62, 69, 88
- **Fix:** Use `null!` (null-forgiving) for intentional null-testing

### CS4014: unawaited async call (4 warnings)
- **File:** DeduplicationService.cs lines 130, 131, 135, 136
- **Fix:** Add `await` or assign to `_ =` with comment if fire-and-forget is intentional

### CS0649/CS0414/CS0169: unused fields (3 warnings)
- StudentActor.cs: `_streamExists` assigned but never read, `_redisCbPid` never assigned
- LearningSessionActor.cs: `_lastHintRequestedThenCancelled` never assigned, `_minutesSinceLastBreak` never used
- **Fix:** Remove dead fields or implement usage

### CS8602: null dereference (1 warning)
- **File:** LlmClientRouter.cs line 24
- **Fix:** Add null check or `!` assertion

### CS8604: null argument (1 warning)
- **File:** MasteryEndpoints.cs line 187
- **Fix:** Add null guard before passing to `RequestAsync`

## Acceptance
- `dotnet build --no-incremental 2>&1 | grep "warning CS" | wc -l` returns 0
