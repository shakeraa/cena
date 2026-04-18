# RDY-062: Live Assistance — Teacher-First, AI-Fallback

- **Status**: Requested — not started
- **Priority**: High — closes the "student is stuck, what happens?" UX gap
- **Source**: Shaker 2026-04-19 — "student on a question, asking the assistant
  (human if available, AI mostly as backup). Does the AI provide accurate /
  correct / helpful assistance?"
- **Tier**: 2 (quality — teacher effectiveness + student satisfaction)
- **Effort**: 6-9 days end-to-end
- **Depends on**:
  - RDY-020 SignalR event push-back (landed, student-side)
  - RDY-060 Admin SignalR hub (filed — teacher presence signal lives here)
  - RDY-061 Syllabus advancement (landed — AI grounding pulls chapter context)
- **Co-ships with**: ADR-0035 Live Assistance Routing Protocol

## Problem

When a student is stuck on a question, the current options are:
1. Pre-authored hint ladder (good, but bounded to what the seed authored per
   question — `LearningSessionActor.HintRequest` respects `HintLevel ≤
   scaffoldMeta.MaxHints`)
2. Open a tutor thread with `TutorMessageService` (AI — Claude via
   `ClaudeTutorLlmService` — with CAS verification on the output)

There is **no human-in-the-loop path**. A teacher who is online for the
class right now has no signal that a student wants help, no inbox to
grab the thread, and no ability to intercept before the AI responds.

The pedagogical consequence: a teacher who's available in the classroom
(physical or virtual) is functionally invisible to the product. The AI
tutor is competent at *math correctness* but weaker at *personalised
scaffolding*, and there's no mechanism to prefer the better signal when
it's available.

## Current state (2026-04-19)

### Works well today

| Capability | Where | Quality |
|---|---|---|
| CAS verification on LLM output | `Cena.Actors/Cas/CasLlmOutputVerifier.cs` | Math claims filtered via SymPy before student sees them |
| Step-by-step CAS gating | `StepVerifierService.cs` | Each explanation step checked for equivalence-preservation |
| Safeguarding classifier | `SafeguardingClassifier.cs` | High-severity concerns suppress LLM call, route to crisis escalation |
| PII scrubbing before LLM | `TutorPromptScrubber.cs` | FIND-privacy-008 |
| Cost circuit breaker | `ICostCircuitBreaker` | Per-student + per-school + global token gate |
| Tutor thread persistence | `TutorMessageDocument` | 90-day retention, GDPR Art. 17 wired |
| Pre-authored hint ladder | `LearningSessionActor.HintRequest` | Bounded by `scaffoldMeta.MaxHints` |

### NOT wired

- **Teacher presence** — no signal for "teacher X is online for classroom Y right now"
- **Help-request routing** — no logic for "ping teacher first, fall through to AI on timeout"
- **Teacher inbox UI** — no surface for a teacher to see incoming help requests with the student's current attempt
- **AI grounding upgrade** — `TutorContext` doesn't carry the student's current chapter, current partial attempt on the specific question, or the `QuestionCasBinding` canonical solution. AI tutors a generic student instead of *this* student on *this* question
- **Feedback loop** — no thumbs up/down or "this answer was wrong" escalation back to teachers or ReasoningBank

## Design

### Six pieces, tightly scoped

1. **Teacher presence** (depends on RDY-060)
   - SignalR group: `teacher:classroom:{classroomId}` + `teacher:school:{schoolId}`
   - Teacher's browser connection = presence signal; drop = presence lost
   - Gauge: `cena_live_teachers_online{schoolId,classroomId}` for the admin dashboard
   - Heartbeat: 30s client-side idle timeout dropping a teacher from the eligible list

2. **HelpRequest aggregate** (new, event-sourced)
   - Stream id: `help-{sessionId}-{questionId}-{seqNo}`
   - Events:
     - `HelpRequested_V1 { helpRequestId, studentId, sessionId, questionId, chapterId, studentAttemptSoFar, requestedAt }`
     - `TeacherNotified_V1 { helpRequestId, notifiedTeacherIds[], notifiedAt }`
     - `TeacherClaimed_V1 { helpRequestId, teacherId, claimedAt }`
     - `TeacherReplied_V1 { helpRequestId, teacherId, replyMessageId, repliedAt }`
     - `AiAnswered_V1 { helpRequestId, aiMessageId, casVerified, answeredAt }`
     - `HelpResolvedBy_V1 { helpRequestId, resolvedBy: "teacher"|"ai"|"student-gave-up", resolvedAt }`
   - Audit surface for "teacher-response SLA was met / not met"

