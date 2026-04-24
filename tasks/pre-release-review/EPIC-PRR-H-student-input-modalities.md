# EPIC-PRR-H: Student input modalities (pad / keyboard / chem / physics / language)

**Priority**: P0 — launch-blocker for Bagrut Math + Physics + Chem + PET Q + humanities
**Effort**: XL (12-16 weeks engineering + parallel vendor procurement; some pieces deferred to Launch+1)
**Lens consensus**: all 10 personas (2 RED, 1 yellow-red, 7 yellow on 001-brief; amber + 2 red on 002-brief); + Kimi swarm research 2026-04-22 (overall YELLOW 5.5/10)
**Source docs**:
- [STUDENT-INPUT-MODALITIES-001-discussion.md](../../docs/design/STUDENT-INPUT-MODALITIES-001-discussion.md)
- [STUDENT-INPUT-MODALITIES-002-discussion.md](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md)
- [q3-writing-pad-hwr-swarm-report-2026-04-22.md](../../pre-release-review/reviews/research/q3-writing-pad-hwr-swarm-report-2026-04-22.md)
- 10-persona findings under [pre-release-review/reviews/persona-*/student-input-modalities-findings.md](../../pre-release-review/reviews/) + the 002 variants
**Assignee hint**: Shaker (coordinator) + kimi-coder (frontend/backend) + vendor-procurement (ops) + legal (privacy DPIA) + content-engineering (chem notation linter)
**Tags**: source=student-input-modalities-001,002; type=epic; epic=epic-prr-h; launch-blocker
**Status**: Not Started — awaiting decision-holder resolution of 5 open items (§5 below)
**Source**: 001-brief persona review + 002-brief persona review + Kimi swarm research 2026-04-22
**Tier**: launch (most) / launch+1 (chem Lewis pad, writing-pad-primary math)
**Related epics**:
- [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md) — onboarding; renderer consumes modality choices
- [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md) — item banks need to carry modality flags
- [EPIC-PRR-D](EPIC-PRR-D-shipgate-scanner-v2.md) — shipgate bans on countdown/streak apply here too

---

## 1. Epic goal

Cena's renderer must let a student answer each question in a way that matches how the real exam is taken AND what their accessibility + locale + device allow. This epic covers:

1. **Q1 — photo of solution** (narrow framing only; MSP architecture).
2. **Q2 — hide-then-reveal** (visible-first default + opt-in hide + classroom-enforced mode).
3. **Q3 — typed vs. writing-pad vs. shared primitives across math / physics / chem / language**.

All three converge through a shared `AttemptMode` concept + a shared input-component abstraction + a shared moderation/DPA/compliance stack. This epic prevents them from being built as three disconnected silos.

## 2. Locked decisions (10 persona reviews + swarm research)

- **Q1**: narrow framing only (diagnose-wrong-answer). Broad framing deferred to Launch+1 behind premium-SKU. MSP architecture: vendor-moderated intake + structured vision-LLM output + 24h TTL + hash ledger.
- **Q2**: default = **visible-first, student opts in to hide**, per-session toggle. Classroom-enforced server-side redaction with **visible banner + override-request path**. Excluded from PRR-228 diagnostic. Scaffolding hints go stem-grounded under hidden mode. No countdown / timer / auto-hide (ADR-0048 ban).
- **Q3 math**: **MathLive typed primary at Launch**, writing-pad as client-only scratch-capture (not graded). Writing-pad-primary deferred to Launch+1 after multi-vendor HWR proven.
- **Q3 physics**: existing FBD canvas (PRR-208) + MathLive for equations.
- **Q3 chem**: **typed-primary** with [Ministry-notation linter](TASK-PRR-255-ministry-notation-linter-chem.md). **Lewis pad DEFERRED** (DECIMER 73% / MolScribe 8% hand-drawn exact match — not exam-grade). SMILES/InChI a11y fallback kept as accessible input.
- **Q3 language**: **keyboard-only** (10/10 unanimous). Handwritten essay HWR categorical no-ship.
- **Q3 PET quant**: **MathLive OFF by default** (pad is scratch, MC is graded commit — ministry).
- **HWR vendor**: **MyScript iink.js (client, WASM) primary + Claude Sonnet 4 vision fallback**, fallback hard-capped ≤5% of HWR calls. Mathpix disqualified pending DPA. Pure Sonnet rejected.
- **Stroke data retention (revised per swarm)**: **raw strokes session-only, in-memory, never persisted**. Only canonical symbolic output retained 30d per ADR-0003. PPL Amendment 13 (effective 2025-08-14) treats biometric IDs as "highly sensitive".
- **Vision-LLM fallback defense**: OCR pre-filter + dual-LLM validation + canary-nonce in structured envelope. Redteam's canary-glyph-in-image dropped (HWR destroys it).
- **Tier bump**: basic tier caps HWR at ~40 calls/mo; premium ($18-22/mo) unlocks ~600 + optional broad photo upload.

