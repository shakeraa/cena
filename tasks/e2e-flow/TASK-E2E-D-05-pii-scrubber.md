# TASK-E2E-D-05: PII scrubber on LLM input (ADR-0047)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-D](EPIC-E2E-D-ai-tutoring.md)
**Tag**: `@compliance @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/pii-scrubber.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Student free-texts "my address is 5 King St, Tel Aviv" in an explanation → backend scrubber strips → LLM prompt never contains the original string.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB audit | `scrubbed=true` recorded |
| LLM payload (recorder) | Original string NOT present; redaction marker present |
| Regex drift | New PII patterns (Israeli ID, UK postcode) all scrubbed |

## Regression this catches

PII regex drift missing new pattern; scrubber bypassed on a new code path; compliance incident.

## Done when

- [ ] Spec lands
- [ ] Corpus of ~15 PII patterns tested
- [ ] Tagged `@ship-gate @p0`
