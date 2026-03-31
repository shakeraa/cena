# MOB-049: Adaptive Interleaving Probability

**Priority:** P4.3 — High
**Phase:** 4 — Advanced Intelligence (Months 8-12)
**Source:** learning-science-srs-research.md Section 3
**Blocked by:** MST-* (Mastery Engine)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Current `ItemSelector` uses fixed 0.5 interleaving probability. Research shows novices need blocked practice first, then gradual interleaving as mastery grows (Brunmair & Richter, 2019; d=0.42 in favor of interleaving for practiced material).

## Subtasks

### MOB-049.1: Adaptive Probability Formula
- [ ] `interleavingP = min(0.7, max(0.0, (avgMastery - 0.3) * 1.17))`
- [ ] When P(known) < 0.30 (novice): P(interleave) = 0.0 (pure blocking)
- [ ] When P(known) = 0.50: P(interleave) ≈ 0.23
- [ ] When P(known) ≥ 0.90: P(interleave) = 0.70 (maximum mixing)

### MOB-049.2: ItemSelector Integration
- [ ] Replace fixed 0.5 in `ItemSelector.cs` with adaptive formula
- [ ] Interleaving computed per-concept using BKT P(known)
- [ ] When interleaving: draw from different concept within same subject
- [ ] "Surprise!" visual cue when topic switches (subtle, not alarming)

### MOB-049.3: Desirable Difficulties
- [ ] When concept is mastered (P > 0.85): increase spacing, increase interleaving
- [ ] When concept is struggling (P < 0.30): block practice, no interleaving

### MOB-049.4: Analytics
- [ ] Track interleaving benefit: delayed-test accuracy on interleaved vs blocked concepts
- [ ] KPI target: > 20% improvement from interleaving

**Definition of Done:**
- Interleaving probability adapts to mastery level per concept
- Novices get blocked practice; masters get 70% interleaving
- ItemSelector uses adaptive formula instead of fixed 0.5

**Test:**
```csharp
[Theory]
[InlineData(0.1, 0.0)]   // Novice: no interleaving
[InlineData(0.5, 0.23)]  // Intermediate: some interleaving
[InlineData(0.9, 0.70)]  // Master: max interleaving
public void AdaptiveInterleaving_ScalesWithMastery(double mastery, double expectedP)
{
    var p = AdaptiveInterleaving.Probability(mastery);
    Assert.InRange(p, expectedP - 0.05, expectedP + 0.05);
}
```