3. **Routing service** (new, admin-api)
   - `POST /api/me/help-requests` from student
     - Creates `HelpRequested_V1`
     - Checks SignalR presence for `teacher:classroom:{classroomId}`
     - If ≥1 online → `TeacherNotified_V1` + pushes to teachers; returns status=`awaiting_teacher`
     - If 0 online → immediately fall through to AI tutor path; returns status=`ai_responding`
   - 30s SLA timer — if no `TeacherClaimed_V1` arrives, routing service fires
     the AI fallback. Configurable per-school.
   - `POST /api/admin/help-requests/{id}/claim` — teacher grabs the thread;
     emits `TeacherClaimed_V1`. AI fallback is cancelled if still pending.
   - `POST /api/admin/help-requests/{id}/reply` — teacher sends; emits
     `TeacherReplied_V1` + creates `TutorMessageDocument` on the thread.

4. **AI grounding upgrade**
   - `TutorContext` gains:
     - `CurrentChapterId` (from `StudentAdvancementState`)
     - `CanonicalAnswer` (from `QuestionCasBinding.CorrectAnswer`)
     - `StudentAttemptSoFar` (last wrong submission, if any)
     - `AttemptedSteps[]` (session-scoped misconception patterns, per ADR-0003)
   - System prompt additions: "The student is currently on Chapter {X}
     ({chapter title}) of the {track} syllabus. They tried {attempt} and
     it was incorrect because {misconception}. Scaffold toward the
     canonical answer WITHOUT revealing it directly."
   - CAS verification on response stays unchanged — `CasLlmOutputVerifier`
     runs every math claim against SymPy before the student sees it.

5. **Teacher reply UI** (admin-spa)
   - New page: `/apps/teaching/inbox` — live list of open help requests,
     sorted by wait time
   - Opening an entry shows:
     - The question + the student's current attempt inline (so the teacher
       sees what they're looking at)
     - The conversation history
     - A reply composer with a "mark this as a worked example for the
       question bank" checkbox — promotes high-quality explanations into
       the seed corpus (requires curriculum-admin review before publish)
   - Keyboard-first: Cmd+Enter sends, Esc closes without replying
   - Per-classroom filter for homeroom teachers

