# RDY-046: AI-Batch `DroppedForCasFailure` — Behavioral Test

- **Priority**: Medium — counter correctness
- **Complexity**: Mid engineer
- **Effort**: 3-4 hours

## Problem

`AiGenerationService.BatchGenerateAsync` promises per-batch `DroppedForCasFailure` + `CasDropReasons`. No test exercises the path end-to-end.

## Scope

- `AiGenerationCasGatingTests` drives the real gate through `BatchGenerateAsync`
- Assert dropped count == `DroppedForCasFailure`
- Assert `CasDropReasons` contains a reason per dropped question
- Assert counter `cena_ai_gen_cas_drops_total` increments by dropped count

## Acceptance

- [ ] Test file exists + passes
- [ ] Counter verified
