---
agent: arch
lens: System, Contract & Event-Schema Architect
run: cena-review-v2-reverify
date: 2026-04-11
worktree: .claude/worktrees/review-arch
branch: claude-subagent-arch/cena-reverify-2026-04-11
base_sha: cc3f702
preflight_report: docs/reviews/reverify-2026-04-11-preflight.md
prior_findings_file: docs/reviews/agent-1-arch-findings.md
prior_high_id_in_queue: FIND-arch-012
prior_high_id_in_findings_file: FIND-arch-016
new_id_start: FIND-arch-017
---

# Agent `arch` — Re-verification Findings (2026-04-11)

## Method

Worktree built fresh from `origin/main` at `cc3f702`. Read-only audit.
Every finding is backed by file:line evidence (`rg`/file read) inside
this worktree. No symptom-based or speculative findings filed.

`FIND-arch-013..016` already exist in `docs/reviews/agent-1-arch-findings.md`
from the v1 run (P2/P3, never enqueued). To avoid ID collision and to
keep the queue cross-referenceable, this re-verification numbers new
findings starting at **FIND-arch-017**.

## Counts

| Severity | Count |
|---|---|
| P0 | 4 |
| P1 | 5 |
| P2 | 4 |
| P3 | 1 |
| **total new** | **14** |

| Verdict on prior arch findings | Count |
|---|---|
| regressions | 0 |
| fake-fixes | 0 |
| verified-fixed (per Phase-0 preflight) | 12/12 |

The preflight already cleared all 12 enqueued FIND-arch-001..012 as
verified-fixed. This report adds NEW findings discovered while drilling
into surrounding code (per the v2 instruction to treat verified-fixed
areas as lower-priority but not skipped).

---

## Findings

