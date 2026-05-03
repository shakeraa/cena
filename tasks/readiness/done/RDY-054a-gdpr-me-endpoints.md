# RDY-054a: MeEndpointsCqrsRaceTests — CQRS Race Repair

- **Priority**: High — CI noise
- **Complexity**: Mid engineer
- **Effort**: 4-6 hours

## Failing tests (baseline 2026-04-15)

- `Cena.Admin.Api.Tests.MeEndpointsCqrsRaceTests.SubmitOnboarding_Appends_OnboardingCompleted_V1_Event`
- `Cena.Admin.Api.Tests.MeEndpointsCqrsRaceTests.UpdateProfile_DoesNot_Call_DirectSnapshotStore`
- `Cena.Admin.Api.Tests.MeEndpointsCqrsRaceTests.SubmitOnboarding_Idempotent_DoesNot_Reappend_WhenAlreadyOnboarded`
- `Cena.Admin.Api.Tests.MeEndpointsCqrsRaceTests.SubmitOnboarding_DoesNot_Call_DirectSnapshotStore`
- `Cena.Admin.Api.Tests.MeEndpointsCqrsRaceTests.UpdateProfile_Appends_ProfileUpdated_V1_Event`

## Hypothesis

The `/me` endpoints were refactored to event-source writes but the tests still expect direct snapshot-store interactions. Either the test fixture has a stale mock contract or the endpoint is calling the snapshot store on a path it shouldn't.

## Acceptance

- [ ] Root cause identified (write path vs test fixture)
- [ ] 5 tests green
- [ ] No regressions in other Me endpoints
