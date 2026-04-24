# TASK-PRR-362: Step-skipping tolerance ("I couldn't follow between X and Y")

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #6 engineering (real-student work habits)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-j, cas, priority=p0, ux
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

When a student skips multiple steps on paper (legit mental math), system accepts the leap if mathematically valid. If not valid, surface "I couldn't follow between step 2 and step 3" rather than false-wrong flag on a correct step.

## Scope

- Mathematical validity check: if step_{i+1} is reachable from step_i via any known operation path, accept.
- If not reachable and student obviously skipped intermediate work → ambiguity message, not wrong-step call.
- Distinguish "valid but skipped" from "actually wrong."
- Output distinguishes both in `VerificationResult`.

## Files

- `src/backend/Cena.Diagnostic/CAS/StepSkippingTolerator.cs`
- Tests: valid skips, invalid leaps, genuinely wrong transitions.

## Definition of Done

- Valid skip accepted.
- Invalid leap surfaces ambiguity message.
- Distinct from "wrong step" flag.

## Non-negotiable references

- Memory "Honest not complimentary" — honest about uncertainty.
- Memory "Labels match data" — don't call a correct step wrong.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-360](TASK-PRR-360-step-chain-verifier.md)
