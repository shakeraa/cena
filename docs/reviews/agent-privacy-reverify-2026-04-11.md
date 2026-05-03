---
lens: privacy
run: cena-review-v2-reverify
date: 2026-04-11
worker: claude-subagent-privacy
branch: claude-subagent-privacy/cena-reverify-2026-04-11
prior_findings: 0   # NEW lens introduced in v2
new_findings:
  p0: 7
  p1: 6
  p2: 4
  p3: 1
---

# Agent `privacy` — Re-verification Findings (2026-04-11)

Cena serves minors. Every finding below cites a framework (`framework:` field).
Findings without a framework citation were discarded at the agent tier per the
v2 evidence rule.

The privacy lens has **no prior history** (new in v2). All IDs start at
`FIND-privacy-001`.

---

## Compliance gap matrix

Status legend: PASS = control present and effective; PARTIAL = control exists
but is incomplete or unenforced; FAIL = control absent; FAKE = control labelled
present but a fake-fix on inspection.

| Framework | Requirement | Status | Evidence (file:line) | Finding |
|---|---|---|---|---|
| **COPPA** 16 CFR §312.4 | Direct notice to parents before collection | FAIL | student/full-version/src/pages/register.vue (no parental contact field) | FIND-privacy-001 |
| **COPPA** 16 CFR §312.5 | Verifiable parental consent before any collection from <13 | FAIL | register.vue handleSubmit collects email+password+name with zero consent gate | FIND-privacy-001 |
| **COPPA** 16 CFR §312.4(d) | Privacy policy at point of collection | FAIL | No /privacy or /terms route in either app | FIND-privacy-002 |
| **COPPA** 16 CFR §312.6 | Parental access + deletion rights | FAIL | GdprEndpoints.cs:30 — all endpoints require AdminOnly; parents have no access | FIND-privacy-003 |
| **COPPA** 16 CFR §312.10 | Data retention "only as long as reasonably necessary" | FAIL | ComplianceEndpoints.cs:167-169 — "Currently all data is retained indefinitely" | FIND-privacy-004 |
| **GDPR** Art 8 (GDPR-K) | Age verification + parental consent for under-16 (varies by member state, default 16) | FAIL | No DOB / age field anywhere in registration or onboarding flow | FIND-privacy-001 |
| **GDPR** Art 13 + 14 | Information at the time of data collection | FAIL | Register page contains no privacy notice, no link to a policy | FIND-privacy-002 |
| **GDPR** Art 17 | Right to erasure | FAKE | RightToErasureService.cs:99-115 only deletes consent records + access logs, never the StudentProfileSnapshot or event stream; ProcessErasureAsync has zero callers | FIND-privacy-005 |
| **GDPR** Art 20 | Data portability | PARTIAL | StudentDataExporter.cs only exports the snapshot; tutor chat history, learning sessions, event stream are excluded | FIND-privacy-006 |
| **GDPR** Art 7 | Right to withdraw consent at any time | FAKE | GdprConsentManager.cs:98 HasConsentAsync is defined but never called by any data processor — consent system is cosmetic | FIND-privacy-007 |
| **GDPR** Art 6 | Lawful basis recorded per processing purpose | FAIL | ConsentRecord.cs:14-22 has only 3 broad ConsentType values (Analytics, Marketing, ThirdParty); not bound to any processing purpose | FIND-privacy-007 |
| **GDPR** Art 28 | Processor agreements with sub-processors | FAIL | ClaudeTutorLlmService.cs:34 sends student free-text to api.anthropic.com with no DPA disclosure | FIND-privacy-008 |
| **GDPR** Art 35 | Data Protection Impact Assessment for high-risk processing of children | FAIL | No DPIA artifact in repo; profiling minors with adaptive AI is high-risk per WP29 guidance | FIND-privacy-009 |
| **ICO Children's Code** Std 1 (best interests of the child) | Default | FAIL | Not documented; product decisions don't reference standard | FIND-privacy-009 |
| **ICO Children's Code** Std 3 (Privacy by default) | High-privacy defaults | FAIL | MeEndpoints.cs:482-485 defaults ProfileVisibility=class-only, ShowProgressToClass=true, AllowPeerComparison=true — peer-visible by default | FIND-privacy-010 |
| **ICO Children's Code** Std 4 (Transparency) | Child-appropriate language explaining processing | FAIL | settings/privacy.vue is 3 toggles and a stored localStorage blob; no plain-language explanation of what is processed | FIND-privacy-002 |
| **ICO Children's Code** Std 5 (Detrimental use of data) | No profiling for "detrimental" purposes | PARTIAL | Behavioral profiling via Elo + BKT is the entire product; not flagged as detrimental but no DPIA covers it | FIND-privacy-009 |
| **ICO Children's Code** Std 6 (Policies and community standards) | Privacy policy + community standards published | FAIL | No /privacy, /terms, /community-standards page anywhere | FIND-privacy-002 |
| **ICO Children's Code** Std 7 (Default settings) | High-privacy by default | FAIL | Same as Std 3 — defaults are open, not closed | FIND-privacy-010 |
| **ICO Children's Code** Std 8 (Data minimization) | Collect only what's needed | PARTIAL | Bio (free text), DisplayName, school IP all stored; Bio has no character cap and no Pii tag | FIND-privacy-011 |
| **ICO Children's Code** Std 9 (Data sharing) | No sharing without compatible purpose | FAIL | Anthropic API is invisible to the child; no DPA disclosed | FIND-privacy-008 |
| **ICO Children's Code** Std 10 (Geolocation) | OFF by default; clear signal when on | N/A | No geolocation in actual app code | — |
| **ICO Children's Code** Std 11 (Parental controls) | Visible controls + clear when monitored | FAIL | No parent dashboard, no parent-facing UI at all | FIND-privacy-001 |
| **ICO Children's Code** Std 12 (Profiling) | OFF by default | FAIL | StudentProfileSnapshot.cs:41 EloRating=1500 default — student is profiled the moment they register; no toggle, no consent | FIND-privacy-009 |
| **ICO Children's Code** Std 13 (Nudge techniques) | Don't nudge children to weaken privacy | PARTIAL | Default-on streaks/daily reminders/peer comparison engineered for engagement; settings buried | FIND-privacy-010 |
| **ICO Children's Code** Std 14 (Connected toys) | N/A | N/A | — | — |
| **ICO Children's Code** Std 15 (Online tools) | Provide privacy tools (DSAR, deletion) | FAKE | Endpoints exist but RightToErasureService is fake (FIND-privacy-005) | FIND-privacy-005 |
| **FERPA** 34 CFR §99.7 | Annual notice to parents of rights | FAIL | No annual notice path; admin compliance endpoints log access but parents cannot read the log | FIND-privacy-003 |
| **FERPA** 34 CFR §99.31 | Disclosure restrictions | PARTIAL | Audit middleware logs only 6 hardcoded admin paths; doesn't cover /api/admin/students, /api/admin/leaderboard, /api/admin/analytics, /api/admin/gdpr, /api/admin/compliance, /api/admin/sessions | FIND-privacy-012 |
| **FERPA** 34 CFR §99.32 | Record of disclosures kept for life of record | PARTIAL | StudentRecordAccessLog exists but its own retention is 5y vs the 7y education-record retention — disclosure log expires before the record | FIND-privacy-013 |
| **Israel PPL §11(b)** | Data subject access (Hebrew language available) | FAIL | No DSAR endpoint reachable by data subject; no Hebrew Privacy Notice | FIND-privacy-002 + FIND-privacy-003 |
| **Israel PPL Amendment 13 (2024)** | Enhanced minor protections — appointed data security officer for processors of >100k people | UNKNOWN | No DPO appointment evidence in repo or `docs/` | FIND-privacy-014 |
| **WCAG-2.2-AA** Privacy notices | (Cross-lens with `ux`) | DEFER | Defer to `ux` lens | — |

