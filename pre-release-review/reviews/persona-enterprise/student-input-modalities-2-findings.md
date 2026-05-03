---
persona: enterprise
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: yellow
---

## Summary

The 002-brief adds two more tenancy-policy surfaces on top of the photo-upload one we flagged in the 001-brief review — classroom-enforced redaction (3.3) and tenant-level writing-pad enablement (implied by 4, explicit ask in 6.5) — and it layers HWR vendor choice on top, which is where FedRAMP / data-sovereignty schools will kill the deal if we procure wrong. Three more capability flags, same `TenantPolicyOverlay<T>` pattern if we keep our discipline. If we don't, we ship four parallel policy models and then one retrofit per flag the first time a US district asks for a BAA or a Gulf tenant asks where stroke data lands. This is the same Phase-2-blocked critical path as 001. Same fix: commit to the overlay now, ship the schemas empty, wire UIs later.

## Section 6.5 answers

**Q1 — Section 3.3 classroom-enforced redaction: wire through PRR-236?**

Yes, same answer and same plumbing as the `DefaultAttemptMode` recommendation from the 001-brief review (PRR-249). The server-side redaction toggle is a **classroom capability**, not a tenant capability and not a student-unbounded capability. Shape: `Classroom.EnforceOptionRedaction: bool` (defaults false, inherited from `TenantSettings.AllowEnforcedRedaction` — because some tenants will want to ban teachers from enforcing redaction, e.g. diagnostic-heavy assessment tenants). Teacher turns it on in PRR-236 classroom settings → session-start handshake pins the policy on the session → `GET /question/:id` returns the redacted projection (stem without options) until a `POST /question/:id/reveal` call → reveal event is audited (Source=Classroom, AssignedById=teacher, per ADR-0050). The redacted projection is **a separate API shape**, not a view-model flag the client can ignore; 001-brief redteam was right that UI-hiding-only is theater, and 002-brief confirms it.

Wire this through the same `TenantPolicyOverlay<T>` base as PRR-248. Resolution order: tenant-deny-wins → classroom-enforce-or-not → student-toggle (only if neither tenant nor classroom has enforced a mode). Do NOT add a fourth resolution layer for per-question override (3.6) — that's author-time metadata on the question, not a policy overlay layer. Keep the overlay stack at three layers max or we recreate the policy-override-hell we're trying to avoid.

**Q2 — Tenant-level "allow writing pad: true/false": same overlay pattern?**

Same overlay pattern, yes. Shape: `TenantSettings.WritingPadPolicy: WritingPadPolicy` (`Enabled | Disabled | AllowedWithClassroomOverride`), mirrored on `Classroom.WritingPadPolicy: Inherit | Disabled`. Student-level toggle inside those bounds is the session-mode question. The policy gate happens at **capability render time** (is the pen tool visible in the toolbar?) and at **submit time** (does the server accept stroke-data uploads on this session?), double-gated for the same reason photo-upload is double-gated: client-side gating is advisory, server-side is authoritative.

