# EPIC-PRR-J: Photo-Upload CAS Diagnostic Chain

**Priority**: P1 — Premium/Plus tier differentiator; not launch-blocker for Basic tier. Ship with or shortly after initial launch.
**Effort**: L (8-12 weeks engineering + 4-6 weeks misconception-taxonomy content + parallel vendor DPA on top of EPIC-PRR-H §3.1 MSP intake)
**Lens consensus**: 10-persona review 2026-04-22 — 10 changes forced (see §2)
**Source docs**:
- [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md) (CAS oracle mandate applies to every step)
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) (session-scope + 30d retention for derived data)
**Assignee hint**: Shaker (coordinator) + backend-dev (CAS chain + taxonomy service) + ML-engineer (step extraction + template selection) + math-education-SME (misconception taxonomy authoring) + frontend (diagnostic UX + reflection gate + dispute flow) + support-lead (dispute workflow + audit view) + legal (vendor DPA + PPL A13 DPIA)
**Tags**: source=photo-upload-diagnostic; type=epic; epic=epic-prr-j; commercial-differentiator; cas-gated; session-scoped
**Status**: Not Started — awaiting decision-holder resolution of 6 open items (§5 below) + dependency on EPIC-PRR-H §3.1 MSP intake being live
**Source**: 10-persona photo-upload diagnostic review 2026-04-22
**Tier**: launch (v1: first-wrong-step detection) / launch+1 (v2: full-derivation narration, longitudinal misconception tracking)

**CRITICAL SCOPE NOTE — relationship to EPIC-PRR-H §3.1**