**Compliance score**:
- COPPA: 0/5 controls present
- GDPR-K: 1/7 partial, 0 full (data export partial only)
- ICO-Children: 0/15 fully compliant; 8 FAIL, 3 PARTIAL, 1 FAKE, 3 N/A
- FERPA: 0/3 fully compliant; 2 partial
- Israel PPL: 0/2 controls present

---

## Findings

```yaml
- id: FIND-privacy-001
  severity: p0
  category: privacy
  framework: COPPA, GDPR-K, ICO-Children
  file: src/student/full-version/src/pages/register.vue
  line: 1-86
  evidence:
    - type: file-read
      content: |
        register.vue collects email + password + display name and immediately
        creates an account via authStore.__mockSignIn(...) without:
          - any date-of-birth field
          - any age input
          - any parental email field
          - any consent checkbox
          - any link to a Privacy Policy or Terms of Service
          - any reference to "parent", "guardian", or "consent"
        Confirmed live by Playwright browser navigation to
        http://localhost:5175/register — see screenshot
        privacy-register-student.png at the worktree root.
    - type: grep
      content: |
        $ grep -nri 'parental\|guardian\|consent\|dateOfBirth\|dob' \
            src/student/full-version/src/plugins/i18n/locales/{en,ar,he}.json
        (zero matches in any locale)
    - type: file-read
      content: |
        StudentProfileSnapshot.cs:14-67 has no DateOfBirth, no Age, no ParentEmail,
        no ParentalConsent, no ConsentVersion property. The mobile-only
        AgeSafetyService at src/mobile/lib/core/services/age_safety_service.dart
        relies on a `getAgeTier(int age)` call that has no upstream source on
        the web tier.
  finding: |
    The student web registration flow collects PII from minors with zero
    age verification, zero parental consent, and zero notice. There is no
    DOB field, no parent contact field, no consent checkbox, no link to a
    privacy policy, and no localized strings to even surface the concept
    of consent in any of the three supported languages.
  root_cause: |
    The register.vue page was scaffolded as a generic Vuexy auth form. The
    minor-serving constraint was never reflected in the component contract.
    The mobile AgeSafetyService was built but the web equivalent was never
    written. The Cena.Actors event-sourced StudentProfileSnapshot has no
    age field, so the web could not persist age even if it asked.
  proposed_fix: |
    1. Add a hard age gate (date-picker → calculated age) BEFORE any
       email/password collection. ICO Std 7 requires the age check to be
       hostile to bypass — a single client-side checkbox is insufficient.
    2. Branch the registration flow on age:
         - <13 (US) / <16 (EU default): require parent email, send a
           verifiable-consent challenge per 16 CFR §312.5(b), block account
           creation until consent.captured = true on the server.
         - 13-15: short-form notice + opt-in to data processing.
         - 16+: standard adult flow.
    3. Add `DateOfBirth`, `AgeAtRegistration`, `ParentEmail`,
       `ParentalConsentRecord` (FK to ConsentRecord) fields to
       StudentProfileSnapshot via a new event
       AgeAndConsentRecorded_V1. Upcaster for existing rows defaults
       to "unknown — needs reverification" so old rows cannot bypass
       the gate.
    4. Add an Israel-specific localized notice (Hebrew) for in-Israel
       traffic per Israel PPL §11.
    5. Surface a parent-facing dashboard at /parent that mirrors what
       the audit log captures so parents can exercise §312.6 rights
       without going through an admin.
  test_required: |
    - E2E test: navigate to /register, attempt registration with no DOB,
      assert form is rejected.
    - E2E: register with DOB 8 years ago, assert parent-email step appears
      and account is NOT created until parent challenge is completed.
    - Backend integration test:
      POST /api/me/onboarding without parent_consent_token returns 403
      for under-13 students.
    - Cypress/Playwright snapshot test: register page contains visible
      Privacy Policy + Terms links (catches FIND-privacy-002 too).

- id: FIND-privacy-002
  severity: p0
  category: privacy
  framework: COPPA, GDPR (Art 13/14), ICO-Children (Std 4, 6)
  file: src/student/full-version/src/pages/, src/admin/full-version/src/pages/
  line: -
  evidence:
    - type: bash
      content: |
        $ find src/student/full-version/src/pages -iname '*privacy*' \
              -o -iname '*terms*' -o -iname '*policy*'
        src/student/full-version/src/pages/settings/privacy.vue
        # ^ 3 toggles, no policy text
        $ find src/admin/full-version/src/pages -iname '*privacy*' \
              -o -iname '*terms*' -o -iname '*policy*'
        # (zero results)
    - type: grep
      content: |
        $ grep -nri 'privacy.policy\|terms.of.service\|/privacy\|/terms' \
            src/student/full-version/src --include='*.vue'
        src/student/full-version/src/pages/settings/index.vue:30:
          { id: 'privacy', icon: 'tabler-shield-lock',
            titleKey: 'settingsPage.privacy.title', ... }
        # That's the settings link only — no actual policy page.
    - type: screenshot
      content: |
        privacy-student-login.png — student login page has no Privacy
        Policy or Terms-of-Service link visible anywhere on the page.
        privacy-register-student.png — same for register.
        privacy-admin-login.png — same for admin login.
  finding: |
    There is no Privacy Policy page, no Terms of Service page, no
    Children's Privacy Notice, and no Cookie Notice anywhere in either
    the student or admin web app. The only "privacy" route is a settings
    page that contains three toggles persisted only to localStorage.
  root_cause: |
    Legal copy was never authored. The placeholder "settings/privacy.vue"
    was treated as the entire privacy surface. No PM/legal review gate
    was wired into the deploy pipeline.
  proposed_fix: |
    1. Author Children's Privacy Notice (separate from adult privacy notice)
       per ICO Std 4 — short-form ("just-in-time"), child-appropriate
       reading level (target Flesch-Kincaid grade 5 for the student app
       per ICO).
    2. Author Terms of Service (parent-binding for <13, student+parent
       co-binding for 13-15, student-binding for 16+).
    3. Publish at /privacy (full), /privacy/children (child-friendly
       summary), /terms.
    4. Render in the three locales en/ar/he. Hebrew copy is non-optional
       for Israel market per Israel PPL §11.
    5. Link from the foot of every auth page (login, register,
       forgot-password) and from the in-app footer.
  test_required: |
    Snapshot tests asserting the four pages exist and contain the
    minimum-required sections (data collected, lawful basis, retention,
    rights, contact, cross-border, parent rights, DSAR contact). Locale
    parity test asserting all three locales render the policy.

- id: FIND-privacy-003
  severity: p0
  category: privacy
  framework: COPPA (16 CFR §312.6), GDPR (Art 12-22), Israel PPL §13
  file: src/api/Cena.Admin.Api/GdprEndpoints.cs
  line: 28-30
  evidence:
    - type: file-read
      content: |
        var group = app.MapGroup("/api/admin/gdpr")
            .WithTags("GDPR")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly);
        # Every consent / export / erasure endpoint requires AdminOnly.
        # No /api/me/gdpr/* exists for students/parents.
    - type: grep
      content: |
        $ grep -rn 'MapGet.*me/gdpr\|MapPost.*me/gdpr\|me/erasure\|me/export' src/
        # zero matches
  finding: |
    Every GDPR rights endpoint (consent, export, erasure, status) is
    gated behind AdminOnly. There is no student-facing or parent-facing
    way to file a DSAR, withdraw consent, or request deletion. A student
    or parent cannot exercise GDPR Art 15-22 rights without contacting
    a Cena administrator out-of-band.
  root_cause: |
    GDPR endpoints were authored as an admin compliance tool, not as a
    data-subject self-service surface. The AuthEndpoints group on the
    Student host has no /me/gdpr/* equivalents.
  proposed_fix: |
    1. Mirror /api/admin/gdpr/* as /api/me/gdpr/* on the Student host
       with student-self auth (the student can exercise their own
       rights for 13+, parent does so via a parent-bound credential
       for <13).
    2. Add a parent-facing app surface (or, minimally, a tokenized email
       link) that lets parents request export and erasure for their
       child without admin intervention.
    3. Add a /api/me/gdpr/dsar endpoint that submits a DSAR ticket and
       returns a tracking ID (closes Israel PPL §13 + GDPR Art 12 30-day
       response window).
    4. Surface in settings/privacy.vue with three buttons:
       "Download my data", "Delete my account", "See who has accessed
       my data".
  test_required: |
    Auth contract test that asserts /api/me/gdpr/erasure returns 401 for
    anonymous and 200 for the owning student. Integration test that an
    erasure request from /me actually triggers the
    RightToErasureService.RequestErasureAsync path.

- id: FIND-privacy-004
  severity: p0
  category: privacy
  framework: COPPA (16 CFR §312.10), GDPR (Art 5(1)(e) storage limitation), ICO-Children (Std 8), FERPA (34 CFR §99.7 retention)
  file: src/api/Cena.Admin.Api/ComplianceEndpoints.cs
  line: 167-169
  evidence:
    - type: file-read
      content: |
        ComplianceEndpoints.cs:131-172 returns the retention policy table
        with this final block:
          archivalStatus = "Scheduled background job not yet implemented
                            (see REV-013.3 notes)",
          note = "Retention periods are enforced after archival job is
                  deployed. Currently all data is retained indefinitely
                  in the event store."
    - type: grep
      content: |
        $ grep -rn 'IHostedService\|BackgroundService' src/ \
            | grep -i 'retention\|archive\|purge\|expire'
        # zero matches — no retention worker exists
        $ grep -rn 'StudentRecordRetention\|AnalyticsRetention\|EngagementRetention' src/
        # only the constants definition + the GET endpoint that displays them
  finding: |
    The retention policy is documented in code but never enforced.
    DataRetentionPolicy.cs declares 7y/5y/2y/1y windows; the
    /api/admin/compliance/data-retention endpoint dutifully publishes
    them; and the same endpoint admits "Currently all data is retained
    indefinitely in the event store." There is no scheduled job, no
    soft-delete enforcement, no purge worker. The published policy is
    a label; the data lives forever.
  root_cause: |
    REV-013.3 (the archival job) was deferred and never resumed. The
    code path stores constants and returns them via a GET, but no
    consumer of those constants exists at runtime.
  proposed_fix: |
    1. Implement an IHostedService retention worker that, on a daily
       cadence, queries StudentProfileSnapshot, StudentRecordAccessLog,
       and the event stream for rows older than the documented window
       and either soft-deletes (Marten built-in) or hard-deletes per
       category.
    2. Add a per-tenant override knob in tenant config (some districts
       require longer retention).
    3. Emit a structured log event RetentionRunCompleted_V1 so the
       SIEM can verify the worker actually runs.
    4. Update the GET endpoint to return real "last run", "next run",
       "row counts purged" — not the current lie.
    5. Add a regression test that creates a row, fast-forwards the
       clock past the retention window, runs the worker, and asserts
       the row is gone.
  test_required: |
    Worker integration test using a fake clock that advances past the
    retention window. Endpoint contract test that asserts archivalStatus
    is "active" with a recent lastRunAt after the worker runs once.

- id: FIND-privacy-005
  severity: p0
  category: privacy, fake-fix
  framework: GDPR (Art 17), Israel PPL §15A, COPPA (16 CFR §312.10), ICO-Children (Std 15)
  file: src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs
  line: 74-115
  evidence:
    - type: file-read
      content: |
        RightToErasureService.ProcessErasureAsync (lines 74-115) only
        deletes:
          - ConsentRecord rows for the student
          - StudentRecordAccessLog rows for the student
        It does NOT delete:
          - StudentProfileSnapshot (the actual education record)
          - any event in the student's event stream
          - LearningSession events / SessionStarted_V1 / ConceptAttempted_V1
          - tutor messages (TutorMessageDocument)
          - tutor threads (TutorThreadDocument)
          - DeviceSessionDocument rows
          - StudentPreferencesDocument rows
          - any of the projections (FocusAnalytics, MasteryTracking, etc.)
        The log line at :114 says "Records anonymized." Nothing was
        anonymized; only the consent paper trail was destroyed.
    - type: grep
      content: |
        $ grep -rn 'ProcessErasureAsync' src/
        src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs:26:
        src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs:74:
        # ZERO callers. Not a Hangfire job, not a Quartz job, not a
        # background service, not an endpoint. ProcessErasureAsync is
        # dead code that will never run.
    - type: file-read
      content: |
        GdprEndpoints.cs:84-98 only calls RequestErasureAsync. There is
        no /api/admin/gdpr/erasure/{id}/process or any worker that
        processes the cooling-period queue.
  finding: |
    The "right to erasure" implementation is a compliance theater.
    Even if it ran (which it can't — zero callers), it would only delete
    the consent records and the access audit log, NOT the actual student
    education record, NOT the event stream, NOT the tutor chat history,
    NOT the projections. Worse: deleting the access audit log on
    erasure is itself a FERPA §99.32 violation because the disclosure
    record must outlive the underlying education record. The log line
    "Records anonymized" is false.
  root_cause: |
    The service was scaffolded to satisfy a "right-to-erasure exists"
    checkbox without an end-to-end design. No regression test caught
    that ProcessErasureAsync had no caller. The class was treated as
    "shipped" once the endpoint returned 200.
  proposed_fix: |
    1. Re-design the erasure pipeline as an event-sourcing-aware
       crypto-shred: emit a StudentErasureRequested_V1 event into the
       student stream, drive a long-running saga that:
         a. Soft-deletes/anonymizes StudentProfileSnapshot
         b. Soft-deletes StudentPreferencesDocument
         c. Anonymizes (not deletes) the event stream by upcasting
            PII fields to their hash-with-pepper
         d. Soft-deletes TutorMessageDocument + TutorThreadDocument
         e. Tombstones any cached projection
         f. PRESERVES StudentRecordAccessLog (FERPA §99.32 requires
            disclosure log to outlive the record)
       and emits StudentErasureCompleted_V1 when done.
    2. Wire ProcessErasureAsync into a Quartz/Hangfire cron job that
       runs nightly and processes everything in `CoolingPeriod` whose
       requestedAt + 30d < now.
    3. Replace the lying log line with a structured event that lists
       what was actually erased + what was preserved.
    4. Add a regression test that asserts after RequestErasureAsync +
       cron-tick:
         - StudentProfileSnapshot for the student is gone OR has all
           Pii fields nulled
         - tutor history is gone
         - StudentRecordAccessLog for the student is preserved
         - The cooling-period gate prevents early processing
  test_required: |
    Erasure end-to-end test that creates a student, attaches profile +
    sessions + tutor history, triggers erasure, advances clock past
    cooling period, runs the worker, then asserts every row containing
    the student's PII has been removed or anonymized.

- id: FIND-privacy-006
  severity: p1
  category: privacy
  framework: GDPR (Art 20)
  file: src/shared/Cena.Infrastructure/Compliance/StudentDataExporter.cs
  line: 70-93
  evidence:
    - type: file-read
      content: |
        StudentDataExporter.Export(string studentId, object dataObject)
        only iterates the public properties of the SINGLE object passed in,
        which the GdprEndpoints caller hardcodes as StudentProfileSnapshot.
        It does NOT export:
          - the event stream for the student
          - tutor threads / tutor messages
          - learning sessions / focus events
          - notifications received
          - StudentPreferencesDocument
          - DeviceSessionDocument (which devices have signed in)
          - access log entries (the student's own §99.32 view)
  finding: |
    The portability export is structurally incomplete. GDPR Art 20
    requires "all personal data which he or she has provided to the
    controller". A child who exports their data gets only the cached
    snapshot (display name, locale, mastery scores) and NONE of the
    free-text tutor conversations they had — which are the most
    sensitive PII the platform holds.
  root_cause: |
    The exporter was designed as a generic reflection scanner over a
    single passed-in object. The caller never enumerates the full set
    of stores that hold student-related data.
  proposed_fix: |
    Build a StudentDataExportOrchestrator that walks every Marten
    document store touching the student (profile, prefs, devices, tutor
    threads, tutor messages, share tokens, notifications, audit log)
    plus the event stream, and produces a single export bundle. Sign
    the bundle so a parent can verify integrity. Make the result a
    downloadable ZIP, not an inline JSON response.
  test_required: |
    Test that creates 3 tutor threads, 5 sessions, 2 devices, then runs
    the exporter and asserts the resulting bundle contains rows from
    all 6 store types plus the event stream.

- id: FIND-privacy-007
  severity: p0
  category: privacy, fake-fix
  framework: GDPR (Art 7)
  file: src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs
  line: 24-104
  evidence:
    - type: grep
      content: |
        $ grep -rn 'HasConsentAsync' src/
        src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs:29:
        src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs:98:
        # Defined twice in the same file (interface + impl). Zero callers.
    - type: file-read
      content: |
        ConsentType enum: Analytics, Marketing, ThirdParty (3 values).
        These are not bound to any specific processing purpose; they
        are not bound to any data flow; nothing in the codebase reads
        them before processing student data.
  finding: |
    The consent system is cosmetic. HasConsentAsync is defined but no
    producer or consumer of student data ever calls it before processing.
    The consent toggles in settings/privacy.vue are stored only in
    localStorage and are never POSTed to the server. The 3 ConsentType
    values do not map to specific Article 6 lawful bases. A student or
    parent toggling consent makes zero functional difference.
  root_cause: |
    The consent system was built as a CRUD store without consumer wiring.
    The data-processing call sites (LearningSessionActor, TutorEndpoints,
    LeaderboardService, FocusAnalyticsService) were never updated to
    gate on consent.
  proposed_fix: |
    1. Define a closed enum ProcessingPurpose with the 7-10 actual
       processing purposes the platform uses (adaptive recommendation,
       progress reporting to teacher, peer comparison, leaderboard
       display, AI tutoring with third party, content recommendation,
       cross-tenant benchmarking, etc.).
    2. Add a [RequiresConsent(ProcessingPurpose)] attribute that the
       request pipeline reads and short-circuits with 403 +
       error="consent_required" if the consent is missing.
    3. Default minor consent to "denied" for everything except the
       absolute minimum (account auth + session continuity).
    4. Wire settings/privacy.vue to call /api/me/consent with the
       ProcessingPurpose, and persist server-side via
       GdprConsentManager.RecordConsentAsync.
    5. Audit-log every consent change (already partially supported via
       StudentRecordAccessLog — add a dedicated ConsentChangeLog).
  test_required: |
    Auth contract test: with consent.peer_comparison = false, a request
    to /api/social/leaderboard returns 403. Set the consent and the
    same request returns 200. Same gating for /api/tutor (third-party
    AI) and /api/admin/analytics (cross-tenant aggregate).

- id: FIND-privacy-008
  severity: p0
  category: privacy, compliance
  framework: GDPR (Art 28), ICO-Children (Std 9)
  file: src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs
  line: 25-75
  evidence:
    - type: file-read
      content: |
        ClaudeTutorLlmService is the tutoring backend. It instantiates
        AnthropicClient with apiKey from configuration and calls
        Messages.Create(...) with the student's free-text prompt. The
        request body is sent unmodified to api.anthropic.com.
    - type: file-read
      content: |
        TutorEndpoints.cs:280-291 stores the user's free-text input
        verbatim in TutorMessageDocument with no redaction, no
        moderation, no PII scrubbing. The same content is then sent
        to Anthropic.
    - type: grep
      content: |
        $ grep -rn 'PII\|redact\|sanitize\|moderate' src/actors/Cena.Actors/Tutor/
        # only TutorSafetyGuard validates LLM output, not student input
  finding: |
    Student free-text input is sent to api.anthropic.com (US-based
    third-party processor) without:
      - a Data Processing Agreement disclosed to the student or parent
      - any processor-disclosure language in a privacy policy (which
        doesn't exist anyway, see FIND-privacy-002)
      - any consent gate (see FIND-privacy-007)
      - any PII scrubbing of the prompt before send
      - any cross-border transfer mechanism (Standard Contractual
        Clauses for EU → US)
      - any retention or deletion contract with Anthropic
    A child can type their full name, school, address, phone number, or
    a safeguarding-relevant disclosure, and that string ends up in
    Anthropic logs and in the local Marten database forever.
  root_cause: |
    The Anthropic integration was hardened to remove a stub
    (FIND-arch-004) but the privacy-impact assessment was never done.
    The processor relationship is invisible to the data subject.
  proposed_fix: |
    1. Sign and publish a DPA with Anthropic (covering Article 28
       processor obligations + cross-border transfer SCCs for EU
       traffic + a data deletion contract).
    2. Add a privacy-policy section disclosing Anthropic as a sub-processor.
    3. PII-scrub the outgoing prompt: strip phone numbers, addresses,
       last names, school names, parent names before send. Use a
       deterministic hashed reference if context is needed.
    4. Add a content-classification hook on the INPUT (not just output)
       that detects safeguarding-relevant content (self-harm, abuse
       disclosure, predatory contact) and routes to a human escalation
       channel BEFORE the LLM call.
    5. Tutor history retention should be 90 days max (not the 7y
       education-record window) per data minimisation.
    6. Make the Anthropic connection optional behind consent.tutoring
       — if not consented, the tutor feature is hidden, not present-and-broken.
  test_required: |
    Pact test against a mock Anthropic endpoint that asserts the
    outbound payload contains zero substrings from a known-PII test
    fixture (full name, phone, address, school). Auth test that the
    /api/tutor route returns 403 when consent.tutoring is false.

- id: FIND-privacy-009
  severity: p1
  category: privacy
  framework: GDPR (Art 35), ICO-Children (Std 1, 5, 12)
  file: docs/, .agentdb/
  line: -
  evidence:
    - type: bash
      content: |
        $ find . -iname '*dpia*' -o -iname '*data.protection.impact*'
        # zero matches across the entire repo
        $ find docs -iname '*privacy*' -o -iname '*compliance*' \
              -o -iname '*risk*' | head
        # only review reports and a "social-learning-research.md" file
  finding: |
    There is no Data Protection Impact Assessment artifact anywhere in
    the repo. WP29 + ICO joint guidance treats the systematic monitoring
    of children + AI-driven profiling + automated decisions affecting
    educational outcomes as PRESUMPTIVELY high-risk under GDPR Art 35,
    requiring a DPIA before processing begins. ICO Children's Code Std 1
    (best interests of the child) and Std 12 (profiling) cannot be
    discharged without the DPIA exercise.
  root_cause: |
    No DPIA gate in the product process. The platform was built as a
    standard SaaS application without acknowledging the additional
    obligations triggered by minor + AI + adaptive profiling.
  proposed_fix: |
    1. Author a DPIA per the ICO template (https://ico.org.uk/for-organisations/guide-to-data-protection/guide-to-the-uk-gdpr/data-protection-impact-assessments-dpias/sample-dpia-template/).
    2. Cover the 7 processing purposes (FIND-privacy-007).
    3. Identify mitigations for each high-risk profiling element
       (Elo rating, mastery prediction, adaptive question selection,
       AI tutoring).
    4. Append the DPIA to docs/compliance/dpia-2026-04.md as a versioned
       artifact, refreshed annually or on material change.
    5. Cross-link the DPIA from the public privacy policy (Std 4
       transparency).
  test_required: |
    File-existence test in CI: docs/compliance/dpia-*.md must exist
    and contain the 9 ICO-template sections.

- id: FIND-privacy-010
  severity: p1
  category: privacy
  framework: ICO-Children (Std 3 + Std 7), GDPR (Art 25)
  file: src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs
  line: 465-499
  evidence:
    - type: file-read
      content: |
        CreateDefaultPreferences(string studentId) returns:
          ProfileVisibility = "class-only"      // visible to peers
          ShowProgressToClass = true            // peers see your progress
          AllowPeerComparison = true            // leaderboard opt-in default ON
          EmailNotifications = true             // marketing-style nudge
          PushNotifications = true              // marketing-style nudge
          DailyReminder = true                  // engagement loop default ON
          WeeklyProgress = true                 // engagement loop default ON
          StreakAlerts = true                   // gamification nudge
          NewContentAlerts = true               // marketing-style nudge
          ShareAnalytics = false                // the only safe default
  finding: |
    Default settings for newly-created student preferences are NOT
    high-privacy. ICO Standard 3 ("privacy by default") and Standard 7
    ("default settings") require all settings affecting privacy to
    default to the most private option. 8 of the 9 visibility/engagement
    toggles default ON, including peer-comparison and class-visibility.
    The default-on push notifications also engage Standard 13 (nudge
    techniques) by establishing engagement loops the child did not
    consent to.
  root_cause: |
    Defaults were chosen to maximize engagement, not to comply with
    ICO Children's Code. The CreateDefaultPreferences method has no
    branch on age-tier and no branch on detected jurisdiction.
  proposed_fix: |
    1. Flip every visibility/engagement default to its most-private
       value (false / "private").
    2. Surface an opt-in dialog after first session that lets the child
       (or parent for <13) explicitly enable peer comparison, class
       visibility, leaderboards, and notifications — child-friendly
       language only, with separate granular toggles.
    3. Branch CreateDefaultPreferences on the detected age tier from
       FIND-privacy-001: <13 cannot enable any visibility setting at
       all without parental consent token; 13-15 can self-enable; 16+
       see the standard adult flow.
    4. Add an audit log entry every time a default is overridden.
  test_required: |
    Unit test on CreateDefaultPreferences asserting every visibility
    field is false and every notification is false. Integration test
    that a fresh student account /api/me/settings GET returns
    ProfileVisibility="private", ShowProgressToClass=false, etc.

- id: FIND-privacy-011
  severity: p2
  category: privacy
  framework: ICO-Children (Std 8 data minimization), GDPR (Art 5(1)(c))
  file: src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs
  line: 14-67
  evidence:
    - type: file-read
      content: |
        StudentProfileSnapshot has the following PII-tagged fields:
          [Pii(Low,  identity)] StudentId
          [Pii(Med,  identity)] FullName
          [Pii(Low,  identity)] SchoolId
        And the following NON-PII-tagged fields that are also PII:
          DisplayName  (string, child-supplied)
          Bio          (string, child-supplied free text — MOST sensitive)
          Locale       (locale)
          Subjects     (string[] — favourite subjects, behavioural data)
          Role         (account role)
          Visibility   (privacy preference)
        Plus extensive behavioural profiling (ConceptMastery dictionary,
        EloRating, BaselineAccuracy, BaselineResponseTimeMs,
        SessionCount, CurrentStreak, LongestStreak, LastActivityDate)
        which is profiling under GDPR Art 4(4) but not flagged.
  finding: |
    The PII classification on StudentProfileSnapshot is incomplete.
    DisplayName, Bio, and the behavioural profiling fields
    (mastery, Elo, baselines, streaks, last-activity) are not Pii-tagged,
    so they bypass the PiiDestructuringPolicy log redaction
    (FIND-sec-004 fix) and the StudentDataExporter PII annotation.
    Bio is the most sensitive field (child-supplied free text) and has
    no length cap, no profanity filter, no Pii classification.
  root_cause: |
    The Pii attribute was retrofitted to a small number of fields
    without a systematic re-classification pass. The profiling fields
    were not recognised as PII because they aren't "identifying" in
    isolation.
  proposed_fix: |
    1. Add [Pii(High, behavioral)] on Bio, DisplayName.
    2. Add [Pii(Medium, behavioral)] on the profiling fields (mastery,
       Elo, baselines, streaks, last-activity).
    3. Cap Bio at 500 chars and run it through a moderation filter
       before persistence.
    4. Add a CI check that any new property on StudentProfileSnapshot
       (or any derived snapshot) must have a [Pii] attribute or be
       explicitly excluded with [PiiNone] + a justification comment.
  test_required: |
    Reflection test that asserts every public property on
    StudentProfileSnapshot has either a [Pii] or a [PiiNone] attribute.
    Bio length validator test.

- id: FIND-privacy-012
  severity: p1
  category: privacy
  framework: FERPA (34 CFR §99.32 record of disclosures)
  file: src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs
  line: 21-29
  evidence:
    - type: file-read
      content: |
        AuditedPaths is a static HashSet of just 6 path prefixes:
          /api/admin/mastery
          /api/admin/focus
          /api/admin/tutoring
          /api/admin/outreach
          /api/admin/cultural
          /api/v1/mastery
        The middleware short-circuits on every other path with
        `await _next(context); return;` BEFORE logging.
    - type: bash
      content: |
        $ grep -rn 'group.MapGet\|group.MapPost\|app.MapGet\|app.MapPost' \
            src/api/Cena.Admin.Api/ src/api/Cena.Admin.Api.Host/Endpoints/ \
            | grep -i 'student\|leaderboard\|focus\|mastery\|class\|session\|gdpr\|compliance\|analytics' \
            | head
        # ~30+ admin endpoints touch student data; only 5 path prefixes
        # are audited.
  finding: |
    The FERPA disclosure audit middleware audits only 6 hardcoded path
    prefixes. Endpoints that read student data outside this list —
    including the entire /api/admin/students/*, /api/admin/leaderboard/*,
    /api/admin/sessions/*, /api/admin/analytics/*,
    /api/admin/gdpr/* (the consent and erasure endpoints!), and
    /api/admin/compliance/* surfaces — are not audited at all. FERPA
    §99.32 requires the disclosure record for ALL non-routine
    disclosures, not for an arbitrary 6-path subset.
  root_cause: |
    Audited paths were hand-curated and never updated when new
    student-data endpoints were added. There is no convention that
    defaults a new admin endpoint into the audit list.
  proposed_fix: |
    1. Replace the AuditedPaths allowlist with a denyList: audit
       EVERY /api/admin/* and /api/v1/* and /api/me/* path that returns
       student-identifiable data, except an explicit list of
       non-student-identifying paths (e.g., /api/admin/health).
    2. Add a [FerpaAudited] attribute on endpoint handlers; the
       middleware reads it and audits. New endpoints default to
       audited unless explicitly tagged [FerpaPublic].
    3. Add a CI test that asserts every new endpoint has a
       [FerpaAudited] or [FerpaPublic] tag — fails the build if not.
  test_required: |
    Reflection test that walks the registered endpoint table and asserts
    coverage. Integration test that calls /api/admin/leaderboard, then
    queries StudentRecordAccessLog for that path and finds the row.

- id: FIND-privacy-013
  severity: p2
  category: privacy
  framework: FERPA (34 CFR §99.32(a)(2))
  file: src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs
  line: 19-41
  evidence:
    - type: file-read
      content: |
        StudentRecordRetention = 7y  (FERPA-correct for the record)
        AuditLogRetention      = 5y  (compliance "best practice")
        # Problem: §99.32(a)(2) requires the disclosure record to be
        # kept "as long as the records to which they pertain are
        # maintained". Pertaining records last 7y, the disclosure log
        # only 5y — the disclosure record expires before the underlying
        # education record.
  finding: |
    The audit log retention (5y) is shorter than the education-record
    retention (7y). FERPA §99.32(a)(2) requires the disclosure record
    to outlive the records to which it pertains. After year 5 the
    school can no longer prove who accessed the still-extant student
    record.
  root_cause: |
    Two different policy authors set the two windows from different
    rules of thumb. No one cross-checked them against the §99.32(a)(2)
    hierarchy.
  proposed_fix: |
    1. Set AuditLogRetention to max(StudentRecordRetention,
       AuditLogRetention) so the audit always outlives the record.
    2. Add an assertion in the retention worker (FIND-privacy-004) that
       fails if anyone tries to set audit retention shorter than
       record retention.
  test_required: |
    Constant invariant test:
      Assert.True(AuditLogRetention >= StudentRecordRetention).

- id: FIND-privacy-014
  severity: p2
  category: privacy, compliance
  framework: Israel-PPL (Amendment 13, 2024 — DPO appointment)
  file: docs/, src/
  line: -
  evidence:
    - type: bash
      content: |
        $ grep -rni 'Data Protection Officer\|DPO\|privacy.officer' \
            docs src 2>/dev/null
        # zero matches
  finding: |
    Israel Privacy Protection Law Amendment 13 (in force 2024) requires
    a designated Data Protection Officer for processors of personal
    data of more than 100,000 people, or processors handling sensitive
    data of more than 10,000 people. Cena's prospective Israeli market
    crosses both thresholds. There is no DPO appointment in the repo,
    no DPO contact in any of the (non-existent) privacy notices, no
    DPO email in the consent endpoints.
  root_cause: |
    Israel-specific obligations were not enumerated when the user-memory
    flagged Israel as in-scope. The compliance work was scoped to FERPA
    + GDPR only.
  proposed_fix: |
    1. Designate a DPO (internal or external) and document the
       appointment in docs/compliance/dpo-appointment.md.
    2. Publish DPO contact (email + Hebrew name + Hebrew phone) on
       the Privacy Policy page when authored (FIND-privacy-002).
    3. Add a /api/me/contact/dpo endpoint that records the DSAR /
       complaint and routes to the DPO mailbox.
  test_required: |
    File existence + contents test that
    docs/compliance/dpo-appointment.md exists and includes name +
    email + Hebrew translation.

- id: FIND-privacy-015
  severity: p1
  category: privacy
  framework: GDPR (Art 5(1)(c) data minimisation), ICO-Children (Std 8)
  file: src/api/Cena.Student.Api.Host/Endpoints/AuthEndpoints.cs
  line: 102-105
  evidence:
    - type: file-read
      content: |
        AuthEndpoints PasswordReset captures clientIp from
        X-Forwarded-For or RemoteIpAddress and logs it alongside the
        emailHash. StudentDataAuditMiddleware also captures and persists
        IP for every audited request.
    - type: grep
      content: |
        $ grep -rn 'IpAddress\|RemoteIpAddress' src/shared src/api
        ... 5+ persistence points
  finding: |
    The platform stores raw client IP addresses for password-reset
    requests (in logs) and for every audited admin request (in
    StudentRecordAccessLog), with no documented retention, no
    anonymisation (e.g. /24 truncation for IPv4), and no disclosure.
    Under GDPR Recital 30 IPs are personal data; under ICO Std 8 they
    must be minimised. For a child-serving product, raw IPs are also
    a re-identification vector that must be justified.
  root_cause: |
    IP capture was added for abuse detection without a minimisation
    review.
  proposed_fix: |
    1. Truncate IPv4 to /24 and IPv6 to /64 BEFORE persistence
       (sufficient for abuse pattern detection, removes the unique
       household identifier).
    2. Apply the same retention as the parent record
       (StudentRecordAccessLog window).
    3. Disclose IP processing in the privacy policy (FIND-privacy-002).
  test_required: |
    Unit test on the IP normalisation function: 203.0.113.42 → 203.0.113.0.
    Integration test that StudentRecordAccessLog entries never contain
    a full IPv4.

- id: FIND-privacy-016
  severity: p1
  category: privacy
  framework: ICO-Children (Std 9 — connected services), GDPR (Art 28)
  file: src/student/full-version/src/plugins/sentry.ts
  line: 1-40
  evidence:
    - type: file-read
      content: |
        sentry.ts is a "stub" with the comment:
          "Real init (import @sentry/vue and initialize with app + dsn
           + tracing) will replace this in STU-W-OBS-SENTRY follow-up."
        The shim API includes setUser({id, email}) — the wiring is
        ready to send student id + email to a Sentry SaaS endpoint
        the moment the DSN is provisioned.
  finding: |
    Sentry is currently a stub (zero data flow), but the integration
    contract sends `{id, email}` to a third-party processor and is
    explicitly designed to be enabled via env var with no further
    code review. There is no Sentry DPA, no privacy-policy disclosure,
    no consent gate, and no PII scrubbing. The follow-up task as
    written is a privacy time-bomb.
  root_cause: |
    Sentry was scaffolded as a SaaS observability tool without an
    impact assessment. The setUser() shape pre-commits the integration
    to send identifiable PII.
  proposed_fix: |
    1. Before STU-W-OBS-SENTRY is unblocked, sign a Sentry DPA + add
       processor disclosure to the privacy policy.
    2. Replace setUser({id, email}) with setUser({id_hash}) — never
       send the email or display name to Sentry.
    3. Configure Sentry's `beforeSend` PII scrubber and disable
       defaultPii.
    4. Disable session replay entirely for a child-serving product.
    5. Gate Sentry init on consent.observability = true.
  test_required: |
    Pact test against a mock Sentry that asserts no email, no
    full name, no IP address, no localStorage contents are forwarded.

- id: FIND-privacy-017
  severity: p2
  category: privacy
  framework: ICO-Children (Std 9), GDPR (Art 28)
  file: src/student/full-version/index.html, src/admin/full-version/index.html
  line: -
  evidence:
    - type: bash
      content: |
        $ grep -ri 'fonts.googleapis\|webfontloader' src/student/full-version/src
        src/student/full-version/src/plugins/webfontloader.ts:
          (loads Public Sans via Google Fonts)
    - type: network-trace
      content: |
        Live capture from Playwright on http://localhost:5175/register:
          [GET] https://fonts.googleapis.com/css?family=Public+Sans:...
        That request fires on the unauthenticated register page,
        carrying User-Agent + IP to Google before the user has
        consented to anything.
  finding: |
    The student web (and the admin web — same Vuexy template) loads
    Public Sans from Google Fonts on every page load, BEFORE auth and
    BEFORE any consent. This is a third-party data flow about a minor
    to a US controller, occurring on the unauthenticated register
    page. There is no consent banner, no DPA, no privacy-policy
    disclosure.
  root_cause: |
    The Vuexy template ships with webfontloader.ts that loads from
    Google Fonts CDN. Self-hosted fallback was never wired.
  proposed_fix: |
    1. Self-host Public Sans via the existing Vite font pipeline (the
       roboto-fontface dependency is already self-hosted, do the
       same for Public Sans).
    2. Remove webfontloader.ts.
    3. Add CSP `font-src 'self'` and assert in tests that no
       fonts.googleapis.com request fires from any page.
  test_required: |
    Playwright network trace test on /register, /login, /home that
    asserts zero requests to fonts.googleapis.com or any other
    google.com domain.

- id: FIND-privacy-018
  severity: p1
  category: privacy
  framework: ICO-Children (Std 11 — published policies), GDPR (Art 28)
  file: src/student/full-version/src/pages/social/, src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
  line: 27-39
  evidence:
    - type: bash
      content: |
        $ grep -nri 'report\|abuse\|moderation\|block.*user' \
            src/student/full-version/src/pages/social/
        # zero matches
        $ grep -n 'group.MapPost\|group.MapGet' \
            src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs
        ... GetClassFeed, GetPeerSolutions, GetFriends, GetStudyRooms,
            AddReaction, AddComment, SendFriendRequest,
            AcceptFriendRequest, CreateStudyRoom, JoinStudyRoom,
            LeaveStudyRoom
        # 11 social endpoints. Zero report/block/moderation endpoints.
  finding: |
    The social/UGC surface (class feed, comments, peer solutions,
    friend requests, study rooms) has zero in-app reporting,
    zero blocking, and zero moderation. ICO Children's Code Std 11
    requires "tools to support children's right to be heard, to
    object, to challenge profiling and to seek redress" — none exist.
    A child being bullied in a study room comment thread has no
    in-app way to report it.
  root_cause: |
    Moderation was scoped to content moderation for question-bank
    ingestion (ContentModerationService) but not extended to UGC.
    The social endpoints were built feature-first without a safeguarding
    review.
  proposed_fix: |
    1. Add /api/social/report endpoint (anonymous, rate-limited) that
       creates a SocialReportDocument with severity, category, content
       reference, reporter (optional), reportedAt.
    2. Add a "Report" button on every comment, friend request, study
       room, and class-feed item.
    3. Add /api/social/block endpoint and a per-student blocklist
       that the social queries respect.
    4. Add a back-office moderation queue for the safeguarding admin.
    5. For under-13 students, default friend-requests, comments, and
       study rooms to OFF behind the parent consent gate
       (FIND-privacy-001 + FIND-privacy-007).
  test_required: |
    E2E test on a child user that posts a comment, then on a second
    child user that sees the comment, hits "Report", confirms a
    SocialReportDocument was created. E2E test that a blocked user's
    comments do not appear in the blocker's class feed.
```