## 3. Sub-task ladder (to be created after decision-holder greenlight)

### 3.1 Q1 photo-of-solution (MSP architecture)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-244 | MSP Layer 1 client pre-filter + 3-path entry | P0 | yes |
| PRR-245 | MSP Layer 2 vendor-moderated intake (reuse PhotoUploadEndpoints) | P0 | yes |
| PRR-246 | MSP Layer 3 structured vision call + hash ledger | P0 | yes |
| PRR-247 | First-use consent modal (legal-reviewed, grade-9 copy) | P0 | yes |
| PRR-248 | Self-diagnosis UX beat (educator non-negotiable) | P0 | yes |
| PRR-249 | CSAM quarantine owner + NCMEC workflow | P0 | **legal-op gate** |
| PRR-250 | Upload disclaimer checkbox (civil-liability shifting) | P1 | — |

### 3.2 Q2 hide-then-reveal

(Already created as [PRR-260](TASK-PRR-260-hide-reveal-session-toggle.md) through [PRR-264](TASK-PRR-264-hide-reveal-shipgate-audit.md).)

- [PRR-260](TASK-PRR-260-hide-reveal-session-toggle.md) student session toggle
- [PRR-261](TASK-PRR-261-classroom-redacted-projection.md) classroom redacted projection + override
- [PRR-262](TASK-PRR-262-scaffolding-stem-grounded-hints.md) stem-grounded scaffolding
- [PRR-263](TASK-PRR-263-diagnostic-ignores-hide-reveal.md) diagnostic carve-out
- [PRR-264](TASK-PRR-264-hide-reveal-shipgate-audit.md) shipgate audit

Optional follow-up: PRR-265 commit-and-compare (decision-holder-gated; cogsci vs a11y+sre split).

### 3.3 Q3 input-modality stack

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-270 | `AttemptMode` + `FreeformInputField<T>` shared abstraction | P0 | yes |
| PRR-271 | MathLive-primary + pad-scratch-capture for Bagrut Math | P0 | yes |
| PRR-272 | Chem typed-primary input + Ministry-notation linter (supersedes PRR-255) | P0 | yes |
| PRR-273 | PET renderer: MathLive OFF by default, MC-committed | P0 | yes |
| PRR-274 | Keyboard-only language text input + RTL Vuetify VTextarea workarounds (Arabic 500-word) | P0 | yes |
| PRR-275 | Physics FBD + typed-math extension (ties to existing PRR-208) | P1 | — |
| PRR-276 | SMILES/InChI accessible a11y fallback for chem | P1 | — |
| PRR-277 | `TenantPolicyOverlay<WritingPadPolicy>` per-subject per-tenant | P1 | — |
| PRR-278 | Per-modality cost dashboard + per-student HWR daily cap | P1 | — |
| PRR-279 | Tier-bump: premium SKU with higher HWR call cap | P1 | **product-gate** |

### 3.4 HWR + vision-LLM infrastructure (shared with Q1 MSP)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-280 | MyScript iink.js WASM procurement + K-12 DPA + RTL math QA | P0 | **vendor-procurement gate** |
| PRR-281 | Vision-LLM fallback: OCR pre-filter + dual-LLM validation + canary-nonce | P0 | yes |
| PRR-282 | 2 GB Android device-floor benchmark (MyScript WASM + Vue 3 PWA) | P0 | yes |
| PRR-283 | Vision-LLM fallback cap enforcement (≤5% of HWR calls) + alerting | P0 | yes |
| PRR-284 | Stroke-data session-only-in-memory enforcement + PPL A13 compliance | P0 | yes |
| PRR-285 | PPL Amendment 13 DPIA for handwriting strokes (legal + DPO) | P0 | **legal gate** |

### 3.5 Deferred to Launch+1 (intentionally)

- Chem Lewis-structure pad + OCSR (DECIMER/MolScribe too weak). Consider template-select surrogate.
- Writing-pad-primary for Bagrut Math (promote from scratch-only once multi-vendor HWR proven).
- Broad-framing Q1 photo-upload (ask-about-anything) — premium SKU or post-Launch.
- Commit-and-compare flow (cogsci/a11y split — user call).
- Handwritten chem Hebrew state-symbol recognition — no vendor support today.

## 4. Non-negotiable references

