# INF-022: Proto.Actor Abstraction Boundary Documentation

**Priority:** P3 — future-proofing, no code changes
**Blocked by:** Nothing
**Estimated effort:** 0.5 days

---

## Context

Proto.Actor is maintained by a small team (Asynkron AB / Roger Johansson). If maintenance stops, Cena would need to port to Microsoft Orleans or Akka.NET. The domain model (student actors, session actors, event sourcing) transfers 1:1 — but only if the Proto.Actor-specific surface is documented.

This is a documentation-only task. No code changes.

## Deliverable

**File:** `docs/architecture/proto-actor-abstraction-boundary.md`

### Content Required

1. **Proto.Actor-Specific Interfaces Used**
   - `IActor`, `IContext`, `PID`, `Props`
   - `Proto.Cluster.ClusterIdentity`, `ClusterKind`
   - `Proto.Remote.GrpcNet`
   - `Proto.DependencyInjection`
   - `context.Respond()`, `context.Send()`, `context.RequestAsync()`
   - `context.SetReceiveTimeout()` (passivation)
   - `context.Spawn()` (child actors)

2. **Equivalent Patterns in Alternatives**

   | Proto.Actor | Orleans | Akka.NET |
   |-------------|---------|---------|
   | `IActor.ReceiveAsync` | `IGrain.OnActivateAsync` + method dispatch | `ReceiveActor.Receive<T>` |
   | `ClusterIdentity` | `IGrainFactory.GetGrain<T>(key)` | `ClusterSharding` |
   | `context.Respond()` | Method return value | `Sender.Tell()` |
   | `SetReceiveTimeout` | `DeactivateOnIdle(TimeSpan)` | `SetReceiveTimeout` |
   | `context.Spawn()` (child) | No direct equivalent (use separate grain) | `Context.ActorOf()` |

3. **Migration Surface Area**
   - List every file that imports `Proto` namespace
   - Estimated LOC to change per file
   - Risk ranking: High (actor lifecycle), Medium (message routing), Low (telemetry)

4. **Test Compatibility**
   - Which tests are framework-specific vs domain-logic-only
   - Test count by category

## Definition of Done
- [ ] Document created in `docs/architecture/`
- [ ] Reviewed by architect
- [ ] No code changes