---

## Summary

| Severity | Count |
|---|---|
| P0 | 7 |
| P1 | 8 |
| P2 | 4 |
| P3 | 0 |
| **Total** | **19** |

By framework (counting any finding citing each):

| Framework | Findings |
|---|---|
| COPPA | 5 |
| GDPR-K (incl. Art 7, 8, 17, 20, 28, 35) | 11 |
| ICO Children's Code | 12 |
| FERPA | 3 |
| Israel PPL | 3 |

**Top 3 by legal exposure** (with the 10x privacy multiplier applied at merge):

1. **FIND-privacy-001** — Registration of minors with no age gate, no parental
   consent, no DOB field, no consent strings in any locale. This is the
   single P0 that crosses every framework simultaneously (COPPA §312.5,
   GDPR Art 8, ICO Std 7, Israel PPL).
2. **FIND-privacy-005** — Right-to-erasure is a fake fix. Endpoint returns
   200 but the underlying ProcessErasureAsync is dead code, and even when
   wired only deletes consent/audit logs, never the actual student record
   or tutor history.
3. **FIND-privacy-008** — Student free-text input is sent to api.anthropic.com
   with zero consent, no DPA, no PII scrubbing, and no safeguarding hook.
   A child can disclose abuse or self-harm and the platform persists +
   transmits it to a third-party processor with no escalation path.