[EPIC-PRR-H §3.1](EPIC-PRR-H-student-input-modalities.md#31-q1-photo-of-solution-msp-architecture) already owns:

- PRR-244..250: MSP architecture (client pre-filter, vendor-moderated intake, structured vision call, consent modal, CSAM quarantine, disclaimer)
- Narrow-framing diagnose-wrong-answer scope confirmed
- 24h TTL + hash ledger
- Broad framing deferred

**This epic (EPIC-PRR-J) owns the layer ABOVE the MSP intake:**

- Extraction of student's step sequence from OCR+vision output into canonical math steps
- CAS-chain verification of each step-to-step transition
- Misconception-template taxonomy (closed-set mapping of CAS break types → pedagogical explanations)
- Reflection-gate diagnostic UX ("try again" before full narration)
- Dispute flow + support audit view
- Per-tier caps + soft-cap upsell UX
- Teacher-auditable "show my work" view

This epic **cannot start integration work until EPIC-PRR-H §3.1 delivers PRR-244..246** (the MSP intake pipeline this epic consumes). Sub-task creation for §3.1 below gated on EPIC-PRR-H progress.

**Related epics**:
- [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md) — §3.1 provides MSP intake; this epic builds the diagnostic chain on top
- [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md) — per-tier photo-diagnostic caps live there (Basic=0, Plus=20/mo, Premium=100/mo soft + 300/mo hard)
- [EPIC-PRR-B](EPIC-PRR-B-llm-routing-governance.md) — step-extraction LLM routing + dual-LLM validation
- [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md) — under-16 parental consent for photo feature

---

## 1. Epic goal

When a student gets a wrong answer on a practice item, they can upload a phone photo of their handwritten solution (through EPIC-PRR-H §3.1 MSP intake) and receive a **pedagogically useful, CAS-verified diagnostic** identifying the *first mathematical break* in their work and mapping it to a curated misconception template.

Scope for v1 (launch): **detect first wrong step, narrate from closed taxonomy, reflection gate, dispute-ready.** No full-derivation narration, no whole-work-explanation until v2.

## 2. Locked decisions (10-persona review 2026-04-22)

| # | Decision | Trigger persona |
|---|----------|-----------------|
| 1 | OCR confidence threshold + editable "is this what you wrote?" preview before CAS. Below threshold, show extracted text in editable form; student confirms or edits before CAS runs. Never silently commit to a mis-reading. | #1 student, #8 accessibility |
| 2 | One-card privacy disclosure shown before first upload (HE/AR/EN): "Photos deleted within 5 minutes / step analysis kept 30 days / never used to train AI / delete anytime." Parental consent for under-16. | #2 parent, #5 compliance |
| 3 | Expandable "show my work" view surfaces the CAS verification chain (SymPy transformation tree) — makes the system teacher-auditable and contestable, not oracular | #3 teacher |
| 4 | **Reflection gate**: first wrong step detected → "I see the error. Try again — hint available" (one student retry) BEFORE full narration unlocks. Turns feature from answer-checker into learning scaffold. Backed by productive-failure research. | #4 education research |
| 5 | OCR vendor DPA with **EU/Israel data residency** + **no-training clause** required. If Mathpix doesn't offer, evaluate alternatives (in-house HWR, Pix2Text with controlled residency). PPL Amendment 13 DPIA for minor photos. | #5 compliance |
| 6 | **v1 scope: first wrong step detection only.** No whole-derivation narration. Hallucination attack surface too wide until v2. Accept step-skipping ambiguity with "I couldn't follow between step 2 and step 3" rather than false-wrong flag. | #6 engineering, #7 ML safety |
| 7 | **Closed misconception taxonomy** — LLM narration sourced from curated mapping (CAS-break-type → 1-3 candidate misconception templates), never freeform. If no template matches confidently, conservative "let me check with your teacher" message. | #7 ML safety |
| 8 | **Typed-steps alternative** surfaced as default when a student's OCR quality is consistently below threshold. Never call out their handwriting; reframe positively. | #8 accessibility |
| 9 | **Dispute workflow**: "this diagnosis seems wrong" button on every diagnostic. Support audit view shows photo + OCR + CAS chain + LLM narration + template. Auto-credit on confirmed system errors. | #9 support |
| 10 | **Soft cap 100 uploads/mo on Premium** → graceful upsell ("you're in the top 1%! book tutor session?"). **Hard cap 300/mo.** Above-200 users flagged for account-sharing review. | #10 CFO |

## 3. Sub-task ladder (to be created after decision-holder greenlight AND EPIC-PRR-H §3.1 PRR-244..246 complete)

### 3.1 Step-extraction service (takes MSP vision-LLM structured output → canonical step sequence)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-350 | `StepExtractionService` — consumes MSP structured output, produces canonical ordered step sequence (LaTeX + AST) | P0 | yes — blocks CAS chain |
| PRR-351 | OCR-confidence gate: if any extracted step <confidence threshold, route to preview UX instead of CAS | P0 | yes |
| PRR-352 | Editable preview UX: student sees "is this what you wrote?" with inline math editor, confirms or edits | P0 | yes |
| PRR-353 | Original-problem grounding check: extracted steps must trace back to the posed problem's initial expression; reject otherwise | P0 | yes |

### 3.2 CAS verification chain

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-360 | `StepChainVerifier` — SymPy-backed step-to-step equivalence checker; returns first failing transition + expected-vs-detected expressions | P0 | yes — core of the epic |
| PRR-361 | Canonicalization layer: normalize equivalent forms (e.g., `(x-2)(x+3)` ≡ `x²+x-6`) before comparison | P0 | yes |
| PRR-362 | Step-skipping tolerance: if an N-step leap is mathematically valid, accept; if it's not, surface "I couldn't follow between X and Y" not "X is wrong" | P0 | yes |
| PRR-363 | CAS chain export format for teacher-auditable "show my work" view | P0 | yes |

### 3.3 Misconception taxonomy (content-heavy; content-engineering own)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-370 | Misconception taxonomy structure definition: `BreakType → [MisconceptionTemplate]`, with template = (trigger conditions, student-facing explanation HE/AR/EN, example counter-case, suggested next step) | P0 | yes |
| PRR-371 | Initial taxonomy content for Bagrut Math 4-unit: **sign-flip distributive, minus-as-subtraction, premature cancellation, factoring errors (incomplete, sign, like-terms), quadratic-formula sign errors, fraction-over-fraction errors, exponent-rule slips, FOIL mistakes** — minimum 40 templates covering 80% of common student errors | P0 | yes — **SME gate: 4-6 weeks math-education authoring** |
| PRR-372 | Taxonomy content for 5-unit math (advanced topics: trig identity errors, calculus chain-rule errors, L'Hôpital misapplications) | P1 | — |
| PRR-373 | Taxonomy content for physics (launch+1) | P2 | — |
| PRR-374 | Template-matching scorer: given CAS break (expected expr, detected expr, context), score each candidate template; pick best match if confidence >threshold, else conservative "let me check" | P0 | yes |
| PRR-375 | Taxonomy governance: review board, versioning, update workflow; track which templates get disputed and iterate | P1 | — |

### 3.4 Diagnostic UX (student-facing)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-380 | Diagnostic result screen v1: "first wrong step" surface with reflection gate ("try again — hint available") | P0 | yes |
| PRR-381 | Post-reflection flow: if student retries and corrects → celebration + mastery signal; if student still stuck → unlock misconception narration | P0 | yes |
| PRR-382 | Expandable "show my work" CAS-chain view (teacher-auditable, student-curious) | P0 | yes |
| PRR-383 | Privacy one-card + consent flow (HE/AR/EN) on first-ever photo upload | P0 | yes |
| PRR-384 | Typed-steps alternative UX — offered when OCR quality consistently low for a student, framed positively | P0 | yes |
| PRR-385 | "This diagnosis seems wrong" dispute button on every diagnostic | P0 | yes |
| PRR-386 | Soft-cap reached UX: "you're in the top 1%! book tutor session?" — not hard block | P0 | yes — **shipgate must pass: no scarcity / loss-aversion framing** |

### 3.5 Support + dispute infrastructure

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-390 | Support portal audit view: photo (pre-delete), OCR output, CAS chain, LLM narration, template selected, student dispute reason | P0 | yes |
| PRR-391 | Auto-credit on confirmed system error (support 1-click resolves dispute) | P0 | yes |
| PRR-392 | Weekly disputed-diagnosis review → feedback into taxonomy (regression test when template updated) | P1 | — |
| PRR-393 | Dispute metrics dashboard: dispute rate per template, per-item, per-locale; flag templates >5% dispute rate for taxonomy review | P1 | — |

### 3.6 Tier + abuse controls (integrates with EPIC-PRR-I)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-400 | Per-tier upload counter + enforcement (Basic=0, Plus=20/mo, Premium=100/mo soft + 300/mo hard) — consumes `SubscriptionTier` from EPIC-PRR-I | P0 | yes — couples to EPIC-PRR-I PRR-310 |
| PRR-401 | Soft-cap threshold trigger → upsell UX (shipgate-compliant positive framing) | P0 | yes |
| PRR-402 | Hard-cap threshold → "contact support" UX (for legitimate exam-week cram) | P0 | yes |
| PRR-403 | Abuse detection: users >200 uploads/mo flagged for account-sharing investigation | P1 | — |
| PRR-404 | Image-similarity check: reject re-upload of nearly-identical photos within short window (prevents "keep re-asking until I like the answer") | P1 | — |

### 3.7 Compliance + vendor

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-410 | OCR vendor DPA — EU/Israel data residency + no-training clause (coordinate with EPIC-PRR-H PRR-280 vendor procurement) | P0 | **legal + vendor gate** |
| PRR-411 | PPL Amendment 13 DPIA for minor photos (coordinate with EPIC-PRR-H PRR-285) | P0 | **legal gate** |
| PRR-412 | Photo-deletion SLA enforcement: verifiable delete within 5 min of upload; monitoring + alerting; hash-ledger retained (not photo itself) | P0 | yes |
| PRR-413 | Face + name + school-logo redaction before OCR (reduce incidental PII exposure) | P0 | yes |
| PRR-414 | Parental consent registration for under-16 accounts (coordinate with EPIC-PRR-C) | P0 | **legal gate** |

### 3.8 ML safety + observability

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-420 | Per-student OCR-confidence tracking → triggers typed-steps fallback recommendation | P0 | yes |
| PRR-421 | Template-selection confidence tracking → below threshold routes to "let me check with your teacher" conservative message | P0 | yes |
| PRR-422 | End-to-end latency SLO: <15 seconds from upload to diagnostic return (p95) | P0 | yes |
| PRR-423 | Accuracy-audit sampling: weekly 1% sample of diagnostics human-reviewed by math SME; error rate tracked over time | P1 | — |

### 3.9 Deferred to Launch+1 (intentionally)

- Full-derivation narration (narrate every step, not just first wrong one)
- Longitudinal misconception report in parent dashboard ("your child struggled with X across 4 problems this month")
- Teacher-assigned handwriting grading (teacher uploads student work, gets graded batch)
- OCR model fine-tuning on consented samples (requires data-collection consent infrastructure first)
- Multi-page solution uploads (v1 = single photo per diagnostic)
- Non-math subjects (physics diagrams, chem notation — coordinate with EPIC-PRR-H physics/chem streams)

## 4. Non-negotiable references

- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md) — every diagnostic step CAS-verified; no unverified math reaches students.
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — diagnostic outputs session-scoped, 30-day retention, never on student profile, never trained on.
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) — step extraction uses tier-appropriate model; dual-LLM validation per EPIC-PRR-H PRR-281.
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no time-pressure in diagnostic UX; soft-cap upsell must be positively framed.
- [`docs/engineering/shipgate.md`](../../docs/engineering/shipgate.md) — CI scanner bans scarcity / loss-aversion / variable-ratio mechanics in upsell UX.
- Memory "No stubs — production grade" — no placeholder taxonomy; every template is real, SME-reviewed, and CAS-gated.
- Memory "Labels match data" — UI must say "I see the error in step 3" only when CAS confirms it; never cosmetic certainty.
- Memory "Honest not complimentary" — when confidence is low, say so ("I'm not fully sure — let me flag this for your teacher") rather than fabricate.
- Memory "Math always LTR" — diagnostic math renders LTR in RTL pages (HE/AR).
- Memory "Full sln build gate" — `Cena.Actors.sln` green before merge.
- Memory "SymPy CAS oracle" — non-negotiable.
- Memory "Misconception session scope" — non-negotiable.
- PPL Amendment 13 (effective 2025-08-14) — minor photo data sensitivity; DPIA required.

