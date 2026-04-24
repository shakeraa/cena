# TASK-PRR-261: Classroom-enforced redacted-question server projection + override-request path

**Priority**: P1
**Effort**: M (2 weeks)
**Lens consensus**: persona-ethics, persona-enterprise, persona-redteam, persona-sre, persona-educator
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3.3](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md)
**Assignee hint**: kimi-coder + front-end coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p1, server, tenancy, classroom, q2
**Status**: Blocked on [PRR-260](TASK-PRR-260-hide-reveal-session-toggle.md) + [PRR-236](TASK-PRR-236-classroom-assigned-target-teacher-ui.md) + [PRR-248 TenantPolicyOverlay](TASK-PRR-248-tenant-policy-overlay.md)
**Source**: 10-persona 002-brief review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

When a teacher enables `Classroom.EnforceOptionRedaction`, the server returns a **redacted question projection** (options stripped) to students in that class. Student cannot bypass via DevTools / direct API call. Student retains an **override-request path** to teacher — preserves agency, prevents ethics-violating silent enforcement.

## Scope

### API shape

New query parameter on the existing question-fetch endpoint:

```
GET /api/sessions/{sid}/question/{qid}?mode=auto|full|redacted
```

- `mode=auto` (default): server decides based on `Classroom.EnforceOptionRedaction` + student's `attemptMode`.
- `mode=full`: request options explicitly; rejected if classroom enforces.
- `mode=redacted`: request stripped projection.

Server logic:

1. If classroom enforces redaction AND no active override grant → return projection without `options[]`. Body carries `revealState: 'classroom_enforced'`.
2. If student opted in (`attemptMode=hidden_reveal`) → return redacted. Body carries `revealState: 'student_opted'`.
3. Else → full question. Body carries `revealState: 'full'`.

### Reveal endpoint

```
POST /api/sessions/{sid}/question/{qid}/reveal
```

- For `revealState=student_opted`: reveals options immediately (just flips client).
- For `revealState=classroom_enforced`: **does not reveal**. Returns 403 with `override_request_url`.

### Override-request path (ethics non-negotiable)

```
POST /api/sessions/{sid}/question/{qid}/override-request
body: { reason?: string (optional) }
```

- Creates a notification in the teacher's classroom console.
- Teacher grants → `OverrideGranted` event → student can then call `/reveal`.
- Teacher denies → stays redacted; student proceeds.
- Grant scope = single question, single session (not class-wide).

### UI surfaces

- Student sees a visible banner when in `classroom_enforced` mode: "Your teacher has hidden options for this session. [Request to show options]" — per persona-ethics non-negotiable.
- Student in `student_opted` mode sees the [PRR-260](TASK-PRR-260-hide-reveal-session-toggle.md) placeholder.

### Cache key (persona-sre)

- Response cache must include mode dimension: `q:{qid}:{ver}:{mode}`.
- On `EnforceOptionRedaction` toggle change mid-session, invalidate `q:{qid}:{ver}:full` and `q:{qid}:{ver}:redacted` for that classroom.
- Redis TTL 5 min; pre-warm on classroom flip where possible.

### Rate limiting

- `/reveal` rate limit: 5/min/student (persona-sre).
- `/override-request` rate limit: 10/session/student (abuse prevention).

### Redteam tests

Contract fuzzing (persona-redteam):

- Assert `mode=full` returns 403 when enforcement is on.
- Assert direct `GET` without `mode=` falls back to `auto` correctly.
- Assert DevTools-level manipulation of client state never fetches options when server enforces.
- 100-state property test over `{EnforceOptionRedaction, attemptMode, active-override}` combinations.

### Server policy resolution (persona-enterprise)

Three-layer: `tenant-deny > classroom-enforce > student-toggle`. Implemented via `TenantPolicyOverlay<EnforceOptionRedaction>` from [PRR-248](TASK-PRR-248-tenant-policy-overlay.md).

## Files

- `src/api/Cena.Student.Api.Host/Endpoints/SessionQuestionEndpoints.cs` — extend with mode parameter.
- `src/api/Cena.Student.Api.Host/Endpoints/RevealEndpoint.cs` (new).
- `src/api/Cena.Student.Api.Host/Endpoints/OverrideRequestEndpoint.cs` (new).
- `src/actors/Cena.Actors/Classrooms/RedactionPolicy.cs` (new).
- `src/actors/Cena.Actors/Classrooms/OverrideRequestEvents.cs` (new events `OverrideRequested`, `OverrideGranted`, `OverrideDenied`).
- `src/admin/full-version/src/pages/teacher/classroom-overrides.vue` (teacher override queue UI — ties to [PRR-236](TASK-PRR-236-classroom-assigned-target-teacher-ui.md)).
- `src/student/full-version/src/components/session/EnforcedRedactionBanner.vue` (new).
- Tests: 100-state fuzz, cache invalidation on flip, override grant flow end-to-end, SR announcement on state changes.

## Definition of Done

- Student receives redacted projection when classroom enforces — verified via API contract test.
- DevTools bypass attempt fails — verified via redteam fuzz.
- Override request → teacher grant → student reveal flow works end-to-end.
- Cache invalidation on classroom flip tested at load.
- Banner renders + SR-announces state per persona-a11y.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md) — tenancy.
- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md).
- Memory "No stubs — production grade".
- Persona-ethics: visible banner + override path required.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + fuzz-test output>"`

## Related

- PRR-260, PRR-236, PRR-248.
- Persona findings: ethics, enterprise, redteam, sre, educator 002 files.
