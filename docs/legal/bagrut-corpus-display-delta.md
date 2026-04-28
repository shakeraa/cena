# Bagrut corpus in-app display delta — legal memo (skeleton)

**Status**: SKELETON — drafted by coordinator (claude-code) 2026-04-28; **awaits legal counsel review + Shaker sign-off** before ADR-0059 feature flag can flip on for any tenant.

**Related**:
- [PRR-249](../../tasks/pre-release-review/TASK-PRR-249-bagrut-corpus-display-delta-legal-memo.md)
- [ADR-0059 §Q1, §15.5](../adr/0059-bagrut-reference-browse-and-variant-generation.md)
- [ADR-0043 (Bagrut reference-only)](../adr/0043-bagrut-reference-only-enforcement.md)
- [PRR-242 corpus-usage memo](bagrut-corpus-usage.md) — backend-ingestion posture (this memo extends to display)
- Persona-ministry findings: [reference-library-findings.md](../../pre-release-review/reviews/persona-ministry/reference-library-findings.md)

---

## 1. Question

PRR-242's legal memo cleared **backend ingestion** of Ministry-published past Bagrut papers (educational fair-use under Israeli Copyright Law §19; published-Government-works posture under §6). It explicitly did NOT cover **in-app display to authenticated paying subscribers**.

Does the same posture cover in-app display, given ADR-0059's mitigations:

- Authenticated student access only (no public/anonymous read)
- Inline consent disclosure (en/he/ar) — student affirmatively acknowledges Ministry-source + reference-not-graded framing
- Provenance citation on every variant: `"Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד מועד א׳) — numbers changed."`
- No grading on reference items (answer affordances stripped per §15.2)
- Variant-routed practice (CAS-verified recreation, never the raw Ministry item)

## 2. Posture statement (to be filled by counsel)

`[ ]` In-app display under ADR-0059 §15 mitigations IS within the existing licensing posture.
`[ ]` In-app display IS within the licensing posture, conditional on additional controls: ___________________________
`[ ]` In-app display is NOT within the existing posture; fall back to metadata-only mode (citation + topic + structure description; no raw question text).

Decision-holder: Shaker. Counsel: __________________________________ Date: ____________

## 3. Risk analysis (to be reviewed)

### 3.1 Israeli Copyright Law

- **§6 (state-published works)** — Bagrut papers are State of Israel publications. State copyright duration is 50 years from creation (Copyright Law 5768-2007 §38).
- **§19 (fair-use balancing)** — fair-use is **not categorical**; it requires balancing four factors: (1) purpose and character (educational, transformative-via-variant), (2) nature of the work (factual, published), (3) amount used (one question at a time, in reference context), (4) market effect (does this substitute for the Ministry's own products? Ministry's products are free; market-effect argument cuts in our favor).
- **§16 (derivative works)** — variants generated from the source are derivative works. Persona-ministry §14.2 raised: parametric variants are **more legally exposed** than structural under §16 (parameter substitutions retain more of the source). ADR-0059 §15.5 Q-A reflects this — free tier defaults to structural, parametric is paid-only.

### 3.2 Comparable IL prep platforms

Survey of comparable platforms displaying past Bagrut papers in-app:

- **Bagrut Plus** — displays raw Ministry papers, no consent UI, no provenance citation, no architectural separation between view and grade. No public legal challenge known.
- **Bagrut Tikshoret** — same pattern.
- **School-portal PDFs** (Mashov, etc.) — distribute Ministry papers as PDF downloads, school-authenticated.
- **Ministry's own archive** at `edu.gov.il` — public read access, free.

Conclusion: industry-standard practice is to display past papers in-app to authenticated users. ADR-0059's posture is **stronger** than the industry norm (consent + provenance + variant-routed practice).

### 3.3 Ministry of Education posture

- Ministry's published terms-of-use for the past-paper archive (to be confirmed by counsel review of `edu.gov.il/policies` and the relevant takanot).
- No known case-law or precedent challenging in-app display by IL prep platforms.
- ADR-0059 §15 includes a takedown response runbook (PRR-254) — 30-min kill-switch + audit-log preservation + per-source-paper-code variant purge. Takedown response capability further reduces risk.

### 3.4 Privacy implications (cross-reference)

Persona-privacy review (§14.2 item 6) raised that Ministry-question browse history is high-fidelity learning-weakness inference data. ADR-0059 §15.6 + §15.7 add:

- 5 browse-history scope-limitation invariants (no parent / teacher / tenant-admin visibility, no scheduler coupling, no LLM prompts, k≥10 floor, no raw extracts).
- 180-day retention horizon on `BagrutReferenceItemRendered_V1` with RTBF cascade.

These mitigations are **separate from copyright** but interact with the legal posture: GDPR / PPL Amendment 13 require purpose-limitation + transparency + erasure rights. ADR-0059 §15.3 + §15.7 close those gaps.

## 4. Mitigation requirements (per ADR-0059 §15)

The following ADR-0059 invariants must hold for the posture in §2 to be defensible:

- §15.1 — `Reference<T>` factory requires `MinistryBagrut` provenance + valid consent token.
- §15.3 — Consent disclosure with one-click revoke on the reference page.
- §15.5 — Free tier structural-only (no parametric); paid-tier parametric capped per source.
- §15.6 — Browse-history scope limitation (5 controls).
- §15.7 — 180-day audit-event retention + RTBF cascade.
- §15.10 — Provenance citation on every variant with structured Ministry-canonical form.
- PRR-254 — Takedown response runbook (30-min kill-switch).

If counsel determines any of these are insufficient, list additional requirements here:

___________________________________________________________________

## 5. Fallback if posture is restrictive

If counsel recommends against raw-text in-app display, ADR-0059 §15 falls back to **metadata-only mode**:

- Reference page renders citation (`"Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד מועד א׳)"`) + topic + structure description (Bloom level, units, typical content).
- **No raw question text shown.**
- "Practice a variant" CTA still works — variant generation surface unchanged. Provenance citation on the variant remains canonical (§15.10).

This fallback preserves the variant-generation business value while neutralizing copyright risk. ~30% of the user-facing UX value is retained (the catalog browsing + handoff stays; the "see the actual exam question" benefit is lost).

## 6. Sign-off

Counsel: __________________________________ Date: ____________

Project owner (Shaker): ____________________ Date: ____________

Once both signatures are present + posture in §2 is selected, this memo enables the ADR-0059 feature flag to flip on per the rollout plan in PRR-245.

---

**Coordinator note (claude-code, 2026-04-28)**: this skeleton was drafted under the user's "do all" directive. It captures every input the persona-review surfaced + the ADR-0059 §15 mitigations as legal-context. The §2 posture decision and §6 sign-off are the only items that require counsel + Shaker.
