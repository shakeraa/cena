# EPIC-PRR-M: Student relationship graph — production parent binding + human-tutor decision

**Priority**: P0 — launch-gate for multi-institute parent flows; partially blocked on age-band UX
**Effort**: L (3-4 weeks aggregate across 5 launch-scope sub-tasks + 1 decision-gated task)
**Lens consensus**: persona-privacy, persona-enterprise, persona-ethics, persona-redteam, persona-educator
**Source docs**:
- [ADR-0041 parent auth role + age bands](../../docs/adr/0041-parent-auth-role-age-bands.md)
- [ADR-0042 consent aggregate bounded context](../../docs/adr/0042-consent-aggregate-bounded-context.md)
- [ADR-0050 multi-target student exam plan §9 parent visibility](../../docs/adr/0050-multi-target-student-exam-plan.md)
- [ParentChildBinding.cs](../../src/actors/Cena.Actors/Parent/ParentChildBinding.cs) — canonical binding record (exists)
- [ParentChildBindingService.cs](../../src/actors/Cena.Actors/Parent/ParentChildBindingService.cs) — guard service (exists)
- [InMemoryParentChildBindingStore.cs](../../src/actors/Cena.Actors/Parent/InMemoryParentChildBindingStore.cs) — **placeholder store — this epic replaces it**
- [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md) — parent aggregate / age-band / IDOR (complementary, not this epic)
- [Done PRR-009](done/TASK-PRR-009-parent-child-claims-binding-idor-enforcement-helper.md), [Done PRR-014 ADR-0041](done/TASK-PRR-014-adr-parent-auth-role-age-band-multi-institute-visibility.md)
- [TASK-PRR-325 tutor-handoff-pdf](TASK-PRR-325-tutor-handoff-pdf.md) — treats human tutors as external channel, not platform actors
**Assignee hint**: kimi-coder + SRE + privacy review; decision-holder owns the human-tutor scoping question
**Tags**: source=student-relationship-graph-audit-2026-04-22, type=epic, epic=epic-prr-m, parent-binding, no-stubs, launch-gate
**Status**: Not Started — blocks on none; one sub-task (PRR-445) is decision-gated
**Tier**: launch (PRR-440..444) + launch+1 decision (PRR-445) + conditional launch+1 (PRR-446)
**Related epics**: [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md) (consent substrate, upstream), [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md) (PRR-325 tutor-handoff PDF)

---

## 1. Why this epic exists

The domain model for "who can act on behalf of a student" is designed correctly but not built. Three facts that make today's state unshippable:

