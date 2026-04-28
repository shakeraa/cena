# ADR-0059 — Reference-browse surface for ingested past-Bagrut corpus + student-side variant generation

- **Status**: Accepted — §15 below supersedes §1-§6 where they conflict. Original draft retained for audit. Flag-flip remains gated on R7-R9 launch items (PRR-249 legal memo, PRR-254 takedown runbook, PRR-251 corpus dev-ingest).
- **Date proposed**: 2026-04-28
- **Deciders**: Shaker (project owner), claude-code (coordinator)
- **Extends**: [ADR-0043 (Bagrut reference-only enforcement)](0043-bagrut-reference-only-enforcement.md) — this ADR carves out the *reference-browse* seam from the strict delivery ban, with its own consent-token, audit, and arch-test discipline.
- **Related**:
  - [ADR-0002 (SymPy CAS oracle)](0002-sympy-correctness-oracle.md) — every student-practiced variant CAS-verified before render.
  - [ADR-0026 (LLM 3-tier routing)](0026-llm-three-tier-routing.md) — Tier-1 parametric vs Tier-3 structural variants drive the cost ceiling.
  - [ADR-0032 (CAS-gated question ingestion)](0032-cas-gated-question-ingestion.md) — variant persistence reuses the same gate.
  - [ADR-0048 (exam-prep time framing)](0048-exam-prep-time-framing.md) — reference page bans countdown / streak / days-until copy.
  - [ADR-0050 (multi-target student exam plan)](0050-multi-target-student-exam-plan.md) — reference browse is filtered by the student's active `ExamTarget.QuestionPaperCodes`.
- **Source**: User directive 2026-04-28 — "browse syllabus / past papers reference page that deep-links to base materials we ingest. recreate with different parameters."
- **Epic**: [EPIC-PRR-N](../../tasks/pre-release-review/EPIC-PRR-N-reference-library-and-variants.md) (created 2026-04-28; EPIC-PRR-H letter was already taken by student-input-modalities).

---

## Context

Two prior decisions converge into a tension:

1. [ADR-0043](0043-bagrut-reference-only-enforcement.md) bans raw Ministry-published Bagrut text on any student-facing surface — three defensive layers (compile-time `Deliverable<T>`, runtime `IItemDeliveryGate.AssertDeliverable`, arch test `BagrutRecreationOnlyTest`). A Ministry item on the student wire is a P0 data-leak incident.
2. [PRR-242](../../tasks/pre-release-review/done/TASK-PRR-242-past-bagrut-corpus-ingestion.md) ingested the Ministry past-Bagrut corpus (2015→present) into `BagrutCorpusItemDocument` precisely so the AI authoring pipeline could anchor recreations to real exam style. Cost reduction: ~$40-60k → ~$20-30k for EPIC-PRR-G.

The user's 2026-04-28 directive asks for a third surface that neither ADR anticipated:

> "there should be a separate browse syllabus / past papers reference page that deep-links to base materials that we ingest. recreate with different parameters."

This is **not** delivery-for-assessment (which ADR-0043 protects against). It is **reference browsing followed by CAS-verified practice on a variant**. The student sees the Ministry question as context, then practices on a parametric or structural recreation with explicit provenance citation.

Two reasons this is a real and load-bearing surface:

- **Pedagogical authenticity**: every Israeli prep platform (Bagrut Plus, Bagrut Tikshoret, school-issued PDFs) shows past papers. Refusing to even surface them creates an asymmetric information environment — students *will* go to the Ministry website directly, and we lose the structured handoff to a CAS-verified variant.
- **Activates a paid corpus**: PRR-242 ingested the corpus at $3-5k infra + SME time. Without a student-facing surface, that investment only funds the LLM authoring backend.

ADR-0043's spirit is *"no Ministry item silently graded as if it were a CAS-verified recreation."* The reference-browse surface honors that spirit by making the reference→variant boundary explicit in UX, contract, and audit.

---

## Decision

A **reference-browse surface** is permitted under the four invariants below. Outside these invariants, ADR-0043's ban stands unchanged.

### 1. Reference items are wrapped in `Reference<T>`, never `Deliverable<T>`

Introduce a sibling type to `Deliverable<T>`:

```csharp
public readonly record struct Reference<T>(
    T Value,
    Provenance Provenance,            // MinistryBagrut allowed here
    ConsentTokenId ConsentToken,      // proves student saw §3 disclosure
    ReferenceContextKind Context);    // BrowseLibrary | VariantSourceCitation

public enum ReferenceContextKind
{
    BrowseLibrary = 1,                // dedicated reference page
    VariantSourceCitation = 2,        // citation chip on a practice variant
}
```

`Reference<T>` does **not** pass through `IItemDeliveryGate.AssertDeliverable` — that gate is for delivery-for-assessment, a different epistemic surface. `Reference<T>` has its own factory `Reference<T>.From(value, provenance, consentToken, context)` that:

