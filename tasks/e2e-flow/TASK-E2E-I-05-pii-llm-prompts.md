# TASK-E2E-I-05: PII never in LLM prompts (ADR-0047)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@compliance @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/pii-llm-prompts.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Student input with various PII patterns (email, phone, address, Israeli ID, UK postcode) → captured LLM payload contains zero PII.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| LLM recorder | Payload scanned per PII pattern — all zero |
| DB audit | `scrubbed=true` recorded |

## Regression this catches

New PII pattern introduced not scrubbed; regex regression drops a case; scrubber bypassed on new code path.

## Done when

- [ ] Spec lands
- [ ] Shares LLM recorder harness with D-05
- [ ] Tagged `@ship-gate @p0`
