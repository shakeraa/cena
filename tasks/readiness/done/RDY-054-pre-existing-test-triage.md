# RDY-054: Triage the 33 Remaining Pre-existing Test Failures

- **Priority**: High — CI signal
- **Complexity**: Varies by category
- **Effort**: Multi-day, splittable

## Problem

Commit `adf14d0` resolved 19 of the original 52 pre-existing failures. 33 remain across at least four categories:

- GDPR / Me endpoints CQRS race tests
- Focus analytics path bugs
- NSubstitute matcher ambiguity
- Marten proxy-cast issues

## Scope

Produce per-category root-cause + fix tickets:
- `RDY-054a-gdpr-me-endpoints.md`
- `RDY-054b-focus-analytics-paths.md`
- `RDY-054c-nsubstitute-matchers.md`
- `RDY-054d-marten-proxy-cast.md`
- `RDY-054e-tenant-scoping-tests.md`

Each child ticket fixes its own category; merge independently.

## Acceptance

- [ ] Five child tickets filed
- [ ] Categories + representative failing test names recorded