1. Requires `provenance.Kind == MinistryBagrut` (rejects misuse for non-corpus items — those use `Deliverable<T>`).
2. Validates `consentToken` is non-expired and bound to the calling student.
3. Emits structured audit log under pinned `EventId(8009, "BagrutReferenceBrowsed")` with `{studentId, sessionId?, itemRef, context, consentTokenId}` — never the raw item body.

### 2. Reference items have no answer affordances

The student-facing render path for `Reference<T>` strips every input control:

- No answer textbox, no choice radios, no submit button.
- No "Did I get this right?" affordance.
- A single CTA: **"Practice a variant of this question"** that opens the variant-generation flow (§4).

Architecture-test extension: `BagrutRecreationOnlyTest.cs` adds a positive-list — student-facing namespace `Cena.Api.Contracts.Reference.**` is the ONLY namespace where Ministry-named fields (`MinistryBagrut*`, `BagrutReferenceId`, `MinistrySubjectCode`, `MinistryQuestionPaperCode`, `MoedSlug`) are permitted, AND every DTO in that namespace must declare `[ReferenceContext]` attribute proving §1's wrapping happened. Sessions/Challenges/Tutor/Me/Hub stay banned exactly as before.

### 3. One-time consent disclosure per student, audited

Before a student first reaches the reference page, an inline disclosure card surfaces:

> "These are Ministry-published past Bagrut questions, included as reference material. Cena does not grade your answers on these — they're for context. To practice the underlying skill, tap **Practice a variant** to get a CAS-verified recreation with different numbers or scenario."

Confirmation creates a `BagrutReferenceConsentGranted_V1` event on the student stream + a `ConsentTokenId` cached for 90 days. The token is required for every subsequent `Reference<T>` render. Re-prompt on token expiry, not on every visit. Per [ADR-0042](0042-consent-aggregate-bounded-context.md) the consent record is event-sourced and crypto-shreddable via the same RTBF pathway as other consents.

Localization: copy in en/he/ar with `<bdi dir="ltr">` around שאלון codes per the math-always-LTR rule.

### 4. Reference filter scope is bounded by the student's plan

A student only sees reference items aligned to their active `ExamTarget` set:

- Bagrut targets contribute their `QuestionPaperCodes` to the filter (per ADR-0050 + PRR-243).
- Non-Bagrut targets (SAT/PET) do not surface a Bagrut reference library — they get their own catalog ("SAT released items", "PET sample sections") if and when corpora exist.
- Freestyle students see no reference library by default. They can opt-in via `/settings/reference-library` which adds them to a per-subject catalog (no exam-target gating, but rate-limit halved per §5).

This is a deliberate scope reduction: students browsing un-targeted corpus is low-signal and amplifies the cost surface in §5.

### 5. Variant generation: two tiers, rate-limited, server-side cost-budgeted

The "Practice a variant" CTA produces a CAS-verified recreation via two distinct paths:

| Tier | Mechanism | Cost | Free-tier limit | Paid-tier limit |
|---|---|---|---|---|
| **Parametric** | Deterministic parameter substitution + SymPy verify | $0 | 20/day | 100/day |
| **Structural** | LLM-authored recreation via `GenerateSimilarHandler` (Tier-3 Sonnet/Opus) | ~$0.005-0.015/call | 3/day | 25/day |

The student picks: "Same problem, different numbers" (parametric) vs "Similar problem, different scenario" (structural). Conflating them lets a free-tier user mash the Sonnet button.

Endpoint shape:
```
POST /api/v1/reference/{paperCode}/q/{questionNumber}/variant
  body: { variationKind: "parametric" | "structural" }
  response: { variantQuestionId, sessionRouteHint }
```

Server-side enforcement:
- Rate-limit at the per-(student, day) level via existing `RateLimitedEndpoint` decorator. Limits configurable per `IInstitutePricingResolver` (per PRR-244 — institutional plans can lift caps).
- Variant items persist via `CasGatedQuestionPersister` with `SourceType = "variant"` and provenance lineage `{sourceProvenance: MinistryBagrut, sourceShailonCode, sourceQuestionIndex, variationKind, generatedFor: studentId, generatedAt}`.
- Persisted variants are reusable across students who request the same source — second request returns cached variant (idempotent, cost-amortizing). De-dup keyed on `{sourceShailonCode, sourceQuestionIndex, variationKind, parametricSeed?}`.

### 6. Variant-practice attempt feeds mastery the same as any session

Once a variant is generated, the student is routed into a single-question practice session (`SessionMode = "freestyle"` with the variant as the only queued item). The answer flows through the existing session-answer pipeline:

- BKT update on `(studentId, skillId)` — per ADR-0050 Item 4, skill-keyed not target-keyed.
- Misconception-data per ADR-0003 — session-scoped, 30-day max.
- Provenance citation rendered on the answer screen: *"Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) — numbers changed."* — required by the labels-match-data rule.

There is no special "reference-practice mode" in mastery accounting. The variant is just a question that happens to have a Ministry-corpus lineage tag.