## 5. Open decisions blocking sub-task creation

**Decision-holder (Shaker) resolves these before sub-task generation:**

1. **Taxonomy SME resource**: who authors the 40-template initial Bagrut-Math-4-unit taxonomy? 4-6 weeks FTE equivalent. In-house math SME, or contracted? Budget approval?
2. **v1 scope confirmed first-wrong-step only?** Persona consensus was yes; needs product-owner sign-off given it limits wow-factor.
3. **Reflection gate mandatory or optional?** Pedagogical research favors mandatory; product instinct may favor skip-option. Decision-holder call.
4. **OCR vendor swap trigger**: if Mathpix DPA doesn't deliver Israel/EU residency by week 4, swap to alternative? Which alternative is pre-vetted?
5. **Diagnostic caps per tier — confirm**: Basic=0 (upgrade prompt), Plus=20/mo, Premium=100/mo soft + 300/mo hard. Or different?
6. **Accuracy-audit SME resource**: weekly 1% sample human-review requires ongoing ~8h/week of SME time. Approved?

Plus the cross-epic dependency flags:

7. **EPIC-PRR-H §3.1 delivery timing**: this epic's §3.1 cannot start until PRR-244..246 land. Coordinator tracks that dependency in PRR-H.
8. **EPIC-PRR-I tier enforcement timing**: PRR-400 requires `SubscriptionTier` from EPIC-PRR-I PRR-310. Coordinate sequencing.