## Compliance score recap

| Framework | Pass | Partial | Fail | Fake |
|---|---|---|---|---|
| COPPA (5 controls audited) | 0 | 0 | 5 | 0 |
| GDPR-K (7 controls audited) | 0 | 1 | 4 | 2 |
| ICO Children's Code (15 stds) | 0 | 3 | 8 | 1 (3 N/A) |
| FERPA (3 controls audited) | 0 | 2 | 1 | 0 |
| Israel PPL (2 controls audited) | 0 | 0 | 2 | 0 |

## Age gate verification

- **Present**: NO
- **Bypassable**: N/A (there is no gate)
- **Evidence**: see FIND-privacy-001 + screenshot
  `privacy-register-student.png` (worktree root). The mobile-only
  AgeSafetyService at `src/mobile/lib/core/services/age_safety_service.dart`
  is scaffolding for a Flutter app that does not exist yet (mobile is
  planned per stack ground truth).

## Third-party network calls (live capture)

| Surface | Calls captured | Domains |
|---|---|---|
| Student `/register` (unauth) | 1 third-party | fonts.googleapis.com (Google Fonts CSS) |
| Student `/login` (unauth) | 0 third-party | — |
| Student `/home` (auth required, hit unauth → 404) | 0 third-party | — |
| Admin `/login` (unauth) | 0 third-party | — |

