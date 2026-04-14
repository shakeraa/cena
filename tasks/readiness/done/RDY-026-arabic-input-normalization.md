# RDY-026: Arabic Variable Input Normalization

- **Priority**: Critical — Arabic students cannot type answers naturally
- **Complexity**: Mid engineer (frontend + backend validation)
- **Source**: Cross-review — Amjad (Curriculum)
- **Tier**: 1 (blocks usability for Arabic-speaking students)
- **Effort**: 3-5 days

## Problem

Arab students naturally type Arabic letters (س ص ع) for variables, but SymPy and the answer validation pipeline expect Latin letters (x y z). No input normalization exists. A student who types the mathematically correct answer in Arabic characters gets marked wrong.

This is not an edge case — it affects the core interaction loop for 80% of the target population.

## Scope

### 1. Input normalization layer

Add a normalization step between student input and SymPy validation:
- Map common Arabic letter substitutions: `س→x`, `ص→y`, `ع→z`, `ن→n`, `م→m`
- Map Arabic-Indic numerals (٠١٢٣٤٥٦٧٨٩) to Western Arabic (0123456789)
- Normalize Arabic mathematical operators (× → *, ÷ → /)
- Preserve original input for misconception analysis (what the student actually typed)

### 2. MathQuill/MathLive input acceptance

- Configure math input widgets to accept Arabic letter input
- Display normalized form in real-time as student types (optional, toggleable)
- Show "We understood your input as: x² + 3x" confirmation

### 3. SymPy validation integration

- Pass normalized input (not raw) to SymPy CAS for verification
- Store both raw and normalized in session event (for research)
- Log normalization actions: `[INPUT_NORMALIZED] raw=س²+3س normalized=x²+3x`

### 4. Edge cases

- Mixed Latin + Arabic in same expression (e.g., `sin(س)`)
- Arabic decimal separator (٫) vs. Western (.)
- Hebrew letter input (similar problem, lower priority)

## Files to Modify

- New: `src/shared/Cena.Infrastructure/Math/ArabicInputNormalizer.cs`
- `src/actors/Cena.Actors/Services/AnswerValidationService.cs` — call normalizer before SymPy
- `src/student/full-version/src/components/session/MathInput.vue` — accept Arabic chars
- New: `tests/Cena.Infrastructure.Tests/Math/ArabicInputNormalizerTests.cs`

## Acceptance Criteria

- [ ] Arabic letters (س ص ع ن م) accepted and normalized to x y z n m
- [ ] Arabic-Indic numerals normalized to Western Arabic
- [ ] SymPy validates against normalized input (not raw)
- [ ] Original student input preserved in session event
- [ ] Mixed Arabic + Latin expressions handled correctly
- [ ] Normalization logged for analysis
- [ ] Unit tests cover all normalization mappings + edge cases
