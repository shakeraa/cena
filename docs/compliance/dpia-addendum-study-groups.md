# DPIA Addendum — Peer Study Groups (DRAFT)

> **🚨 DRAFT — DPO + Legal review required before any code work 🚨**
>
> Engineering first-pass data-protection-impact assessment for the
> RDY-076 peer-study-group feature. The task explicitly prohibits
> starting implementation code until this document is signed off;
> this file exists so the DPO review can start in parallel with
> other engineering.

- **Task**: RDY-076 F10 peer study group
- **Parent DPIA**: `docs/compliance/parental-consent.md` §2 (general
  platform DPIA — to be extended by this addendum)
- **Regulator scope**: UK-GDPR Art. 35, EU-GDPR Art. 35, Israel PPL
  Amendment 2024 § 17, US COPPA
- **Panel-review source**: Round 2.F10 + Round 4 Item 5 (Ran's
  cross-exam)

## 1. Scope of this addendum

The base platform DPIA covers single-student data flows: a student
works on problems, produces answer events + mastery updates, sees
their own trajectory, their parent sees their trajectory, their
classroom teacher sees an aggregate. Peer study groups introduce
**cross-student data flows** that the base DPIA does not cover:

1. **Topic-level mastery visibility** between group members
2. **Anonymous help-request routing** — member posts "stuck on X",
   the system anonymously identifies another member who solved X
3. **Moderator actions** — leave, suspend, report-abuse

Each of these flows processes personal data of one student on behalf
of another. Each needs:
- A lawful basis distinct from the base platform consent
- An explicit risk assessment + mitigation
- A revocation path

## 2. Data flows introduced

### 2.1 Topic-level mastery visibility

**What's visible**: the topic-level bucket (HIGH / MEDIUM / LOW /
Inconclusive, per RDY-071) of each member to each other member.
**NEVER** item-level data — no "Yael got item #47 wrong" visibility.

**Lawful basis**: explicit opt-in consent per member (student) +
verifiable guardian consent for minors. Consent is revocable at any
time; revocation removes the student from every group they're in
within 30 days.

**Minimisation**: the projection that powers the group-mastery view
is a **NEW projection** keyed by (groupId, memberAnonId, topicSlug)
that stores only the bucket + a sample-size band (not the raw count).
It does NOT read the general mastery projection; mastery data flows
into this projection only AFTER consent is on file.

### 2.2 Anonymous help-request routing

**Flow**:
1. Student A posts "stuck on {topic}" into the group
2. System searches for members of the same group whose mastery on
   {topic} is HIGH
3. System sends an anonymous invitation to each match: "a group
   member needs help on {topic} — would you like to help?"
4. Match consents **per request** (not blanket) → the system
   introduces them to Student A with their group-display-name
5. They chat about that topic **only** — no free-text outside the
   topic (Phase 1A UX enforces topic context)

**Lawful basis**: opt-in group membership + per-request consent. A
member can always say no to a specific routing request.

**Minimisation**:
- Request body is restricted to (topic-slug, student-display-name,
  optional short context string)
- Free-text context capped at 200 chars and scrubbed via the PII
  scrubber (same pipeline as tutor prompts) BEFORE it reaches the
  matched member
- No cross-request linking: if Student A asks for help on derivatives
  twice in a week, the two requests do NOT aggregate into "Student A
  is weak on derivatives" visible to anyone

### 2.3 Moderator events

**What's logged**:
- MemberLeftGroup (self-initiated)
- MemberSuspended (group owner OR Cena moderator)
- AbuseReportFiled (any member → Cena moderator)

**Retention**:
- Member-left events: retained while the group exists, then 30 days
- Suspensions: retained 12 months for pattern analysis (Cena safety team)
- Abuse reports: retained 24 months + forwarded to the Cena DPO

## 3. Risk assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Student compares mastery buckets with peers and feels shamed | Medium | High (anxiety, retention) | HIGH/MED/LOW bucket only; no ordering, no "top student" callouts; sample-size band not raw count; RDY-071 honest-framing rules apply |
| Help-request routing exposes who-is-struggling | Medium | Medium | Routing is per-request consent; matcher sees only "a member needs help on {topic}", not the requester's identity, until the match opts in |
| Cross-student PII leaks via chat | Low | High | Chat is topic-scoped; tutor-prompt PII scrubber applies to every message BEFORE delivery; no free-form chat outside a topic |
| Minor's guardian revokes consent; ex-member data persists | Medium | High | Revocation triggers 30-day purge of member-level projections + chat history scrubbed to topic aggregates |
| Group becomes cyberbullying vector | Low | Very High | AbuseReportFiled + moderator suspension; report is 1-click in the group UI; suspended member's outbound messages are held until moderator review |
| Under-13 student consented → COPPA enhanced scrutiny | Medium | High | Under-13 students EXCLUDED from peer-study groups entirely in v1; revisit when FTC 2025 guidance on minors' peer platforms stabilises |

## 4. Consent architecture

### 4.1 Two-sided consent

For a minor student to join a group:
1. **Parent/guardian** grants: "my child may join a peer study group"
   (parent-console, separate consent bucket from platform general use)
2. **Student** grants: "I want to join this specific group"
   (student UI, per-group assent)

Both sides must be active. If either withdraws, the student is
removed from the group within 24 hours and their member-level
projections purged within 30 days.

### 4.2 Consent buckets (distinct from platform general)

- `study-group-membership-2026` — base opt-in to join ANY group
- `study-group-mastery-visibility-2026` — separate checkbox to
  make topic-level mastery visible to group members (can be off
  while membership is on; student joins without sharing mastery)
