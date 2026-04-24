# TASK-E2E-E-03: Unsubscribe one-click (prr-051)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@parent @compliance @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/unsubscribe.spec.ts`

## Journey

Digest email → parent clicks unsubscribe → token-auth anonymous endpoint → preferences flipped → confirmation page → next digest cycle: no email/WhatsApp.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Confirmation page with success state |
| DB | `IParentDigestPreferencesStore` reflects opt-out on both channels |
| Bus | `ParentDigestPreferencesChangedV1` |
| Token | Single-use (second click → 409) |

## Regression this catches

Unsubscribe silently fails; token replayable; cascade misses WhatsApp side.

## Done when

- [ ] Spec lands
- [ ] Next-digest-cycle no-send asserted via IClock fast-forward