```yaml
- id: FIND-arch-017
  severity: p0
  category: stub
  file: src/student/full-version/src/plugins/fake-api/index.ts
  line: 142
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ grep -rn "import\.meta\.env\.\(DEV\|PROD\)" src/student/full-version/src/plugins/fake-api/
        (no matches)

        $ sed -n '142,156p' src/student/full-version/src/plugins/fake-api/index.ts
        export default function () {
          // Defense-in-depth: even after admin's migration lands, a user with a
          // stale browser profile could still carry poison cookies on first load.
          // Scrub them before MSW's cookie parser runs, not after.
          scrubInvalidCookieNames()

          const workerUrl = `${import.meta.env.BASE_URL ?? '/'}mockServiceWorker.js`

          worker.start({
            serviceWorker: {
              url: workerUrl,
            },
            onUnhandledRequest: 'bypass',
          })
        }

        $ sed -n '42,53p' src/student/full-version/src/@core/utils/plugins.ts
        export const registerPlugins = (app: App) => {
          const imports = import.meta.glob<{ default: (app: App) => void }>(
            ['../../plugins/*.{ts,js}', '../../plugins/*/index.{ts,js}'],
            { eager: true })
          ...
          importPaths.forEach(path => { pluginImportModule.default?.(app) })
        }

        $ sed -n '46,48p' src/student/full-version/.gitignore
        # public/mockServiceWorker.js   ← line is COMMENTED OUT, file IS shipped

        $ ls src/student/full-version/public/mockServiceWorker.js
        public/mockServiceWorker.js
  finding: |
    The student web fake-api plugin (MSW) is loaded and started
    UNCONDITIONALLY in every Vite build, including production. The header
    comment in `plugins/fake-api/index.ts:23` says "Production bypasses
    MSW entirely" — that statement is false. There is no
    `import.meta.env.DEV/PROD` gate, no env flag, no router exclusion.
    `registerPlugins()` walks `import.meta.glob('../../plugins/*/index.ts',
    { eager: true })` which Vite resolves at build time, bundles the
    fake-api `default` function, and invokes it on every app boot. The
    `mockServiceWorker.js` is committed (.gitignore line is commented out)
    so it ships with the SPA, and MSW intercepts every `/api/*` request
    in production, including 13 student-facing handler categories
    (`student-sessions`, `student-tutor`, `student-gamification`,
    `student-social`, `student-analytics`, `student-me`, `student-knowledge`,
    `student-notifications`, `student-challenges`, plus the Vuexy demo
    handlers). Combined with `onUnhandledRequest: 'bypass'`, ANY request
    that matches a handler returns canned data; only unmatched requests
    fall through to the real `Cena.Student.Api.Host`. Real student data
    cannot be trusted in production until this is fixed. Per the user's
    "labels match data" rule and the locked "no stubs — production grade"
    rule, this is the most severe lying-label finding in the codebase.
  root_cause: |
    The Vuexy template ships fake-api as a dev-loop convenience and the
    Cena port inherited the loader pattern (`import.meta.glob`) without
    adding the env gate that Nuxt's official examples wrap the same call
    in. The header comment was added during FIND-ux-005 fix work to
    document intent, but the gate itself was never written.
  proposed_fix: |
    Two-layer fix:
      1. Inside `plugins/fake-api/index.ts`, wrap `worker.start()` in
         `if (import.meta.env.DEV) { ... }` so the call is tree-shaken
         out of production bundles.
      2. Inside `vite.config.ts`, add an `import.meta.glob` exclusion
         pattern OR a custom plugin that strips the `fake-api` directory
         from the production glob results, so MSW handlers themselves are
         not bundled at all in `vite build` mode.
      3. Add a Playwright e2e test that runs against `vite preview` and
         asserts `window.msw === undefined` and that an unstubbed
         `/api/me` request reaches the real backend (returns 401, not the
         MSW mock 200).
      4. Re-enable the `# public/mockServiceWorker.js` gitignore line and
         move the worker generation to a dev-only npm script.
  test_required: |
    `tests/e2e/no-msw-in-production.spec.ts`:
    1. `npm run build && npm run preview`
    2. visit `/`
    3. assert `window.navigator.serviceWorker.controller === null`
    4. fetch `/api/me` → expect 401 from real backend, NOT 200 from MSW
  task_body: |
    **Goal**: Stop MSW from intercepting `/api/*` calls in production
    student-web builds. The student app currently ships with mock data
    overriding real backend calls because the fake-api plugin's
    `worker.start()` is invoked unconditionally at app boot.

    **Files to read first**:
      - src/student/full-version/src/plugins/fake-api/index.ts
      - src/student/full-version/src/@core/utils/plugins.ts
      - src/student/full-version/vite.config.ts
      - src/student/full-version/.gitignore

    **Files to touch**:
      - src/student/full-version/src/plugins/fake-api/index.ts
        (gate `worker.start()` behind `import.meta.env.DEV`)
      - src/student/full-version/vite.config.ts
        (add a build-mode exclusion so the fake-api dir is not bundled)
      - src/student/full-version/.gitignore
        (re-enable `public/mockServiceWorker.js` line; add a dev-only
         postinstall to generate it)
      - tests/e2e/student-web/no-msw-in-production.spec.ts (new)

    **Definition of Done**:
      - [ ] `npm run build` produces a `dist/` bundle that has zero
            references to `setupWorker`, `fake-api`, or `mockServiceWorker`.
            Verify with `grep -r 'setupWorker\|fake-api' src/student/full-version/dist/`.
      - [ ] `npm run preview` followed by manual `/api/me` request
            returns the real backend's 401 (not the MSW mock 200).
      - [ ] New Playwright e2e test asserts the above.
      - [ ] FIND-ux-005's "no stub leakage" guard script still passes.
      - [ ] Dev loop (`npm run dev`) still uses MSW for offline work.

    **Reporting requirements**:
      - In your --result, paste the byte-size delta on `dist/assets/index-*.js`
        before and after the fix (it should shrink by the size of all
        student-* MSW handlers, ~50–200 KB).
      - Paste the new e2e test output and the curl/network log showing
        the unmocked /api/me hitting the real backend.

    **Reference**: FIND-arch-017 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-018
  severity: p0
  category: stub
  file: src/actors/Cena.Actors/Notifications/NotificationChannelService.cs
  line: 177
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '177,230p' src/actors/Cena.Actors/Notifications/NotificationChannelService.cs
        private async Task<bool> SendWebPushAsync(...)
        {
            // STB-07c: Web Push stub implementation
            _logger.LogInformation("[WEB PUSH] Would send to {Endpoint}: ...");
            // In production, this would use WebPush library with VAPID keys
            // Example: await _webPushClient.SendNotificationAsync(...);
            await Task.Delay(10, ct); // Simulate async work
            return true;
        }

        private async Task<bool> SendEmailAsync(...)
        {
            // STB-07c: Email stub implementation
            _logger.LogInformation("[EMAIL] Would send to {Email}: ...");
            // In production, this would use SMTP or email service (SendGrid, SES, etc.)
            await Task.Delay(10, ct); // Simulate async work
            return true;
        }

        private async Task<bool> SendSmsAsync(...)
        {
            // STB-07c: SMS stub implementation
            _logger.LogInformation("[SMS] Would send to {Phone}: ...");
            // In production, this would use Twilio or similar SMS gateway
            await Task.Delay(10, ct); // Simulate async work
            return true;
        }
  finding: |
    `NotificationChannelService` claims to dispatch notifications across
    Web Push, Email, and SMS, but all three channels are stubs that log
    "[WEB PUSH] Would send..." / "[EMAIL] Would send..." / "[SMS] Would
    send..." and return `true` without actually sending anything.
    Callers (`NotificationDispatcher`, `OutreachEngagementService`)
    treat the `true` return value as success, persist a "delivered"
    notification, and update outreach engagement metrics. End-state:
    a child waiting for a parent-consent reset email or a teacher
    waiting for a behavioural alert SMS will never receive the message,
    yet the system reports 100% delivery success. Banned per the user's
    locked rule "no stubs — production grade" (2026-04-11) and per the
    "labels match data" rule (the boolean return value lies).
  root_cause: |
    STB-07c was originally enqueued as a "Phase 1 stub → Phase 1b real"
    pair, but the Phase 1b ticket was never opened. The user has since
    banned the entire stub-then-harden pattern.
  proposed_fix: |
    Three independent real implementations:
      1. Web Push via the `WebPush` NuGet package + VAPID key pair.
         VAPID public key shipped to client at registration time.
      2. Email via `MailKit` against the existing SMTP env vars (or
         SendGrid if a key is configured).
      3. SMS via Twilio SDK (skip if no Twilio creds — return false,
         not true).
    Each method must return `false` on send failure and surface the
    error to the caller. Add structured log fields `channel`,
    `notification_id`, `result`, and `error_code` for observability.
  test_required: |
    `NotificationChannelServiceIntegrationTests` (new):
      - WebPush: register a fake VAPID subscription, assert HTTP POST
        to the subscription endpoint with the correct body.
      - Email: assert MailKit `SmtpClient` is invoked with the
        templated body (use a fake `ISmtpClient`).
      - SMS: skip if no Twilio creds; otherwise mock Twilio's
        `MessageResource.CreateAsync` and assert it is called.
      - All three: failure path returns `false`, never `true`.
  task_body: |
    **Goal**: Replace the three "Would send" stubs with real Web Push,
    SMTP/SendGrid, and Twilio implementations. The user has banned
    stubs in production paths.

    **Files to read first**:
      - src/actors/Cena.Actors/Notifications/NotificationChannelService.cs
      - src/actors/Cena.Actors/Notifications/NotificationDispatcher.cs
      - src/shared/Cena.Infrastructure/Documents/NotificationDocuments.cs
      - src/actors/Cena.Actors/Events/NotificationEvents.cs

    **Files to touch**:
      - src/actors/Cena.Actors/Notifications/NotificationChannelService.cs
        (replace SendWebPushAsync, SendEmailAsync, SendSmsAsync)
      - src/actors/Cena.Actors/Notifications/IWebPushClient.cs (new)
      - src/actors/Cena.Actors/Notifications/WebPushClient.cs (new — wraps WebPush.NetCore)
      - src/actors/Cena.Actors/Notifications/SmtpEmailSender.cs (new — MailKit)
      - src/actors/Cena.Actors/Notifications/TwilioSmsSender.cs (new)
      - appsettings.Development.json (add VAPID + SMTP + Twilio sections,
        leave keys blank for dev)
      - src/actors/Cena.Actors.Tests/Notifications/NotificationChannelServiceTests.cs (new)

    **Definition of Done**:
      - [ ] `grep -n "Would send\|stub implementation\|Simulate async work" src/actors/Cena.Actors/Notifications/` returns zero
      - [ ] All three Send* methods return `false` on failure with a
            specific error reason (not just a boolean)
      - [ ] Notification persistence still happens BEFORE the channel
            send attempt, so a failed send does not lose the in-app row
      - [ ] Integration test asserts each channel calls its real client
      - [ ] Per-tenant + global rate limit on each channel (cost guardrail)

    **Reporting requirements**:
      - In your --result, paste a sample log line from each successful
        channel showing the structured fields (`channel`, `notification_id`,
        `result`, `error_code`).
      - Confirm the in-app fallback still works when an external channel
        is down.

    **Reference**: FIND-arch-018 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-019
  severity: p0
  category: stub
  file: src/api/Cena.Admin.Api/EventStreamService.cs
  line: 145
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '145,156p' src/api/Cena.Admin.Api/EventStreamService.cs
        public Task<RetryMessageResponse> RetryMessageAsync(string id)
        {
            _logger.LogInformation("Retrying DLQ message {MessageId}", id);
            // Real retry would re-enqueue to outbox; for now acknowledge the request
            return Task.FromResult(new RetryMessageResponse(id, true, null));
        }

        public Task<BulkRetryResponse> BulkRetryAsync(IReadOnlyList<string> ids)
        {
            _logger.LogInformation("Bulk retrying {Count} DLQ messages", ids.Count);
            return Task.FromResult(new BulkRetryResponse(ids.Count, 0, new List<string>()));
        }
  finding: |
    The DLQ admin endpoints `POST /api/admin/events/dead-letters/{id}/retry`
    and `POST /api/admin/events/dead-letters/bulk-retry` (admin UI calls
    them per `useDeadLettersStore`) return `success: true` for every
    retry without ever re-enqueuing the failed event to the outbox or
    deleting it from the `NatsOutboxDeadLetter` table. Operators clicking
    "Retry" or "Bulk retry all" in the admin live monitor see green
    success toasts while the DLQ depth never decreases and no event is
    re-published. Combined with `CheckDlqDepthAsync` returning real
    counts, this is a worst-case lying label: the metric is honest, the
    action is fake.
  root_cause: |
    EventStreamService was scaffolded for the admin UI before the
    NatsOutboxPublisher retry path existed. When the publisher landed it
    gained an in-memory `_retryCountBySequence` dictionary and a
    DeadLetter document model, but the admin retry endpoint was never
    re-wired to clear the entry, increment a "force-retry" counter, or
    re-publish the event from Marten via NatsOutboxPublisher.
  proposed_fix: |
    Make `RetryMessageAsync(id)`:
      1. Look up the `NatsOutboxDeadLetter` document by `Guid.Parse(id)`.
      2. Read the original event from `Marten.Events.QueryRawEventDataOnly`
         using `EventSequence`.
      3. Call `NatsOutboxPublisher.PublishOneAsync(event)` (a new public
         method that bypasses the cycle and force-publishes).
      4. On success, delete the DLQ document.
      5. On failure, increment a `force_retry_failed` counter and return
         `success: false` with the upstream error.
    `BulkRetryAsync` is the same in a loop with per-id error tracking
    so a single failure doesn't abort the batch.
  test_required: |
    `EventStreamServiceRetryTests` (new):
      - Seed a NatsOutboxDeadLetter row + the original event in Marten.
      - Call RetryMessageAsync(id).
      - Assert the DLQ row is deleted, the event is republished to NATS
        (use a fake INatsConnection that records publish calls).
      - Negative test: when the publish fails, the DLQ row STAYS and
        the response is `success=false`.
  task_body: |
    **Goal**: Make admin DLQ retry actually retry, not lie.

    **Files to read first**:
      - src/api/Cena.Admin.Api/EventStreamService.cs
      - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
      - src/api/Cena.Admin.Api/AdminApiEndpoints.cs (the
        MapEventStreamEndpoints group)
      - src/admin/full-version/src/stores/useDeadLettersStore.ts

    **Files to touch**:
      - src/api/Cena.Admin.Api/EventStreamService.cs (real retry logic)
      - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
        (expose `PublishOneAsync(long sequence, ...)`)
      - src/api/Cena.Admin.Api.Tests/EventStreamServiceRetryTests.cs (new)

    **Definition of Done**:
      - [ ] `grep -n "Real retry would" src/api/Cena.Admin.Api/EventStreamService.cs` returns zero
      - [ ] RetryMessageAsync deletes the DLQ row only on successful republish
      - [ ] BulkRetryAsync per-id error tracking; partial failures reported
      - [ ] Integration test against a Marten + fake NATS asserts the row
            is gone after a successful retry
      - [ ] Admin UI sees the DLQ depth drop after a successful bulk retry
            (verify in admin live monitor)

    **Reporting requirements**:
      - Paste the integration test run output.
      - Paste the new public PublishOneAsync signature.

    **Reference**: FIND-arch-019 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-020
  severity: p0
  category: stub
  file: src/api/Cena.Admin.Api/IngestionSettingsService.cs
  line: 163
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '163,200p' src/api/Cena.Admin.Api/IngestionSettingsService.cs
        public Task<bool> TestEmailConnectionAsync(EmailIngestionConfig config)
        {
            // Placeholder: validate required fields are present
            ...
            // In production this would attempt an actual IMAP connection
            _logger.LogInformation("Email connection test (placeholder) for {Host}:{Port}", ...);
            return Task.FromResult(true);
        }

        public Task<bool> TestCloudDirAsync(CloudDirConfig config)
        {
            ...
            // S3 / GCS / Azure: placeholder success (would use SDK in production)
            _logger.LogInformation("Cloud dir test (placeholder) for provider={Provider}, path={Path}", ...);
            return Task.FromResult(true);
        }

        $ grep -rn "EmbeddingAdminService\.cs" src/api/Cena.Admin.Api/EmbeddingAdminService.cs:289
        // In production, this would publish a NATS message to trigger the
        // reindex pipeline. For now, we log the request and return the job ID.
  finding: |
    Three more "test connection / kick off pipeline" admin endpoints are
    stubs that return `true` / a fake jobId without doing the real work:
      1. `IngestionSettingsService.TestEmailConnectionAsync` returns
         `true` for any IMAP config with a non-empty host/port — never
         dials the IMAP server.
      2. `IngestionSettingsService.TestCloudDirAsync` returns `true` for
         any S3/GCS/Azure path without contacting the provider; only
         `local` paths get a real `Directory.Exists` check.
      3. `EmbeddingAdminService.RequestReindexAsync` (line 289) counts
         how many blocks WOULD be reindexed but never publishes the
         NATS reindex command. Admin sees a jobId returned, no work
         happens, no events flow.
    All three trip the user's "no stubs" rule and ship lying success
    signals to admin operators.
  root_cause: |
    The same Phase-1 stub pattern banned by the user on 2026-04-11.
    These endpoints predate the ban and were never retroactively hardened.
  proposed_fix: |
    1. `TestEmailConnectionAsync`: use `MailKit.ImapClient.ConnectAsync`
       + `AuthenticateAsync` with a 5-second timeout. Surface the
       specific failure (DNS, TCP, TLS, AUTH).
    2. `TestCloudDirAsync`: for `s3`, instantiate AWS SDK
       `IAmazonS3.GetBucketLocationAsync(bucket)`; for `gcs`, use
       Google.Cloud.Storage.V1; for `azure`, use Azure.Storage.Blobs.
       Each provider returns a real reachability + permission check.
    3. `RequestReindexAsync`: publish a `cena.embeddings.reindex.request`
       message via INatsConnection with the scope/filter/jobId, and
       wire a subscriber in `EmbeddingIngestionHandler` that processes
       it (or in a new ReindexCoordinatorService).
  test_required: |
    Integration tests against fake IMAP/S3 endpoints (e.g. LocalStack
    for S3) that prove a real network call is made.
  task_body: |
    **Goal**: Replace three placeholder admin endpoints with real
    network calls. No more `return Task.FromResult(true)` lies.

    **Files to touch**:
      - src/api/Cena.Admin.Api/IngestionSettingsService.cs
        (TestEmailConnectionAsync, TestCloudDirAsync)
      - src/api/Cena.Admin.Api/EmbeddingAdminService.cs
        (RequestReindexAsync — publish NATS command)
      - src/actors/Cena.Actors/Services/EmbeddingIngestionHandler.cs
        OR a new ReindexCoordinatorService to handle the reindex command
      - Cena.Admin.Api.csproj (add MailKit, AWSSDK.S3, Google.Cloud.Storage.V1,
        Azure.Storage.Blobs as needed)

    **Definition of Done**:
      - [ ] `grep -n "Placeholder\|placeholder\|would use" src/api/Cena.Admin.Api/IngestionSettingsService.cs src/api/Cena.Admin.Api/EmbeddingAdminService.cs` returns zero
      - [ ] Each test method returns `false` with a specific reason on
            real-world failure modes (DNS, auth, permission denied)
      - [ ] Reindex publishes a real NATS command consumed by a real
            background worker that updates `ReindexJobDocument` rows
      - [ ] Admin UI shows real connection-test results

    **Reporting requirements**:
      - Paste sample success and failure output for each provider.
      - Confirm the reindex job actually completes (not just enqueues).

    **Reference**: FIND-arch-020 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-021
  severity: p1
  category: contract
  file: src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs
  line: 117
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -n 'cena\.ingest\.' src/ --type cs
        src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs:117:    await _nats.PublishAsync("cena.ingest.file.received", ...
        src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs:242:    await _nats.PublishAsync("cena.ingest.content.extracted", ...
        src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs:320:    await _nats.PublishAsync("cena.ingest.item.classified", ...
        src/actors/Cena.Actors/Services/EmbeddingIngestionHandler.cs:29:    private const string NatsSubject = "cena.ingest.content.extracted";

        $ rg -n 'cena\.student\.escalation\|cena\.admin\.methodology' src/ --type cs
        src/actors/Cena.Actors/Students/StudentActor.Queries.cs:92:        await _nats.PublishAsync("cena.student.escalation", ...
        src/actors/Cena.Actors/Students/StudentActor.Methodology.cs:104: await _nats.PublishAsync("cena.admin.methodology.confidence-reached", ...
        src/actors/Cena.Actors/Students/StudentActor.Methodology.cs:188: await _nats.PublishAsync("cena.admin.methodology.switch-deferred", ...
        src/actors/Cena.Actors/Students/StudentActor.Methodology.cs:239: await _nats.PublishAsync("cena.student.escalation", ...
  finding: |
    Five orphan NATS publishers — subjects produced with zero subscribers
    anywhere in the source tree. Confirmed: `NatsSubjects.AllEvents`
    (`cena.events.>`) does NOT match these subjects, so the catch-all
    `NatsEventSubscriber.cs` does not pick them up either:

      1. `cena.ingest.file.received` (IngestionOrchestrator:117)
      2. `cena.ingest.item.classified` (IngestionOrchestrator:320)
      3. `cena.student.escalation` (StudentActor.Queries:92, .Methodology:239)
      4. `cena.admin.methodology.confidence-reached` (StudentActor.Methodology:104)
      5. `cena.admin.methodology.switch-deferred` (StudentActor.Methodology:188)

    Each publish-only subject is a contract liability: a downstream
    consumer that NEEDS one of these (e.g. an admin alert when a student
    is escalated, or an ingestion-status SignalR push) will silently get
    no events when it's added later. This is the same anti-pattern that
    FIND-arch-011 / FIND-arch-012 fixed for `cena.serve.item.published`
    and `cena.review.item.*`. The fix-or-delete decision here belongs
    to the arch agent, not the implementer.
  root_cause: |
    The same drift mode as FIND-arch-011/012 — actor code emits NATS
    "side-channel" events for hypothetical downstream consumers and the
    consumers are never built. There is no compile-time or test-time
    enforcement that every published subject has at least one wired
    subscriber (or an explicit `[OrphanByDesign("reason")]` opt-out).
  proposed_fix: |
    For each of the five subjects, the team must decide:
      A. Wire a real subscriber that does something user-visible
         (admin alert, SignalR push, audit log).
      B. Delete the publish call AND the subject constant.
    For `cena.student.escalation` and `cena.admin.methodology.*`, option
    A is almost certainly correct — admin teachers should be notified
    when a student is escalated or when the system is about to switch
    methodology — so a `MethodologyAdminAlertHandler` BackgroundService
    in `Cena.Admin.Api` is the natural target.
    For `cena.ingest.file.received` and `cena.ingest.item.classified`,
    if the SignalR live-monitor admin page does not consume them, delete
    them.
    Add a contract test `OrphanSubjectGuardTests.cs` that walks every
    `_nats.PublishAsync(...)` literal and asserts every subject is
    either subscribed by some `_nats.SubscribeAsync` call OR present in
    a maintained `KnownOrphanSubjects` allowlist.
  test_required: |
    `OrphanSubjectGuardTests.NoUnauthorizedOrphanPublishers_OnMain`
    walks the source tree and fails CI on any new orphan.
  task_body: |
    **Goal**: Reconcile every NATS subject in the codebase to a known
    publisher AND a known subscriber, OR delete it. Add a CI guard.

    **Files to read first**:
      - src/actors/Cena.Actors/Bus/NatsSubjects.cs
      - src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs
      - src/actors/Cena.Actors/Students/StudentActor.Queries.cs
      - src/actors/Cena.Actors/Students/StudentActor.Methodology.cs
      - docs/reviews/agent-1-arch-findings.md (FIND-arch-011 + 012 for the
        canonical fix pattern)

    **Files to touch**:
      - src/actors/Cena.Actors/Notifications/MethodologyAdminAlertHandler.cs (new BackgroundService subscribing to cena.student.escalation + cena.admin.methodology.*)
      - src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs (register the handler)
      - OR delete the orphan publish calls if no consumer makes business sense
      - src/actors/Cena.Actors.Tests/Bus/OrphanSubjectGuardTests.cs (new)

    **Definition of Done**:
      - [ ] All five orphan publishers either have a real subscriber or
            are deleted
      - [ ] OrphanSubjectGuardTests passes on the resulting tree
      - [ ] CI runs the guard on every PR

    **Reporting requirements**:
      - For each of the five subjects, state the decision (wired vs deleted)
        and the reason.
      - Paste the orphan-subject test output.

    **Reference**: FIND-arch-021 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-022
  severity: p1
  category: contract
  file: src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
  line: 226
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -n 'cena\.durable\.(learner|pedagogy|engagement|outreach|system|curriculum)' src/ --type cs
        src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs:60: stemSubject = ... DurableCurriculumEvent("QuestionStemEdited_V1")
        src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs:61: optionSubject = ... DurableCurriculumEvent("QuestionOptionChanged_V1")
        src/actors/Cena.Actors/Bus/NatsSubjects.cs:141: const string DurableCurriculumPrefix = "cena.durable.curriculum"
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:304: $"cena.durable.learner.{eventTypeName}"
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:307: $"cena.durable.pedagogy.{eventTypeName}"
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:310: $"cena.durable.engagement.{eventTypeName}"
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:312: $"cena.durable.outreach.{eventTypeName}"
        src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs:319: $"cena.durable.system.{eventTypeName}"

        $ sed -n '225,227p' src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
        // Publish to NATS and wait for confirmation
        await _nats.PublishAsync(subject, payload, headers: headers, cancellationToken: ct);
  finding: |
    Two coupled architecture problems in `NatsOutboxPublisher`:

    1. **Five orphan durable categories.** The publisher routes events
       into six `cena.durable.{category}.{EventTypeName}` namespaces:
       `learner`, `pedagogy`, `engagement`, `outreach`, `system`,
       `curriculum`. Only `curriculum` has a known subscriber
       (`ExplanationCacheInvalidator`). The other five are pumped on
       every cycle with zero consumers, so every `ConceptAttempted_V2`,
       `MasteryUpdated_*`, `SessionStarted_*`, `XpAwarded_V1`,
       `BadgeEarned_*`, `OutreachSent_*`, `FocusUpdated_*` is published
       into the void. Cost waste + no audit trail consumer.

    2. **"Durable" is a label lie.** The class name says
       "DurableOutbox" and the comments say "JetStream durability"
       (lines 68, 292), but the actual call at line 226 is
       `_nats.PublishAsync(subject, payload)` — that's CORE NATS, not
       JetStream. Core NATS is fire-and-forget; messages with no
       subscriber are dropped at the broker. The Marten event store
       still replays from the checkpoint on restart so the SOURCE side
       is durable, but the wire is not. The comments and class name
       claim "durable" delivery semantics that the code does not
       provide.

    Combined, this means a downstream consumer connecting to one of the
    six categories cannot rely on getting the historical backlog —
    only events that arrive AFTER it subscribes will be visible (and
    only if it's connected at the moment of publish). This is the
    opposite of what "durable outbox" should mean.
  root_cause: |
    The outbox was scaffolded as the boundary between Marten and a
    future JetStream pipeline. The JetStream stream definitions
    (`StreamConfig` calls) and the `js.PublishAsync` call were never
    written; the team kept calling the core-NATS `_nats.PublishAsync`
    and the comments / names were not corrected.
  proposed_fix: |
    Two-track fix:
      1. **Wire JetStream for real**: At Actor Host startup, call
         `js.CreateOrUpdateStreamAsync(new StreamConfig { Name =
         "CENA_DURABLE_LEARNER", Subjects = ["cena.durable.learner.>"],
         ... })` for each of the six categories. Then change line 226
         to `await js.PublishAsync(subject, payload, ...)`. Use a
         `ConcurrentDictionary<string, INatsJSStream>` cache to avoid
         per-cycle overhead.
      2. **Decide each category's consumer story**: For each of the
         five orphan categories, either spawn a real durable consumer
         (e.g. `LearnerEventConsumer` for analytics ingest into a
         time-series store) or delete the routing branch from
         `GetDurableSubject` and have the events fall through to
         `cena.durable.system.{type}` where they can be archived in a
         single retention-bounded stream.
      3. **Rename or remove the "durable" label** until JetStream is
         actually wired, OR add a `[NotYetDurable]` attribute on the
         class with a tracking ticket. The current naming + comments
         are a lying-label finding.
  test_required: |
    Integration test against a real NATS server: publish an event,
    bounce both the publisher and the broker, then start a consumer —
    the consumer must see the event (proves JetStream durability).
  task_body: |
    **Goal**: Either wire NatsOutboxPublisher to JetStream for real OR
    rename it. The "durable" promise must hold or be removed.

    **Files to read first**:
      - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
      - src/actors/Cena.Actors/Bus/NatsSubjects.cs
      - src/actors/Cena.Actors/Explanations/ExplanationCacheInvalidator.cs

    **Files to touch**:
      - src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs
        (use IJetStreamContext / NATS.Client.JetStream)
      - src/actors/Cena.Actors.Host/Program.cs (stream creation at startup)
      - src/actors/Cena.Actors.Tests/Infrastructure/NatsOutboxPublisherJetStreamTests.cs

    **Definition of Done**:
      - [ ] Lines 304-319 still produce the same subject names but the
            publish call uses JetStream
      - [ ] Six JetStream streams exist with documented retention policies
      - [ ] Each of the five non-curriculum categories has a real
            consumer OR is removed from GetDurableSubject's routing
      - [ ] Integration test proves messages survive broker bounce
      - [ ] Class header comment matches reality

    **Reporting requirements**:
      - Paste the new stream config at startup.
      - Paste the integration test that bounces NATS and proves
        durability.
      - List which categories are now consumed by which service.

    **Reference**: FIND-arch-022 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-023
  severity: p1
  category: perf
  file: src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
  line: 219
  related_prior_finding: FIND-data-009
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '219,225p' src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
        var events = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_attempted_v1")
            .ToListAsync();
        var sessionEvents = events
            .Where(e => ExtractString(e, "sessionId") == doc.SessionId)
            .ToList();

        $ sed -n '348,353p' src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
        var rawEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_attempted_v1")
            .ToListAsync();
        var attempts = rawEvents
            .Where(e => ExtractString(e, "sessionId") == doc.SessionId)
            .OrderBy(e => e.Timestamp)
  finding: |
    Two student-facing per-session endpoints in `SessionEndpoints.cs`
    load **every `concept_attempted_v1` event in the entire database**
    on every request, then filter in-memory to the requested session:
      - `GET /api/sessions/{sessionId}` (`GetSessionDetail`, line 219)
      - `GET /api/sessions/{sessionId}/replay` (`GetSessionReplay`, line 348)
    Both use `session.Events.QueryAllRawEvents()` with NO sequence/time
    bound, NO LIMIT, NO sessionId server-side filter (sessionId lives
    inside the JSON payload, not on the column, so the LINQ projection
    has nothing to push down). At any meaningful event count this is a
    full table scan plus a payload deserialize per row. With ~1k
    students × ~100 sessions × ~30 attempts ≈ 3M rows, every page load
    becomes multi-second and ties up a Postgres connection.

    The IDOR check on `doc.StudentId != studentId` fires AFTER the load,
    so an authorized user merely loads the entire global event store
    on every legitimate page open, but the cost and latency footprint is
    identical to an unauthorized one.

    This is the broader drill-down that Phase-0 preflight surfaced as
    a "QueryAllRawEvents anti-pattern survives" observation. FIND-data-009
    only scoped tenant safety on analytics endpoints; the perf footprint
    on student-facing per-session reads is a new finding owned by the
    arch lens.
  root_cause: |
    Marten's `QueryAllRawEvents` is the only LINQ surface that exposes
    raw events, and the team reached for it because the canonical event
    payload (`ConceptAttempted_V1`) does not have a Marten projection
    that exposes (sessionId → events). The right fix is a projection,
    not an in-memory filter. FIND-data-009 added
    `StudentLifetimeStatsProjection` for one set of metrics; this
    needs an analogous `SessionAttemptHistoryProjection`.
  proposed_fix: |
    1. Add `SessionAttemptHistoryDocument` keyed by `(SessionId)` with
       a list of `(QuestionId, IsCorrect, ResponseTimeMs, Timestamp,
       PriorMastery, PosteriorMastery, FatigueScore, ConceptId)` items.
    2. Add `SessionAttemptHistoryProjection` (Inline single-stream)
       that listens to `ConceptAttempted_V2` and appends to the document.
    3. Replace both `QueryAllRawEvents` calls with a single
       `session.LoadAsync<SessionAttemptHistoryDocument>(sessionId)`.
    4. Add a Postgres index on `(StudentId)` so the projection rebuild
       (one-time) can complete without a full scan.
  test_required: |
    `SessionDetailEndpointPerfTests`:
      - Seed 1000 sessions across 100 students with 30 attempts each.
      - Hit `GET /api/sessions/{id}` for one specific session.
      - Assert the request reads ≤ 30 events (use Marten's IQuerySession.Logger
        to count rows hit).
      - Assert request latency p95 < 100ms.
  task_body: |
    **Goal**: Stop full-scanning the event store on every per-session
    page load. Replace with a projection-backed read.

    **Files to read first**:
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
        (lines 200-410 for GetSessionDetail and GetSessionReplay)
      - src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs
        (template — same fix pattern)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
      - src/actors/Cena.Actors/Events/LearnerEvents.cs

    **Files to touch**:
      - src/actors/Cena.Actors/Projections/SessionAttemptHistoryProjection.cs (new)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register)
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs (replace QueryAllRawEvents calls)
      - src/actors/Cena.Actors.Tests/Projections/SessionAttemptHistoryProjectionTests.cs (new)

    **Definition of Done**:
      - [ ] `grep -n "QueryAllRawEvents" src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` returns zero matches
      - [ ] Both endpoints load a single document by sessionId
      - [ ] Projection rebuild on a 10k-event seed completes in < 30s
      - [ ] Perf test asserts the new path reads ≤ 30 events per call

    **Reporting requirements**:
      - Paste before/after p95 latency from the perf test.
      - Paste the projection rebuild timing on the seed dataset.

    **Reference**: FIND-arch-023 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-024
  severity: p1
  category: observability
  file: src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs
  line: 52
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '49,90p' src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs
        public sealed class FeatureFlagActor : IActor
        {
            private readonly ILogger<FeatureFlagActor> _logger;
            private readonly Dictionary<string, FeatureFlag> _flags = new(StringComparer.OrdinalIgnoreCase);
            ...
            private void InitializeDefaults()
            {
                var now = DateTimeOffset.UtcNow;
                SetDefault("llm.kimi.enabled", true, 100.0, now);
                SetDefault("llm.sonnet.enabled", true, 100.0, now);
                ...
            }
        }

        $ rg -n 'session\.Store.*FeatureFlag\|FeatureFlagDocument\|persist.*flag' src/ --type cs
        (no matches)
  finding: |
    `FeatureFlagActor` keeps every flag override in a private in-memory
    `Dictionary<string, FeatureFlag>`. Three failure modes:
      1. **No persistence.** Restart the Actor Host node and every
         non-default flag value is lost. Production rollback via
         "set flag X to false" survives only until the next deploy or
         pod restart.
      2. **No distribution.** When the Actor Host runs more than one
         replica, each instance has its own private dictionary. A
         `SetFlag` command goes to whichever replica handled the actor
         message; the other replicas keep stale values. There is no
         NATS broadcast, no Marten persistence, no Raft consensus.
      3. **No audit trail.** Every flag write is `_logger.LogInformation`
         only — there is no FeatureFlagChanged_V1 event so an admin
         asking "who turned off llm.opus.enabled at 02:14 UTC" cannot
         get an answer.
    Per the v2 prompt's expanded scope ("Feature flags / kill-switches
    for newly-fixed code paths so rollback is possible"), this kill
    switch infrastructure cannot be relied on for production rollback.

    This is independently confirmed by the existence of a SECOND,
    DIFFERENT feature-flag system in `SystemMonitoringService` that
    writes a `PlatformSettingsDocument` with its own
    `FeatureFlagSettings` record. The two systems do not talk to each
    other and neither is canonical. (See FIND-arch-026 below for the
    duplication.)
  root_cause: |
    The actor pattern was chosen to make flag reads constant-time
    (in-process dictionary access). The team never circled back to add
    a Marten-backed source of truth + a NATS broadcast for replica
    coordination, and never deduped against PlatformSettings.
  proposed_fix: |
    1. Add `FeatureFlagDocument` (Marten document, one row per flag).
       Make `SetFlag` write to Marten + emit a
       `FeatureFlagChanged_V1` event with `(name, oldValue, newValue,
       changedBy, changedAt, reason)`.
    2. Wire NatsOutboxPublisher to broadcast the event so other
       replicas pick it up via a `cena.events.feature_flag.changed`
       subject.
    3. On actor `Started`, hydrate `_flags` from Marten before serving
       reads.
    4. Subscribe to `FeatureFlagChanged_V1` in-process so replicas
       converge.
    5. Decide which of (FeatureFlagActor, PlatformSettingsDocument) is
       canonical; collapse the other into it. Per the actor pattern's
       in-memory hot-read advantage, FeatureFlagActor wins — convert
       PlatformSettings.Features.* into a thin wrapper that calls
       FeatureFlagActor.GetFlag.
  test_required: |
    `FeatureFlagActorPersistenceTests`:
      - SetFlag, restart the actor system, GetFlag → assert the
        non-default value is restored.
      - Two-replica simulation: SetFlag on replica A, GetFlag on
        replica B → assert replica B sees the new value within 1s.
  task_body: |
    **Goal**: Make the FeatureFlagActor production-grade as a kill
    switch — persistent, distributed, and audited.

    **Files to read first**:
      - src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs
      - src/api/Cena.Admin.Api/SystemMonitoringService.cs (the rival
        PlatformSettings.Features path)
      - src/shared/Cena.Infrastructure/Documents/PlatformSettingsDocument.cs

    **Files to touch**:
      - src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs
        (Marten-backed + event emission)
      - src/shared/Cena.Infrastructure/Documents/FeatureFlagDocument.cs (new)
      - src/actors/Cena.Actors/Events/FeatureFlagEvents.cs (new — FeatureFlagChanged_V1)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register)
      - src/api/Cena.Admin.Api/SystemMonitoringService.cs (delegate to FeatureFlagActor)
      - src/actors/Cena.Actors.Tests/Infrastructure/FeatureFlagActorPersistenceTests.cs

    **Definition of Done**:
      - [ ] FeatureFlagActor hydrates from Marten on Started
      - [ ] Every SetFlag persists + emits an event
      - [ ] Two-replica integration test passes
      - [ ] Audit log shows every flag change with actor identity
      - [ ] PlatformSettings.Features delegates to FeatureFlagActor

    **Reporting requirements**:
      - Paste the persistence test output.
      - Paste the audit query showing a flag change with the actor identity.

    **Reference**: FIND-arch-024 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-025
  severity: p1
  category: fake-fix
  file: src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs
  line: 101
  related_prior_finding: FIND-arch-004
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '42,121p' src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs
        public async IAsyncEnumerable<LlmChunk> StreamCompletionAsync(...)
        {
            ...
            try
            {
                var response = await _client.Messages.Create(new MessageCreateParams { ... }, ct);
                fullText = string.Join("", response.Content
                    .Select(b => b.Value).OfType<TextBlock>().Select(b => b.Text));
                var inputTokens = response.Usage?.InputTokens ?? 0;
                var outputTokens = response.Usage?.OutputTokens ?? 0;
                totalTokens = (int)(inputTokens + outputTokens);
            }
            ...
            // Simulate streaming by yielding word by word
            var words = fullText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                yield return new LlmChunk(Delta: word + " ", Finished: false, ...);
                await Task.Delay(20, ct); // Small delay for natural feel
            }
        }

        $ sed -n '323p' src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
        // Stream tokens from real LLM (HARDEN: No stubs)
  finding: |
    `ClaudeTutorLlmService.StreamCompletionAsync` is the LLM client used
    by the SSE streaming endpoint `POST /api/tutor/threads/{id}/stream`.
    It is named "Stream" and the call site comment at TutorEndpoints.cs:323
    proudly declares "Stream tokens from real LLM (HARDEN: No stubs)".

    The actual implementation calls `_client.Messages.Create(...)` —
    the **non-streaming** Anthropic API — waits for the full response,
    then "simulates streaming by yielding word by word" (line 101) with
    a `Task.Delay(20, ct)` between each fake chunk. This is a label-drift
    fake-fix:

    - The `_V1` SSE wire format claims streaming.
    - The student waits the full LLM completion latency (3-15s for
      Claude Sonnet on a thoughtful tutor reply) BEFORE seeing the
      first token, then watches a 20ms-per-word fake stream.
    - Real Anthropic streaming would deliver the first token in
      ~300ms and the rest at the network's natural rate.
    - Token usage is captured from the unary response, not summed
      across stream events, so the per-stream metering is technically
      correct but wastes the real streaming API.

    This was almost certainly introduced as a "Phase-1 stub → harden
    later" pattern, which the user banned 2026-04-11. Combined with
    FIND-arch-004's "no canned tutor placeholder" rule, this label
    qualifies as a fake-fix per the v2 prompt's `fake-fix` category:
    "label/symptom changed, root cause intact". The endpoint is no
    longer canned, but it is no longer streaming either, despite all
    the comments saying it is.
  root_cause: |
    The Anthropic SDK supports `Messages.CreateStream` (returns an
    `IAsyncEnumerable<MessageStreamEvent>`). The Cena tutor service was
    written against the unary `Messages.Create` API and the team added
    a fake-stream wrapper to keep the SSE plumbing happy without
    rewriting the LLM call.
  proposed_fix: |
    1. Replace `_client.Messages.Create(...)` with
       `_client.Messages.CreateStream(...)`.
    2. Iterate the resulting `IAsyncEnumerable<MessageStreamEvent>` and
       yield each `ContentBlockDeltaEvent.Delta.Text` as an `LlmChunk`.
    3. Sum input/output tokens from the `MessageDeltaEvent.Usage`
       chunks (the final usage event).
    4. Remove the `Task.Delay(20, ct)` and the "Simulate streaming"
       comment.
    5. Update the file header from "Real Anthropic Claude integration
       with simulated streaming" to "Real streaming Anthropic Claude
       integration".
    6. Add a unit test that fakes the SDK and asserts the service
       yields chunks AS they arrive (not all-at-once after a full
       upstream wait).
  test_required: |
    `ClaudeTutorLlmServiceStreamingTests.StreamCompletion_YieldsBeforeUpstreamFinishes`:
      - Inject a fake `IAnthropicClient` whose `Messages.CreateStream`
        yields 3 deltas with 100ms gaps.
      - Subscribe to the service's `IAsyncEnumerable<LlmChunk>`.
      - Assert the first chunk arrives within 150ms of the first
        upstream delta (NOT after all 3 deltas have arrived).
  task_body: |
    **Goal**: Make the tutor SSE endpoint actually stream from
    Anthropic instead of fake-streaming a unary response.

    **Files to read first**:
      - src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs
      - src/actors/Cena.Actors/Tutor/ITutorLlmService.cs
      - src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs (line 244+ for the SSE handler)
      - Anthropic SDK Messages.CreateStream docs

    **Files to touch**:
      - src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs (use CreateStream)
      - src/actors/Cena.Actors.Tests/Tutor/ClaudeTutorLlmServiceStreamingTests.cs (new)

    **Definition of Done**:
      - [ ] `grep -n "Simulate streaming\|Task.Delay" src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs` returns zero
      - [ ] First LlmChunk arrives before Anthropic finishes the full response
      - [ ] Token accounting still correct (sum streaming usage events)
      - [ ] SSE endpoint sends real, immediate token deltas to the browser
      - [ ] File header updated to remove "simulated streaming"

    **Reporting requirements**:
      - Paste a tcpdump or browser DevTools network screenshot showing
        a real progressive SSE response (not a single burst).
      - Paste streaming test output.

    **Reference**: FIND-arch-025 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-026
  severity: p2
  category: label-drift
  file: src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
  line: 13
  related_prior_finding: FIND-data-003
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '7,16p' src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
        namespace Cena.Actors.Projections;
        /// <summary>
        /// Marten inline projection for learning session question queue.
        /// One document per active session. Provides O(1) question selection.
        /// </summary>
        public class LearningSessionQueueProjection
        {

        $ sed -n '7,15p' src/actors/Cena.Actors/Projections/ActiveSessionSnapshot.cs
        namespace Cena.Actors.Projections;
        /// <summary>
        /// Marten inline projection for active session state.
        /// One document per student. Deleted when session ends.
        /// </summary>
        public class ActiveSessionSnapshot

        $ sed -n '179,194p' src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
        // STB-01: Active session tracking
        // FIND-data-004: Changed from Inline Snapshot to regular document.
        // This type is mutated directly via session.Store(), not rebuilt from events.
        opts.Schema.For<ActiveSessionSnapshot>().Identity(x => x.Id).Index(x => x.StudentId);
        ...
        // FIND-data-003: Changed from Inline Snapshot to regular document.
        // This type is mutated directly via session.Store() in SessionEndpoints,
        // not rebuilt from events via Apply handlers.
        opts.Schema.For<LearningSessionQueueProjection>().Identity(...).Index(...);
  finding: |
    `LearningSessionQueueProjection` and `ActiveSessionSnapshot` are no
    longer Marten projections or snapshots. FIND-data-003 / FIND-data-004
    fixed a bug by switching them from `Projections.Snapshot<>(Inline)`
    to `Schema.For<>()` plain documents — they are now mutated directly
    via `session.Store(...)` in SessionEndpoints, not rebuilt from
    events. Both class names AND both XML doc comments still claim
    "inline projection". The XML namespace
    `Cena.Actors.Projections` reinforces the lie.

    Per the user's locked rule "labels match data" (2026-03), this is
    a P2 naming-debt finding. The Phase-0 preflight already surfaced
    these as "label drift candidates"; the arch lens owns the formal
    write-up.
  root_cause: |
    FIND-data-003/004 fixed the runtime bug (no Apply handlers, infinite
    Marten warning loop) by changing the registration but did not
    rename the types. A class rename is a public-API churn, but in
    these two cases the types are internal to `Cena.Actors` so the
    cost is grep-and-replace.
  proposed_fix: |
    1. Rename `LearningSessionQueueProjection` →
       `LearningSessionQueueDocument`.
    2. Rename `ActiveSessionSnapshot` → `ActiveSessionDocument`.
    3. Move both files from `Cena.Actors/Projections/` to
       `Cena.Actors/Documents/`.
    4. Update XML doc comments to say "Marten document" instead of
       "Marten inline projection".
    5. Update every grep hit (`SessionEndpoints.cs`, `MartenConfiguration.cs`,
       any tests) to the new names.
  test_required: |
    Build green; existing session tests still pass after the rename.
  task_body: |
    **Goal**: Rename two mis-labeled types so the names describe the
    actual storage model. "Labels match data" is a locked user rule.

    **Files to read first**:
      - src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
      - src/actors/Cena.Actors/Projections/ActiveSessionSnapshot.cs
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (lines 179-194)
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs (callers)
      - docs/reviews/reverify-2026-04-11-preflight.md (preflight section
        "Observations surfaced for Phase 1" — this is the formal write-up)

    **Files to touch**:
      - rename src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
        → src/actors/Cena.Actors/Documents/LearningSessionQueueDocument.cs
      - rename src/actors/Cena.Actors/Projections/ActiveSessionSnapshot.cs
        → src/actors/Cena.Actors/Documents/ActiveSessionDocument.cs
      - update every reference in SessionEndpoints.cs, MartenConfiguration.cs,
        Cena.Actors.Tests, Cena.Admin.Api/Queries

    **Definition of Done**:
      - [ ] Both files moved + renamed
      - [ ] Build green
      - [ ] All session tests pass
      - [ ] `grep -rn "LearningSessionQueueProjection\|ActiveSessionSnapshot" src/` returns zero
      - [ ] XML docs say "Marten document", not "inline projection"

    **Reporting requirements**:
      - Paste the full diff size (file moves + name updates).
      - Confirm no behaviour change.

    **Reference**: FIND-arch-026 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-027
  severity: p2
  category: contract
  file: src/actors/Cena.Actors/Gateway/LlmClientRouter.cs
  line: 31
  related_prior_finding: FIND-arch-005
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '20,40p' src/actors/Cena.Actors/Gateway/LlmClientRouter.cs
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            ...
            if (request.ModelId?.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new NotImplementedException(
                    $"Moonshot provider not yet implemented. ModelId='{request.ModelId}'");
            }
            ...
        }

        $ sed -n '88,93p' src/actors/Cena.Actors/Gateway/LlmUsageTracker.cs
        _ when modelId.StartsWith("kimi-k2.5", StringComparison.OrdinalIgnoreCase) => ...,
        _ when modelId.StartsWith("kimi-", StringComparison.OrdinalIgnoreCase) => ...,
  finding: |
    `LlmClientRouter` ships a runtime `NotImplementedException` on the
    happy path for any `ModelId` starting with `kimi-`. Meanwhile,
    `LlmUsageTracker` carries pricing for `kimi-k2.5` and a generic
    `kimi-` fallback (lines 88-92), and `FeatureFlagActor` initializes
    a default `llm.kimi.enabled = true` flag. So the system's three
    artifacts give three different stories:
      - Cost tracker: kimi is supported, here is its $/token.
      - Feature flag: kimi is enabled by default for everyone.
      - Router: kimi throws a runtime exception.
    If an admin selects a kimi model in `AiGenerationService.UpdateSettingsAsync`
    (which accepts an arbitrary ModelId string with no validation), the
    next AI generation request crashes the request pipeline.

    FIND-arch-005 deleted the OpenAI/Google/AzureOpenAI stub providers
    for the same reason. The kimi branch is a re-occurrence of the
    same anti-pattern: a model ID prefix is registered in the system
    but the only thing the router knows how to do with it is throw.
  root_cause: |
    Moonshot was scoped as a future provider but the work was deferred.
    The router added a `kimi-` branch as a placeholder, the cost
    tracker added pricing in anticipation, and the feature flag was
    enabled by default — all without a real implementation behind
    them.
  proposed_fix: |
    Two acceptable paths:
      A. Implement a real `MoonshotLlmClient` (Moonshot exposes an
         OpenAI-compatible API at https://api.moonshot.ai/v1) and wire
         it. Add a `MOONSHOT_API_KEY` env var. Validate via a smoke
         test against the live API.
      B. Delete the kimi branch from `LlmClientRouter`, the kimi
         pricing rows from `LlmUsageTracker`, and the
         `llm.kimi.enabled` default from `FeatureFlagActor`. Reject
         any kimi model ID at the input boundary in
         `AiGenerationService.UpdateSettingsAsync`.
    Either way, the three artifacts must agree.
  test_required: |
    `LlmClientRouterTests.RouterAndCostTrackerAgreeOnSupportedProviders`
    asserts the set of "supported model ID prefixes" in the router
    equals the set in the cost tracker.
  task_body: |
    **Goal**: Make the LLM router, cost tracker, and feature flag
    agree on whether kimi/Moonshot is supported. Either implement it
    or delete it everywhere.

    **Files to read first**:
      - src/actors/Cena.Actors/Gateway/LlmClientRouter.cs
      - src/actors/Cena.Actors/Gateway/LlmUsageTracker.cs
      - src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs (line 82)
      - src/api/Cena.Admin.Api/AiGenerationService.cs (UpdateSettingsAsync)
      - docs/reviews/agent-1-arch-findings.md (FIND-arch-005 — same
        pattern, prior decision)

    **Files to touch (option A — implement)**:
      - src/actors/Cena.Actors/Gateway/MoonshotLlmClient.cs (new)
      - src/actors/Cena.Actors/Gateway/LlmClientRouter.cs (wire it)
      - src/actors/Cena.Actors.Tests/Gateway/MoonshotLlmClientSmokeTests.cs (new — gated on env var)

    **Files to touch (option B — delete)**:
      - src/actors/Cena.Actors/Gateway/LlmClientRouter.cs (drop kimi branch)
      - src/actors/Cena.Actors/Gateway/LlmUsageTracker.cs (drop kimi pricing)
      - src/actors/Cena.Actors/Infrastructure/FeatureFlagActor.cs (drop default)
      - src/api/Cena.Admin.Api/AiGenerationService.cs (reject kimi at boundary)

    **Definition of Done**:
      - [ ] `grep -rn 'kimi-\|Moonshot' src/` returns the same set of
            files for "knows about kimi" — either all three (option A
            with new client) or zero non-test references (option B)
      - [ ] LlmClientRouterTests.RouterAndCostTrackerAgree passes
      - [ ] `NotImplementedException` is gone from production code paths

    **Reporting requirements**:
      - State which option you chose and why.
      - Paste the agreement test output.

    **Reference**: FIND-arch-027 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-028
  severity: p2
  category: contract
  file: src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
  line: 305
  related_prior_finding: FIND-arch-014
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -n 'session\.Store\|session\.Events\.Append' src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
        305: session.Events.Append(studentId, reactionEvent);
        346: session.Store(comment);
        356: session.Events.Append(studentId, commentEvent);
        420: session.Store(friendRequest);
        429: session.Events.Append(studentId, requestEvent);
        464: session.Store(friendRequest);
        476: session.Store(friendship);
        485: session.Events.Append(studentId, acceptedEvent);
        525: session.Store(room);
        537: session.Store(membership);
        549: session.Events.Append(studentId, roomEvent);
        615: session.Store(membership);
        623: session.Events.Append(studentId, joinEvent);
        654: session.Store(membership);
        662: session.Events.Append(studentId, leaveEvent);
  finding: |
    Re-confirmation of FIND-arch-014, which was filed in v1 (P2) but
    never enqueued as a task. Eight social endpoints in
    SocialEndpoints.cs perform a dual-write: `session.Store(<document>)`
    AND `session.Events.Append(<event>)` in the same transaction. This
    is not event sourcing — it is imperative CRUD with a side-effect
    event. Two divergence risks:
      1. Any future projection that reads the event and writes its own
         document doubles the write and the two copies can diverge
         under partial failure.
      2. Aggregates are bypassed; no `Apply` invariants run.
    The same pattern appears in `IngestionOrchestrator.cs` (lines 199-214,
    317) and `QuestionBankService.cs` for QuestionState. The full
    list is six service files.
  root_cause: |
    The DDD guidance ("event sourcing for state changes") collides with
    the need to return a fresh read model from the POST response. The
    chosen shortcut is dual-write, which leaves the read model and the
    event stream in independent transactions for any future projection
    that wants to participate.
  proposed_fix: |
    Per FIND-arch-014's proposal: use a Marten
    `SingleStreamProjection<TDocument, TKey>` per affected document
    type. The endpoint only calls `session.Events.Append(...)` and
    returns a predicted DTO from the in-memory event data. The
    projection owns the read model and gets rebuilt on a fresh database.
  test_required: |
    For each refactored endpoint:
      - Append the event, save, then immediately load the document via
        the projection — assert it matches.
      - Replay the projection against the event store — assert the
        document is reconstructed identically.
  task_body: |
    **Goal**: Stop dual-writing read-model documents alongside events.
    This re-files FIND-arch-014 with current evidence and a queue task
    so it stops getting orphaned.

    **Files to read first**:
      - src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs (full file)
      - src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs (lines 175-330)
      - src/api/Cena.Admin.Api/QuestionBankService.cs
      - docs/reviews/agent-1-arch-findings.md (FIND-arch-014)

    **Files to touch**:
      - src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
        (remove session.Store calls where an event is appended)
      - src/actors/Cena.Actors/Projections/CommentDocumentProjection.cs (new)
      - src/actors/Cena.Actors/Projections/FriendshipProjection.cs (new)
      - src/actors/Cena.Actors/Projections/StudyRoomProjection.cs (new)
      - src/actors/Cena.Actors/Projections/StudyRoomMembershipProjection.cs (new)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register the new projections)

    **Definition of Done**:
      - [ ] Endpoints only call session.Events.Append for the affected
            document types
      - [ ] Marten projections own the read model
      - [ ] Replay-from-zero produces identical documents
      - [ ] Existing social endpoint integration tests still pass

    **Reporting requirements**:
      - Paste the projection rebuild output (counts must match before/after).

    **Reference**: FIND-arch-028 in docs/reviews/agent-arch-reverify-2026-04-11.md (re-file of FIND-arch-014)

- id: FIND-arch-029
  severity: p2
  category: stub
  file: src/api/Cena.Admin.Api/AiGenerationService.cs
  line: 332
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '332,360p' src/api/Cena.Admin.Api/AiGenerationService.cs
        public Task<bool> UpdateSettingsAsync(UpdateAiSettingsRequest request, string userId)
        {
            if (request.ActiveProvider.HasValue)
                _activeProvider = request.ActiveProvider.Value;

            var current = _providers[_activeProvider];
            _providers[_activeProvider] = current with { ApiKey = request.ApiKey ?? current.ApiKey, ... };

            _defaults = new(...);

            _logger.LogInformation("AI settings updated by {UserId}: provider={Provider}, model={Model}",
                userId, _activeProvider, _providers[_activeProvider].ModelId);

            return Task.FromResult(true);
        }
  finding: |
    `AiGenerationService.UpdateSettingsAsync` updates the active provider,
    API key, model ID, temperature, and generation defaults — all in
    process-local mutable fields. There is NO persistence: no
    Marten document, no PlatformSettings, no env var write. Restart the
    Cena.Admin.Api host and every customisation reverts to the
    appsettings defaults. Worst impact: an admin rotates the Anthropic
    API key in production, the next pod restart pulls the old key
    from appsettings, AI generation breaks until someone re-pastes the
    key. Also: in a multi-replica deployment, a SetSettings call only
    updates the replica that handled the request.

    Same root pattern as FIND-arch-024 (FeatureFlagActor) — process-local
    mutable state with no persistence and no replica coordination.
  root_cause: |
    AI provider settings were stubbed in-memory during the v1 admin UI
    build. The persistence story was deferred and never landed.
  proposed_fix: |
    Persist to a `AiGenerationSettingsDocument` (Marten singleton, id
    "ai-settings") with fields for active provider, per-provider
    config, and defaults. `UpdateSettingsAsync` writes the document and
    emits a `AiGenerationSettingsChanged_V1` event so other replicas
    can refresh their in-memory cache via NATS broadcast (same pattern
    as FIND-arch-024). On host startup, hydrate from the document.
  test_required: |
    Settings persistence test: SetSettings, restart, GetSettings →
    asserts the change survived. Two-replica test: SetSettings on A,
    GetSettings on B within 1s.
  task_body: |
    **Goal**: Persist AI generation settings so admin changes survive
    restarts and propagate across replicas.

    **Files to read first**:
      - src/api/Cena.Admin.Api/AiGenerationService.cs
      - src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs
        (currently registers AiGenerationService as Singleton)
      - FIND-arch-024 (same pattern, same fix shape)

    **Files to touch**:
      - src/shared/Cena.Infrastructure/Documents/AiGenerationSettingsDocument.cs (new)
      - src/api/Cena.Admin.Api/AiGenerationService.cs (load + persist + broadcast)
      - src/actors/Cena.Actors/Events/AiGenerationEvents.cs (new — Settings_V1 event)
      - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register doc)
      - src/api/Cena.Admin.Api.Tests/AiGenerationServicePersistenceTests.cs (new)

    **Definition of Done**:
      - [ ] UpdateSettingsAsync persists to Marten in the same transaction
            as the broadcast event
      - [ ] On host startup, GetSettingsAsync reads from Marten if a
            document exists, otherwise from appsettings defaults
      - [ ] API key is encrypted at rest (or referenced via env var
            indirection) — never log raw key value
      - [ ] Persistence test passes
      - [ ] Two-replica test passes

    **Reporting requirements**:
      - Paste the persistence test output.
      - Confirm the API key never appears in logs.

    **Reference**: FIND-arch-029 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-030
  severity: p2
  category: contract
  file: src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs
  line: 80
  related_prior_finding: null
  framework: null
  evidence:
    - type: grep
      content: |
        $ sed -n '78,82p' src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs
        Level: CalculateLevel(profile.TotalXp),
        StreakDays: profile.CurrentStreak,
        AvatarUrl: null); // TODO: Avatar storage in STB-00b

        $ sed -n '104,108p' src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs
        Email: ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? "",
        AvatarUrl: null, // TODO: Avatar storage in STB-00b
  finding: |
    `MeEndpoints.GetBootstrap` and `GetProfile` both unconditionally
    return `AvatarUrl: null` with a `// TODO: Avatar storage in STB-00b`
    comment. The DTO includes the field, the Vue UI binds to it, and
    the user sees the default placeholder forever. STB-00b is no longer
    a tracked task. This is a soft-stub of the avatar feature — the
    field is in the contract but the storage is missing.
    Per the user's "no stubs" rule and the v1 Wave 1/2 cleanup, this
    needs either an implementation or removal of the field from the
    DTO.
  root_cause: |
    Avatar storage was scoped under STB-00b in the original wave plan
    but never landed. The field was kept in the DTO to keep the UI
    component stable.
  proposed_fix: |
    Two paths:
      A. Implement avatar storage:
         - `POST /api/me/avatar` accepting a multipart upload
         - Store in S3 (or local blob store in dev) with a content-type
           whitelist (image/png, image/jpeg, image/webp), size cap
           (≤2 MB), and an Image.Sharp resize step (max 512×512)
         - Persist `AvatarUrl` on `StudentProfileSnapshot` via a new
           `AvatarUrlChanged_V1` event
         - Wire `GET /api/me` and `GET /api/me/profile` to return the
           real URL
      B. Remove `AvatarUrl` from the DTO and the Vue components until
         storage exists.
    Per the user's "fix everything" rule and the fact that the field
    is already in the contract, option A is preferred.
  test_required: |
    Upload + read round-trip integration test, plus event-replay test
    proving the AvatarUrl survives a profile snapshot rebuild.
  task_body: |
    **Goal**: Implement student avatar storage. Stop returning a hardcoded
    null in /api/me + /api/me/profile.

    **Files to read first**:
      - src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs
      - src/shared/Cena.Infrastructure/Documents/StudentProfileSnapshot.cs
      - src/actors/Cena.Actors/Events/StudentEvents.cs (or wherever
        ProfileUpdated_V1 lives)

    **Files to touch**:
      - src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs (POST /me/avatar; return real URL)
      - src/shared/Cena.Infrastructure/Storage/IAvatarStorage.cs (new)
      - src/shared/Cena.Infrastructure/Storage/S3AvatarStorage.cs (new)
      - src/actors/Cena.Actors/Events/StudentEvents.cs (AvatarUrlChanged_V1)
      - src/shared/Cena.Infrastructure/Documents/StudentProfileSnapshot.cs (AvatarUrl field)
      - src/api/Cena.Student.Api.Host.Tests/MeEndpointAvatarTests.cs (new)

    **Definition of Done**:
      - [ ] `grep -n "AvatarUrl: null" src/api/Cena.Student.Api.Host/` returns zero
      - [ ] Real upload + serve round-trip works in dev (local blob store)
      - [ ] Content-type whitelist + size cap + resize enforced
      - [ ] AvatarUrl survives profile snapshot rebuild
      - [ ] Vue student profile shows the uploaded image

    **Reporting requirements**:
      - Paste the upload + GET round trip output.
      - Note the privacy considerations (avatar of a minor — see privacy lens).

    **Reference**: FIND-arch-030 in docs/reviews/agent-arch-reverify-2026-04-11.md

- id: FIND-arch-031
  severity: p3
  category: contract
  file: src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs
  line: 22
  related_prior_finding: FIND-arch-016
  framework: null
  evidence:
    - type: grep
      content: |
        $ rg -n 'Project\(' src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs
        (only Project(BadgeEarned_V1, ...) — no LearningSessionEnded handler)
  finding: |
    Re-confirmation of FIND-arch-016 (P3, never enqueued). The class
    docblock claims `ClassFeedItemProjection` listens for
    `LearningSessionEnded` and "future events", but only the
    `BadgeEarned_V1` `Project` overload is implemented. Re-filed at
    P3 so it lands in the queue this cycle.
  root_cause: |
    Same as v1: optimistic comment written during scaffolding,
    drift never reconciled.
  proposed_fix: |
    Either implement the `LearningSessionEnded_V1` projection or
    remove that line from the comment. Author's preference: remove the
    comment unless the feed actually wants session highlights, which
    is a product decision out of scope here.
  test_required: |
    Test that asserts every Project method on the projection has a
    matching mention in the docblock and vice versa.
  task_body: |
    **Goal**: Honest docblock on ClassFeedItemProjection. Re-file of
    FIND-arch-016 so it actually enters the queue.

    **Files to touch**:
      - src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs

    **Definition of Done**:
      - [ ] Docblock matches the implemented Project overloads
      - [ ] Test asserts the docblock + Project overloads agree

    **Reference**: FIND-arch-031 in docs/reviews/agent-arch-reverify-2026-04-11.md (re-file of FIND-arch-016)
```

---

## Notes for the merge coordinator

1. **Highest pre-existing arch finding ID** in the v1 findings file is
   `FIND-arch-016` (only IDs 001-012 were enqueued). To avoid collision
   I started new findings at `FIND-arch-017`. If you want sequential
   numbering tied to the queue rather than the findings file, the
   safest mapping is:
     - **FIND-arch-013..016** are open from v1 — they should ALSO be
       enqueued by the coordinator (FIND-arch-013 messaging tenant-leak,
       FIND-arch-014 social dual-write, FIND-arch-015 hardcoded "admin"
       editor, FIND-arch-016 ClassFeedItemProjection comment drift).
       FIND-arch-028 in this report re-files arch-014 with current
       evidence; FIND-arch-031 re-files arch-016. Arch-013 and arch-015
       are NOT re-filed because the v1 evidence still stands and the
       coordinator can enqueue them as-is.
2. **No regressions, no fake-fixes on prior arch findings.** The Phase-0
   preflight already cleared all 12 v1 P0/P1 fixes (`arch-001..012`),
   and my drill-down on the surrounding code agrees.
3. **Cross-lens overlap**:
     - `FIND-arch-017` (MSW in production) is the most user-visible
       finding in the entire review and overlaps the `ux` lens — they
       will likely re-discover it. Merge by file:line.
     - `FIND-arch-018` (notification channels) overlaps the `privacy`
       lens — child-safety alerting cannot rely on stub channels.
     - `FIND-arch-022` (durable outbox label) overlaps the `data` lens
       on JetStream durability cost.
     - `FIND-arch-023` (per-session full scan) overlaps the `data` lens
       on the broader QueryAllRawEvents inventory; arch owns the
       student-facing two endpoints, data owns the rest.
4. **Cost guardrails** — `FIND-arch-022`'s "every event published into
   the void" is technically a cost finding too; flag it with `cost`
   tag in the queue.

## Areas not covered (handed to other lenses)

- Auth flows / Firebase JWT verification → `sec` lens.
- AI cost metering, per-tenant rate limits, AI prompt cache hit rate
  → `data` lens.
- Lighthouse / axe / a11y / WCAG 2.2 AA → `ux` lens.
- COPPA / GDPR-K / Children's Code compliance → `privacy` lens.
- Test coverage delta + flaky tests + pre-fix-failure proofs → `qa` lens.
- Pedagogy citations (research backing for BKT, Elo, scaffolding) →
  `pedagogy` lens.

## Enqueued tasks

| Finding | Severity | Priority | Task ID |
|---|---|---|---|
| FIND-arch-017 | P0 | critical | t_37119818c91a |
| FIND-arch-018 | P0 | critical | t_25df87c51509 |
| FIND-arch-019 | P0 | critical | t_60bf2c15d4cc |
| FIND-arch-020 | P0 | critical | t_017eed8be44b |
| FIND-arch-021 | P1 | high | t_3c0bbeea2124 |
| FIND-arch-022 | P1 | high | t_7f3d9fcf1b56 |
| FIND-arch-023 | P1 | high | t_766c5582f2a2 |
| FIND-arch-024 | P1 | high | t_a2aef6aa1112 |
| FIND-arch-025 | P1 | high | t_b62dff440b61 |

P2/P3 findings (FIND-arch-026..031) are not enqueued per v2 protocol
(only P0/P1 require queue tasks). The coordinator may opt to enqueue
them in a follow-up sweep.
