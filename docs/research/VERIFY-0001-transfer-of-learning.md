# VERIFY-0001: Transfer-of-Learning Literature Review

> **Goal**: Design review for cross-track mastery sharing (ADR-0001 Decision 2)
> **Status**: Review complete 2026-04-13
> **Verdict**: Cross-track mastery sharing is pedagogically sound with constraints

## Research findings

### Transfer of learning is real but narrow

1. **Thorndike & Woodworth (1901)** — Transfer occurs only between tasks sharing "identical elements." Math algebra → physics formula manipulation transfers; math algebra → history essay writing does not.

2. **Singley & Anderson (1989)** — ACT-R framework: transfer is proportional to shared production rules. A student mastering quadratic equations in MATH-BAGRUT-806 has transferable productions for MATH-BAGRUT-807 (same algebra skills, different application context).

3. **Bransford & Schwartz (1999)** — "Preparation for future learning": even when immediate transfer is weak, prior learning increases the *speed* of acquiring the new skill. Mastery should decay cross-track but not reset to zero.

## Design recommendation for ADR-0001 Decision 2

### Model: Seepage with decay

When a student enrolls in a second track that shares skills with their first track, their mastery state "seeps" across with a decay factor.

```
Cross-track mastery = Source mastery × seepage_factor × time_decay
```

| Factor | Value | Rationale |
|--------|-------|-----------|
| seepage_factor (same subject) | 0.60 | 60% of source mastery transfers |
| seepage_factor (cross subject) | 0.20 | Algebra → physics is weaker transfer |
| time_decay | Ebbinghaus (BKT+ half-life) | Same forgetting curve as within-track |

### Constraints

1. **Never inflate**: cross-track seepage can only *initialize* mastery, never increase it above what the student earned in the target track
2. **One-time**: seepage is applied at enrollment time. After that, mastery in each track evolves independently
3. **Auditable**: every seepage event is logged with source track, target track, and factor applied
4. **Skill mapping**: only skills that exist in both tracks can seep. The prerequisite DAG (BKT-PLUS-001) is the authoritative skill list

### Implementation path

This unblocks TENANCY-P2a (mastery state re-key per ADR-0002) with the seepage model above.
