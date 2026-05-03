# RDY-054c: AnalysisJobActor — Event Type Alias Snake-Case

- **Priority**: Medium — event-naming hygiene
- **Complexity**: Low
- **Effort**: 1-2 hours

## Failing test (baseline 2026-04-15)

- `Cena.Actors.Tests.Services.AnalysisJobActorTests.ConceptAttemptedV1_EventTypeAlias_IsSnakeCase`

## Hypothesis

Marten's event-type alias registration for `ConceptAttempted_V1` is not producing snake_case per the repo convention. Either the Marten `AddEventType` call lacks the alias override, or the event record's naming conflicts with the default alias generator.

## Acceptance

- [ ] Test green
- [ ] All other event aliases audited for the same issue
