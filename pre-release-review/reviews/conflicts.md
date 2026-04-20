# Conflicts Requiring Human Decision — Pre-Release Review 2026-04-20

Companion to `SYNTHESIS.md`. These eight conflicts represent genuine tensions between personas that block multiple downstream tasks. A coordinator decision is required before the affected tasks can be safely resolved.

---

## C-01: Personalized Explanation Caching vs Cold Explanation Every Time

- **Feature/Doc**: axis1 F4 (self-explanation) + SAI-003 L2 cache pattern
- **persona-finops (Svetlana)**: **adopt** — L2 explanation cache (Redis 30-day TTL) delivers ~40x cost reduction at 10k-student scale; without it, Socratic F1 on Sonnet is unbounded.
- **persona-privacy (Noa)**: **revise** — cached explanations risk carrying student work, transient misconceptions, or incidental PII in key/value. Key design must prove PII-free; explanation payload must be scrubbed before persist.
- **Tension**: cost containment vs privacy contamination.
- **Blocks**: prr-012, prr-047
- **Recommended decision frame**: Can the cache key be provably derived from problem-id + canonical misconception-category only (no student text, no session id)? If yes, finops win with Noa's scrubber requirement. If no (cache key must include student paraphrase), privacy wins and cache retires.

---

## C-02: StudentActor split now vs ship first

- **Feature/Doc**: ADR-0012 (StudentActor decomposition)
- **persona-enterprise**: **retire-greenfield-until-split** — every pedagogy/SRL feature adds state to an already->2969-LOC god-aggregate. Refactor now or we freeze tomorrow.
- **persona-educator, persona-cogsci**: **adopt features now** — Bagrut timeline does not wait for refactors; ship behind feature flags and migrate later.
- **Tension**: architectural debt vs product deadline.
- **Blocks**: prr-002 + ~20 pedagogy/SRL tasks
- **Recommended decision frame**: Split `LearningSession` off first (lowest coupling, highest new-feature magnet). Freeze new state on StudentActor via arch test after split; all remaining features ride new aggregate. Deadline: before next 5 pedagogy tickets land.

---

## C-03: Hard-delete vs crypto-shred for event-sourced erasure

- **Feature/Doc**: axis9 right-to-be-forgotten; ADR gap #13
- **persona-enterprise**: **adopt crypto-shred** — deleting events corrupts projections and aggregate rebuilds; shred keys instead.
- **persona-privacy + persona-redteam**: **adopt hard-delete** — crypto-shred is "forensic theater"; regulators may reject; key compromise resurrects data.
- **Tension**: correctness of event-sourcing rebuild vs unambiguous erasure.
- **Blocks**: prr-003, prr-038
- **Recommended decision frame**: Hybrid — hard-delete misconception + PII events (session-scoped, no rebuild dependency); crypto-shred identity+enrollment events (required for rebuild). Codify in ADR. Need privacy officer + event-sourcing architect agreement.

---

## C-04: Parent visibility vs student autonomy at 13-16

- **Feature/Doc**: AXIS-4 parent engagement; age-band ADR gap
- **persona-ethics + persona-privacy**: **revise** — at 13+ student sees what parent sees; at 16+ student can withhold categories. Default-parent-sees-all is coercive.
- **persona-educator**: **adopt parent-sees-all** — parents expect full visibility, especially during Bagrut year; hiding data breaks trust with school-parent pact.
- **persona-ministry**: **adopt with caveat** — follow Israeli MoE guidance; legal minors (parent-accessible); 18+ student-controlled.
- **Tension**: student agency vs parent-school relationship vs legal minor status.
- **Blocks**: prr-014, prr-051, prr-052, prr-096
- **Recommended decision frame**: Need legal review of Israeli MoE guidance + ADR. Proposed compromise: <13 parent sees all, 13-16 notify-student-on-view, 16+ student can withhold. Decision gate for AXIS-4 entirely.

---

## C-05: CAS-gate vs teacher-moderate for peer math explanations

- **Feature/Doc**: AXIS_7 F3 peer explanation
- **persona-ministry + persona-redteam**: **adopt CAS-gate** — only way to prevent wrong-math-at-scale; automated and deterministic.
- **persona-educator**: **adopt teacher-moderate** — CAS accepts only final-answer correctness; misses pedagogical quality (hand-wavy, culturally off). Teachers must review.
- **persona-a11y**: **revise** — teacher moderation may delay by hours; blocks learning. Prefer CAS-gate + flag for later review.
- **Tension**: correctness throughput vs pedagogical quality.
- **Blocks**: prr-025, prr-125
- **Recommended decision frame**: Two-gate: CAS-gate at post-time (blocks wrong math instantly); teacher-flag queue for pedagogical review (async, SLA ≤24h). Both required before delivery to peers in AXIS_7.