- [ADR-0001](../../docs/adr/0001-multi-institute-enrollment.md) — tenancy isolation.
- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md) — SymPy CAS oracle applies to math + chem + quantitative.
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — session-scope + 30d data rules for derived output.
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) — 3-tier LLM routing; vision fallback stays in tier budget.
- [ADR-0043](../../docs/adr/0043-bagrut-reference-only-enforcement.md) — Bagrut reference-only.
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no time-pressure mechanics (Q2 bans timers).
- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md) — multi-target plan; stroke retention addendum needed.
- Memory "No stubs — production grade" — no placeholder input components ship.
- Memory "Math always LTR" — bdi wrapping across all modalities.
- Memory "Honest not complimentary" — effect-size honesty; vendor accuracy numbers with citations.
- Memory "Full sln build gate" — `Cena.Actors.sln` green before merge.
- PPL Amendment 13 (effective 2025-08-14) — biometric IDs "highly sensitive"; affects stroke data.

## 5. Open decisions blocking sub-task creation

**Decision-holder (Shaker) resolves these before sub-task generation:**

1. **MyScript K-12 DPA + pricing** — procurement path approved? Budget envelope? Per-seat / per-call / flat?
2. **RTL math vendor QA** — can we ship Hebrew/Arabic math handwriting samples to MyScript + Mathpix + Google Vision for a benchmark bake-off? If not, HE/AR streams become typed-only at Launch.
3. **PPL Amendment 13 legal opinion** — are handwriting strokes biometric under A13? If yes, DPIA is mandatory before any writing-pad ships.
4. **2 GB Android device-floor testing** — does Cena own a low-end test device, or do we procure?
5. **Chem Lewis-pad scope**: (a) defer entirely Launch+1, (b) ship template-select scaffolding aid (not grading), or (c) ship OCSR knowing 73% ceiling and accept the hit?

Plus the prior-batch open items:

6. **Q2 commit-and-compare**: ship at Launch (cogsci) or defer to Launch+1 (a11y + sre)?
7. **Broad Q1 photo**: premium-SKU feature at Launch or Post-Launch?

## 6. Vendor procurement dependencies

This epic cannot fully ship without external vendor contracts:

| Gate | Party | Status |
|---|---|---|
| MyScript iink.js enterprise license + DPA + RTL QA test set | MyScript sales + Cena legal | not started |
| Anthropic Claude Sonnet 4 enterprise tier + zero-retention DPA + K-12 addendum | Anthropic + Cena legal | partially started (EPIC-PRR-G) |
| CSAM moderation + NCMEC reporting pipeline | Rekognition / Guardrails / third-party | not started |
| PPL Amendment 13 DPIA + DPO engagement | Cena legal | not started |

Total vendor critical path: **4-8 weeks** beyond engineering.

## 7. Launch timeline estimate

Subject to §5 resolutions + §6 procurement:

- Engineering-only path: 8-12 weeks (pad-secondary MathLive-primary math + typed chem + keyboard language).
- With MyScript procurement: +2-4 weeks critical path.
- With PPL DPIA + legal reviews: +2-3 weeks overlapping.
- **Net realistic: 12-16 weeks from decision-holder greenlight to Launch.**

Parallel critical paths:
1. Engineering (Q1 MSP + Q2 hide-reveal + Q3 typed-primary modalities).
2. Procurement (MyScript + vision-LLM vendor + CSAM).
3. Legal (DPIA + consent modal + disclaimer copy).
4. Content (Ministry-notation linter, RTL item QA, humanities rubric).

None of the four can independently carry Launch.

## 8. Out of scope

- Speech-to-text input (Launch+1).
- Handwritten essay HWR (categorical no-ship per privacy).
- Full-page OCR of textbook pages (separate content-authoring pipeline).
- Chem Lewis HWR at Launch (73% exact-match ceiling).
- Writing-pad-primary math at Launch (deferred until multi-vendor HWR proven).
- Broad Q1 photo (ask-about-anything) at Launch base-tier.

## 9. Reporting

Epic-level progress tracked via the sub-task complete calls. No direct `complete` on the epic — it closes when all sub-tasks in §3.1-§3.4 close AND all §5 decisions are resolved.

## 10. Related

- [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)
- [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md)
- [EPIC-PRR-D](EPIC-PRR-D-shipgate-scanner-v2.md)
- Swarm research report: [q3-writing-pad-hwr-swarm-report-2026-04-22.md](../../pre-release-review/reviews/research/q3-writing-pad-hwr-swarm-report-2026-04-22.md)
- Persona findings under [pre-release-review/reviews/persona-*/](../../pre-release-review/reviews/)
- Existing [PRR-260..264](TASK-PRR-260-hide-reveal-session-toggle.md) Q2 tasks already created
- Existing [PRR-255](TASK-PRR-255-ministry-notation-linter-chem.md) Ministry-notation linter (referenced by PRR-272)