6. **Feedback loop**
   - Thumbs-up / thumbs-down buttons after every tutor response (AI or
     teacher)
   - Thumbs-down opens: "What was wrong?" → reasons (incorrect math, too
     complex, didn't answer my question, gave away the answer)
   - Down-voted AI responses:
     - Flag the trajectory for ReasoningBank cohort-level distillation
       (PII-redacted via the RDY-061 `AdvancementTrajectoryRedactor` pattern)
     - Surface in the teacher inbox as "review this AI response" — teacher
       can rewrite; their rewrite becomes the response the student sees
       (the bad AI message is marked superseded, kept for audit)

## Quality claims + how they're enforced

| Claim | Enforcement |
|---|---|
| AI never emits mathematically incorrect reasoning | `CasLlmOutputVerifier` runs every claim through SymPy; mismatches reject the response |
| AI never gives away the direct answer | System-prompt rule + CAS equivalence check — if the LLM's response matches `QuestionCasBinding.CanonicalAnswer` without scaffolding, it's rejected |
| Teacher-first routing respects teacher availability | Routing service checks SignalR presence before falling through to AI; audit log shows which path was taken |
| No student PII reaches the LLM | `TutorPromptScrubber` + `StudentPiiContext` (existing) |
| Safeguarding concerns never reach the LLM | `SafeguardingClassifier.Scan` before prompt build (existing) |
| Cost is bounded | `ICostCircuitBreaker` per-student/per-school/global (existing) |
| Teacher-reply audit trail exists | `HelpResolvedBy_V1` event stream; admin can query who answered what |
| Parental consent covers AI tutoring for under-13 | Verify with Ran before ship — may need consent copy update |

## Scope

### Phase 1 — HelpRequest aggregate + routing (2 days)

- Events, Marten projection, state
- `IHelpRequestRouterService` with presence check + SLA timer
- `POST /api/me/help-requests` + `POST /api/admin/help-requests/{id}/claim` + `POST /api/admin/help-requests/{id}/reply`
- Integration tests: teacher-online fast-path, timeout fallback, teacher-claim-mid-AI cancellation

### Phase 2 — AI grounding upgrade (1-2 days)

- Extend `TutorContext` DTO
- `TutorContextBuilder` service that loads chapter + canonical answer +
  attempt history inside a single Marten round-trip
- Prompt template update; CAS verification unchanged
- A/B marker in the LLM trajectory log so we can measure grounded-vs-
  ungrounded response quality after ship

### Phase 3 — Teacher presence (1 day, RDY-060 dep)

- SignalR groups on admin hub
- Presence registry (Redis-backed) + heartbeat
- `GetOnlineTeachersForClassroomAsync` API for the router to consult

### Phase 4 — Teacher inbox UI (2-3 days)

- `/apps/teaching/inbox` page
- Live-updated list via SignalR
- Question + attempt preview card
- Reply composer with "promote as worked example" checkbox
- Keyboard shortcuts

### Phase 5 — Feedback loop (1-2 days)

- `POST /api/me/help-requests/{id}/feedback { rating, reason? }`
- AI-down-voted responses routed to teacher review queue
- ReasoningBank cohort aggregator integration (reuses redactor from RDY-061)

## Acceptance Criteria

### Routing
- [ ] Student help request lands `HelpRequested_V1` within 200ms
- [ ] If teacher present: `TeacherNotified_V1` fires; teacher sees notification within 500ms; SLA timer starts at 30s
- [ ] If no teacher present OR 30s timeout: AI fallback fires; response CAS-verified; student sees it
- [ ] If teacher claims during AI stream: AI stream is cancelled within 2s; teacher reply is the visible response
- [ ] Audit log captures every `HelpResolvedBy_V1` with the resolution path

### AI quality
- [ ] Grounded context reaches Claude (verified via trajectory log)
- [ ] CAS verifier still filters every response — existing guarantee preserved
- [ ] Down-voted response with "incorrect math" reason: backend cross-checks via `CasLlmOutputVerifier`; if the verifier had passed it, flag for manual review

### Teacher UX
- [ ] Inbox page live-updates without refresh
- [ ] Question + current student attempt rendered in the reply view
- [ ] Reply round-trip (teacher hits Send → student sees) < 1s on local network

### Safety + privacy
- [ ] Safeguarding classifier still suppresses LLM call before routing fires (existing guarantee preserved)
- [ ] No student PII in the LLM prompt — `TutorPromptScrubber` applied to grounded context additions too
- [ ] Parental-consent check for under-13 before routing a help request to a human teacher (new consent axis)
- [ ] Down-vote trajectory flagged to ReasoningBank passes the PII-redactor test from RDY-061

### Observability (Iman's lens)
- [ ] Metrics: `cena_help_requests_total`, `cena_help_resolved_by{path}`,
  `cena_help_sla_met_total`, `cena_live_teachers_online{schoolId}`,
  `cena_help_ai_cas_rejected_total`
- [ ] Alerts: teacher-response-SLA drops below 70% over 15m rolling window
- [ ] Runbook: `docs/ops/runbooks/live-help-degraded.md`

### Architecture tests (Rami's lens)
- [ ] Help-request endpoint cannot be reached without an owning session
  (student claims they want help on a question they aren't on — 403)
- [ ] Teacher cannot claim a help request from another school (IDOR test)
- [ ] AI fallback cannot skip CAS verification (integration test: mock LLM
  returns bad math → response is rejected, NOT sent to student)
- [ ] PII scrubber is non-bypassable in the prompt path (unit test:
  passing an unscrubbed context throws before reaching the LLM client)

## Open questions

1. **SLA window**: 30s default is a guess. Need teacher-UX testing.
2. **Multiple teachers online — who gets notified?** All vs round-robin vs
   "first to claim wins". I'd start with "all notified, first-to-claim
   wins" and metricize.
3. **Teacher-edit-AI-response workflow**: should teacher see the AI's
   response before the student does, with a 10s window to edit? More
   quality, worse latency. Decide after Phase 4 pilot.
4. **Parental consent for human teacher messaging**: does the existing
   AI-tutoring consent form cover this, or is human messaging a distinct
   axis? Ran's call.
5. **Cost**: per-help-request LLM cost is higher with grounded context
   (longer prompt). Per-session budget ceiling needs review; cost circuit
   breaker may need a `help-request` tier.

## Persona lens check

- **Dr. Nadia**: grounded AI context respects ZPD — system prompt points
  the model at the student's current chapter + misconception evidence,
  not the whole corpus; scaffold instruction "without revealing the
  answer" honors the faded-worked-example pattern
- **Dr. Yael**: help requests are NOT a psychometric signal — they don't
  affect θ. Item exposure control unchanged
- **Prof. Amjad**: the "promote-as-worked-example" flow lets teachers
  feed high-quality explanations back into the corpus, over time the
  question bank's hints become curated by the teachers who know the
  kids
- **Dina**: HelpRequest is a new aggregate, not folded into
  TutoringSession (different lifecycle — help requests open + close in
  minutes; sessions span weeks)
- **Oren**: new endpoints versioned + typed DTOs; SignalR groups follow
  the RDY-060 pattern
- **Tamar**: teacher reply UI fully i18n/RTL; student feedback UX
  keyboard-accessible (thumbs via ←/→ keys)
- **Dr. Lior**: thumbs-down flow uses progressive disclosure (reason
  picker only after initial vote); teacher inbox sorts by wait time so
  the most-stuck students are first
- **Ran**: parental-consent gate on human messaging for under-13; audit
  log on every resolution path; PII scrubber guard at the routing
  boundary
- **Iman**: presence heartbeat, SLA metric, runbook for degraded mode
  (all teachers offline → AI-only with a dashboard alert)
- **Rami**: every correctness claim above maps to an arch test or
  integration test

## Out of scope (explicit)

- **Voice / video help sessions** — text only for v1; voice is a
  separate scope with different safeguarding requirements
- **Teacher-to-teacher escalation** — if a teacher doesn't know the
  answer either, they can fall back to AI, but there's no "ask
  another teacher" routing
- **Automatic teacher matching by subject expertise** — first-online
  teacher claims; we'll add matching if data shows it matters
- **Peer-to-peer help** (student helping student) — separate policy
  decision, different COPPA profile
- **Streaming the AI response via SSE** — keep the existing `SendAsync`
  round-trip for v1; streaming is a perf optimisation, not a feature
  dependency

## Links

- AI tutor path: [src/actors/Cena.Actors/Tutor/TutorMessageService.cs](../../src/actors/Cena.Actors/Tutor/TutorMessageService.cs)
- CAS verifier: [src/actors/Cena.Actors/Cas/CasLlmOutputVerifier.cs](../../src/actors/Cena.Actors/Cas/CasLlmOutputVerifier.cs)
- Safeguarding: [src/actors/Cena.Actors/Tutor/SafeguardingClassifier.cs](../../src/actors/Cena.Actors/Tutor/SafeguardingClassifier.cs)
- Hint ladder: `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` (HintRequest handler)
- Advancement state (for grounding): [src/actors/Cena.Actors/Advancement/StudentAdvancementState.cs](../../src/actors/Cena.Actors/Advancement/StudentAdvancementState.cs)
- Admin SignalR hub (dependency): [tasks/readiness/RDY-060-admin-signalr-replace-polling.md](RDY-060-admin-signalr-replace-polling.md)
- Personas: [docs/tasks/pre-pilot/PERSONAS.md](../../docs/tasks/pre-pilot/PERSONAS.md)
- ADR-0002 (CAS oracle — preserved): `docs/adr/0002-sympy-correctness-oracle.md`
- ADR-0003 (misconception scope — referenced): `docs/adr/0003-misconception-session-scope.md`