---

## C-06: Misconception storage-of-record — Marten events vs Redis-only

- **Feature/Doc**: AXIS_6 F6 + axis9 F3; ADR-0003 Decision 7 gap
- **persona-privacy + persona-redteam**: **adopt Redis-only** — ephemeral, natural TTL, no hard-delete problem.
- **persona-educator + persona-ministry**: **revise to Marten events** — need misconception history within session for teacher-facing mid-session dashboard; Redis-only loses continuity on restart.
- **persona-sre**: **merge-with** — mix is actually a hidden two-source-of-truth bug; resolve first.
- **Tension**: retention simplicity vs in-session continuity.
- **Blocks**: prr-003, prr-015, prr-020
- **Recommended decision frame**: Redis-only session view + Marten append for catalog-grade patterns (not per-student). Dashboard reads Redis; retention worker still guards Marten catalog. ADR-0003 Decision 7 must formalize.

---

## C-07: 3-tier model routing — strict vs advisory

- **Feature/Doc**: ADR-026 (does not yet exist); contracts/llm/routing-config.yaml
- **persona-finops + persona-sre**: **adopt strict** — CI scanner fails build on silent-default-to-Sonnet. No exceptions without ADR addendum.
- **persona-cogsci + persona-educator**: **revise to advisory** — some hints need Sonnet quality (complex word-problem scaffolding); strict tier rejection will degrade pedagogy.
- **Tension**: cost ceiling vs pedagogical quality on hard problems.
- **Blocks**: prr-004, prr-012, prr-046, prr-047, prr-105, prr-145
- **Recommended decision frame**: Strict with documented Sonnet-exception list per feature; exception requires cost-impact estimate + owner sign-off. ADR-026 must enumerate the exception process.

---

## C-08: Exam-day change-freeze scope — all-repo vs prod-only

- **Feature/Doc**: feature-discovery-2026-04-20 + AXIS_10
- **persona-sre + persona-ministry**: **adopt all-repo freeze** during Bagrut weeks (including docs/ADRs) to eliminate any merge-related risk.
- **persona-educator + persona-enterprise**: **revise to prod-only** — docs/ADR freeze blocks content team during their highest-workload period; degrade teacher-facing content quality.
- **Tension**: production stability vs content-authoring throughput when it matters most.
- **Blocks**: prr-016, prr-053, prr-101
- **Recommended decision frame**: Freeze scoped to: (a) production helm charts + infra, (b) Cena.Actors + contracts/ + deploy/. Content repo paths (content/, docs/, locales/) remain open with ship-gate CI. Publish freeze policy as ADR before first Bagrut week of the cycle.

---

## C-09: Ship-now vs refactor-first on ADR-026 routing scanner

