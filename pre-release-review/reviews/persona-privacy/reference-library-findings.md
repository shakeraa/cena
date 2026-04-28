---
persona: privacy
subject: ADR-0059
date: 2026-04-28
verdict: yellow
reviewer: claude-subagent-persona-privacy
---

## Summary

Verdict **yellow**, leaning red on three axes: (1) the 90-day consent token is presented as a privacy control but it is structurally a UX-friction token, not a lawful-basis instrument under PPL Amendment 13 Art. 8 / GDPR Art. 7(3); (2) the `BagrutReferenceItemRendered_V1` audit event has no stated retention policy and ADR-0059 does not name it as crypto-shred-cascade-bound — that is a GDPR Art. 17(1)(c) gap with a measurable shred-window bug if shipped as drafted; (3) Ministry-question browse history is high-fidelity learning-weakness inference data and is materially more sensitive than the ADR's "non-PII identifiers only" framing acknowledges. The four invariants (§1-6) are well-architected; the privacy gaps are at the seams (token semantics, audit-event lifecycle, derived-inference scope). Each has a concrete mitigation. None are full launch-blockers if fixed in PRR-245; all become blockers if shipped as drafted in §3.

## Section Q2 prompt answers

### Q2.a — Consent token model + 90-day expiry: defensible under PPL Amendment 13?

**Partially defensible. The token is structurally fine; the framing is wrong, and 90 days is a soft ceiling not a hard one.**

PPL Amendment 13 (in force Jan 2025) imports purpose-limitation, transparency, and revocability as binding statutory principles, aligned with GDPR Articles 5-7. The relevant provisions for this review:

