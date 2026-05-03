# QLT-002d: Remove Dead Fields in Actor Classes

**Priority:** P2
**Errors:** 8 × CS0649/CS0414/CS0169

## Fields

### StudentActor.cs
- `_streamExists` (CS0414): assigned but never read → remove
- `_redisCbPid` (CS0649): never assigned → remove or wire up

### LearningSessionActor.cs
- `_lastHintRequestedThenCancelled` (CS0649): never assigned → remove or implement
- `_minutesSinceLastBreak` (CS0169): never used → remove or implement

## Fix
Remove the fields if they're dead code. If they're placeholders for future work, add `#pragma warning disable` with a TODO comment.

## Verify
```bash
dotnet build src/actors/Cena.Actors.sln --no-incremental 2>&1 | grep -E "CS0649|CS0414|CS0169" | wc -l
# Should return 0
```