One nuance the 001-brief review missed and 002 forces into view: tenant policy is not binary "writing-pad yes/no." It's **per-modality**, because a tenant might allow writing-pad for math (paper-exam fidelity per persona-ministry's PET concern) but disallow it for chemistry (stroke-data + Lewis-structure HWR pipes into LLM-vision which the tenant's procurement says no to). Generalize to `WritingPadPolicy` as a **set** keyed by subject family: `{Math: Enabled, Physics: Enabled, Chem: Disabled, Language: NotApplicable}`. Same overlay base class, richer value type. Don't ship a single boolean and then retrofit per-subject six weeks later.

**Q3 — HWR vendor selection per-tenant for FedRAMP / data-sovereignty schools?**

Required, and this is the biggest enterprise risk in 002. The three HWR paths in 4.6 have radically different procurement profiles:

- **Claude Sonnet vision (MSP-reuse)**: stroke images go to Anthropic. For FedRAMP Moderate tenants, Anthropic is not in the authorization boundary today; for EU / Saudi data-residency tenants, routing student handwriting through us-east-1 Anthropic is a DPA-breaking event. Convenient for the product team, **unshippable** for half the enterprise pipeline.
- **MyScript / iink.js (client-side)**: zero network exfiltration, zero vendor DPA needed, works offline. Procurement-clean. Licensing recurring but predictable. **Preferred default** for privacy-sensitive tenants.
- **Mathpix / Google Cloud Vision (server-side vendor)**: each vendor is a separate BAA/DPA negotiation. Google has FedRAMP High; Mathpix does not as of last check. Cena procurement overhead multiplies.

Shape: `TenantSettings.HwrVendorPreference: HwrVendor` (`Default | ClientOnly | ServerVendor:<name> | LlmVision:<model>`), defaulting to `Default` (which resolves to whichever vendor finops + SRE pick globally). Tenant admin UI exposes `ClientOnly` as the privacy-default option, and FedRAMP tenants get a vendor allowlist constrained to client-only + Google-FedRAMP-High. **Do not default to LLM-vision** — it's the most capable and the least procurement-safe; make tenants opt into it explicitly.

This is not the same overlay type as PhotoUploadPolicy — it's a **procurement preference** not a **capability gate** — but it should sit on the same `TenantSettings` aggregate and carry the same version-pin-on-session semantics. A mid-session vendor swap is as bad as a mid-session policy swap.

**Q4 — Feature-flag shape: same `TenantPolicyOverlay<T>` as 001-brief?**

Yes for the capability gates (writing-pad, redaction, photo — all three are "can the student invoke this affordance"). No for vendor selection — that's procurement metadata, not a capability gate. Model it as `TenantSettings.Procurement` with its own value-object stack but piggyback on the overlay infrastructure for versioning, audit, and session-pinning. One overlay base class, two extension points.

## Section 7 positions

**#1 Q2 default state (3.1):** visible-first with opt-in hide. Hidden-first is dark-pattern-adjacent — paternalism without consent. Student chooses per-session to self-test; teacher default via PRR-249 can flip the class to hidden-first with explicit "your teacher set this" copy. That's the consent boundary.

**#2 Q2 server-side enforcement (3.3):** classroom-only, wired through PRR-236 / PRR-249. Never student-unbounded server-enforcement (weird UX — student enforces redaction on themselves via API?), never "always optional" (theater, redteam is right). Three states: off, student-toggled (UI-only), teacher-enforced (server + UI). No fourth state.

**#3 Q2 commit-and-compare flow (3.7):** ship it, narrow. Only for MC items where a parseable short-answer equivalence check is cheap (numeric, single expression, single MC letter guess). Not for free-text comparisons against MC options — that's a CAS call and a cost spike for dubious pedagogy gain.

**#4 Q3 math modality (4.1):** writing-pad primary + MathLive secondary for Bagrut Math and PET quant, per persona-ministry's paper-fidelity argument. **Enterprise corollary**: this is the modality that forces the HWR procurement question. Do not ship writing-pad-primary math without the HwrVendorPreference overlay in place.

**#5 Q3 chem modality (4.3):** typed-primary for reactions + stoichiometry, writing-pad secondary for Lewis. Agrees with brief. Lower HWR volume → lower procurement pressure → chem can ship with client-side HWR only.

**#6 Q3 language modality (4.4):** keyboard-only, confirm. No HWR, no procurement question, per-locale keyboard is a content+a11y issue not enterprise.

**#7 Q3 HWR procurement (4.6):** **client-side default, server-vendor as tenant opt-in, LLM-vision explicitly not default.** The brief's "LLM-vision is compelling" framing is procurement-naive — compelling for engineering, toxic for enterprise sales.

**#8 Section 5 cap:** the $5.20/student/month overshoot is a client-side-HWR problem. Client-side HWR = zero per-call cost. The cap problem disappears if we pick correctly in #7. If tenants opt into server-vendor HWR, bill them for the differential — premium-SKU path, same model as photo-upload rate limits (PRR-251).

## Recommended new PRR tasks

1. **PRR-252 — `WritingPadPolicy` per-subject value object on `TenantSettings` + Classroom overlay.** Extends PRR-248 (`TenantPolicyOverlay<T>`) with a non-boolean value type. Schema + resolver + session-pin. UI deferred. Owner: human-architect (ADR needed for per-subject shape). Priority P0 — ships with writing-pad modality.
2. **PRR-253 — `Classroom.EnforceOptionRedaction` + server-side redacted-question projection + `POST /question/:id/reveal` endpoint.** Extends PRR-249. Includes audit event (Source=Classroom per ADR-0050). Owner: kimi-coder. Priority P1.
3. **PRR-254 — `TenantSettings.HwrVendorPreference` + procurement-allowlist enforcement.** Schema, resolver, session-pin, FedRAMP allowlist constants. Default = `ClientOnly`. Owner: human-architect. Priority P0 — blocks HWR vendor procurement decision.
4. **PRR-255 — Per-tenant writing-pad + HWR usage dashboard (finops surface per tenant per subject).** Extends PRR-251 pattern. Owner: kimi-coder. Priority P2 for Launch, P1 before enterprise GTM.

## Blockers / non-negotiables

- **Blocker (procurement):** PRR-254 must land before any tenant sees a writing-pad modality. Shipping writing-pad-primary math with hardcoded LLM-vision HWR is a DPA-breaking event for every non-US tenant and a FedRAMP-failing event for every US district. **Do not ship the modality before the overlay.**
- **Non-negotiable (ADR-0050):** teacher-enforced redaction events carry `Source=Classroom, AssignedById=<teacher>`. Reveal-endpoint audits same. No second provenance model.
- **Non-negotiable (labels match data):** if tenant `WritingPadPolicy.Math = Disabled`, the pen-tool affordance must not render — not grey, not disabled-tooltipped, absent. Same rule as photo-upload button from 001-brief review.
- **Non-negotiable (no-stubs):** an empty `HwrVendorPreference` overlay is fine (resolves to global default); a tenant admin screen that shows a vendor picker but doesn't persist is a stub and fails the 2026-04-11 ban.

## Questions back to decision-holder

1. `WritingPadPolicy` default for existing tenants: `Enabled` for all subjects (opt-out, fastest GTM) or `Disabled` for Chem (opt-in, because Lewis-structure HWR is the riskiest procurement path)? Suggest per-subject default: Math/Physics Enabled, Chem Disabled-pending-vendor-decision, Language N/A.
2. Does `HwrVendorPreference` inherit cross-tenant for a student enrolled in two tenants? Suggest: **most-restrictive-vendor-wins**, same least-privilege principle as photo-upload policy from 001-brief Q4.
3. Who owns the FedRAMP allowlist constants — product, security, or tenant admin? Suggest: security owns the list, product gates the UI, tenant admin picks from the list. Same pattern as data-residency region allowlist.
4. Is LLM-vision HWR ever allowed as tenant opt-in, or globally banned? If ever-allowed, which Anthropic endpoint (Bedrock-fronted for FedRAMP Moderate tenants)? This is a real architectural decision, not a config knob.