- `study-group-help-requests-2026` — separate checkbox to allow
  anonymous routing (student can be a pure observer)

Each bucket has its own `GrantedV1` / `RevokedV1` events in the
student stream.

## 5. Revocation guarantees

| Action | Effect | Window |
|---|---|---|
| Member leaves group | Removed from group member list; their messages remain attributed to "former member" (placeholder display name); their mastery bucket no longer visible | Immediate |
| Member revokes mastery-visibility consent | Mastery bucket hidden from all group views; projection purges their rows | 24 hours |
| Member revokes help-request consent | Anonymous routing stops including them | 24 hours |
| Parent revokes study-group membership for a minor | Member leaves ALL groups they're in; all member-level data purged | 30 days |
| Group owner dissolves group | All members removed; projections retained 30 days for any leaving-member DSR, then purged | 30 days |

## 6. Moderator model

### 6.1 Roles

- **Group owner**: a student (or teacher, Phase 2) who created the
  group. Can invite / remove members. Cannot read private chats
  (no "owner-as-admin" privilege over content).
- **Cena moderator**: Cena employee who responds to AbuseReportFiled
  events. Has read access to the reported content scope ONLY (not
  the entire group history). Audit logged.
- **Member**: regular participant.

### 6.2 Moderator workflow

1. Member clicks "Report abuse" in the group UI
2. System creates AbuseReportFiled event; reported content snapshot
   (topic, messages) is retained in a review queue for 24 months
3. Cena moderator reads the report scope + decides:
   - Dismiss (insufficient evidence) → log outcome
   - Warning → ping the reported member; no removal
   - Suspend → MemberSuspended event; member cannot post until
     appeal
   - Permanent removal → member leaves all groups; data purged
     per §5
4. Appeal: suspended member has 30 days to appeal in writing

### 6.3 Audit trail

Every moderator action is event-sourced. The Cena DPO has standing
read access to the full moderation log for regulatory compliance.

## 7. Data-subject rights

Each member (or their guardian) has:
- **Access**: request a copy of all their member-level data in a
  group — messages they sent, mastery buckets they exposed,
  help-requests they posted / responded to
- **Rectification**: typos in display name
- **Erasure**: full member data purge within 30 days
- **Portability**: JSON export of all their member-level data (not
  the messages of others in the group)
- **Object**: stop mastery visibility / help-request participation
  while remaining a member

All rights exercisable via the student-facing `/settings/privacy`
surface; requests auto-route to the Cena DPO.

## 8. International transfers

Group data processed in the same jurisdictions as base platform data
(see `docs/legal/dpa-anthropic-draft.md` §8). No additional
transfers — peer-study-group data does NOT flow to Anthropic (no LLM
summarisation of group content in Phase 1A; revisit for Phase 1B if
useful).

## 9. Under-13 exclusion (v1)

Phase 1A policy: under-13 students **cannot** create or join peer
study groups. Rationale:
- COPPA 2025 AI-related amendments require separate verifiable
  parental consent for ML-mediated peer interactions; peer routing
  uses ML to match (§2.2)
- The cost/benefit for under-13 is not favourable in the pilot
  jurisdictions (UK / Israel primary)
- v2 revisits after Ministry + FTC guidance stabilises

Enforcement: `StudyGroupAggregate.CanMember()` refuses enrolment
when `Student.AgeYears < 13`. CI test asserts the refusal.

## 10. Phase 1A deliverable (engineering)

Phase 1A = **this document** + the domain types that the Phase 1B
code will consume. Code-path scaffolding (aggregate / consent /
help-routing engine) ships ONLY after §11 sign-offs.

## 11. Sign-off required before Phase 1B code starts

- [ ] DPO: full DPIA reviewed + signed
- [ ] Legal (Ran): UK-GDPR / Israel PPL / COPPA applicability
- [ ] Dr. Lior: UX review confirming no chat duplication of WhatsApp
- [ ] Dr. Rami: adversarial review on all §3 risks
- [ ] Product lead: moderator staffing model (Cena headcount)
- [ ] Engineering (coder + Dina): §5 retention windows technically
      achievable with the current event-sourced architecture

## 12. What Phase 1B looks like (after sign-off)

Under `src/actors/Cena.Actors/StudyGroups/` (Cena.Domain project
does not exist; follow the existing pattern):

- `StudyGroupAggregate.cs` — state machine (Created / Open /
  Dissolved), invite flow, max-6-members invariant, under-13 refusal
- `TwoSidedConsent.cs` — consent-bucket lookups + revocation event emission
- `HelpRoutingService.cs` — topic mastery lookup + anonymous match +
  per-request consent prompt
- `AbuseReport.cs` + moderator event types
- Admin parent-console endpoint for guardian consent + DSR
- Student SPA: `StudyGroupHome.vue` + `HelpRequestCompose.vue` +
  `MasteryVisibilityToggle.vue` + `/settings/privacy` extensions

## References

- Parent DPIA: `docs/compliance/parental-consent.md`
- ADR-0003 (data scope): `docs/adr/0003-misconception-session-scope.md`
- RDY-071 honest-framing: `docs/engineering/mastery-trajectory-honest-framing.md`
- Tutor PII scrubber (reused for chat): `src/actors/Cena.Actors/Tutor/TutorPromptScrubber.cs`

---
**Status**: DRAFT — awaiting DPO + Legal + Dr. Lior + Dr. Rami sign-off.
**Last touched**: 2026-04-20 (engineering draft)