## 6. Vendor procurement dependencies

| Gate | Party | Status |
|---|---|---|
| OCR vendor DPA (Mathpix or alternative) with EU/Israel residency + no-training | OCR vendor + Cena legal | **shared with EPIC-PRR-H PRR-280** — not started |
| Anthropic zero-retention DPA for Sonnet step-extraction | Anthropic + Cena legal | **shared with EPIC-PRR-G** — partially started |
| PPL Amendment 13 DPIA | Cena legal + DPO | **shared with EPIC-PRR-H PRR-285** — not started |
| Math-education SME for taxonomy authoring | Contracted / in-house | not started |

## 7. Launch timeline estimate

Subject to §5 resolutions + §6 procurement + EPIC-PRR-H §3.1 completion:

- Engineering-only path (step extraction + CAS chain + diagnostic UX + dispute flow, assuming MSP intake in place): **6-8 weeks**.
- Misconception taxonomy authoring (40 Bagrut-Math-4 templates, SME-reviewed): **4-6 weeks parallel**.
- Vendor DPA (shared with EPIC-PRR-H): **4-6 weeks** parallel.
- Legal (PPL A13 DPIA, consent flow, privacy card): **2-3 weeks** overlapping.
- **Net realistic: 8-12 weeks from decision-holder greenlight AND EPIC-PRR-H §3.1 complete, to diagnostic v1 launch.**

