# ADR-0059 — Reference-browse surface for ingested past-Bagrut corpus + student-side variant generation

- **Status**: Proposed (pending persona review — see §Open Questions)
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
- 2026-04-28 verification sweep (PRR-250): claude-1 read-only audit of the 7 faith-based references in this ADR + PRR-245. Findings: [tasks/pre-release-review/reviews/PRR-250-verification-sweep-findings.md](../../tasks/pre-release-review/reviews/PRR-250-verification-sweep-findings.md). Two items rated **BLOCKER** for implementation: (a) BagrutCorpusItemDocument has no Marten table in dev (corpus never ingested) — ADR-0059 §3+§4 filter scope is moot until corpus is populated; (b) ICasGatedQuestionPersister is registered ONLY in Admin.Api DI today — student-api needs its own DI registration + endpoint-layer auth gate before student-side variants are reachable. Five lower-severity items captured (rate-limit override resolver gap, corpus field-name mismatches, missing IFeatureFlagReader facade, decorator naming, single dangling EPIC-PRR-H link in PRR-245).
