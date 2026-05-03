# RDY-054e: NSubstitute Ambiguity + Marten Proxy Cast

- **Priority**: Medium — CI stability
- **Complexity**: Mid
- **Effort**: 3-5 hours

## Categories

1. **NSubstitute ambiguous-argument errors** — `AmbiguousArgumentsException` when tests mix literal + matcher args on calls with parameters of the same type.
2. **Marten proxy-cast failures** — tests that cast a document proxy to its interface fail because Marten's dynamic proxy for document types doesn't implement the interface the way the test assumes.

## Scope

- Sweep failing tests by category; convert literal + matcher mixes to all-matchers.
- For proxy-cast failures, either assert via `.IsType<T>` / behavioural equality, or rework the test to avoid relying on Marten proxy identity.

## Acceptance

- [ ] Both categories resolve to zero CI failures
