# RDY-033: Error Pattern Matching Infrastructure

- **Priority**: Critical — prerequisite for RDY-014 (Misconception Detection)
- **Complexity**: Senior engineer + educational research
- **Source**: Adversarial review — Rami
- **Tier**: 1
- **Effort**: 2-3 weeks

## Problem

RDY-014 (Misconception Detection) assumes error pattern matching exists, but it doesn't. The task says "extract the error pattern" without specifying HOW. MisconceptionCatalog has 15 buggy rules, but no matching algorithm connects wrong answers to these rules.

This is a research task disguised as engineering. Pattern matching for mathematical misconceptions requires:
- Symbolic math comparison (is the student's answer structurally equivalent to a known buggy transformation?)
- Error type classification (sign error? distribution error? order-of-operations error?)
- Confidence scoring (how likely is this misconception vs. random error?)

## Scope

### 1. Research phase (3-5 days)

- Review ASSISTments pattern matching literature (Koedinger et al.)
- Evaluate SymPy-based symbolic matching vs. rule-based regex
- Document chosen approach in ADR

### 2. Pattern matching engine

- For each buggy rule in MisconceptionCatalog, implement a matcher:
  - Input: student answer (SymPy expression) + correct answer (SymPy expression)
  - Output: { matched_rule_id, confidence, student_transformation }
- Start with 5 highest-frequency misconceptions:
  1. DIST-EXP-SUM: (a+b)^n → a^n + b^n
  2. CANCEL-COMMON: (a+b)/a → b
  3. SIGN-NEGATIVE: -(a+b) → -a+b
  4. ORDER-OPS: 2+3*4 → 20
  5. FRACTION-ADD: a/b + c/d → (a+c)/(b+d)

### 3. SymPy integration

- Use SymPy to parse both student and correct answers
- Apply known buggy transformations to correct answer
- Check if student answer matches any buggy transformation output
- Confidence = 1.0 if exact symbolic match, 0.0-0.9 for partial matches

### 4. Fallback for unmatched errors

- If no buggy rule matches: classify as "unrecognized error"
- Log unmatched errors for future rule development
- Do NOT assume a misconception when confidence is low

## Files to Create/Modify

- New: `src/actors/Cena.Actors/Services/ErrorPatternMatcher.cs`
- New: `src/actors/Cena.Actors/Services/BuggyRuleMatchers/` — one matcher per rule
- `src/actors/Cena.Actors/Services/ErrorClassificationService.cs` — integrate matcher
- New: `tests/Cena.Actors.Tests/Services/ErrorPatternMatcherTests.cs`
- New: `docs/adr/XXXX-error-pattern-matching-approach.md`

## Acceptance Criteria

- [ ] ADR documents chosen matching approach (symbolic vs. rule-based)
- [ ] 5 highest-frequency buggy rules have working matchers
- [ ] SymPy symbolic comparison produces correct match/no-match for test cases
- [ ] Confidence scoring distinguishes exact match from partial
- [ ] Unmatched errors logged (not silently dropped)
- [ ] Unit tests cover all 5 rules with positive and negative examples
- [ ] Performance: matching completes in < 100ms per answer

> **Dependency**: Must be completed before RDY-014 can begin misconception pipeline wiring.
