# PP-009: Route Step Solver Hints Through Answer-Mask Filter

- **Priority**: Medium — prevents canonical trace leakage to students
- **Complexity**: Senior engineer — wiring change in step verifier
- **Source**: Expert panel review § CAS Engine (Oren)

## Problem

`StepVerifierService.FindCanonicalHint` in `src/actors/Cena.Actors/Cas/StepVerifierService.cs:173-181` returns the canonical step's `Operation` or `Justification` string directly as a `SuggestedNextStep` in the `StepVerificationResult`.

Per ADR-0002 Decision 5, any content derived from the canonical trace must pass through the answer-mask filter before being shown to the student. The hint path bypasses this filter — a canonical step's justification might contain the target expression (e.g., "Divide both sides by 2 to get x = 3"), directly leaking the answer.

## Scope

1. The `SuggestedNextStep` field in `StepVerificationResult` should contain only the pedagogical hint operation (e.g., "try factoring" or "apply the quadratic formula"), not the concrete expression
2. Add a hint sanitization step that:
   - Strips any concrete mathematical expressions from the hint string
   - Replaces specific values with placeholders: "Divide both sides by 2 to get x = 3" becomes "Divide both sides by [constant]"
   - Or, better: return only the operation type from the canonical step, not the full justification
3. If the `answerRevealAllowed` gate is satisfied (student has exhausted the hint ladder), the full canonical step can be revealed

## Implementation Option A: Strip expressions from hints

```csharp
private static string? SanitizeHint(string? rawHint, CanonicalTrace canonical)
{
    if (rawHint == null) return null;
    // Remove any substring that matches the canonical answer
    var sanitized = rawHint;
    sanitized = sanitized.Replace(canonical.FinalAnswer, "[answer]");
    foreach (var step in canonical.Steps)
        sanitized = sanitized.Replace(step.Expression, "[expression]");
    return sanitized;
}
```

## Implementation Option B: Return operation type only

```csharp
private static string? FindCanonicalHint(int stepNumber, CanonicalTrace canonical)
{
    if (stepNumber > 0 && stepNumber <= canonical.Steps.Count)
    {
        // Return ONLY the operation name, never the expression or justification
        return canonical.Steps[stepNumber - 1].Operation;
    }
    return null;
}
```

Option B is simpler and more conservative. Recommend Option B unless the operation names are too vague to be useful.

## Files to Modify

- `src/actors/Cena.Actors/Cas/StepVerifierService.cs` — sanitize or restrict hint content
- `src/actors/Cena.Actors.Tests/Cas/StepVerifierServiceTests.cs` — NEW: test that hints don't leak canonical expressions

## Acceptance Criteria

- [ ] `SuggestedNextStep` never contains the canonical answer or intermediate expressions
- [ ] Operation-only hints are returned (e.g., "factor", "apply quadratic formula", "simplify")
- [ ] Test: canonical trace with answer "x = 3" — verify hint does not contain "3" or "x = 3"
- [ ] If `answerRevealAllowed` is true, full canonical step may be revealed (separate code path)