- **PPL Amendment 13 Art. 11 (transparency on collection)**: requires the data subject be told the purpose, the recipients, and the legal basis at point of collection. The §3 disclosure copy ("These are Ministry-published past Bagrut questions, included as reference material…") covers purpose but is silent on **what events are written, how long the audit log lives, and what happens at revocation**. The current draft fails the transparency standard.
- **PPL Amendment 13 Art. 13B (right to erasure)**: cross-references ADR-0042's RTBF pathway. Token storage is event-sourced and crypto-shreddable per ADR-0042 §"Bounded-context shape" — the token mechanics are compliant if and only if the cascade actually fires (see Q2.b).
- **PPL § purpose-limitation**: the 90-day expiry is a *re-prompt* cadence, not a *retention* cadence. As re-prompt cadence, 90 days is defensible (matches the PPA's 2025 informal guidance on durable consents for non-special-category data), but it is **not** a substitute for documenting how long the underlying consent record itself persists. Two distinct lifecycles must be named: (a) *token validity* (90 days, OK), (b) *consent record retention post-archive* (currently undefined — must be ≤24 months default per ADR-0050 §6 precedent).

**Under GDPR Art. 7(3) (right to withdraw consent at any time, as easy to withdraw as to grant)**: the ADR is silent on the revocation UX. There is no "I changed my mind, hide the reference library" affordance described. Revocation that requires a help-desk ticket fails Art. 7(3); revocation that requires a settings deep-link three clicks away is borderline. **A one-click revoke control on the reference page itself is the only defensible UX.**

**On 90-day specifically**: the duration is defensible because (a) it is shorter than the 12-month "stale consent" threshold the EDPB Guidelines 05/2020 on consent flag for re-confirmation, (b) it matches the academic-cadence rhythm (a quarter of a Bagrut prep cycle), and (c) it is short enough to give regulators a clean answer when asked "do you re-confirm consent?" — yes, every 90 days. A 12-month or "until revoked" expiry would be harder to defend; a 30-day expiry would be operationally onerous for students mid-prep without privacy benefit. 90 is the right number. The framing problem is not the number but the unspecified consent-record lifecycle around it.

**On under-13 cohort consent**: ADR-0059 is silent on whether under-13 students can grant consent at all. Per ADR-0042 + ADR-0041 age-band matrix, under-13 self-grants are blocked (COPPA VPC violation; refused by `AgeBandAuthorizationRules.CanActorGrant`). Two paths: (i) parent grants on behalf — workable but adds a parent-consent flow step; (ii) under-13 students simply do not see the reference library — operationally simpler, defensible because Bagrut prep targets older students anyway. **Recommend path (ii)**: gate the reference library on age ≥13. ADR-0050 §9 already establishes parent-aggregate visibility for under-13 ExamTargets; restate that under-13 reference-library access is hidden, not parent-mediated. Under-13 students preparing for Bagrut are vanishingly rare and can be handled as edge cases.

**Status**: defensible under PPL/GDPR if and only if (i) the §3 disclosure is expanded to name the audit-event lifecycle, (ii) consent-record retention post-revoke is bounded ≤24 months, (iii) a one-click revoke surface exists on the reference page, and (iv) under-13 cohort is gated out of the surface entirely. Without those four, the token is dressed-up UX consent that will not survive a regulator audit.

### Q2.b — RTBF cascade: `BagrutReferenceConsentGranted_V1` crypto-shreddable?

**Yes for the consent event itself, but the cascade is incomplete as drafted.**

ADR-0042's bounded-context shape names the encrypted PII fields on `ConsentGranted_V1` (`subjectId*, grantedByActorId*`) and asserts crypto-shred via `EncryptedFieldAccessor` per ADR-0038. By inheritance, `BagrutReferenceConsentGranted_V1` MUST follow the same field-marking discipline — the ADR-0059 draft does not explicitly call this out. **Required clarification**: the ADR must state that `BagrutReferenceConsentGranted_V1` carries encrypted `subjectId*` (the studentId) and any other PII via `EncryptedFieldAccessor`, and that key destruction tombstone is the same key as the parent ConsentAggregate stream.

**The downstream cascade is the real gap.** ADR-0059 names two new event types:
1. `BagrutReferenceConsentGranted_V1` — on the consent stream.
2. `BagrutReferenceItemRendered_V1` — on the audit-counter side (§Costs paragraph).

Under GDPR Art. 17(1)(c) erasure obligation, when consent is withdrawn the controller must erase data processed on that consent basis "without undue delay." Crypto-shredding the consent grant *but leaving the rendered-audit-event stream un-shredded* leaves an inferable record of "this studentId browsed Ministry items X, Y, Z" — exactly the inference Q2.d flags as sensitive. Two cascade options exist:

- **Option A (registered cascade)**: register `BagrutReferenceItemRendered_V1` events as ExamTarget-linked / consent-linked under PRR-218's RetentionWorker pattern (the same one ADR-0050 §6 + multi-target-exam-plan-findings.md "Blocker-2" established for ExamTarget cascade). This is the architecturally consistent path. Effort: ~1 day to extend the worker registration; any new event-type that touches consent-gated reads must go through this seam.
- **Option B (rolling delete)**: hard-delete `BagrutReferenceItemRendered_V1` events older than retention horizon regardless of consent state. Simpler operationally but loses the audit trail for incident-response (§Costs paragraph names the events as "audit-counter side" — the audit purpose is real).

**Recommend Option A.** This means: ADR-0059 §3 must explicitly state that `BagrutReferenceItemRendered_V1` is registered with the RetentionWorker as a consent-linked store, and the cascade fires on `BagrutReferenceConsentRevoked_V1` (a third event type the draft does not name but must, per Art. 7(3)).

**Status**: cascade design must be added to ADR-0059 BEFORE PRR-245 ships any event-emitter. Shipping events first then retrofitting the cascade is a GDPR Art. 17 violation window (the same Blocker-2 pattern from multi-target-exam-plan-findings.md — we have institutional memory on this exact failure mode).

### Q2.c — `BagrutReferenceItemRendered_V1` audit-log retention policy

**Currently undefined. Must be set to ≤180 days for the audit-counter purpose; cascade-shreddable on consent revoke regardless of retention horizon.**

ADR-0059 names `BagrutReferenceItemRendered_V1` only in passing (§Costs paragraph: "two new event types … `BagrutReferenceItemRendered_V1` for the audit-counter side"). There is no retention specification. Under PPL Amendment 13 purpose-limitation + GDPR Art. 5(1)(e) (storage limitation), an audit-counter event has a bounded operational purpose — typically "detect rate-limit abuse, support incident response, fire SIEM alerts" — and the retention horizon must match that purpose, not "as long as we feel like it."

Comparable retention horizons in the codebase to anchor against:
- **Misconception data** (ADR-0003): 30-day session scope. Aggressive — set because the data is high-inference-value.
- **ExamTarget archive** (ADR-0050 §6): 24-month default, 60-month max. Much longer — set because the data is declared-plan, low-inference-value, and operationally useful for transcript-style review.
- **`ExamSimulationItemDelivered_V1`** (ADR-0043 §4): no explicit retention named in the ADR. This is itself a gap but out of scope here.

`BagrutReferenceItemRendered_V1` sits closer to misconception data than to ExamTarget — it is a per-item-render trace, not a declared-plan record. Browse-pattern data is high-fidelity behavioral telemetry (Q2.d). **Recommend retention horizon = 180 days** (6 months — matches typical SIEM retention envelopes; long enough for incident response post-discovery; short enough to limit profiling surface). Anything longer needs a stated incident-response or compliance purpose specific to that horizon.

**Hard requirements regardless of retention horizon**:
1. Cascade-shred on consent revoke (Q2.b).
2. Cascade-shred on RTBF erasure (ADR-0038).
3. Subject-ID encrypted via `EncryptedFieldAccessor` per ADR-0038 even within the retention window — the at-rest record must not store plaintext studentId.
4. **Never** carry the raw item body, only `itemRef` (paperCode + questionNumber) — ADR-0059 §1 already specifies this; restate explicitly on the V1 event shape.
5. Excluded from any analytics/ML pipeline by allowlist (PRR-022 LLM-prompt rule precedent — extend the same allowlist discipline to behavioral-telemetry analytics).

**Status**: ADR-0059 must add a §"Retention" subsection naming the 180-day horizon (or justify a different horizon) and binding the cascade rules above before acceptance.

### Q2.d — Ministry-browse-history inferences: sensitive?

**Yes, materially more sensitive than the ADR acknowledges. This is the single most under-weighted privacy concern in the draft.**

"Which Ministry questions a student browsed" is not a neutral activity log. It is high-fidelity inference data about:

- **Subject-area weakness** ("student browsed 12 quadratics questions and 0 trigonometry questions" → identifies a weakness vector). Browse-pattern → weakness inference is well-established in educational data mining literature (Romero & Ventura 2010 surveys; the inference does not need engagement-time, item count + topic distribution suffices).
- **Year-of-exam-prep** ("student browsed only 2025 papers" → tightens age/grade band — particularly powerful when combined with Q2.a's tenant + locale signals).
- **Track-level confusion** ("student is enrolled in 4U but spent 80% of browse time on 5U items" → reveals that the student may be over- or under-tracked, a sensitive academic disclosure).
- **Anxiety patterns** ("student browsed exam-day-2024 paper at 23:47 the night before their actual exam" → behavioral inference of test anxiety / pre-exam stress). The diurnal pattern alone — concentration of browse activity in late-night hours within 7 days of canonical_date — is an actuarial signal of pre-exam stress.
- **Re-identification through browse signature**: a student's browse trajectory across 30+ items is high-entropy. Even after pseudonymization, a unique browse pattern can re-identify a student given auxiliary data (the Sweeney 2002 / Narayanan-Shmatikov 2008 re-identification literature applies; ZIP+DOB+gender re-identifies 87% of US population, and a 30-item browse signature is comparable entropy).

Under PPL Amendment 13's narrow special-category list (health, sexual orientation, political, religious, criminal), browse history is **not** statutorily special-category. Under GDPR Art. 9 same answer — Recital 51 is narrow. **However**, under PPL purpose-limitation + the Privacy Protection Authority's 2025 guidance on derived-inferences-as-special-category-when-deterministic, learning-weakness inference is treated as a *derived* sensitive category when it is deterministic enough to act on. Browse-history fed into a recommendation engine or shown to a teacher absolutely meets that bar. Same answer under GDPR Art. 22 (automated decision-making with significant effects) once any downstream system uses browse-history as a personalization input.

**Required controls**:

1. **No teacher visibility of browse history**. The student's browse log must not be readable on the teacher dashboard, the parent dashboard, or any tenant-admin export. ADR-0059 is silent here — this is a hard requirement to add. Comparable precedent: the multi-target-exam-plan parent-visibility default ("hidden by default 13+") sets the pattern.
2. **No browse-history → mastery-projection coupling**. ADR-0050 §"4. Mastery state is skill-keyed" already locks mastery to attempts (variant submissions), not browse signal. Restate this invariant explicitly: browsing a Ministry item must NOT touch BKT, must NOT update misconception state, must NOT factor into the AdaptiveScheduler. Browse is purely a UX surface; ADR-0059 §6 partially says this ("There is no special 'reference-practice mode' in mastery accounting") but only for variant attempts, not for browse events. **Add §6.1: browse events have zero impact on any pedagogical projection.**
3. **No browse-history → LLM-prompt context**. Same allowlist discipline as PRR-221 / multi-target-exam-plan-findings.md "LLM-prompt leakage" — prompts must NEVER receive browse-history fields. Extend the prompt-assembly lint rule.
4. **No analytics export below k-anonymity floor (k≥10)**. Same pattern as multi-target-exam-plan-findings.md "k-anonymity floor (PRR-026)". A tenant of 14 students reporting "2 students browsed שאלון 035582 q3 last week" de-anonymizes those students by their inferred weakness vector. Hard k≥10 floor for any browse-distribution aggregate.
5. **No tenant-admin export of browse logs at all** (stricter than k-anonymity). Browse-log raw extracts to school admin = leaking minor academic anxiety data to a school official. The k-anonymity floor governs aggregates; raw extracts must be banned outright.

**Status**: ADR-0059 must add §"Browse-history scope limitation" naming the five controls above before acceptance. The current draft treats browse as cheap telemetry; under realistic inference it is academic-anxiety telemetry one schema-add away from being a P0 disclosure incident.

## Additional findings

### Cross-reference to multi-target-exam-plan-findings.md institutional memory

The 2026-04-21 multi-target-exam-plan privacy review identified three blockers (free-text PII, indefinite retention, RTBF cascade gap). Two of those three patterns recur in ADR-0059 in slightly different form:

- **Indefinite retention pattern**: ADR-0059 leaves `BagrutReferenceItemRendered_V1` retention undefined. Same failure mode as the original ExamTarget "retained indefinitely" claim. Same fix: name the horizon, default to the shortest operationally feasible value, document the purpose justifying anything longer than 30-90 days.
- **RTBF cascade pattern**: ADR-0059 names two new event types but does not register them with the RetentionWorker / RTBF cascade pathway (PRR-015 / PRR-218 precedent). Same failure mode as the original ExamTarget cascade gap (Blocker-2 in the prior review). Same fix: register before any emitter ships.

The free-text-PII pattern does not recur here (ADR-0059 has no free-text fields — credit to whoever drafted it; the §3 disclosure copy is the only natural-language seam and it is one-way controller-to-subject, not subject-to-store). This is a meaningful improvement from the multi-target-exam-plan baseline and should be preserved through PRR-245.

### Default-on at Launch (Open Question Q3) is the wrong default

The ADR §Q3 leans toward "default-off feature flag for two weeks then default-on." For a privacy-novel surface, default-off persists until the four mitigations above are operational AND the legal-delta memo (Q1) closes green. The "two-week post-Launch flip" should be replaced with "flip on legal sign-off + four privacy mitigations live + first 30 days of consent-grant-rate / revoke-rate metrics review." Tying the flip to a calendar window rather than to operational privacy controls is a regression of the privacy posture.

### Free-tier vs paid-tier consent disparity

§5 sets different rate limits per tier (3/day structural for free, 25/day structural for paid). Privacy concern: are free-tier students consenting to a *different* product than paid-tier? Under PPL Amendment 13 Art. 11 transparency, the consent disclosure copy must not differ in privacy-relevant ways across tiers. Cost-related limits (rate caps) are fine; data-collection-scope differences are not. Verify the consent copy and the audit-event shapes are identical across tiers.

### Variant lineage is itself sensitive

§5 "Persisted variants are reusable across students who request the same source — second request returns cached variant." The de-dup key includes `{sourceShailonCode, sourceQuestionIndex, variationKind, parametricSeed?}`. This is fine for the variant content; the privacy concern is the *variant cache's metadata* — does the cache record which students fetched which cached variant? If yes, that is browse-history-by-another-name and inherits all Q2.d controls. Recommend: cache the variant CONTENT keyed on the de-dup tuple; do NOT record per-student fetch logs in the cache layer. Per-student fetch records belong on the audit event (`BagrutReferenceItemRendered_V1`-equivalent for variant-fetches), governed by the same retention + cascade rules.

### Parental visibility default for under-13 cohort

ADR-0059 is silent on whether under-13 students (who under ADR-0050 §9 are visible to parent by default for ExamTarget) get the same parent-visibility default for reference-browse activity. **Recommend hidden-by-default for ALL age bands**. Reference-browse is academic-anxiety inference data (Q2.d); even a parent of a 12-year-old does not have an intrinsic right to "your child browsed quadratics questions at 11pm last Tuesday." This is a stricter posture than ExamTarget visibility because the inference fidelity is higher. ADR-0042 parent-aggregate gates COPPA VPC for *consent grants*; that is a different surface from *browse-log visibility*.

### Ministry-side disclosure obligation

If the Ministry of Education ever asserts a posture (per Open Q1 — Ministry posture risk) that includes a request for browse-history records, Cena's posture must be clean: the only thing we can hand over is anonymized aggregate counts above k=10. Per-student browse logs cannot be requested without a court order, and we should refuse pre-litigation. Document this posture in `docs/ops/legal-disclosure-policy.md` (out of ADR scope but a downstream task).

### PRR-250 verification-sweep blocker interaction

claude-1's PRR-250 verification sweep identifies two blockers for ADR-0059 implementation: (a) BagrutCorpusItemDocument is not ingested in dev (corpus empty) and (b) ICasGatedQuestionPersister is not wired into student-api DI. Both are infrastructure blockers, not privacy blockers — but they interact with this review in two ways:

1. **The empty-corpus path that PRR-250 suggests as a fallback** ("gate the reference-library UI behind an 'empty state' path") is privacy-positive: it gives us a real opportunity to ship the consent flow, retention worker, cascade registration, and one-click revoke surface BEFORE any rendered-item events are written. Use this window. Do not let the corpus ingest before the privacy seams are operational.
2. **The student-api DI registration of `ICasGatedQuestionPersister` MUST add the auth-gate at the endpoint layer per PRR-250 §6** — and the auth-gate must enforce that variant-creation requires a non-expired `ConsentTokenId` per ADR-0059 §3. Pure persister-DI without consent-gate at the endpoint layer is a bypass surface. Tie the two in PRR-245's task body.

### Token-binding strength

ADR-0059 §1 says `Reference<T>.From` "validates `consentToken` is non-expired and bound to the calling student." The mechanism for "bound to the calling student" is unspecified. Naive implementation (token contains studentId, validated against authenticated principal) is fine if and only if (a) tokens are non-replayable across sessions, (b) tokens are revoked on session-end if the underlying auth degrades, (c) tokens cannot be lifted from one device to another. ADR-0042's ConsentAggregate is event-sourced with `subjectId*` encrypted — the binding is durable. Ensure PRR-245 implementation does not introduce a separate cookie-shaped token that drifts from the aggregate.

### Cena.Api.Contracts.Reference namespace surface

ADR-0059 §2 says all Ministry-named fields must live in `Cena.Api.Contracts.Reference.**`. Privacy adjacency: this namespace is the ONLY place where Ministry-corpus-shaped DTOs are permitted on the student wire. The arch-test must extend to ensure that browse-event DTOs (any DTO that would be returned in response to a "list my recent browses" or "did I see this item before" query — should those endpoints ever exist) are also gated behind the same consent-token check that `Reference<T>` enforces. If a future endpoint surfaces "your last 10 browsed items" without consent-token gating, the privacy posture fragments. Restate the invariant: any DTO that exposes browse-event metadata to the student themselves is also `Reference<T>`-wrapped or equivalent-gated.

## Required mitigations

These must land in ADR-0059 (text changes) + PRR-245 (implementation) before ADR moves to Accepted.

1. **Expand §3 consent disclosure copy** to name the audit-event lifecycle (event types, retention horizon, what happens on revoke), per PPL Amendment 13 Art. 11 transparency. Current copy covers purpose only.
2. **Add §3.1 one-click revoke surface** on the reference page itself, per GDPR Art. 7(3) (revoke as easy as grant). Settings-page-only revoke is borderline; deep-linked revoke is non-compliant.
3. **Add §"Retention" subsection** binding `BagrutReferenceItemRendered_V1` to a 180-day horizon (or justified alternative), with cascade-shred on consent revoke + RTBF erasure (the multi-target-exam-plan-findings.md "Blocker-2" pattern — we have institutional precedent on this exact failure mode).
4. **Add §"Browse-history scope limitation"** with the five Q2.d controls: (i) no teacher/parent/tenant-admin browse-log visibility, (ii) no browse → mastery / scheduler / misconception coupling, (iii) no browse fields in LLM prompts, (iv) k≥10 floor on browse-distribution aggregates, (v) raw extracts banned outright.
5. **Mark `BagrutReferenceConsentGranted_V1` event PII fields encrypted** per ADR-0038 `EncryptedFieldAccessor` — explicit on the V1 shape, not by inheritance.
6. **Name the third event type `BagrutReferenceConsentRevoked_V1`** that the cascade requires. The draft only names two; the cascade needs three.
7. **Tie default-on flip to operational gates**, not calendar (Open Q3) — flip on legal sign-off + 4 privacy mitigations live + 30 days of grant/revoke metrics, not "+2 weeks post-Launch."

## Questions back to decision-holder

1. **Will browse-history ever be displayed back to the student themselves** (e.g. "your recently viewed items" widget)? If yes, that endpoint inherits all Q2.d controls and needs the same `Reference<T>`-style gate. If no: state explicitly "no browse history is exposed back to the student" in ADR-0059 §6.
2. **What is the SIEM use case for `BagrutReferenceItemRendered_V1`?** The event is named as "audit-counter side" in §Costs, but the operational purpose is unstated. If it is rate-limit abuse detection, 30 days of retention is enough. If it is incident-response evidence, 180 days. If it is something else (cost-attribution analytics? tier-upgrade signal?), name it — that determines retention.
3. **Is reference-browse activity in scope for any teacher-visibility feature** (current or planned)? If yes, the privacy controls must land in the teacher-side endpoint, not just the student-side. If no, restate "browse activity is student-private only" as an ADR invariant.
4. **Is the 90-day token expiry coupled to the consent-record retention horizon?** They are conceptually distinct (token = re-prompt cadence, record = retention horizon), but the implementation may want to align them. Decide explicitly in PRR-245.
5. **Cross-tenant variant cache**: §5 says variants are cached "across students who request the same source." Is this cache cross-tenant? A variant generated for a student at Tenant A served to a student at Tenant B is fine (the variant content has no PII), but the cache lookup itself shouldn't expose tenant information across boundaries. Confirm cache is content-keyed only.

## Recommended mitigations

Nice-to-have, not blockers, but each materially improves the privacy posture.

1. **Variant-fetch metadata governance**: variant cache stores content keyed on de-dup tuple; per-student fetch records live on the audit-event stream under the same retention rules. Avoid leaking fetch logs into the cache layer.
2. **Parent-visibility default for under-13 reference-browse activity**: hidden-by-default regardless of age (stricter than ExamTarget posture). High-fidelity anxiety inference > declared-plan disclosure.
3. **Cross-tier consent-copy parity audit**: free-tier and paid-tier consent disclosure must be byte-identical in privacy-relevant clauses. Spot-check during PRR-245 review.
4. **Disclosure-policy doc** (`docs/ops/legal-disclosure-policy.md`): pre-canned response posture for Ministry / police / school-admin requests for browse logs. Refuse pre-litigation; require court order; only ever release k≥10 aggregates.
5. **Behavioral-telemetry exclusion list extension**: extend the PRR-022 LLM-prompt allowlist mechanism to a generic "behavioral-telemetry exclusion" lint rule that catches any new event type that looks browse-shaped from leaking into prompts/analytics.
6. **Consent-grant analytics dashboards**: report grant-rate, revoke-rate, time-to-revoke distribution. Privacy KPIs, not vanity metrics. If revoke-rate exceeds 15% in the first 30 days, we have a UX/privacy mismatch.
7. **Sub-180-day retention experiment**: pilot 60-day retention for `BagrutReferenceItemRendered_V1` and measure whether incident-response operations actually need the longer window. Storage-limitation principle says shorter is better if operationally feasible.

## Recommended new PRR tasks

These are the concrete task-shaped follow-ups, in priority order. Coordinator can choose to fold them into PRR-245 or split into siblings.

1. **PRR-N-priv-1 — Cascade `BagrutReferenceItemRendered_V1` shred via RetentionWorker**. Extends PRR-015 / PRR-218 cascade pattern to cover the new event type. Cascade fires on (a) consent revoke, (b) RTBF erasure, (c) retention-horizon expiry. Launch blocker for GDPR Art. 17 compliance window. Effort: ~1-2 days; pattern is well-established.
2. **PRR-N-priv-2 — Add `BagrutReferenceConsentRevoked_V1` event type + one-click revoke surface**. Required for GDPR Art. 7(3). Lives on the reference page itself; settings-only revoke is non-compliant. Effort: ~1 day for backend event + ~0.5 day for UI affordance.
3. **PRR-N-priv-3 — Define retention horizon for `BagrutReferenceItemRendered_V1`** (recommend 180 days; pilot 60 days as recommended-mitigation-7). ADR-0059 §"Retention" subsection + RetentionPolicy.cs entry. Blocker for storage-limitation principle.
4. **PRR-N-priv-4 — Browse-history scope-limitation ADR text**: §"Browse-history scope limitation" with the five Q2.d controls. Blocker for ADR acceptance.
5. **PRR-N-priv-5 — Expand §3 consent disclosure copy** in en/he/ar with audit-event lifecycle, retention horizon, revocation UX — three-locale prototype before shipping. PPL Amendment 13 Art. 11 transparency.
6. **PRR-N-priv-6 — Encrypt `BagrutReferenceConsentGranted_V1` PII fields** explicitly per ADR-0038 `EncryptedFieldAccessor`. Mark on V1 shape, not by inheritance.
7. **PRR-N-priv-7 — Under-13 cohort gating**: hide reference library entirely for students under 13 (do not surface parent-mediated path). Hard gate on the reference-page endpoint based on student age band; matches ADR-0042 / ADR-0041 age-band matrix.
8. **PRR-N-priv-8 — k-anonymity floor (PRR-026 extension) for browse-distribution aggregates**: extend the k≥10 floor scope annotation to cover any aggregate over `BagrutReferenceItemRendered_V1`. Pattern matches the multi-target-exam-plan PRR-222 extension.
9. **PRR-N-priv-9 — LLM-prompt allowlist extension**: extend PRR-022 / PRR-221 prompt-assembly lint rule to deny any browse-history fields. Same pattern, new field set.
10. **PRR-N-priv-10 — Disclosure-policy doc** (`docs/ops/legal-disclosure-policy.md`): pre-canned response posture for Ministry / police / school-admin requests for browse logs. Out of ADR scope but a downstream task that must exist before first regulator inquiry.

## Blockers / non-negotiables

- **Blocker-1**: `BagrutReferenceItemRendered_V1` retention horizon must be specified in ADR-0059 before any emitter ships. Indefinite or unspecified retention violates PPL Amendment 13 purpose-limitation + GDPR Art. 5(1)(e). *Fix via PRR-N-priv-3.*
- **Blocker-2**: RTBF cascade must be designed and registered before `BagrutReferenceItemRendered_V1` emitters ship. Same failure mode as multi-target-exam-plan-findings.md "Blocker-2"; we have institutional memory on this. *Fix via PRR-N-priv-1.*
- **Blocker-3**: One-click revoke surface on the reference page itself. Settings-only revoke is borderline non-compliant under GDPR Art. 7(3); deep-link is non-compliant. *Fix via PRR-N-priv-2.*
- **Non-negotiable**: No teacher / parent / tenant-admin visibility of browse logs (raw or aggregated below k=10). *Fix via PRR-N-priv-4 + PRR-N-priv-8.*
- **Non-negotiable**: Browse history MUST NOT factor into BKT, scheduler, misconception state, or LLM prompts. *Fix via PRR-N-priv-4 + PRR-N-priv-9.*

## Sign-off

**Reviewer**: claude-subagent-persona-privacy
**Date**: 2026-04-28
**Verdict**: yellow (the 4 invariants are well-architected; the 7 required mitigations close the privacy gaps at the seams)
**Block on**: ADR-0059 §3 expansion + cascade-shred design before any `BagrutReferenceItemRendered_V1` emitter ships in PRR-245. The institutional memory from multi-target-exam-plan-findings.md "Blocker-2" tells us that shipping events first and retrofitting the cascade is a GDPR Art. 17 violation window we have already learned from once. Do not repeat the lesson.