---

## Consequences

### Good

- Activates the PRR-242 corpus on the student surface, not just the LLM backend.
- Closes the "students go to Ministry website anyway" leak by giving them a reference→variant handoff inside Cena.
- Reuses `GenerateSimilarHandler` (currently admin-only) without forking — the only delta is exposing it to students with rate + tier gating.
- Provenance lineage is enforceable: every variant carries its source şaalon code, and the session-answer screen cites it. Auditable end-to-end.
- Honors ADR-0043's pedagogical spirit (no rote memorization via raw Ministry items) by stripping answer affordances and routing all practice through CAS-verified variants.

### Costs

- New surface to maintain: reference page, consent flow, variant endpoint, two new event types (`BagrutReferenceConsentGranted_V1`, `BagrutReferenceItemRendered_V1` for the audit-counter side).
- Architecture-test discipline now branches: positive-list for `Cena.Api.Contracts.Reference.**`, negative-list for everything else. New contributors must understand the carve-out.
- Variant cost ceiling depends on tier-gating discipline. If the parametric/structural distinction blurs in UI copy, cost can run away. Mitigated by §5 rate-limits + pricing-resolver overrides.
- Reference library only works for Bagrut at Launch (corpus exists). SAT-released items + PET sample sections are post-Launch unless the corpora are licensed in time.

### Risks accepted

- **Legal posture for in-app display**: PRR-242's legal memo confirmed Ministry papers are publicly published under terms permitting reference use, but that memo focused on backend ingestion. **Display-to-students requires a delta review** — listed in Open Questions §Q1. Until that closes, the reference page ships behind a feature flag with rollout gated on legal sign-off.
- **Pedagogical effectiveness**: showing the Ministry question and then a "variant" creates a mental anchor — students may pattern-match the variant against the source. This is probably a net pedagogical win (worked-example effect, Sweller 1998) but warrants persona-cogsci review before defaulting the reference page on for all students.
- **Ministry posture**: if the Ministry of Education objects to Israeli prep tools surfacing past papers in-app (despite public publication), Cena absorbs the reputational risk. Mitigated by including consent disclosure and provenance citation — we are not laundering Ministry text as our own.

---

## Open questions

### Q1 — Display-to-student legal delta from PRR-242 memo

PRR-242's legal review covered backend ingestion. Does the same legal posture cover serving raw Ministry text to authenticated students inside Cena's UI (no public/anonymous access)? **Owner**: Shaker. **Blocks**: ADR acceptance + PRR-245 implementation start. **Suggested resolution path**: 1-page delta memo to PRR-242's compliance file; if lawyer flags risk, fall back to the metadata-only variant (citation + topic + structure description, no raw text) and defer raw-text display.

### Q2 — Persona reviews required

Run the following lenses against §1-6 before ADR-0059 locks:
- **persona-privacy**: consent-token model + 90-day expiry sound? RTBF cascade tested?
- **persona-ministry**: Ministry-of-Education posture on in-app display of past papers — known case-law or precedent?
- **persona-cogsci**: variant-cadence and reference-anchor effects — is 20 parametric/day too generous? Should the structural cadence be lower?
- **persona-finops**: confirm cost ceiling against ADR-0026 budget + PRR-244 pricing-resolver. What is the upper bound on a single student's variant spend per month?
- **persona-redteam**: rate-limit bypass surface (e.g. enumerate variants by spamming source IDs); IDOR on variant ownership; consent-token forgery.
- **persona-a11y**: reference-page DOM walkable in screen readers; consent disclosure surfaced as `aria-live` not modal; variant-tier choice discoverable without a mouse.

### Q3 — Default-on or default-off at Launch

Conservative: ship behind a feature flag, on for tenants who explicitly enable. Aggressive: on by default for all Bagrut-targeted students Day 1. Cost+risk tradeoff lives with Shaker. **Suggested**: feature-flag default-off for the first two weeks post-Launch to bank usage data, then default-on once metrics confirm cost ceiling holds.

### Q4 — SAT released items + PET sample sections at Launch

Out of scope for PRR-245 implementation, but the architecture must not Bagrut-couple. Reference filter (§4) and variant routing (§5) must accept any `ProvenanceKind` that has a corpus (SAT released items if licensed, PET if NITE permits). Document the extension contract; defer the SAT/PET corpora to future tasks.

### Q5 — Coordinate with PRR-243 שאלון multi-pick

PRR-243 (DONE) gave students per-שאלון selection at onboarding. The reference filter (§4) MUST honor that — a student who unchecked שאלון 035583 sees no 035583 reference items. Confirm `ExamTarget.QuestionPaperCodes` is the canonical filter source; no second source-of-truth.

---

## Operational rules

### For code review