- **Feature/Task**: prr-004 (Promote contracts/llm/routing-config.yaml governance to ADR-026 + CI scanner)
- **Tension**: ship the routing ADR + scanner against today's StudentActor/Cena.Actors code vs pause and refactor the 3-tier routing primitives first so the scanner's rules can enforce cleanly.
- **Source findings** (labels present: `refactor-first`, `ship-now`, `teacher-moderate`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-002 — both `refactor-first` and `ship-now` stances appear on findings attached to this task across the persona YAMLs that raised the routing governance concern (persona-finops, persona-sre ship-now; persona-enterprise refactor-first).
- **Blocks**: prr-004, prr-012, prr-046, prr-047, prr-145 (per C-07 overlap)
- **Recommended decision frame**: ship ADR-026 now as advisory + scanner in warn-only mode; promote to hard-fail on a future sprint after 3-tier primitives are factored. Tie the promotion date to prr-002 ADR-0012 milestones.

---

## C-10: Ship-now vs refactor-first on recreated-items exam-simulation policy

- **Feature/Task**: prr-008 (Lock 'recreated items only' policy in exam-simulation code path)
- **Tension**: ship the lock as a CAS-gated runtime enforcement now vs refactor the exam-simulation code path first (extract a clean `BagrutContentSource` boundary) so the lock has a single choke point.
- **Source findings** (labels present: `cache`, `cas-gate`, `refactor-first`, `ship-now`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-004 — persona-redteam/persona-ministry ship-now (Bagrut window is closing) vs persona-enterprise refactor-first (policy without a single enforcement seam will leak).
- **Blocks**: prr-008, prr-033 (rubric DSL), prr-111 (3-unit smoke), prr-122 (reference-only read path)
- **Recommended decision frame**: dual-track — land the CAS-gate on the current path immediately (ship-now satisfies the Bagrut window), then open a follow-up task to extract `BagrutContentSource` and move the gate there (refactor-first satisfied post-window). Must not merge without both tasks filed.

---

## C-11: Cache vs no-cache for parent↔child claims binding

- **Feature/Task**: prr-009 (Parent→child claims binding + IDOR enforcement helper)
- **Tension**: cache the parent-child binding decision per request vs re-check every hop to guarantee freshness on child-account state changes (consent withdrawal, age-band transitions).
- **Source findings** (labels present: `cache`, `cost-reduce`, `crypto-shred`, `hard-delete`, `no-cache`, `parent-visible`, `quality-first`, `ship-now`, `student-autonomy`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-005 — persona-finops/persona-sre cache (p99 and cost); persona-privacy/persona-redteam no-cache (stale auth decisions after consent withdrawal = security incident).
- **Blocks**: prr-009, prr-014 (parent-auth ADR), prr-052 (13/16 consent), prr-096 (admin parental consent UI)
- **Recommended decision frame**: short-TTL (≤60s) per-request cache keyed by `(parent-id, child-id, consent-version)`; invalidate on `ConsentChanged` / `AgeBandTransitioned` events. Codify TTL + invalidation in the parent-auth ADR. Privacy officer signs off on TTL.

---

## C-12: Cache vs no-cache for parent-auth ADR decisions

- **Feature/Task**: prr-014 (ADR: Parent auth role + age-band + multi-institute visibility)
- **Tension**: same cache-vs-fresh-auth-decision debate as C-11, but at the ADR/policy level — does the ADR mandate a cacheable authorization decision or a fresh-on-every-call one?
- **Source findings** (labels present: `cache`, `cost-reduce`, `crypto-shred`, `hard-delete`, `no-cache`, `parent-visible`, `quality-first`, `ship-now`, `student-autonomy`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-006 — same persona alignment as W-005; the ADR must pick a primitive so downstream implementations (prr-009, prr-052, prr-096) follow the same rule.
- **Blocks**: prr-014, prr-051, prr-052, prr-096 (all AXIS-4 parent features; overlaps with C-04 age-band tension)
- **Recommended decision frame**: ADR-0014 mandates "short-TTL cache with event-driven invalidation" (same recipe as C-11), explicitly rejects long-lived or session-long caches. Cache window + invalidation events enumerated in the ADR itself, not in code comments.

---

## C-13: Cache vs no-cache for RetentionWorker store-registration

- **Feature/Task**: prr-015 (Register every new misconception/PII store with RetentionWorker pre-release)
- **Tension**: cache the store→retention-policy map (hot path, every write consults it) vs re-read from config per-write so policy changes land immediately without redeploy.
- **Source findings** (labels present: `cache`, `no-cache`, `ship-now`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-007 — persona-sre/persona-finops cache (write-path hot); persona-privacy no-cache (stale policy = silent retention violation).
- **Blocks**: prr-015, prr-020 (Redis eviction alerts), prr-036 (reflective-text PII scrub), prr-086 (device fingerprint purge)
- **Recommended decision frame**: reload-on-config-version-bump (cache by config version hash, invalidate when version changes). Policy author must bump version to promulgate; retention worker emits `RetentionPolicyReloaded` event on pickup. No TTL — purely event-driven.

---

## C-14: Cache vs no-cache for Mashov credentials

- **Feature/Task**: prr-017 (Store Mashov credentials in secret manager + rotation runbook)
- **Tension**: cache the decrypted Mashov credential in process memory for connection pool reuse vs fetch-per-call from secret manager so rotations take effect immediately.
- **Source findings** (labels present: `cache`, `no-cache`, `ship-now`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-008 — persona-finops/persona-sre cache (Mashov rate-limits + fetch latency); persona-redteam/persona-privacy no-cache (compromised credential remains useful beyond rotation window).
- **Blocks**: prr-017, prr-039 (Mashov sync circuit-breaker), prr-133 (Mashov partial-failure reconciliation)
- **Recommended decision frame**: cache with explicit rotation hook — secret manager emits `SecretRotated` webhook; cached cred purged within 5s of rotation. Runbook documents that rotation is not complete until the `SecretRotated` event is observed end-to-end.

---

## C-15: CAS-gate vs teacher-moderate for cultural-context review board

- **Feature/Task**: prr-034 (Cultural-context community review board — ops queue with DLQ + SLA)
- **Tension**: gate cultural-context content via CAS-like automated rules (fast, deterministic, but blind to nuance) vs require teacher/reviewer moderation (accurate but slow and scale-limited).
- **Source findings** (labels present: `cas-gate`, `ship-now`, `teacher-moderate`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-010 — persona-ministry/persona-redteam cas-gate (throughput + deterministic sign-off); persona-educator teacher-moderate (cultural nuance doesn't lint).
- **Blocks**: prr-034, prr-060 (mother-tongue hints), prr-076 (reviewer roster onboarding), prr-129 (variant diversity)
- **Recommended decision frame**: two-gate pattern (same shape as C-05) — automated rules (banned-term list, character-set validation) gate at submit-time; teacher/reviewer queue for nuance with published SLA. Both required before content ships to students.

---

## C-16: Cache vs no-cache for grade-passback whitelist

- **Feature/Task**: prr-037 (Grade-passback policy ADR + teacher opt-in veto + whitelist)
- **Tension**: cache the teacher-opt-in + whitelist decision per grade-passback call vs fresh-on-every-call to honor a teacher's veto the moment they set it.
- **Source findings** (labels present: `cache`, `cas-gate`, `no-cache`, `student-autonomy`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-011 — persona-finops cache (grade-passback is chatty); persona-educator/persona-privacy no-cache (a teacher's veto must take effect before the next student grade lands, not 10 minutes later).
- **Blocks**: prr-037, prr-124 (admin passback dry-run preview)
- **Recommended decision frame**: no-cache on the veto flag itself (cheap read, high-consequence); cache the whitelist membership for 60s with `WhitelistUpdated` invalidation. Document asymmetry in the grade-passback ADR.

---

## C-17: Ship-now vs refactor-first on AdaptiveScheduler live-caller wiring

- **Feature/Task**: prr-149 (Live caller for AdaptiveScheduler at session start)
- **Tension**: wire `AdaptiveScheduler.PrioritizeTopics` into `LearningSessionActor` today on the existing StudentActor god-aggregate vs wait for the ADR-0012 split (prr-002) to land first so the scheduler lives on the correct successor aggregate.
- **Source findings** (labels present: `cache`, `cas-gate`, `cost-reduce`, `quality-first`, `refactor-first`, `ship-now`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-013 — persona-educator/persona-finops ship-now (substrate exists, users have no plan without it); persona-enterprise refactor-first (every new caller on StudentActor deepens the god-aggregate debt this review raised as P0).
- **Blocks**: prr-149, prr-148 (depends on same aggregate), prr-065 (strategy-discrimination), prr-150 (mentor override)
- **Recommended decision frame**: ship now behind a feature flag with the caller anchored in `LearningSessionActor`; migration to the post-split aggregate is covered by a follow-up task tied to the ADR-0012 milestone. Feature flag + follow-up task filed together or neither merges. Overlaps with C-02 (StudentActor split vs ship).

---

## C-18: Ship-now vs refactor-first on mentor/tutor schedule-override aggregate

- **Feature/Task**: prr-150 (Mentor/tutor override aggregate for schedule)
- **Tension**: build the override aggregate as a new bounded context today vs defer until ADR-0012 locks the StudentActor split so the override aggregate has a stable sibling to integrate with.
- **Source findings** (labels present: `cas-gate`, `refactor-first`, `ship-now`):
  - See `pre-release-review/reviews/audit/tight-match-weak-dedups.md` row W-014 — persona-educator/persona-ministry ship-now (teachers need the override this term); persona-enterprise refactor-first (a new bounded context should not integrate with an aggregate that is scheduled to be decomposed).
- **Blocks**: prr-150, prr-077 (Bagrut-track sign-off UI), prr-127 (teacher mark-item-for-review)
- **Recommended decision frame**: same frame as C-17 — ship a minimum-viable override aggregate now targeting the current `StudentActor` plan-config surface, with a documented migration task tied to the ADR-0012 split schedule. No aggregate ships without the migration task filed.

---

Total conflicts: 18. Each resolves to an ADR or a coordinator decision; most unblock 3+ tasks in tasks.jsonl.
