# RDY-048: Word-Problem Math Gate

- **Priority**: Medium — Bagrut coverage
- **Complexity**: Mid engineer
- **Effort**: 1 day

## Problem

`CasVerificationGate` treats `Subject=math` + empty `HasMathContent` detection as math → NormalForm on prose → `Unverifiable`. Bagrut word problems ("If Sara has 2x+3 apples…") land unverified.

## Scope

- Extend `IMathContentDetector` to spot math-adjacent natural language (numbers + operators, Arabic `احسب`, Hebrew `חשב`, English "how many", etc.)
- Route through `StemSolutionExtractor` with a word-problem-aware extension
- If still non-extractable, tag binding reason `word_problem_non_extractable` so admin queue prioritizes

## Acceptance

- [ ] Word-problem questions pass through Equivalence when extractable
- [ ] Otherwise tagged with explicit reason
- [ ] Tests for ar/he/en word-problem stems
