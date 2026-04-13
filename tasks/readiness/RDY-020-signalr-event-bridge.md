# RDY-020: SignalR Event Push-Back Bridge (NATS → Hub → Client)

- **Priority**: Medium — real-time features may not work without this
- **Complexity**: Mid engineer
- **Source**: Expert panel audit — Oren (API)
- **Tier**: 3
- **Effort**: 3-5 days

## Problem

`CenaHub.cs` receives client commands and publishes to NATS for actor processing. But how do events route BACK to connected clients? No `NatsSignalRBridge` service is visible. Students may not receive session events (answer evaluated, hint generated, mastery updated) until they poll the REST API.

## Scope

### 1. Create NATS → SignalR bridge service

Background service in Student API Host that:
- Subscribes to `cena.events.student.{studentId}.>` for all connected students
- Routes events to the appropriate SignalR group: `Clients.Group(studentId).SendAsync(...)`
- Handles connection/disconnection (subscribe on connect, unsubscribe on disconnect)

### 2. Event type mapping

Map NATS event subjects to SignalR client methods:
- `cena.events.student.{id}.answer.evaluated` → `AnswerEvaluated`
- `cena.events.student.{id}.hint.generated` → `HintGenerated`
- `cena.events.student.{id}.mastery.updated` → `MasteryUpdated`
- `cena.events.student.{id}.session.ended` → `SessionEnded`

### 3. Correlation ID propagation

Forward `correlationId` from NATS message to SignalR event so clients can match requests to responses.

## Files to Create/Modify

- New: `src/api/Cena.Student.Api.Host/Services/NatsSignalRBridge.cs`
- `src/api/Cena.Student.Api.Host/Hubs/CenaHub.cs` — register/unregister subscriptions on connect/disconnect
- `src/api/Cena.Student.Api.Host/Program.cs` — register bridge as hosted service

## Acceptance Criteria

- [ ] Events from NATS pushed to connected SignalR clients
- [ ] Subscription scoped to connected student IDs only
- [ ] Correlation ID propagated from NATS to SignalR
- [ ] Clean unsubscribe on client disconnect
- [ ] E2E test: submit answer via Hub → receive AnswerEvaluated via Hub