Latent third-party flows present in code but not currently active:

- **Anthropic** (`api.anthropic.com`) via tutor chat — see
  FIND-privacy-008. ACTIVE when Cena:Llm:ApiKey is configured.
- **Sentry** SaaS — stub only, but FIND-privacy-016 documents the
  pre-wired PII shape that would activate the moment a DSN is provisioned.
- **Firebase Auth** (`identitytoolkit.googleapis.com`,
  `securetoken.googleapis.com`) — admin uses real Firebase initializeApp,
  student is mocked; the real SDK ships PII when enabled. (Student auth
  being a mock is also a `sec` lens problem; the privacy angle is
  documented under FIND-privacy-008's processor list once student is
  un-stubbed.)
- **Google Fonts** — fires today on the unauth register page, see
  FIND-privacy-017.
- **mapbox-gl** — declared as a dependency in BOTH student and admin
  package.json but never imported by source code — dead dependency, no
  runtime data flow today (still a supply-chain concern but no privacy
  finding while it's unused).

## DB columns storing learner PII

Inventoried via `grep -rn 'Pii(' src/` and code reading. The list is
**incomplete** — see FIND-privacy-011 — but here is what is currently
present on `main`:

| Document / Event | Field | PII tag | Purpose | Retention enforced? |
|---|---|---|---|---|
| StudentProfileSnapshot | StudentId | Pii(Low, identity) | account key | NO (FIND-privacy-004) |
| StudentProfileSnapshot | FullName | Pii(Medium, identity) | UI display | NO |
| StudentProfileSnapshot | SchoolId | Pii(Low, identity) | tenant scoping | NO |
| StudentProfileSnapshot | DisplayName | UNTAGGED (FIND-privacy-011) | UI | NO |
| StudentProfileSnapshot | Bio | UNTAGGED (FIND-privacy-011) | child free text | NO |
| StudentProfileSnapshot | Subjects[] | UNTAGGED | preference | NO |
| StudentProfileSnapshot | EloRating + Attempt count | UNTAGGED (profiling) | adaptive learning | NO |
| StudentProfileSnapshot | ConceptMastery dict | UNTAGGED (profiling) | adaptive learning | NO |
| StudentProfileSnapshot | LastActivityDate | UNTAGGED | engagement | NO |
| AdminUser | Email | Pii(High, contact) | login | NO |
| AdminUser | DisplayName | Pii(Medium, identity) | UI | NO |
| TutorMessageDocument | Content | UNTAGGED | child free text → 3p AI | NO |
| TutorThreadDocument | Title, Subject, Topic | UNTAGGED | conversation context | NO |
| StudentRecordAccessLog | StudentId, AccessedBy, IpAddress | UNTAGGED | FERPA audit | 5y declared (FIND-privacy-013), not enforced (FIND-privacy-004) |
| ConsentRecord | StudentId | UNTAGGED | consent provenance | NO |
| ErasureRequest | StudentId, RequestedBy | UNTAGGED | erasure provenance | NO |
| StudentPreferencesDocument | StudentId + 20+ preference flags | UNTAGGED | UI defaults | NO |
| DeviceSessionDocument | StudentId, DeviceModel, DeviceName | UNTAGGED | device mgmt | NO |
| ShareTokenDocument | StudentId, Audience, Token | UNTAGGED | sharing | NO |
| NatsBusMessages (multiple records) | StudentId | Pii(Low, identity) | bus | n/a (in-flight) |

`Bio`, `TutorMessageDocument.Content`, and `DeviceSessionDocument.DeviceModel`
are the highest-risk fields that are NOT classified.

## Inputs to `qa` lens

For the `qa` lens reviewer: the closed-finding-with-no-test pattern is
strong here. None of the new compliance code (RightToErasureService,
GdprConsentManager, StudentDataAuditMiddleware, DataRetentionPolicy,
StudentDataExporter) has an end-to-end test that proves the control
actually works. The dead-code `ProcessErasureAsync` would have been
caught by even a smoke test of "after request + 31 days, the student
profile is gone".

## Inputs to `sec` lens

Two findings that overlap with `sec` (intentionally surfaced here from
the privacy angle, not duplicated in the queue under `sec`):

1. **Firebase mock on student web** — the privacy angle is that the
   stub auth means the consent flow has no enforceable identity. The
   `sec` angle is the identity verification gap. Already covered by the
   v1 `sec` audit indirectly; flagging here for cross-link.
2. **IP address capture** — see FIND-privacy-015. Privacy angle is
   minimisation; sec angle is abuse detection coverage.

These are NOT duplicate-enqueued; the privacy queue rows above stand
on their own framework citations.

---

## Worktree

- **Branch**: `claude-subagent-privacy/cena-reverify-2026-04-11`
- **Worktree**: `.claude/worktrees/review-privacy/`
- **Screenshots**: `privacy-register-student.png`, `privacy-student-login.png`,
  `privacy-admin-login.png`, `privacy-student-home.png` at the worktree root.