Parallel critical paths:
1. Engineering (step extraction, CAS chain, UX, dispute flow, tier enforcement).
2. Content (misconception taxonomy SME authoring).
3. Vendor/legal (OCR DPA, PPL DPIA, consent).
4. QA/safety (accuracy audit sampling harness).

## 8. Out of scope

- Teaching students math they haven't learned (this is diagnostic, not instructional — separate content-authoring pipeline).
- Real-time handwriting diagnosis (this is batch photo, not streaming stroke capture — EPIC-PRR-H §3.3 handles writing pad).
- Grading full homework batches for teachers (v2 feature, separate product scope).
- Non-math subjects at v1 (chem / physics diagrams deferred to v2).
- Peer-comparison features ("other students also got this wrong") — privacy-problematic, descoped.
- AI-generated practice problems from misconception patterns — content-authoring concern, separate epic.

## 9. Reporting

Epic-level progress tracked via sub-task complete calls. No direct `complete` on the epic — it closes when:
1. All §3.1-§3.8 sub-tasks close.
2. All §5 decisions resolved.
3. Accuracy audit shows <3% error rate on 100 consecutive human-reviewed samples.
4. Dispute rate <2% of all diagnostics over 30 consecutive days.

## 10. Success criteria (post-launch)

Measured at day 90 post-v1-launch:

- **End-to-end latency**: p95 <15 seconds from upload to diagnostic return.
- **OCR-preview edit rate**: 20-40% of uploads show the student edited the preview (healthy calibration — too low = threshold too loose, too high = threshold too tight).
- **Reflection-gate retry success**: >30% of students who retry after reflection gate correct their error unaided (validates pedagogical value).
- **Template-matching confidence**: >75% of diagnostics surface a specific template (vs. conservative "let me check" fallback).
- **Dispute rate**: <2% of all diagnostics disputed.
- **Dispute-upheld rate**: of disputes, <30% are upheld as system error (i.e., most disputes reveal the system was right but student was frustrated — not a correctness problem).
- **Accuracy-audit error rate**: <3% of 1% weekly-audit sample.
- **Premium soft-cap hit rate**: <5% of Premium users hit 100/mo soft cap.
- **Abuse-flag rate**: <0.5% of users exceed 200/mo.
- **Typed-steps-fallback uptake**: among students with low OCR confidence, >60% switch to typed-steps alternative within 2 weeks.
- **NPS impact**: Premium subscribers citing diagnostic feature in NPS >20% positive mention.

## 11. Related

- [EPIC-PRR-H §3.1](EPIC-PRR-H-student-input-modalities.md#31-q1-photo-of-solution-msp-architecture) — **hard dependency**; MSP intake PRR-244..246 must land first
- [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md) — tier caps PRR-312/PRR-400 are enforced jointly
- [EPIC-PRR-B](EPIC-PRR-B-llm-routing-governance.md) — step-extraction LLM routing governance
- [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md) — under-16 parental consent for photo feature
- [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md) — Anthropic zero-retention DPA (shared procurement)
- [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md) — source
