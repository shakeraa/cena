# PP-012: Re-Run Figure Quality Gate After AI Generation

- **Priority**: Medium — prevents AI-generated figures from bypassing quality checks
- **Complexity**: Senior engineer — wiring change in AI figure generator
- **Source**: Expert panel review § Figures & Diagrams (Dr. Nadia)

## Problem

`FigureQualityGate` in `src/api/Cena.Admin.Api/Figures/FigureQualityGate.cs` runs 10 validation rules at authoring time in the admin editor. However, `AiFigureGenerator` in `src/api/Cena.Admin.Api/Figures/AiFigureGenerator.cs` generates figure specs via LLM with a 3-attempt retry loop and internal validation — but it may not call the full FigureQualityGate suite.

If the AI generates a figure with a missing AriaLabel, incorrect equilibrium, or marker inconsistency, and the internal validation is less strict than the quality gate, the figure passes AI generation but would fail the quality gate. Since the quality gate only runs when a human edits in the admin editor, AI-generated figures bypass it.

## Scope

1. After each AI generation attempt in `AiFigureGenerator`, call `FigureQualityGate.Validate(spec)` with the full 10-rule suite
2. If validation fails, include the quality gate failure messages in the retry prompt to the LLM (so it can fix the issues)
3. If all 3 attempts fail quality gate validation, return a structured error indicating which rules failed, rather than accepting a non-compliant figure
4. Add a `QualityGateVerified: bool` flag to the AI generation result

## Files to Modify

- `src/api/Cena.Admin.Api/Figures/AiFigureGenerator.cs` — call FigureQualityGate after each LLM attempt
- `src/api/Cena.Admin.Api/Figures/FigureQualityGate.cs` — ensure `Validate` returns structured rule results (not just pass/fail)

## Acceptance Criteria

- [ ] Every AI-generated figure spec passes through FigureQualityGate before acceptance
- [ ] Quality gate failures are fed back to the LLM retry prompt
- [ ] After 3 failed attempts, the generation returns an error with specific rule failures
- [ ] Test: generate a figure spec with missing AriaLabel — verify quality gate catches it