- Any PR that introduces a `Deliverable<T>` factory call with `provenance.Kind == MinistryBagrut` is rejected (use `Reference<T>`).
- Any PR that introduces a Ministry-named field (`MinistryBagrut*`, `MoedSlug`, etc.) on a DTO outside `Cena.Api.Contracts.Reference.**` namespace is rejected by `BagrutRecreationOnlyTest`.
- Any PR that exposes `GenerateSimilarHandler` to students without rate-limit + tier-gate (§5) is rejected.
- Any PR that adds reference-page UI without the consent-token flow (§3) is rejected.
- Any PR that adds countdown/streak/days-until copy on the reference page is rejected by the shipgate scanner (per [ADR-0048](0048-exam-prep-time-framing.md) + PRR-224).

### For new work

Every task that touches the reference library, variant-generation endpoint, or `BagrutCorpusItemDocument` student exposure must reference this ADR + ADR-0043 in its PR description and demonstrate the carve-out invariants (§1-6) hold.

---

## History

- 2026-04-28 proposed: user directive surfaced the gap; coordinator drafted as senior-architect work. Pending persona reviews + legal-delta memo before acceptance. Implementation (PRR-245) blocked on Q1 + Q2 resolution.
- 2026-04-28 verification sweep (PRR-250): claude-1 read-only audit of the 7 faith-based references in this ADR + PRR-245. Findings: [tasks/pre-release-review/reviews/PRR-250-verification-sweep-findings.md](../../tasks/pre-release-review/reviews/PRR-250-verification-sweep-findings.md). Two items rated **BLOCKER** for implementation: (a) BagrutCorpusItemDocument has no Marten table in dev (corpus never ingested) — ADR-0059 §3+§4 filter scope is moot until corpus is populated; (b) ICasGatedQuestionPersister is registered ONLY in Admin.Api DI today — student-api needs its own DI registration + endpoint-layer auth gate before student-side variants are reachable. Five lower-severity items captured (rate-limit override resolver gap, corpus field-name mismatches, missing IFeatureFlagReader facade, decorator naming, single dangling EPIC-PRR-H link in PRR-245). Follow-up tasks filed: PRR-251/252/253.
- 2026-04-28 ADR §5 amendment: dropped fictional "RateLimitedEndpoint decorator" language; canonical primitive is ASP.NET `RequireRateLimiting` + `AddRateLimiter`. Institute-tunable limits gated on PRR-253 extending `IInstitutePricingResolver`.
- 2026-04-28 6-persona review (PRR-248): synthesis below. Status flipped to "Proposed — pending revision."

---

## 14. Persona Review Synthesis (2026-04-28)

Six lenses ran in parallel against ADR-0059 §1-6 and §Q1-Q5. Findings filed under [pre-release-review/reviews/persona-{lens}/reference-library-findings.md](../../pre-release-review/reviews/) per the same schema as the ADR-0050 review.

### 14.1 Verdict roll-up

| Persona | Verdict | Findings file |
|---|---|---|
| privacy | yellow → red | [reference-library-findings.md](../../pre-release-review/reviews/persona-privacy/reference-library-findings.md) |
| ministry | yellow | [reference-library-findings.md](../../pre-release-review/reviews/persona-ministry/reference-library-findings.md) |
| cogsci | yellow | [reference-library-findings.md](../../pre-release-review/reviews/persona-cogsci/reference-library-findings.md) |
| finops | yellow | [reference-library-findings.md](../../pre-release-review/reviews/persona-finops/reference-library-findings.md) |
| **redteam** | **red** | [reference-library-findings.md](../../pre-release-review/reviews/persona-redteam/reference-library-findings.md) |
| **a11y** | **red** | [reference-library-findings.md](../../pre-release-review/reviews/persona-a11y/reference-library-findings.md) |

Two red verdicts (a11y, redteam) and one yellow→red (privacy) block ADR-0059 from moving to Accepted. None demand a redesign — all are spec tightenings against existing primitives plus measurable scope reductions.

### 14.2 Convergent findings (multi-lens agreement — load-bearing)

The following items were flagged by ≥2 lenses and are not optional:

1. **§5 cadence is wrong, two ways** *(cogsci + finops + redteam converge)*. The 20/3 free-tier and 100/25 paid-tier daily caps are simultaneously *too generous per source* (cogsci: encourages over-practice on a single Ministry question, dilutes spacing benefit) and *too cheap per institute* (finops: cost claims understated 3×, real per-call cost ~$0.045 once retry-tax + cache-miss are baked; per-student paid worst-case $22-34/mo breaches ADR-0026's $20 cap saved only by daily backstop). Replace with: per-source caps (5 parametric / 2 structural per source) AND per-institute rate-limit fields (PRR-253 extension) AND free-tier structural = 0 until payment-method or institute-SSO verified (redteam: defeats account-rotation cache exfiltration of the entire corpus for ~$0).

2. **Audit-event retention vacuum** *(privacy + redteam converge, ministry-adjacent)*. `BagrutReferenceItemRendered_V1` has no stated retention horizon, no RTBF cascade-shred registration, and no SIEM-tractable structured key (only free-text `Provenance.Source`). GDPR Art. 17(1)(c) violation window once events ship. Required: 180-day retention horizon (privacy), structured slash-delimited Provenance.Source `ministry-bagrut/035582/2024/summer/A/q3` (ministry; audit-by-paper-code tractable for takedown), RTBF cascade registered with PRR-015/PRR-218 RetentionWorker BEFORE any emitter ships.

3. **Authorization gaps fail the redteam threat model** *(redteam + privacy converge)*. (a) `variantQuestionId` is currently a deterministic dedup-key — IDOR enumeration trivial. Must be opaque server-side HMAC with server-pepper + endpoint ownership gate. (b) `ConsentTokenId` lacks crypto-binding spec — forgery-resistant requires HMAC over `{studentId, contextKind, issuedAt, expiresAt}` with 24h wire TTL (vs 90-day event-sourced fact via ADR-0042 ConsentAggregate), context-scoped to prevent BrowseLibrary token from satisfying VariantSourceCitation validation. (c) Rate limits per-student only — defeated by account-rotation; required: per-student AND per-institute AND per-IP/device.

4. **Consent disclosure under-specified** *(privacy + a11y converge)*. §3 "inline disclosure card" is a name, not a contract. Required normative spec: ARIA `aria-live` + non-trapping focus + Enter/Escape contracts + defined re-prompt path on token expiry. Disclosure copy must cover audit-event lifecycle (privacy: PPL Art. 11 transparency + GDPR Art. 7(3)), and a one-click revoke surface MUST live on the reference page itself — settings-only revoke is borderline; deep-link-only is non-compliant.

5. **Ministry-canonical citation form** *(ministry, ratified by privacy + redteam)*. Current §6 example "קיץ תשפ״ד" is ambiguous. Canonical = "קיץ תשפ״ד מועד א׳". `Provenance.Source` must populate structured slash-delimited form (see §14.2 item 2).

6. **Browse-history scope limitation** *(privacy, ratified by cogsci)*. Ministry-question browse history is high-fidelity learning-weakness + anxiety inference data; a 30-item browse signature triggers Sweeney/Narayanan-Shmatikov re-identification. Required §"Browse-history scope limitation" with 5 controls: no teacher/parent/tenant-admin visibility; no browse → mastery/scheduler/misconception coupling (cogsci agrees: BKT must not read browse signal); no browse fields in LLM prompts (per ADR-0022); k≥10 floor on aggregates (per PRR-026); raw extracts banned outright.

### 14.3 Cross-lens tensions (decision-holder must call)

1. **Parametric vs structural at free tier** *(cogsci ↔ ministry)*. cogsci says favor parametric (cheap, deterministic, lower BKT-inflation risk per its M-1). Ministry says parametric is *more legally exposed* under Israeli Copyright Law §16 (parameter substitutions retain more of the source — derivative-works distance). Reconciliation needed: structural is **legally safer** (different scenario = §16 distance) but **more expensive** (Tier-3 LLM) and **more cognitively risky** (Bloom-drift; cogsci's M-3 mitigation: Haiku second-pass equivalence check). Recommend: free-tier defaults to structural (with cogsci's Haiku-rubric gate), parametric is *paid-only* — flips the current draft. Confirms finops worst-case math.

2. **Three-locale × four-screen-reader prototype gate on PRR-245** *(a11y red blocker)*. Same shape as ADR-0050's VDatePicker red gate. ~30-cell test matrix before implementation; failures fall back to metadata-only render. Decision: **accept the gate or descope to metadata-only Day 1**. (Coordinator recommendation: accept the gate; the carve-out is too valuable to descope, and the gate forces honest a11y posture.)

3. **Consent-token wire TTL: 24h vs 90d** *(redteam ↔ privacy)*. redteam wants 24h wire token (HMAC short-TTL minimizes forgery blast radius) backed by 90d event-sourced consent fact. privacy is fine with this. Resolved: 24h wire + 90d event-sourced. Update ADR §3 normative text.

### 14.4 Required mitigations summary (prioritized)

**P0 — Block ADR-0059 acceptance:**

- **R1** Rewrite §5 cadence: per-source caps (5 parametric / 2 structural), per-institute fields (PRR-253), free-tier structural = 0 until payment/SSO verified.
- **R2** Add normative §"Retention" + §"Audit event retention" — 180d horizon for `BagrutReferenceItemRendered_V1`, RTBF cascade registered with RetentionWorker, structured `Provenance.Source` form.
- **R3** Tighten §3 ConsentToken spec: HMAC binding over `{studentId, contextKind, issuedAt, expiresAt}`, 24h wire TTL, context-scoped, one-click revoke on reference page.
- **R4** Tighten §1 `Reference<T>` factory: opaque `variantQuestionId` (HMAC + pepper, not deterministic key), endpoint ownership gate, CasGatedQuestionPersister student-api DI registration (PRR-252).
- **R5** Add normative §"Browse-history scope limitation" with the 5 privacy controls (§14.2 item 6).
- **R6** Add normative §"Accessibility" specifying ARIA contract for §3 disclosure, RTL+math+numerals rule (every שאלון/MoedSlug in `<bdi dir="ltr">` AND always Western digits), and three-locale × four-screen-reader prototype gate on PRR-245.

**P0 — Block feature-flag flip-on (separate from ADR acceptance):**

- **R7** PRR-249 legal-delta memo (Israeli Copyright Law §19 fair-use + §16 derivative-works) — confirms Q1 resolution.
- **R8** Takedown-response runbook at `docs/ops/runbooks/ministry-takedown.md` — 30-min global feature-flag kill-switch (must bypass §3 90d consent cache), audit-log preservation, per-source-paper-code variant purge.
- **R9** PRR-251 corpus dev-ingest verification — until `mt_doc_bagrutcorpusitemdocument` is non-empty in dev, PRR-245 implementation cannot be tested end-to-end.

**P1 — Concurrent with implementation:**

- **R10** Per-`{variation_kind, cache_layer}` observability on PRR-047 hit-rate metric; explicit `cache_control` breakpoints on variant prompts (finops: prevents <10% hit-rate regression).
- **R11** Cohort single-flight write lock on variant generation (Redis): 30-student classroom burst → 1 author + 29 readers, not 30 parallel authors. Lessons from RDY-081 single-writer.
- **R12** Cogsci M-1: BKT discounting for reference-anchored attempts — 0.5× posterior weight (or deferred update) for ~5 min post-source-render.
- **R13** Cogsci M-3: Tier-2 Haiku second-pass scoring variant against source on 3-axis rubric (Bloom / difficulty / skill) before render; reject on mismatch.

### 14.5 Outstanding questions back to decision-holder (Shaker)

These remain open after the 6-lens review:

- **Q-A** (§14.3.1) Free-tier defaults to structural (legally safer) instead of parametric (cheaper)? Coordinator recommendation: yes. Implications: free tier needs payment/SSO before any variant generation; structural-only on free; Haiku equivalence-check gate required.
- **Q-B** (§14.3.2) Accept the three-locale × four-screen-reader prototype gate on PRR-245, or descope to metadata-only display Day 1? Coordinator recommendation: accept the gate.
- **Q-C** (R6) Reference page wraps Ministry codes in `<bdi dir="ltr">` AND always-Western-digits regardless of student PRR-032 numerals preference? a11y requires this for catalog primary-key parity.
- **Q-D** (R8) Takedown-response runbook owner. Coordinator recommendation: SRE lane (extends PRR-016 exam-day SLO change-freeze pattern).
- **Q-E** (R9) Who closes PRR-251 corpus dev-ingest? PRR-242 author has best context but is unassigned in queue today.

### 14.6 Revised task slate

PRR-245 task body MUST be updated to reflect R1-R13 before any implementation starts. Coordinator (claude-code) will append a "Revised Scope (post-PRR-248)" section to PRR-245 in a follow-up commit.

No new tasks added beyond PRR-251/252/253 (already filed). The R10-R13 mitigations land *inside* PRR-245's existing scope, not as new sub-tasks.

### 14.6 Decision record — 2026-04-28 (Shaker via coordinator delegation)

User directive 2026-04-28 "do all" — coordinator (claude-code) ratifies Q-A through Q-E per the §14.5 recommendations.

| Q | Decision | Rationale |
|---|---|---|
| **Q-A** | Free tier defaults to **structural** (not parametric). Parametric becomes paid-tier only. | Ministry §16 derivative-works distance + cogsci's worked-example anchor logic both favor structural. Cost surfaces correctly via PRR-253 institute-tunable rate-limits + cogsci's per-source caps. |
| **Q-B** | **Accept** the three-locale × four-screen-reader prototype gate on PRR-245. | The carve-out is too valuable to descope; the gate forces honest a11y posture. Same shape as ADR-0050 VDatePicker resolution. |
| **Q-C** | **Yes** — always-Western-digits for Ministry codes regardless of PRR-032 numerals preference. | Ministry codes are catalog primary keys per ADR-0050 Item 2; numerals preference does not apply. Arch-test + shipgate-scanner enforce. |
| **Q-D** | Takedown-response runbook lives in **SRE lane**. New task PRR-254 enqueued (extends PRR-016 exam-day SLO change-freeze pattern). | Regulatory/legal surface needs SRE muscle for the 30-min kill-switch. |
| **Q-E** | **claude-1** owns PRR-251 corpus dev-ingest verification (atomically claimed at 16:45:05 before coordinator's 17:11 reassignment fired; she's already producing `BagrutCorpusSeedData.cs` synthetic dev-bootstrap). | Coordinator's reassignment to claude-3 was a misroute; claude-3 caught the conflict. claude-1 stays. |

---

## 15. Revised Decision (Accepted, supersedes §1-§6 where conflicting)

§1-§6 above are retained as the original draft for audit. The persona-review synthesis (§14) and Q-A through Q-E (§14.6) modify the design as follows. **Where §1-§6 and §15 conflict, §15 wins.**

### 15.1 `Reference<T>` factory + opaque `variantQuestionId` (supersedes §1)

```csharp
public readonly record struct Reference<T>(
    T Value,
    Provenance Provenance,            // MinistryBagrut allowed here
    ConsentTokenId ConsentToken,      // 24h wire HMAC, see §15.3
    ReferenceContextKind Context);    // BrowseLibrary | VariantSourceCitation
```

`Reference<T>.From(value, provenance, consentToken, context)` invariants:

1. Requires `provenance.Kind == MinistryBagrut`.
2. Validates `consentToken` is non-expired AND HMAC-binding holds (see §15.3) AND `consentToken.Context == context` (no cross-context reuse).
3. Emits `EventId(8009, "BagrutReferenceBrowsed")` with structured `Provenance.Source` slash-delimited form `"ministry-bagrut/{שאלון}/{year}/{season}/{moed}/q{n}"` (ministry §14.2 item 5 — SIEM tractable).
4. **Never logs raw item body.**

`variantQuestionId` MUST be opaque server-side: HMAC-SHA256 over `{tenantId, studentId, sourceShailonCode, questionIndex, variationKind, parametricSeed?}` with a server-pepper. **Not** the deterministic dedup-key (redteam IDOR fix). Endpoint-layer ownership gate via `ResourceOwnershipGuard.VerifyStudentAccess` on every variant read (matching commit 5a030d24 cross-tenant guard pattern).

### 15.2 Reference items have no answer affordances + ARIA contract (supersedes §2)

Render path strips input controls (per original §2). **Plus normative §15.6 ARIA contract below.**

### 15.3 Consent disclosure — HMAC token + 24h wire TTL + one-click revoke (supersedes §3)

Consent disclosure surfaces inline (not modal) on first reference-page reach. Confirmation:

- Emits `BagrutReferenceConsentGranted_V1` on student stream — event-sourced fact, retained per ADR-0042 ConsentAggregate (90d functional + RTBF-shreddable).
- Returns a wire `ConsentTokenId` = HMAC-SHA256 over `{studentId, contextKind, issuedAt, expiresAt}` with **24-hour wire TTL** (redteam: minimizes forgery blast radius). Token re-issued silently from the 90d event-sourced fact on next reference-page hit when wire token expires.

**One-click revoke MUST live on the reference page itself** (privacy: GDPR Art. 7(3) compliance — settings-only is borderline, deep-link-only non-compliant). Revoke emits `BagrutReferenceConsentRevoked_V1` + invalidates the current wire token + triggers RTBF cascade on the audit-event projection (§15.5).

Localization: en/he/ar with `<bdi dir="ltr">` per §15.6.

### 15.4 Reference filter scope (unchanged from §4)

§4 stands. Bagrut targets contribute their `QuestionPaperCodes`; non-Bagrut targets get no Bagrut reference library; freestyle students opt-in via settings.

### 15.5 Variant generation — revised cadence + per-source caps + structural-default-on-free (supersedes §5)

| Tier | Mechanism | Per-source cap | Per-day cap | Free-tier | Paid-tier |
|---|---|---|---|---|---|
| **Structural** (default) | Tier-3 LLM via `GenerateSimilarHandler` + Haiku second-pass equivalence-check (§15.7) | 2 per source | 6/day | Available, gated by payment-method or institute-SSO verification (redteam: defeats account-rotation cache exfiltration) | 25/day |
| **Parametric** (paid-only) | Deterministic parameter substitution + SymPy verify | 5 per source | 15/day | **Not available** (Q-A: ministry §16 derivative-works distance) | 50/day |

Server-side enforcement:
- Rate-limit at per-(student, day) AND per-(institute, day) AND per-(IP/device, day) levels via ASP.NET `RequireRateLimiting` + `AddRateLimiter` policies (corrected per PRR-250 §5).
- Limits configurable per `IInstitutePricingResolver` once PRR-253 extends `ResolvedPricing` with `VariantStructuralPerDay`, `VariantParametricPerDay`, `VariantStructuralPerSource`, `VariantParametricPerSource`, burst.
- Cohort single-flight write lock on variant generation (Redis): 30-student classroom burst → 1 author + 29 readers (R11; lessons from RDY-081 single-writer).
- Persisted variant dedup key extended: `{sourceShailonCode, questionIndex, variationKind, track, stream, localeHint, parametricSeed?, payloadHashSafetyV1}` (finops §14.2 item 1 — closes 30× variance).
- Free-tier structural calls require valid payment-method OR institute-SSO verification before the endpoint dispatches the LLM call (redteam M-1).

### 15.6 Browse-history scope limitation (new — privacy §14.2 item 6)

Browse history is high-fidelity learning-weakness inference data. The following 5 controls are normative invariants enforced by code review + arch-test:

1. **No teacher / parent / tenant-admin visibility.** Browse history is not exposed via parent-aggregate (ADR-0042) projections, teacher dashboards (PRR-058), or tenant-admin reports.
2. **No browse → mastery / scheduler / misconception coupling.** BKT (ADR-0050 Item 4) does not read browse signal. Scheduler (PRR-226) does not weight on browse. Misconception detection (ADR-0003) does not consume browse events.
3. **No browse fields in LLM prompts** (per ADR-0022 PII-in-prompts ban). Hint generation, tutor-context, variant-authoring prompts must NOT receive `lastReferencedShailon`, browse counts, or browse timestamps.
4. **k≥10 floor on aggregates** (per PRR-026 anonymity). Any analytics / coverage-matrix exposure of browse counts honors the existing k-anonymity policy.
5. **Raw extracts banned outright.** No CSV / JSONL export of `BagrutReferenceItemRendered_V1` events. Audit access is per-event-by-id only, RBAC-gated to security-incident response.

### 15.7 Audit event retention + RTBF cascade (new — privacy §14.2 item 2)

`BagrutReferenceItemRendered_V1`:
- **Retention horizon: 180 days** post-emit. Beyond 180d, RetentionWorker shreds the event (privacy: PPL Amendment 13 + GDPR Art. 5(1)(e) purpose-limitation).
- **RTBF cascade**: registered with PRR-015 / PRR-218 RetentionWorker pattern. On student RTBF erasure, all `BagrutReference*` events for that student crypto-shred via the same pipeline as misconception data (ADR-0003 cascade pattern).
- **No backfill via legacy retention.** Events emitted before this ADR landed are not retroactively retained; the ADR's first-emit date is the retention clock start.

`BagrutReferenceConsentGranted_V1` / `BagrutReferenceConsentRevoked_V1`:
- Per ADR-0042 ConsentAggregate. 90d functional retention; RTBF-shreddable on the same path.

### 15.8 Cognitive-equivalence gate on structural variants (new — cogsci R13)

Before a structural variant renders to a student:

1. Tier-3 LLM authors the candidate variant.
2. SymPy CAS oracle (ADR-0002) verifies math correctness.
3. **Tier-2 Haiku second-pass** scores the candidate against the source on a 3-axis rubric: Bloom level, difficulty band, skill scope. Reject + regenerate (max 3 attempts) if any axis drifts beyond a configured threshold.
4. On reject after 3 attempts, fall back to "Practice a similar problem from the catalog" affordance (no variant served). User-facing copy explains the fallback honestly.

The Haiku second-pass adds ~$0.0002 + ~500ms per variant — finops-acceptable, cogsci-required.

### 15.9 BKT discounting for reference-anchored attempts (new — cogsci R12)

Variant attempts where the source was rendered within the last 5 minutes get **0.5× posterior weighting** in the BKT update (cogsci: defends against worked-example transient inflating immediate-test performance). Beyond 5 minutes, full posterior weighting resumes.

The discount is recorded on the per-attempt event for analytics + BKT calibration. Spacing benefit (ADR-0050 Item 4) is preserved.

### 15.10 Normative §"Accessibility" (new — a11y R6)

Required spec for the reference-library + variant surfaces:

- **Consent disclosure** — `aria-live="polite"`, non-trapping focus, dismissible via Enter (acknowledge) and Escape (dismiss → return to /home or settings — DOES NOT bypass disclosure; user gets re-prompted on next reference-page reach until acknowledged or revoked). Re-prompt path on 24h wire token expiry: silent re-issue from event-sourced fact (no re-disclosure); on 90d event-sourced consent expiry: full re-prompt.
- **RTL + math + numerals** — every math fragment in `<bdi dir="ltr">`. Every שאלון code, MoedSlug, MinistryQuestionPaperCode, year, season in `<bdi dir="ltr">` AND **always Western digits regardless of PRR-032 student numerals preference** (Q-C: Ministry codes are catalog primary keys per ADR-0050 Item 2). Arch-test `MinistryCodeLatinDigitsTest` enforces. Shipgate scanner extension catches violations in localized strings.
- **Variant-tier picker** — Vuetify segmented button with 2 options (parametric / structural for paid; single structural CTA for free). Each option `aria-describedby` cost + cap badge. Keyboard: Tab to focus, Arrow-keys cycle, Enter selects, Escape closes. No mouse required.
- **Reduced-motion** (per PRR-070) — reference→variant route transitions respect `prefers-reduced-motion`. No spring animations on the segmented button.
- **PRR-245 prototype gate** — three locales (en / he / ar) × four screen readers (NVDA / JAWS / VoiceOver-macOS / VoiceOver-iOS) ≈ 12 prototype cells minimum, expanded to ~30 with affordance variations. Prototype runs MUST land before any production code on PRR-245 ships. Failures fall back to metadata-only render (no raw Ministry text) for affected locale until prototype passes.

---