1. **The only implementation of `IParentChildBindingStore` is `InMemoryParentChildBindingStore`.** A process restart erases every parent-child grant. Nothing persists. The guard at [ParentChildBindingService.cs:29](../../src/actors/Cena.Actors/Parent/ParentChildBindingService.cs#L29) is correct, but it reads from a store that forgets.
2. **No production flow creates bindings.** `ParentChildBinding` records come into existence in tests and in the in-memory store. There is no `POST /api/parent/grant-child-access`, no invite-and-approve flow, no school-roster-triggered binding creation. A real student registering + a real parent registering produces zero bindings — the parent sees nothing about their child, permanently, because the system has no way for them to connect.
3. **The Firebase advisory `children:[]` claim is a dev-useful hint, not a runtime trigger.** `ParentChildBinding.cs:6-8` is explicit: "the JWT `parent_of` claims are an advisory cache re-derived from this record at login." If the binding record doesn't exist, the claim is never derived, and the parent is invisible to their child's session regardless of what Firebase says.

Plus a fourth fact that is a product gap, not a bug:

4. **"Tutor" in the codebase means the AI tutor, not a human tutor.** `TutorActor.cs` is an ephemeral Proto.Actor spawned by `LearningSessionActor` for AI dialogue. The `Cena.Actors.Tutor` namespace is entirely AI-LLM infrastructure (`ClaudeTutorLlmService`, `SafeguardingClassifier`, `SocraticCallBudget`). `PRR-325` treats human tutors as an external PDF channel — a distribution target, not a platform actor with a login, session visibility, or binding. If Cena's business model wants human tutors as first-class platform participants (their own accounts, their own view of the student, their own consent scope), that concept does not exist and needs a product decision before any code work.

Per memory "Senior Architect mindset" — trace data flows end-to-end, fix root causes. The root cause of parent-facing features being vapor today is that the ParentChildBinding data flow is half-built: types exist, guard exists, store doesn't persist, creation flow doesn't exist. This epic closes that flow.

The 03:00-Bagrut-morning failure mode prevented: a parent whose 16-year-old sat Bagrut Math at 08:00 needs to know by 08:30 whether their child actually logged in. Today, even if they have an account, they see nothing because no binding record exists linking them to the student. That is a trust-violation story at the worst possible moment.

## 2. How the epic closes the gap

Six sub-tasks. Five are Launch-scope (binding production flow). The sixth is a decision-gated scoping task for human-tutor-as-actor.

### PRR-440 — Marten-backed `ParentChildBindingStore`

Replace `InMemoryParentChildBindingStore` with a production Marten-backed implementation. Event-sourced (matches the rest of the platform) with `ParentalConsentGranted_V1`, `ParentalConsentRevoked_V1`, `ParentalBindingAmended_V1` as the event stream. Projection materializes the current binding table for the guard's `FindActiveAsync` lookup. Deletes of binding rows are crypto-shredded via the existing [PRR-003b](done/TASK-PRR-003b-implement-crypto-shredding-for-misconception-events.md) pipeline — a binding is misconception-adjacent consent data and must honor RTBF.

### PRR-441 — Parent-child grant/approval flow

The concrete UX + API that creates bindings. Three initiation paths — each with distinct age-band behavior from ADR-0041:

- **Parent-initiated**: parent enters child email/phone → system sends student an approval request → student approves → binding recorded. Student <13: auto-approve (parent consent is the gate, not student). Student 13-17: explicit student approval required + ongoing veto right. Student 18+: parent request is auto-rejected with honest copy.
- **Student-initiated (13+)**: student opts to invite a parent → parent receives email/WhatsApp (per PRR-018 sanitizer) → parent approves → binding recorded.
- **School-initiated**: roster import carries a parent email per student → school-admin triggers batch invitations → both sides approve per age band → binding recorded. K-12 common case.

### PRR-442 — Firebase `children` claim reconciliation at login

When a parent logs in whose Firebase claim has `children:["student@example.com"]`, the system checks whether each child has an active binding. If yes, the claim `parent_of:[studentSubjectId, ...]` is derived into the session JWT. If no, the missing child triggers a binding-invite suggestion in the parent UI ("we see you listed this child; would you like to request access?"). Closes the dev-UX gap where seed parents never actually connect to their seeded children.

### PRR-443 — Binding revocation flow (symmetric)

Any active binding can be revoked by either party, with age-band nuances:
- **Parent revokes**: sets `RevokedAtUtc`; next session JWT refresh (≤5 min per ADR-0041) loses the binding. Emits `ParentalConsentRevoked_V1` on consent stream.
- **Student 13+ revokes** (exercises veto right per ADR-0041 + ADR-0050 §9): same event; parent is notified with neutral copy ("the student has updated their privacy settings").
- **System-triggered revocation at 18th birthday**: automatic per ADR-0050 §9 (parent-visibility never applies to ≥18). Daily worker identifies any binding whose student has just aged out, revokes, notifies both sides.

### PRR-444 — Registration-time invite trigger

When a parent account is created with `children:[...]` hinted in Firebase, or when a school-roster import lists a parent email for a student, **automatically create a pending binding invitation** and route it per §PRR-441 flows. Removes the empty-state trap where new parents must manually figure out how to bind their children. Age-band rules from ADR-0041 still gate whether the invitation can be auto-approved (<13) or requires student action (13+).

### PRR-445 — Decision: human tutor as first-class platform actor (or not)

**Not engineering. Product decision.** Current state: human tutors are external (PRR-325 PDF handoff). Decision needed before any human-tutor engineering:

- Should human tutors have Cena accounts with login + scoped visibility of their student?
- If yes: what do they see? Mastery map only, or full session replay? Pay model (per-tutor seat, parent-sponsored, student-subscription-included)? Consent scope (who grants — parent? student? both)? Compliance shape (is a private tutor an "institute" per ADR-0001, needing its own `InstituteId`)?
- If no: PRR-325 PDF handoff stays the only channel. This epic closes at PRR-444.

This task produces a decision brief + 10-persona review if needed, NOT code. Launch can ship without it.

### PRR-446 — (conditional) Human tutor aggregate + TutorStudentBinding

**Only spawned if PRR-445 decides yes.** Mirrors the parent binding model: `TutorStudentBinding(TutorActorId, StudentSubjectId, InstituteId, GrantedAtUtc, RevokedAtUtc?)` + Marten store + grant/revoke flows + age-banded consent + session-scope visibility. Launch+1 at earliest; may be multi-release work.

## 3. Non-negotiables (architect guardrails)

- **No stubs.** Marten-backed store is the production store, not a hybrid "in-memory in Dev, Marten in Prod." Dev uses Marten against the local Postgres from docker-compose. Test code uses a dedicated `TestParentChildBindingStore` under `Cena.Actors.Tests` that is explicitly test-only.
- **Event-sourced, not state-only.** `ParentalConsentGranted_V1` etc. live on the existing consent stream (ADR-0042 bounded context). The binding projection is a read model derived from events, not the source of truth.
- **Guard keeps no cache.** `ParentChildBindingService` already refuses to cache per ADR-0041's "revocation within one session cycle" rule. This epic does not weaken that.
- **RTBF applies.** Binding events + projection rows honor the existing crypto-shredding pipeline (PRR-003b + PRR-223 cascade extension). Revocation preserves a tombstone row for audit but shredding removes identifiable payload.
- **PII scrubbed at the boundary.** Invitation emails use PRR-018 outbound-SMS/email sanitizer pipeline. No raw minor email addresses to vendor logs.
- **Age bands are load-bearing, not aspirational.** Every binding creation path branches on age per ADR-0041 + ADR-0050 §9. Unit tests assert each band produces the correct behavior.
- **Full `Cena.Actors.sln` builds cleanly.** Per memory 2026-04-13.
- **Verifiable DoD.** Each sub-task's DoD is a passing test or a curl-able API response, not a subjective review.

## 4. Sub-task table

| ID | Title | Priority | Effort | Scope |
|---|---|---|---|---|
| [PRR-440](TASK-PRR-440-marten-parent-child-binding-store.md) | Marten-backed `ParentChildBindingStore` (replaces in-memory) | P0 | M (1 week) | launch |
| [PRR-441](TASK-PRR-441-parent-child-grant-approval-flow.md) | Parent-child grant/approval flow (3 initiation paths × 3 age bands) | P0 | L (2 weeks) | launch |
| [PRR-442](TASK-PRR-442-firebase-children-claim-reconciliation.md) | Firebase `children` claim reconciliation at login | P1 | S (3-5 days) | launch |
| [PRR-443](TASK-PRR-443-binding-revocation-flow.md) | Binding revocation (parent, student ≥13, 18th-birthday auto) | P0 | M (1 week) | launch |
| [PRR-444](TASK-PRR-444-registration-invite-trigger.md) | Registration-time invite trigger (parent + school-roster) | P1 | M (1 week) | launch |
| [PRR-445](TASK-PRR-445-human-tutor-first-class-decision.md) | DECISION: human tutor as first-class actor? | P2 | S (2-3 days — brief + 10-persona review) | decision-gate |
| PRR-446 (conditional) | Human tutor aggregate + `TutorStudentBinding` | TBD | L-XL | launch+1 if PRR-445=yes |

### Execution order

- **Launch-critical path**: PRR-440 → PRR-441 (parallel with PRR-443) → PRR-442 + PRR-444 in parallel. ~3 weeks with two coders.
- **PRR-445 decision**: can run any time; not on the critical path. Target resolution 1-2 weeks before Launch so PRR-446 (if triggered) doesn't block anything else.
- **PRR-446**: launch+1 at earliest; spawned only if decision is yes.

## 5. Definition of Done (epic-level, verifiable)

1. A new parent registers → receives an invitation workflow → binding is recorded in Postgres (Marten) → next parent login derives `parent_of:[...]` JWT claim from the live binding. Verified end-to-end by integration test + manual smoke.
2. A process restart does not erase bindings. Verified by killing the actor-host container and re-querying.
3. `docker exec cena-postgres psql -U cena -d cena -c "SELECT data FROM mt_events WHERE stream_type = 'Consent' AND data ?? 'ParentalConsentGranted_V1'"` returns actual rows after a grant flow runs (Marten events, not in-memory).
4. All three initiation paths (parent, student, school) work end-to-end in staging.
5. Age-band tests: <13 auto-approves under parent consent; 13-17 requires student approval; 18+ rejects parent request; 17→18 auto-revokes.
6. Revocation propagates into session JWTs within one session-refresh cycle (≤5 min).
7. PRR-445 decision brief lands; decision-holder records yes/no; if yes, PRR-446 is spawned with concrete scope.
8. Full `Cena.Actors.sln` builds cleanly.
9. Runbook at `docs/ops/runbooks/parent-child-binding-operations.md` covers: grant a binding manually (admin tool), revoke emergency, audit binding history for a student, handle a failed invitation.
10. Reconciliation test: any code path that calls `IParentChildBindingStore` is backed by the Marten implementation in Prod/Staging; test-only stores are confined to test assemblies (architecture test enforces).

## 6. Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md) — per-institute binding shape.
- [ADR-0041](../../docs/adr/0041-parent-auth-role-age-bands.md) — age bands drive every flow.
- [ADR-0042](../../docs/adr/0042-consent-aggregate-bounded-context.md) — events stream lives in the consent context.
- [ADR-0050 §9](../../docs/adr/0050-multi-target-student-exam-plan.md) — 18th birthday auto-revoke; parent visibility default-hidden 13-17.
- Memory "No stubs — production grade" — Marten-backed store replaces in-memory.
- Memory "Senior Architect mindset" — trace flow end-to-end; don't ship with "state store that forgets."
- Memory "Full sln build gate".
- Memory "Honest not complimentary" — PRR-445 brief presents the tradeoff honestly, doesn't paper over the ops + compliance cost of first-class tutors.
- [PRR-003b](done/TASK-PRR-003b-implement-crypto-shredding-for-misconception-events.md) — crypto-shredding pipeline.
- [PRR-018](done/TASK-PRR-018-outbound-sms-sanitizer-rate-limit-quiet-hours-policy-parent.md) — outbound messaging sanitizer (used by invitation emails).
- [PRR-022](done/TASK-PRR-022-ban-pii-in-llm-prompts-lint-rule-adr.md) — no binding PII in LLM prompts.

## 7. Out of scope (intentional)

- **Tutor PDF handoff** — already covered by PRR-325; this epic does not duplicate.
- **Parent dashboard content** — that's EPIC-PRR-C scope (PRR-320..325). This epic is the relationship substrate, not the dashboard surface.
- **Parent push-notifications UI** — PRR-323 weekly digest + PRR-325 handoff cover the notification surface.
- **Teacher-student binding** — teachers bind via classroom roster, not via `ParentChildBinding`. Covered in EPIC-PRR-F (PRR-236 classroom teacher UI).
- **Any cross-tenant binding** — ADR-0001 requires per-institute grants; this epic does not introduce cross-tenant shortcuts.

## 8. Reporting

Epic closes when PRR-440..444 ship + PRR-445 decision is recorded (yes or no, not undecided).

## 9. Related

- Pair with [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md) — that epic ships the consent-and-messaging substrate; this epic ships the binding creation/lifecycle on top of it.
- Depends on [PRR-003b](done/TASK-PRR-003b-implement-crypto-shredding-for-misconception-events.md) shredding pipeline (already Done).
- Depends on [PRR-018](done/TASK-PRR-018-outbound-sms-sanitizer-rate-limit-quiet-hours-policy-parent.md) outbound sanitizer (already Done).
- Feeds: [PRR-320 parent-dashboard-mvp](TASK-PRR-320-parent-dashboard-mvp.md) and anything under Cena.Admin.Api/Features/ParentConsole/ that depends on an active binding.
