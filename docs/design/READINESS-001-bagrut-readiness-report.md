# READINESS-001: Bagrut Readiness Report with Confidence Intervals

## Design

### Purpose
Give students and parents a data-backed assessment of exam readiness using IRT theta estimates with confidence intervals.

### Report structure

```
┌──────────────────────────────────────────────┐
│  Bagrut 806 Readiness Report                 │
│  Student: [anonymous display name]           │
│  Date: 2026-04-13                            │
│                                              │
│  Overall readiness: 72% (CI: 65%–79%)        │
│  ████████████████████░░░░░░░░ 72%            │
│                                              │
│  By topic:                                   │
│  Algebra basics      ████████████████ 95%    │
│  Quadratic equations ██████████████░░ 82%    │
│  Trigonometry        ██████████░░░░░░ 61%    │
│  Calculus limits     ████████░░░░░░░░ 48%    │
│  Calculus integrals  ██████░░░░░░░░░░ 35%    │
│                                              │
│  Recommendation:                             │
│  Focus on: Calculus integrals, Trig identities│
│  You're strong in: Algebra, Quadratics        │
│                                              │
│  Confidence: Based on 142 questions answered  │
│  More questions = narrower confidence interval│
└──────────────────────────────────────────────┘
```

### Confidence interval computation

Using IRT theta (ability estimate):
- SE(theta) = 1 / √(test information at theta)
- 95% CI = theta ± 1.96 × SE(theta)
- Convert theta to % readiness: P(pass) = logistic(theta - exam_difficulty)

### Data requirements

- IRT-001: Item parameters (discrimination, difficulty) per question
- BKT-PLUS-001: Per-skill mastery with forgetting
- Track-specific exam difficulty threshold (calibrated from historical Bagrut pass rates)

### Privacy constraints

- Report shows only the student's own data
- No comparison to other students (ship-gate: no public ranking)
- Exportable as PDF for parent sharing
- Session-scoped misconception data excluded per ADR-0003
