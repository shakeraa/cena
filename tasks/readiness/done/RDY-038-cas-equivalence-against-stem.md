# RDY-038: CAS Gate — Verify Answer Equivalence Against Stem (Fix #1)

- **Priority**: **Critical / ship-blocker** — direct ADR-0002 violation
- **Complexity**: Senior engineer + CAS + question-schema familiarity
- **Source**: Senior-architect review of `claude-code/cas-gate-residuals` (2026-04-15)
- **Tier**: 1
- **Effort**: 1-2 days
- **Dependencies**: RDY-037 (merged)

## Problem

`CasVerificationGate.VerifyForCreateAsync` ([src/actors/Cena.Actors/Cas/CasVerificationGate.cs:204](../../src/actors/Cena.Actors/Cas/CasVerificationGate.cs#L204)) only issues `CasOperation.NormalForm` on the author's raw answer string. It never compares that answer against the *stem's expected solution*.

RDY-034 §3.1 required: *"For multi-choice questions, verify the marked-correct option against the question's expected answer (if present) via `CasOperation.Equivalence`."* That call site does not exist in the codebase today.

Consequence: for stem `Solve 2x + 3 = 7`, an author marking `x = 99` as correct passes the gate — NormalForm just returns `x = 99`. ADR-0002 says "no math reaches students unverified"; today the gate confirms the answer string parses, not that it's right.

All existing `CasGatingTests` substitute `ICasVerificationGate`, so none of them catches this.

## Scope

### 1. Extract the expected-solution expression from the stem

Add `IStemSolutionExtractor` in `Cena.Actors.Cas`:

- Input: stem text + subject + language
- Output: `{ Expression: string?, Operation: CasOperation, Variable: string? }` or `null` when the stem does not express a tractable equation

Implementation:
- Regex-pull equation patterns (`<expr> = <expr>`, `solve …`, `evaluate …`, `find x …`, Arabic/Hebrew equivalents)
- Delegate multi-language stems to `IGlossaryNormalizer` first
- Return `null` (→ gate falls back to current NormalForm parseability check) when no equation can be confidently extracted

### 2. Extend `CasVerificationGate`

When the extractor returns non-null:
- Call `_casRouter.VerifyAsync(new CasVerifyRequest(Operation.SolveLinear | SolveQuadratic, stemEquation, ...))` to compute the stem's own canonical solution
- Call `_casRouter.VerifyAsync(new CasVerifyRequest(Operation.Equivalence, authorAnswer, stemSolution, variable))` to confirm the author's answer matches
- Outcome mapping: `Verified` → binding `Verified`; `!Verified` → binding `Failed` (was previously Verified-on-parseable — wrong)

When the extractor returns null:
- Fall back to the existing NormalForm parseability path
- Binding status becomes `Unverifiable` (not `Verified`) — preserves the "we couldn't check" signal

### 3. Two-answer multi-choice handling

For MCQ questions (options with `IsCorrect=true`):
- Run Equivalence against the stem solution for the marked-correct option
- Additionally verify that *no other option* is CAS-equivalent to the stem solution (otherwise the question has >1 correct answer → reject with `DUPLICATE_CORRECT_OPTIONS`)

### 4. Tests (real gate, not substituted)

New `CasVerificationGateIntegrationTests`:
- Fixture: seeded SymPy sidecar + MathNet
- `Stem_SolveLinear_WrongAnswer_IsRejected` — stem `2x+3=7`, author `x=99` → `Failed`
- `Stem_SolveLinear_CorrectAnswer_IsVerified` — stem `2x+3=7`, author `x=2` → `Verified`
- `Stem_WithMultipleCorrectOptions_IsRejected`
- `Stem_WithNoTractableEquation_FallsBackToParseability`

### 5. Update ADR-0032

Add §17 documenting the stem-equivalence contract and the explicit fallback when the stem is non-extractable (word problem prose, proof-style question).

## Acceptance Criteria

- [ ] `CasVerificationGate` issues at least one `Equivalence` CAS call when the stem yields an extractable equation.
- [ ] A stem `2x+3=7` with author answer `x=99` returns `CasGateOutcome.Failed`.
- [ ] A stem `2x+3=7` with author answer `x=2` returns `CasGateOutcome.Verified`.
- [ ] MCQ with two CAS-equivalent correct options returns `Failed` with typed reason.
- [ ] Integration tests run against the real `ICasRouterService` (not substituted).
- [ ] ADR-0032 §17 merged.
- [ ] Full `Cena.Actors.sln` builds; new tests pass.
