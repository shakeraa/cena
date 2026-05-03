# RDY-034: Flow State Backend API

- **Priority**: High — prerequisite for RDY-016 (Flow State UX)
- **Complexity**: Mid engineer
- **Source**: Adversarial review — Rami
- **Tier**: 2
- **Effort**: 1-2 weeks

## Problem

RDY-016 says "wire FlowAmbientBackground.vue to actual flow state" but doesn't specify how flow state is computed, transmitted, or exposed as an API. CognitiveLoadService computes fatigue, but there's no state machine mapping fatigue + accuracy trend to the 5 flow states (warming/approaching/inFlow/disrupted/fatigued), and no API to expose current state to the frontend.

## Scope

### 1. Flow state computation

Define the state machine:
- **warming** (session start, first 3 questions): accuracy unknown, fatigue low
- **approaching** (accuracy trending up, fatigue low-medium): building momentum
- **inFlow** (accuracy > 80% over last 5 questions, fatigue low, response time stable): optimal state
- **disrupted** (accuracy drop > 30% from recent trend, or 3+ consecutive wrong): needs difficulty adjustment
- **fatigued** (CognitiveLoadService fatigue > threshold, or 25+ minutes elapsed): needs break

### 2. Backend API

- Add `FlowStateService.cs` that consumes CognitiveLoadService + recent accuracy data
- Expose current flow state via session state endpoint or as part of next-question response
- Include recommended action: { state, recommended_action, cooldown_minutes? }

### 3. SignalR or REST delivery

- Option A: Push flow state changes via SignalR (if RDY-020 completes first)
- Option B: Include flow state in every question response (simpler, no SignalR dependency)
- Document chosen approach

### 4. State transition logging

- Log all flow state transitions: `[FLOW_STATE] session={id} from={state} to={state} trigger={reason}`
- Transitions are analytics-useful (how often do students reach flow? how long do they stay?)

## Files to Create/Modify

- New: `src/actors/Cena.Actors/Services/FlowStateService.cs`
- `src/actors/Cena.Actors/StudentActor.cs` — integrate flow state computation
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` — expose flow state
- New: `tests/Cena.Actors.Tests/Services/FlowStateServiceTests.cs`

## Acceptance Criteria

- [ ] Flow state computed from fatigue + accuracy trend + session duration
- [ ] 5 states with defined transition rules
- [ ] API exposes current flow state and recommended action
- [ ] State transitions logged with structured tags
- [ ] Unit tests for each state transition
- [ ] Frontend can poll or receive flow state updates

> **Dependency**: Must be completed before RDY-016 can wire FlowAmbientBackground to real state.
