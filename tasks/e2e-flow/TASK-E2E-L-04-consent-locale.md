# TASK-E2E-L-04: Consent-gated observability respects locale

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-L](EPIC-E2E-L-accessibility-i18n.md)
**Tag**: `@i18n @compliance @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/consent-locale.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Consent dialog renders in ar / he → text is authoritative-policy translated (not machine-translated) → flipping consent appends event to ConsentAggregate correctly in every locale.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Legal-team-approved translation matches |
| Event | Stored with language-neutral key (not locale string) |

## Regression this catches

Legal text changed without translation; event stored locale-specific (should be neutral).

## Done when

- [ ] Spec lands
