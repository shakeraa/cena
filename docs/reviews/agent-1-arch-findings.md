# Agent 1 — System & Contract Architect Findings
Date: 2026-04-11
Base commit: 989efa06d668d461fff97fdd01c29d10d529e416
Worker: claude-subagent-arch

## Summary
- P0 count: 7
- P1 count: 5
- P2 count: 3
- P3 count: 1

## Findings

- id: FIND-arch-001
  severity: p0
  category: contract
  file: src/api/Cena.Api.Host/Program.cs
  line: 1
  evidence:
    - type: grep
      content: |
        $ grep -rn "Cena\.Api\.Host" src/ --include="*.csproj" --include="*.sln"
        src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj:24:    <ProjectReference Include="..\..\api\Cena.Api.Host\Cena.Api.Host.csproj" />
        # (zero hits in any .sln file)

        $ diff -q src/api/Cena.Api.Host/Endpoints/ src/api/Cena.Student.Api.Host/Endpoints/
        Only in src/api/Cena.Api.Host/Endpoints: ClassroomEndpoints.cs
        Only in src/api/Cena.Api.Host/Endpoints: ContentEndpoints.cs
        # All 10 student endpoint files are byte-identical duplicates.

        $ diff -q src/api/Cena.Api.Host/Hubs/ src/api/Cena.Student.Api.Host/Hubs/
        # (no differences — 4 hub files are byte-identical)

        $ diff -q src/api/Cena.Api.Host/Services/ src/api/Cena.Student.Api.Host/Services/
        # (no differences — all Recommendation service files are byte-identical)
    - type: grep
      content: |
        # Cena.Api.Host is NOT in the solution:
        $ grep -E "Project\(.*\"Cena.Api.Host\"" src/actors/Cena.Actors.sln
        # (zero matches)
        # But Student Api Host references the same namespace `Cena.Api.Host.Endpoints` and duplicates all files.
  finding: The obsolete `Cena.Api.Host` project is excluded from the solution but is still referenced by `Cena.Actors.Tests.csproj`, so the test project compiles against byte-duplicate, now-orphaned copies of every student endpoint, hub, and service. Edits to the live `Cena.Student.Api.Host` endpoints are not covered by tests; the test suite silently verifies the stale copies.
  root_cause: DB-06b split `Cena.Api.Host` into `Cena.Student.Api.Host` + `Cena.Admin.Api.Host` by copying files instead of moving them, and then removed `Cena.Api.Host.csproj` from the solution but forgot to retarget `Cena.Actors.Tests` at the new hosts. The two endpoint trees now drift silently because the compiler never sees them at the same time.
  proposed_fix: Delete `src/api/Cena.Api.Host/` entirely. Update `src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj:24` to reference `Cena.Student.Api.Host` (and `Cena.Admin.Api.Host` if admin endpoints are tested). Re-run all tests; fix any `InternalsVisibleTo` attributes that broke. Grep `src/` for any other `using Cena.Api.Host.Endpoints;` and confirm they resolve from the new hosts.
  task_body: |
    **Goal**: Eliminate the orphaned `Cena.Api.Host` project so tests cover the live endpoints, not byte-duplicate stale copies.
    **Files to touch**:
      - src/api/Cena.Api.Host/ (DELETE the directory)
      - src/actors/Cena.Actors.Tests/Cena.Actors.Tests.csproj (swap ProjectReference to Cena.Student.Api.Host + Cena.Admin.Api.Host)
      - src/api/Cena.Student.Api.Host/Cena.Student.Api.Host.csproj (add `<InternalsVisibleTo Include="Cena.Actors.Tests" />` if tests need it)
      - src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj (same)
    **Definition of Done**:
      - [ ] `src/api/Cena.Api.Host/` no longer exists
      - [ ] `dotnet build src/actors/Cena.Actors.sln` succeeds
      - [ ] `dotnet test src/actors/Cena.Actors.sln` passes without referencing the deleted project
      - [ ] `grep -rn "Cena.Api.Host" src/` returns only references in historical READMEs (if any)
    **Reference**: FIND-arch-001 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-002
  severity: p0
  category: contract
  file: src/actors/Cena.Actors/Notifications/NotificationDispatcher.cs
  line: 40
  evidence:
    - type: grep
      content: |
        $ grep -rn 'events\.xp\.awarded\|cena\.events\.xp\.awarded' src/
        src/actors/Cena.Actors/Notifications/NotificationDispatcher.cs:40: await foreach (var msg in _nats.SubscribeAsync<XpAwarded_V1>("events.xp.awarded", cancellationToken: ct))
        # (exactly one hit — the subscription. No publisher emits "events.xp.awarded".)

        $ grep -rn 'XpAwarded' src/actors/Cena.Actors/Sessions/SessionNatsPublisher.cs
        src/actors/Cena.Actors/Sessions/SessionNatsPublisher.cs:56: public Task PublishXpAwardedAsync(string studentId, XpAwarded_V1 evt) =>
        src/actors/Cena.Actors/Sessions/SessionNatsPublisher.cs:57:     PublishSafe(NatsSubjects.StudentEvent(studentId, "xp_awarded"), evt, $"{studentId}-xp-{evt.TotalXp}");
        # Actual publish subject = cena.events.student.{studentId}.xp_awarded
  finding: `NotificationDispatcher` subscribes to the NATS subject `"events.xp.awarded"` (not even prefixed `cena.`) but the only publisher for `XpAwarded_V1` emits on `cena.events.student.{studentId}.xp_awarded`. The dispatcher never receives a single message — no in-app notifications, no web-push "XP Gained!" toasts, ever.
  root_cause: The dispatcher was written against an imagined early-draft subject scheme and the SessionNatsPublisher subject convention changed afterward; nobody ran end-to-end proof that XP events reach the dispatcher.
  proposed_fix: Change line 40 to subscribe on a wildcard matching the publisher: `"cena.events.student.*.xp_awarded"`. Extract studentId from the subject parts (same parsing as `NatsSignalRBridge.RouteEvent`). Add an integration test: publish via `SessionNatsPublisher.PublishXpAwardedAsync`, assert the dispatcher writes a `NotificationDocument`.
  task_body: |
    **Goal**: Wire NotificationDispatcher to the real XP event subject so in-app and push notifications fire.
    **Files to touch**:
      - src/actors/Cena.Actors/Notifications/NotificationDispatcher.cs:40 (subject + message parsing)
      - src/actors/Cena.Actors.Tests/Notifications/ (add dispatcher integration test)
    **Definition of Done**:
      - [ ] Subscribe subject matches `NatsSubjects.StudentEvent(studentId, "xp_awarded")` wildcards
      - [ ] `studentId` parsed from NATS subject, not trusted from payload
      - [ ] Integration test publishes an XP event and asserts a NotificationDocument is created
      - [ ] All existing tests still pass
    **Reference**: FIND-arch-002 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-003
  severity: p0
  category: contract
  file: src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs
  line: 47
  evidence:
    - type: grep
      content: |
        $ grep -rn 'cena\.events\.QuestionStemEdited_V1\|cena\.events\.QuestionOptionChanged_V1' src/
        src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs:48: var stemTask = SubscribeAsync("cena.events.QuestionStemEdited_V1", stoppingToken);
        src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs:49: var optionTask = SubscribeAsync("cena.events.QuestionOptionChanged_V1", stoppingToken);
        # (exactly two hits — the two subscribers. No publisher matches.)

        $ grep -n 'GetDurableSubject\|cena\.durable' src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:67:// The outbox publisher uses "cena.durable.{category}.{eventType}" for JetStream durability.
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:214:  string subject = GetDurableSubject(eventWrapper.EventTypeName);
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:311:  var e when e.StartsWith("Question") ... => $"cena.durable.curriculum.{eventTypeName}",
        # Question* events are actually published on cena.durable.curriculum.QuestionStemEdited_V1
  finding: `ExplanationCacheInvalidator` claims (line 47 comment: "Outbox publishes events as `cena.events.{EventTypeName}`") that it matches the outbox subject format, but the outbox actually emits on `cena.durable.curriculum.{EventTypeName}`. The L2 Redis explanation cache is never invalidated when a question stem or option is edited — students see stale AI-generated explanations until the next cache expiry.
  root_cause: The comment and subscription were written before NatsOutboxPublisher.GetDurableSubject() (lines 294–314) was introduced. The subject scheme migration to `cena.durable.{category}.*` for JetStream durability was never reflected in downstream subscribers, and no integration test flushes an event end-to-end.
  proposed_fix: Subscribe to `cena.durable.curriculum.QuestionStemEdited_V1` and `cena.durable.curriculum.QuestionOptionChanged_V1`, and update the outdated comment. Better: extract the subject scheme into a shared helper (`NatsDurableSubjects.Question(...)`) so the two sides cannot drift again.
  task_body: |
    **Goal**: Fix explanation cache invalidation subjects so L2 Redis entries are wiped on question edits.
    **Files to touch**:
      - src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs:47-49 (subjects + comment)
      - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs (optionally expose GetDurableSubject as public helper)
      - src/actors/Cena.Actors.Tests/Explanations/ (add integration test: append QuestionStemEdited_V1 event, verify cache.InvalidateQuestionAsync called)
    **Definition of Done**:
      - [ ] Subscribe subjects match outbox publisher subjects character-for-character
      - [ ] Integration test appends an event and asserts cache invalidation happens
      - [ ] Comment in code accurately describes the subject scheme
    **Reference**: FIND-arch-003 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-004
  severity: p0
  category: stub
  file: src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
  line: 245
  evidence:
    - type: grep
      content: |
        $ sed -n '236,248p' src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
        // Placeholder response — use /stream endpoint for AI tutoring
        var assistantMessageId = $"tutor_msg_{Guid.NewGuid():N}";
        var assistantMessage = new TutorMessageDocument
        {
            ...
            Role = "assistant",
            Content = "Great question! Use the /stream endpoint for an AI-powered response.",
            ...
        };
        session.Store(assistantMessage);

        $ grep -rn 'tutor/threads.*messages\|/tutor/.*stream' src/student/full-version/src
        src/student/full-version/src/pages/tutor/[threadId].vue:35:  `/api/tutor/threads/${threadId.value}/messages`,   # <-- UI calls this, gets canned text
        # The UI never invokes /stream anywhere in src/student/.
  finding: `POST /api/tutor/threads/{threadId}/messages` is a hardcoded stub that persists the literal string "Great question! Use the /stream endpoint for an AI-powered response." as the assistant's reply. The student web UI (`src/student/full-version/src/pages/tutor/[threadId].vue:35`) calls exactly this endpoint, so the AI tutor chat UI always shows that canned sentence — the real Claude integration in `/stream` is never reached by the production UI.
  root_cause: STB-04 Phase 1 shipped a stub to unblock UI work, and STB-04b was supposed to replace it. The /stream endpoint was hardened but the POST /messages endpoint was never updated, and no integration test catches that the UI talks to the non-stream path.
  proposed_fix: Either (a) delete the stub `SendMessage` handler and return 410 Gone so the UI migrates to SSE, or (b) refactor `SendMessage` to call `ClaudeTutorLlmService` non-streaming (single `complete` request) and return the assembled message. Either way, update `src/student/full-version/src/pages/tutor/[threadId].vue:35` to hit the real path and add an E2E test that asserts the tutor response is NOT the canned placeholder.
  task_body: |
    **Goal**: Replace the canned "Great question!" placeholder so the tutor chat actually uses Claude.
    **Files to touch**:
      - src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs:236-260 (SendMessage handler — replace stub with real LLM call or deprecate)
      - src/student/full-version/src/pages/tutor/[threadId].vue:35 (switch to /stream endpoint if SendMessage is deprecated)
      - tests/ (add an integration test asserting the assistant reply is not the canned string)
    **Definition of Done**:
      - [ ] `grep -rn "Great question! Use the /stream" src/` returns zero results
      - [ ] Student tutor UI receives real AI responses
      - [ ] Integration test covers the full round trip
    **Reference**: FIND-arch-004 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-005
  severity: p0
  category: stub
  file: src/api/Cena.Admin.Api/AiGenerationService.cs
  line: 628
  evidence:
    - type: grep
      content: |
        $ sed -n '308,316p' src/api/Cena.Admin.Api/AiGenerationService.cs
        AiProvider.OpenAI => await CallOpenAiAsync(effectiveConfig, prompt, request),
        AiProvider.Google => await CallGoogleAsync(effectiveConfig, prompt, request),
        AiProvider.AzureOpenAI => await CallAzureOpenAiAsync(effectiveConfig, prompt, request),

        $ sed -n '625,641p' src/api/Cena.Admin.Api/AiGenerationService.cs
        private Task<...> CallOpenAiAsync(...) {
            throw new NotImplementedException("Provider not yet implemented — use Anthropic");
        }
        private Task<...> CallGoogleAsync(...) {
            throw new NotImplementedException("Provider not yet implemented — use Anthropic");
        }
        private Task<...> CallAzureOpenAiAsync(...) {
            throw new NotImplementedException("Provider not yet implemented — use Anthropic");
        }
  finding: `AiGenerationService.GenerateAsync` switches on `AiProvider` and dispatches to `CallOpenAiAsync`, `CallGoogleAsync`, `CallAzureOpenAiAsync`, all of which throw `NotImplementedException`. The admin UI (`ai-settings.vue:130`) lets admins pick any of these providers and save settings. As soon as they hit "Generate", the server throws 500. This violates the "no stubs — production grade" rule from `feedback_no_stubs_production_grade`.
  root_cause: The dispatcher was wired up ahead of any secondary provider being implemented. No client-side validation limits the provider dropdown to `Anthropic`, and no server-side validation rejects unsupported providers on save.
  proposed_fix: Remove the unsupported provider enum values AND the three stub methods. Make `AiProvider` a closed enum of only the providers that actually work (`Anthropic` today). If secondary providers are genuinely wanted, implement them; if not, delete the placeholder code so the UI cannot reach it.
  task_body: |
    **Goal**: Remove the three stub LLM provider methods and stop the admin UI from offering non-functional options.
    **Files to touch**:
      - src/api/Cena.Admin.Api/AiGenerationService.cs:625-641 (delete stub methods)
      - src/api/Cena.Admin.Api/AiGenerationService.cs:308-316 (delete the unreachable switch cases)
      - src/api/Cena.Api.Contracts/ (restrict AiProvider enum to real values)
      - src/admin/full-version/src/pages/apps/system/ai-settings.vue (remove OpenAI/Google/AzureOpenAI options from dropdown)
    **Definition of Done**:
      - [ ] `grep -rn "NotImplementedException" src/api/` returns zero hits
      - [ ] The admin AI settings dropdown shows only providers that actually work
      - [ ] Existing Claude/Anthropic path still passes all tests
    **Reference**: FIND-arch-005 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-006
  severity: p0
  category: dead-endpoint
  file: src/api/Cena.Admin.Api/GdprEndpoints.cs
  line: 16
  evidence:
    - type: grep
      content: |
        $ grep -rn 'MapGdprEndpoints' src/
        src/api/Cena.Admin.Api/GdprEndpoints.cs:16: public static RouteGroupBuilder MapGdprEndpoints(this IEndpointRouteBuilder app)
        # (exactly one hit — the definition. Zero callers anywhere.)

        $ sed -n '18,22p' src/api/Cena.Admin.Api/GdprEndpoints.cs
        var group = app.MapGroup("/api/admin/gdpr")
            .WithTags("GDPR")
            .RequireAuthorization("AdminPolicy");

        $ grep -rn '"AdminPolicy"\|AdminPolicy\b' src/shared/Cena.Infrastructure/Auth/
        # (zero hits — "AdminPolicy" is not a registered policy name)
    - type: grep
      content: |
        $ grep -n 'options.AddPolicy' src/shared/Cena.Infrastructure/Auth/CenaAuthPolicies.cs
        src/shared/Cena.Infrastructure/Auth/CenaAuthPolicies.cs:31: options.AddPolicy(ModeratorOrAbove, policy =>
        src/shared/Cena.Infrastructure/Auth/CenaAuthPolicies.cs:37: options.AddPolicy(AdminOnly, policy =>
        src/shared/Cena.Infrastructure/Auth/CenaAuthPolicies.cs:43: options.AddPolicy(SuperAdminOnly, policy =>
        src/shared/Cena.Infrastructure/Auth/CenaAuthPolicies.cs:49: options.AddPolicy(SameOrg, policy =>
        # No "AdminPolicy" — the correct name is "AdminOnly".
  finding: `GdprEndpoints.MapGdprEndpoints()` is defined but never called from any Program.cs. All six GDPR endpoints (consent, export, erasure) do not exist on any host, and Article-17/20 compliance requests have no backend. Worse, even if wired up, the group uses `RequireAuthorization("AdminPolicy")` which does not match any registered policy — every call would fail with a 500 because the authorization middleware can't resolve the policy name.
  root_cause: A task wrote the endpoints but never added `app.MapGdprEndpoints()` to `CenaAdminServiceRegistration.MapCenaAdminEndpoints`. The "AdminPolicy" typo indicates the author was unfamiliar with the `CenaAuthPolicies` class and never ran the code.
  proposed_fix: Add `app.MapGdprEndpoints();` to `MapCenaAdminEndpoints` in `CenaAdminServiceRegistration.cs`. Change line 20 to `.RequireAuthorization(CenaAuthPolicies.AdminOnly)`. Write an integration test that hits each GDPR endpoint and asserts the expected status codes.
  task_body: |
    **Goal**: Wire up GDPR endpoints and fix the broken auth policy reference so Article 17/20 compliance is actually functional.
    **Files to touch**:
      - src/api/Cena.Admin.Api/GdprEndpoints.cs:20 ("AdminPolicy" → CenaAuthPolicies.AdminOnly)
      - src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs (add app.MapGdprEndpoints();)
      - tests/ (add integration test per endpoint)
    **Definition of Done**:
      - [ ] `grep -rn "MapGdprEndpoints" src/` returns both definition AND caller
      - [ ] `grep -rn "\"AdminPolicy\"" src/` returns zero hits
      - [ ] Integration test hits /api/admin/gdpr/consents/{studentId} and gets 200 (authorized) / 403 (not authorized)
    **Reference**: FIND-arch-006 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-007
  severity: p0
  category: dead-endpoint
  file: src/api/Cena.Admin.Api/DiagramEndpoints.cs
  line: 16
  evidence:
    - type: grep
      content: |
        $ grep -rn 'MapDiagram\|MapDiagramEndpoints' src/
        src/api/Cena.Admin.Api/DiagramEndpoints.cs:16: public static IEndpointRouteBuilder MapDiagramEndpoints(this IEndpointRouteBuilder app)
        # (exactly one hit — the definition. Zero callers.)

        $ grep -rn 'diagrams/generate\|admin/diagrams' src/admin src/student
        # (zero hits — no UI calls these routes either)
  finding: `DiagramEndpoints.MapDiagramEndpoints()` is never called. The entire LLM-009 diagram generation feature (generate, cache list, get, delete) is dead. Neither admin nor student UIs call `/api/admin/diagrams/*`. The endpoints, the cache, and the underlying `IDiagramGenerator` service are all running but unreachable.
  root_cause: The endpoint file was created but never wired into `MapCenaAdminEndpoints`, and no UI work ever consumed the feature. This is pure orphan code — either the feature was abandoned mid-flight or the wiring was forgotten.
  proposed_fix: Decide whether diagram generation is a live feature. If yes — wire it up in `CenaAdminServiceRegistration.MapCenaAdminEndpoints` and build the admin UI page. If no — delete `DiagramEndpoints.cs`, the underlying `IDiagramGenerator` / `IDiagramCache` if they have no other consumers, and the `Cena.Actors.Diagrams` namespace.
  task_body: |
    **Goal**: Remove dead code OR wire up the orphaned diagram generation feature — this should not sit in limbo.
    **Files to touch**:
      - src/api/Cena.Admin.Api/DiagramEndpoints.cs (DECISION: wire in registration, or delete)
      - src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs (if wiring, add MapDiagramEndpoints)
      - src/actors/Cena.Actors/Diagrams/ (check for orphaned generator/cache)
    **Definition of Done**:
      - [ ] Either the endpoints are reachable with tests covering them, OR the file + orphaned dependencies are deleted
      - [ ] `grep -rn "IDiagramGenerator\|IDiagramCache" src/` only has references inside active code paths
    **Reference**: FIND-arch-007 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-008
  severity: p1
  category: dead-endpoint
  file: src/api/Cena.Admin.Api.Host/Program.cs
  line: 230
  evidence:
    - type: grep
      content: |
        $ grep -rn 'MapComplianceEndpoints' src/
        src/api/Cena.Api.Host/Program.cs:328:app.MapComplianceEndpoints();                  # orphaned Cena.Api.Host
        src/api/Cena.Admin.Api.Host/README.md:32:   - `MapComplianceEndpoints()` — FERPA compliance   # claims to map it
        src/api/Cena.Admin.Api/ComplianceEndpoints.cs:19: public static IEndpointRouteBuilder MapComplianceEndpoints(...)
        src/actors/Cena.Actors.Host/Program.cs:515: app.MapComplianceEndpoints();

        $ grep -n 'MapComplianceEndpoints\|Compliance' src/api/Cena.Admin.Api.Host/Program.cs
        14:using Cena.Infrastructure.Compliance;
        # (NO call to MapComplianceEndpoints — the README claims it is wired but Program.cs does not wire it)
  finding: `Cena.Admin.Api.Host/README.md:32` says the host wires up `MapComplianceEndpoints()` for FERPA compliance, but `Program.cs` in the same directory does not call it. The REV-013.2 FERPA audit-log endpoints are only reachable via `Cena.Actors.Host` (which is a different process with different rate limits and auth scope). Either the README is a lie, or the new Admin API Host is silently missing FERPA compliance endpoints.
  root_cause: DB-06b split Program.cs from `Cena.Api.Host` into `Cena.Admin.Api.Host`, and the `MapComplianceEndpoints()` call was dropped in the copy. The README was not updated and still reflects the old (Cena.Api.Host) expectations.
  proposed_fix: Add `app.MapComplianceEndpoints();` to `src/api/Cena.Admin.Api.Host/Program.cs` immediately after `MapCenaAdminEndpoints()`. Confirm via `curl http://localhost:<port>/api/admin/compliance/audit-log` that the endpoint responds.
  task_body: |
    **Goal**: Restore FERPA compliance endpoints on the production Admin API Host.
    **Files to touch**:
      - src/api/Cena.Admin.Api.Host/Program.cs:230 (add app.MapComplianceEndpoints();)
    **Definition of Done**:
      - [ ] `grep -n "MapComplianceEndpoints" src/api/Cena.Admin.Api.Host/Program.cs` returns one hit
      - [ ] The audit-log endpoint responds when hit via HTTP against the Admin API Host
    **Reference**: FIND-arch-008 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-009
  severity: p1
  category: contract
  file: src/api/Cena.Student.Api.Host/Hubs/NatsSignalRBridge.cs
  line: 188
  evidence:
    - type: grep
      content: |
        $ grep -n 'const events\|BadgeEarned\|conn.on' src/student/full-version/src/api/signalr.ts
        src/student/full-version/src/api/signalr.ts:78:    const events: HubEventName[] = [
        src/student/full-version/src/api/signalr.ts:79:      'SessionStarted', 'SessionEnded', 'AnswerEvaluated', 'MasteryUpdated',
        src/student/full-version/src/api/signalr.ts:83:      'HintDelivered', 'XpAwarded', 'StreakUpdated', 'BadgeEarned',
        src/student/full-version/src/api/signalr.ts:87:      'TutorMessage', 'Error', 'CommandAck',

        $ grep -n 'BadgeEarned\|StudentBadge' src/actors/Cena.Actors/Bus/NatsSubjects.cs
        # (zero hits — no StudentBadgeEarned constant, no bridge case for it)

        $ sed -n '126,191p' src/api/Cena.Student.Api.Host/Hubs/NatsSignalRBridge.cs
        # Switch has: SessionStarted, SessionEnded, AnswerEvaluated, MasteryUpdated,
        # HintDelivered, MethodologySwitched, StagnationDetected, XpAwarded,
        # StreakUpdated, TutoringStarted, TutorMessage, TutoringEnded. No BadgeEarned.
  finding: The student SignalR client subscribes to the hub event `BadgeEarned`, but `NatsSignalRBridge.RouteEvent` has no `case` for it and `NatsSubjects` has no `StudentBadgeEarned` constant. When a student earns a badge, the `BadgeEarned_V1` event goes to Marten and the `ClassFeedItemProjection`, but the student's browser is never notified in real time. Conversely, the bridge emits `MethodologySwitched`, `StagnationDetected`, `TutoringStarted`, and `TutoringEnded` that the UI never subscribes to — those events go nowhere on the client side.
  root_cause: The two sides of the SignalR contract (hub publisher + client subscriber) are defined in two different repos/files with no shared contract artifact. Adding a new event on one side without updating the other is silent, because neither TypeScript nor C# can catch the mismatch at compile time.
  proposed_fix: (1) Add `NatsSubjects.StudentBadgeEarned = "badge_earned"`, a `BadgeEarnedEvent` contract in `Cena.Api.Contracts/Hub/`, a publisher call in whatever issues `BadgeEarned_V1`, and a `case NatsSubjects.StudentBadgeEarned` branch in `NatsSignalRBridge`. (2) Remove `MethodologySwitched`/`StagnationDetected`/`TutoringStarted`/`TutoringEnded` from the bridge OR add them to the UI subscribers — pick one. (3) Add a contract test that reflects over `ICenaClient` interface methods and asserts each one either maps to a bridge `case` or is hub-caller-only.
  task_body: |
    **Goal**: Reconcile the SignalR event contract between `NatsSignalRBridge` and the student web client.
    **Files to touch**:
      - src/actors/Cena.Actors/Bus/NatsSubjects.cs (add StudentBadgeEarned)
      - src/api/Cena.Api.Contracts/Hub/HubContracts.cs (add BadgeEarnedEvent, confirm ICenaClient has BadgeEarned)
      - src/api/Cena.Student.Api.Host/Hubs/NatsSignalRBridge.cs:126-191 (add BadgeEarned case, decide fate of unsubscribed events)
      - src/actors/Cena.Actors/Sessions/SessionNatsPublisher.cs (add PublishBadgeEarnedAsync or similar)
      - src/student/full-version/src/api/signalr.ts:78 (align UI event list with bridge)
    **Definition of Done**:
      - [ ] Every event the UI subscribes to has a corresponding publisher path
      - [ ] Every event the bridge emits is either subscribed in the UI or documented as hub-caller-only
      - [ ] Contract test fails if the two sides drift again
    **Reference**: FIND-arch-009 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-010
  severity: p1
  category: contract
  file: src/admin/full-version/src/pages/apps/focus/student/[id].vue
  line: 189
  evidence:
    - type: grep
      content: |
        $ sed -n '185,190p' src/admin/full-version/src/pages/apps/focus/student/[id].vue
        const fetchTimeline = async () => {
          timelineLoading.value = true
          try {
            const days = timelineRange.value === '7d' ? 7 : 30
            const data = await $api(`/admin/focus/students/${studentId.value}/timeline?days=${days}`)

        $ sed -n '64,69p' src/api/Cena.Admin.Api/AdminApiEndpoints.cs
        group.MapGet("/students/{studentId}/timeline", async (string studentId, string? period, ClaimsPrincipal user, IFocusAnalyticsService service) =>
        {
            var validPeriod = ParameterValidator.ValidatePeriod(period);
            var timeline = await service.GetStudentTimelineAsync(studentId, validPeriod, user);

        $ sed -n '12,17p' src/api/Cena.Admin.Api/Validation/ParameterValidator.cs
        private static readonly HashSet<string> ValidPeriods = new() { "7d", "30d", "90d", "365d" };
        public static string ValidatePeriod(string? period)
            => ValidPeriods.Contains(period ?? "30d") ? (period ?? "30d") : ...
  finding: Admin UI sends `?days=7` or `?days=30` as the timeline range, but the server binds a `string? period` query parameter (values `7d`/`30d`/`90d`/`365d`). `days=...` is ignored, `period` is null, `ValidatePeriod(null)` returns the default `"30d"`. When an admin clicks "7d" on a focus timeline, the server unconditionally returns 30 days of data and the UI displays the wrong window with no error — a silent contract drift.
  root_cause: UI and server were authored independently. No shared DTO or typed client guards this. The server does not 400 on the unrecognised query string — it just falls through to the default — so the bug is invisible to end-to-end smoke tests.
  proposed_fix: Change the UI on line 189 to send `?period=${timelineRange.value}` so the values match the server's whitelist. Add a server-side rejection: if the request has unknown query keys for endpoints declared via `MapGet`, return 400. Alternatively, add a typed admin client generated from a shared OpenAPI spec.
  task_body: |
    **Goal**: Fix the focus timeline query string mismatch so "7d" actually shows 7 days.
    **Files to touch**:
      - src/admin/full-version/src/pages/apps/focus/student/[id].vue:189 (days=... → period=${timelineRange.value})
    **Definition of Done**:
      - [ ] UI sends `period=7d` or `period=30d`
      - [ ] Manual click on "7d" tab shows only 7 days of data in the network panel
      - [ ] Screenshot check — curve reflects the selected range
    **Reference**: FIND-arch-010 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-011
  severity: p1
  category: dead-endpoint
  file: src/actors/Cena.Actors/Serving/QuestionPoolActor.cs
  line: 114
  evidence:
    - type: grep
      content: |
        $ grep -rn 'cena\.serve\.item\.published\|serve\.item\.published' src/
        src/actors/Cena.Actors/Serving/QuestionPoolActor.cs:114: await foreach (var msg in _nats.SubscribeAsync<byte[]>("cena.serve.item.published", cancellationToken: ct))
        # (exactly one hit — the subscription. No publisher emits this subject.)
  finding: `QuestionPoolActor` subscribes to `cena.serve.item.published` on NATS, but nothing in the codebase publishes to that subject. The actor's subscription loop runs forever and processes zero messages. This suggests either a dead hook (feature never shipped its publisher) or a typo in the subject name. Either way, it's wasting a NATS subscription and a background task.
  root_cause: Question pool/serving refactor left an orphan subscription behind. Without an integration test or dashboard metric on "cena.serve.* subscriber throughput", the dead hook is invisible.
  proposed_fix: Trace the git history on `QuestionPoolActor.cs` to identify which task was supposed to publish on `cena.serve.item.published`. Either wire up the publisher, or delete the subscription. Add a startup log that lists every `nats.SubscribeAsync` call so operators can spot orphans.
  task_body: |
    **Goal**: Either publish or delete the orphan `cena.serve.item.published` subscription.
    **Files to touch**:
      - src/actors/Cena.Actors/Serving/QuestionPoolActor.cs:114 (decision + implementation)
      - src/actors/Cena.Actors.Tests/Serving/ (add test that validates the new behavior)
    **Definition of Done**:
      - [ ] `grep -rn "cena\.serve\.item\.published" src/` has matching publisher and subscriber, OR zero hits
    **Reference**: FIND-arch-011 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-012
  severity: p1
  category: contract
  file: src/api/Cena.Admin.Api/ContentModerationService.cs
  line: 224
  evidence:
    - type: grep
      content: |
        $ grep -rn 'cena\.review\.item\.approved\|cena\.review\.item\.rejected' src/
        src/api/Cena.Admin.Api/ContentModerationService.cs:224: await _nats.PublishAsync("cena.review.item.approved", ...)
        src/api/Cena.Admin.Api/ContentModerationService.cs:259: await _nats.PublishAsync("cena.review.item.rejected", ...)
        # (exactly two publishers. No subscribers anywhere in src/.)
  finding: `ContentModerationService` publishes `cena.review.item.approved` and `cena.review.item.rejected` to NATS on every approve/reject action, but no subscriber exists. The publish is effectively a logging call with extra infrastructure — any downstream system that is supposed to react to moderation decisions (republish to JetStream, notify the author, update a projection) never fires.
  root_cause: The publish calls were added speculatively for future consumers that never landed. Without an integration test that sniffs NATS, it's impossible to notice that nothing listens.
  proposed_fix: Either (a) delete the publish calls if the downstream is truly unused, or (b) identify the missing consumer (probably an author-notification dispatcher) and wire it up. Prefer (a) + document the decision — avoid speculative publishes.
  task_body: |
    **Goal**: Either land a subscriber for moderation decisions or drop the speculative publishes.
    **Files to touch**:
      - src/api/Cena.Admin.Api/ContentModerationService.cs:224, 259
      - potentially src/actors/Cena.Actors/Notifications/ (new subscriber + notification document)
    **Definition of Done**:
      - [ ] `grep -rn "cena\.review\.item" src/` has matching publishers/subscribers OR zero hits
      - [ ] Decision recorded in a comment (or better, in an ADR)
    **Reference**: FIND-arch-012 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-013
  severity: p2
  category: contract
  file: src/api/Cena.Admin.Api/MessagingAdminService.cs
  line: 40
  evidence:
    - type: grep
      content: |
        $ sed -n '34,56p' src/api/Cena.Admin.Api/MessagingAdminService.cs
        public async Task<MessagingThreadListResponse> GetThreadsAsync(...)
        {
            await using var session = _store.QuerySession();
            IQueryable<ThreadSummary> query = session.Query<ThreadSummary>();
            if (!string.IsNullOrEmpty(threadType)) query = query.Where(t => t.ThreadType == threadType);
            if (!string.IsNullOrEmpty(participantId)) query = query.Where(t => t.ParticipantIds.Contains(participantId));
            ...

        $ diff <(grep -l "session\.Query<\|Query<\w\+>()" src/api/Cena.Admin.Api/*.cs | sort) \
               <(grep -l "TenantScope\|schoolId is null" src/api/Cena.Admin.Api/*.cs | sort)
        # 17 files query Marten without any TenantScope filter; MessagingAdminService is one.
  finding: `MessagingAdminService.GetThreadsAsync` and `GetThreadDetailAsync` query `ThreadSummary` without any tenant / school scoping. A School_Admin in School A can list and read threads that belong to students in School B. The admin dashboard for messaging thread oversight therefore leaks cross-tenant PII. The same pattern applies to `MessagingAdminEndpoints.cs` and several other admin services (17 files total lack TenantScope while they do `session.Query`).
  root_cause: `TenantScope.GetSchoolFilter(user)` was introduced mid-project for dashboard/focus/mastery services, but the messaging admin service was written earlier and never retrofitted. Consistency is not enforced by code review or by a shared base class.
  proposed_fix: Inject `ClaimsPrincipal user` into every admin service query method that doesn't already have it, call `TenantScope.GetSchoolFilter(user)`, and filter by school. Ideally introduce a Marten `IDocumentSessionListener` or a shared `ITenantAwareQueryBuilder<T>` so tenant filtering is impossible to forget. Then add a contract test that asserts every document type returned from an admin service has a school/org filter applied.
  task_body: |
    **Goal**: Close the messaging-admin cross-tenant leak and audit the other 16 admin services that query without TenantScope.
    **Files to touch**:
      - src/api/Cena.Admin.Api/MessagingAdminService.cs (add tenant filter on all queries)
      - src/api/Cena.Admin.Api/MessagingAdminEndpoints.cs (pass ClaimsPrincipal through)
      - 15+ other admin services per the comm -23 output
    **Definition of Done**:
      - [ ] Every admin service query that returns student-linked data filters by school (or explicitly documents why it is platform-wide)
      - [ ] Integration test: create threads in school A and school B, query as a school-A admin, assert only A threads returned
    **Reference**: FIND-arch-013 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-014
  severity: p2
  category: contract
  file: src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
  line: 307
  evidence:
    - type: grep
      content: |
        $ sed -n '300,320p' src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
        session.Store(comment);
        // Append event
        var commentEvent = new CommentPosted_V1(...);
        session.Events.Append(studentId, commentEvent);
        await session.SaveChangesAsync();
  finding: Several social endpoints do a dual write: they `session.Store(...)` a read-model document AND `session.Events.Append(...)` a `*_V1` event in the same transaction. This is not event sourcing — it is imperative CRUD with a side-effect event. If a projection is added later that reads `CommentPosted_V1` and writes its own CommentDocument, the write amplifies and the two copies can diverge. Aggregates are bypassed, no `Apply(...)` logic runs, and no invariant is enforced.
  root_cause: The DDD guidance (event sourcing for state changes) collides with the need to return a fresh read model from the POST response. The chosen shortcut is dual-write, which violates the rule.
  proposed_fix: Use a Marten `SingleStreamProjection<CommentDocument, string>` that reacts to `CommentPosted_V1`, and only `session.Events.Append` in the endpoint. Return a predicted DTO from the in-memory event data rather than reading the document back. Alternatively, use `Marten`'s projection types that can serve live read models from the write session so the endpoint doesn't have to store the document itself.
  task_body: |
    **Goal**: Stop dual-writing read-model documents alongside events in the social endpoints.
    **Files to touch**:
      - src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs (remove session.Store of CommentDocument, FriendshipDocument, StudyRoomDocument, StudyRoomMembershipDocument where an event is also appended)
      - src/actors/Cena.Actors/Projections/ (add SingleStreamProjection for each affected document type)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register the projections)
    **Definition of Done**:
      - [ ] Endpoints only call session.Events.Append, never session.Store for these document types
      - [ ] Marten projections own the read model
      - [ ] Regression tests prove the read model is populated
    **Reference**: FIND-arch-014 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-015
  severity: p2
  category: contract
  file: src/api/Cena.Admin.Api/QuestionBankService.cs
  line: 137
  evidence:
    - type: grep
      content: |
        $ grep -n 'TODO\|editor = "admin"' src/api/Cena.Admin.Api/QuestionBankService.cs
        137: const string editor = "admin"; // TODO: extract from auth context
  finding: `QuestionBankService.UpdateQuestionAsync` hardcodes the editor name as the literal string `"admin"` for every `QuestionStemEdited_V1` / `QuestionOptionChanged_V1` event, regardless of which admin user actually made the change. The event stream loses forensic provenance — any audit asking "who edited question X" will return "admin" for every edit in history.
  root_cause: A `ClaimsPrincipal` was not passed into the service when it was first written, and the shortcut stuck.
  proposed_fix: Thread `ClaimsPrincipal` (or at least `editorId`) from the endpoint through to `UpdateQuestionAsync`, use `ctx.User.FindFirstValue(...)` to extract the real admin user id/email, and record that in the event. Backfill decision for historical events — probably cannot be fixed retroactively.
  task_body: |
    **Goal**: Record the real editor identity on question-edit events instead of hardcoded "admin".
    **Files to touch**:
      - src/api/Cena.Admin.Api/QuestionBankService.cs:137 (and the public API signature)
      - src/api/Cena.Admin.Api/ (caller endpoints)
    **Definition of Done**:
      - [ ] `grep -rn '"admin"' src/api/Cena.Admin.Api/QuestionBankService.cs` returns zero matches for the literal
      - [ ] Unit test asserts the editor id on the appended event matches the calling user
    **Reference**: FIND-arch-015 in docs/reviews/agent-1-arch-findings.md

- id: FIND-arch-016
  severity: p3
  category: contract
  file: src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs
  line: 22
  evidence:
    - type: grep
      content: |
        $ sed -n '14,46p' src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs
        /// Listens for: BadgeEarned, LearningSessionEnded (highlights), and future events.
        ...
        public void Project(BadgeEarned_V1 e, IDocumentOperations ops) { ... }
        # (no Project method for LearningSessionEnded_V1 — comment claims support)
  finding: `ClassFeedItemProjection` has a docblock claiming it listens for `LearningSessionEnded` and "future events", but only implements the `BadgeEarned_V1` case. The class feed therefore silently drops session highlights. Low-severity because the comment is the only lying label — UI pages don't yet ask for session highlights — but it's a trap for the next implementer.
  root_cause: Comment written optimistically during scaffolding, not synced when the rest of the Project overloads were deferred.
  proposed_fix: Either implement `Project(LearningSessionEnded_V1 e, ...)` with real logic, or remove the "LearningSessionEnded" part of the comment so the docblock matches the code.
  task_body: |
    **Goal**: Honest docblock on ClassFeedItemProjection.
    **Files to touch**:
      - src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs (docblock or new Project method)
    **Definition of Done**:
      - [ ] Comment accurately describes what the projection handles
    **Reference**: FIND-arch-016 in docs/reviews/agent-1-arch-findings.md

## Areas not covered
- Agent 2 owns auth/ResourceOwnershipGuard depth audit; I flagged MessagingAdminService tenant leak only because its controllers have auth but the queries do not scope. Did not exhaustively audit every admin service for the same pattern — left that sweep to FIND-arch-013's "audit the other 16".
- Did not validate correctness of NATS header propagation (W3C trace context) or JetStream stream configuration — that touches Agent 3's perf/projection domain.
- Did not audit Cena.Actors.Host's SSE endpoints for contract drift — that host is out of scope for the student/admin API split.
- Did not verify the Cena.Db.Migrator schema matches the Marten-declared event types — that's a database migration concern.

## Enqueued Tasks

| Finding | Priority | Task ID |
|---|---|---|
| FIND-arch-001 | critical | t_99a1fcd89ee9 |
| FIND-arch-002 | critical | t_08e776c6db85 |
| FIND-arch-003 | critical | t_3bd146ca43ba |
| FIND-arch-004 | critical | t_8d7b0c710c68 |
| FIND-arch-005 | critical | t_6b45c18c0c44 |
| FIND-arch-006 | critical | t_6c7776761bc1 |
| FIND-arch-007 | critical | t_572208ec8ba7 |
| FIND-arch-008 | high | t_fc37b5bee99a |
| FIND-arch-009 | high | t_68470a2ca105 |
| FIND-arch-010 | high | t_d2c63f27e891 |
| FIND-arch-011 | high | t_5d9d73ccd6c4 |
| FIND-arch-012 | high | t_040268b54638 |
