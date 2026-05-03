# PP-004: Harden CAS LLM Output Math Claim Extraction

- **Priority**: High — affects correctness of all LLM tutor output verification
- **Complexity**: Architect level — regex/AST parser design, edge case analysis
- **Source**: Expert panel review § CAS Engine (Dr. Rami)

## Problem

`CasLlmOutputVerifier.ExtractMathClaims` in `src/actors/Cena.Actors/Cas/CasLlmOutputVerifier.cs:134-159` uses three regexes to detect mathematical claims in LLM output:

1. `\$\$([^$]+)\$\$` — block math
2. `\$([^$]+)\$` — inline math
3. `(\b[a-zA-Z]\s*=\s*-?\d+\.?\d*\b)` — bare equations like `x = 42`

### Known misses

- **Math in plain English**: "the answer is three" — no regex catches English number words
- **LaTeX without dollar-sign delimiters**: `\frac{1}{2}` appearing in markdown context (common in LLM output that uses markdown formatting)
- **Unicode math symbols**: √9 = 3, x² + y² = r², ∑ — these are legitimate math that the regexes ignore
- **Multi-line LaTeX environments**: `\begin{align} ... \end{align}` — the current regexes don't handle multiline
- **Markdown code fences with math**: LLMs sometimes wrap math in backtick code blocks

### False positive in IsAnswerLeak

`IsAnswerLeak` at line 163 does `exprNorm.Contains(ansNorm)` — substring containment. The expression `"x = 31"` contains `"x = 3"` as a substring, producing a false positive that would mask a correct student expression. The numerical check at line 170 is correct but the string check needs word-boundary awareness.

## Scope

### 1. Expanded regex set

Add detection patterns for:
- English number words: "zero" through "twenty", "hundred", "thousand" (build a `NumberWordRegex`)
- Bare LaTeX commands outside dollar signs: `\\frac\{[^}]+\}\{[^}]+\}`, `\\sqrt\{[^}]+\}`, etc.
- Unicode math: `[√∑∫∏∂∇∆±×÷≈≠≤≥]` followed by operands
- LaTeX environments: `\\begin\{(align|equation|gather)\}.*?\\end\{\1\}` (multiline, dotall)

### 2. Fix IsAnswerLeak string matching

Replace `exprNorm.Contains(ansNorm)` with word-boundary-aware matching:
```csharp
// Instead of substring containment:
var pattern = $@"\b{Regex.Escape(ansNorm)}\b";
if (Regex.IsMatch(exprNorm, pattern)) return true;
```

### 3. Test coverage

Add unit tests for each gap scenario:
- LLM output containing "the answer is three" with canonical answer "3"
- LLM output containing `\frac{1}{2}` without dollar signs
- LLM output containing `x² + y² = 25` with Unicode superscripts
- False positive test: canonical answer "3", expression "x = 31" should NOT match
- Multi-line LaTeX environment spanning multiple lines

## Files to Modify

- `src/actors/Cena.Actors/Cas/CasLlmOutputVerifier.cs` — expand regex set, fix IsAnswerLeak
- `src/actors/Cena.Actors.Tests/Cas/CasLlmOutputVerifierTests.cs` — NEW: comprehensive extraction tests

## Acceptance Criteria

- [ ] All five known-miss categories have detection patterns
- [ ] `IsAnswerLeak` uses word-boundary matching instead of substring containment
- [ ] Unit tests cover each gap scenario with at least 2 examples each
- [ ] Existing behavior (dollar-sign math, bare equations) is not regressed
- [ ] False positive rate on a sample of 100 LLM tutor responses is below 5%
