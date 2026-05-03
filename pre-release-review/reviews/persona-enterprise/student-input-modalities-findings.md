---
persona: enterprise
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: yellow
---

## Summary

Three modalities, three different tenancy-policy shapes, and the brief treats tenant controls as a footnote under 7.5. Photo-upload without a tenant-level kill switch will not ship into any Israeli religious school, any exam-strict private institute, or any Saudi tenant day one. Hide-then-reveal without teacher wiring means EPIC-PRR-C classroom workflow quietly fails to drive the one pedagogy lever that costs nothing to build. Humanities rubric grading without a per-tenant override means Cena's opinion on how a Tanakh essay gets scored will silently overrule a Literature teacher's departmental rubric — and that is a sales-losing moment the first time a principal sees it. All three gaps sit on the same Phase-2-blocked critical path (ADR-0001 tenancy) that already bit us on the catalog. Fix the shapes now, defer the UIs, do not retrofit after Phase 2 unblocks.

## Section 7.5 answers

**Q1 — Tenant-level policy to disable photo upload. Feature-flag granularity: per-tenant, per-enrollment, per-session?**

All three, composed. Per-tenant is the hard gate (school bans phones, religious institute bans cameras on students, Saudi tenant bans image capture of minors entirely). Per-enrollment is the soft override (a tenant that allows photo in general but a specific classroom — say, a matriculation-exam simulation class — turns it off). Per-session is already implicit in the `AttemptMode` work and should stay student-controllable within the bounds the tenant and classroom allow. The resolution order is **tenant-deny wins, then classroom-deny, then student-choose**. Never the other way. Never let a student toggle past a tenant lockout. Implementation shape: a `PhotoUploadPolicy` value object on `TenantSettings` (`Enabled | Disabled | AllowedWithClassroomOverride`), mirrored on `Classroom` (`Inherit | Disabled`), evaluated at session-start and re-evaluated on each upload attempt (tenants change policy mid-year, classrooms change mid-term; a cached client-side flag is a bug).

**Tenant-override shape — similar to PRR-220 catalog overlay?** Yes, same pattern, and this is not a coincidence — it's the pattern for every tenancy decision we haven't made yet. `GlobalDefault + TenantOverlay + ClassroomOverlay`, each overlay additive/subtractive with explicit resolution order, overlay version pinned on the session so mid-session policy changes don't surprise. One difference: catalog overlay is about **content availability** (what exam tracks exist), photo-upload overlay is about **capability availability** (what features the student can invoke). Model them as the same base class — `TenantPolicyOverlay<T>` — and you can reuse the plumbing for every future tenant-policy lever (voice input, camera, LLM tier preference, third-party integrations). Do this once. Retrofitting a second policy-overlay shape six months after PRR-220 ships is the kind of architectural drift that kills enterprise sales.

**Q2 — Teacher/class-wide default for hide-then-reveal. Wire through PRR-236 classroom UI?**

Yes. This is the single highest-leverage classroom-admin feature in the brief and it is currently invisible. Teacher workflow: in PRR-236 classroom settings, a "Default attempt mode for this class" dropdown with three values: `StudentChoice` (default), `HideOptionsFirst` (generation-effect default-on), `ShowOptionsAlways` (for low-confidence cohorts). This is NOT paternalistic option-C from the brief — the student still controls their own session within the teacher's default, and the teacher's choice is **observable and explainable** ("your teacher set this class to try-first mode; you can toggle per-question"). The distinction between teacher-set-default and pedagogy-opaque-algorithm matters: one is classroom agency, the other is ship-gate-banned dark pattern territory. Schema: add `Classroom.DefaultAttemptMode: AttemptMode` alongside `Classroom.DefaultExamTargetTemplate` from PRR-219. Same plumbing, same event envelope, same audit coverage (PRR-220).

Crucially: teacher default is a **default**, not a lockout. A student who needs options visible for accessibility reasons (a11y lens will file this) must be able to override. Treat teacher-set-default as a starting state, not a mode-prison.

**Q3 — Per-tenant rubric override for humanities. Which Cena rubric, which school rubric, who wins?**

Three-layer rubric resolution, and this is where the brief is dangerously underspecified. Layer 1 is **Cena's base rubric** (PRR-033 DSL, CAS-gated where mechanical, LLM-graded where semantic — and the LLM grading there is a real honesty risk per 8.1 of the brief). Layer 2 is **tenant rubric overlay** — a school that teaches Literature with a specific essay-structure requirement (intro-body-conclusion vs Israeli dialectical format vs British thesis-defense) overrides Cena's structural weights. Layer 3 is **classroom-level rubric tweak** — a specific teacher emphasizes textual-evidence-density over thematic-analysis this semester. Who wins: **most-specific layer wins per criterion**, not per rubric. A classroom override of "evidence weight = 0.4" replaces the tenant's 0.3 replaces Cena's 0.25 — but classroom silence on "grammar weight" falls through to tenant's value. Full-rubric-replacement is wrong (loses Cena's CAS-gated correctness layer entirely); per-criterion override is right.

Non-negotiable: the rubric **resolution output** must be explainable to the student. "Your grade on this essay used your teacher's evidence-weight criterion (0.4) and your school's structure criterion (intro-body-conclusion); all other criteria used Cena's defaults." That's not just UX politeness — it's appeals-path infrastructure (persona-ethics will also flag this).

## Section 8 positions

**#1 Q1 framing: narrow or broad?** Narrow. Broad is a finops and moderation disaster and there is no tenant-policy shape that makes broad safe. "Diagnose-wrong-answer only" is the only framing that keeps the tenant-override simple (disable the affordance, no orphan entry points).

**#2 Q2 implementation: A, B, or C?** B plus teacher-default-via-PRR-236, per Q2 above. Not C. Pedagogy-opaque hide-reveal is paternalistic and scans as dark pattern to every reviewer.

**#3 Q3 architecture: shared or per-subject?** Shared `FreeformInputField<T>` with per-subject renderer adapters. Per-subject standalone components is four times the tenant-policy surface area for no tenant-visible benefit.

**#4, #5 Chem/humanities launch scope:** MC-only is a lying-label violation. A "Bagrut Chemistry" product that can't accept chemistry input is not Bagrut Chemistry; it's Bagrut Chemistry Trivia. Slip Chem and Humanities from Launch rather than ship degraded.

**#6 $3.30 cap:** Q1 narrow-framing + Q3 with a teacher-initiated-only rubric-grader (not student-unbounded) fits the cap. Broad Q1 does not. Persona-finops will have the exact numbers; enterprise position is that the tenant-policy knob exists partly so high-cost tenants pay for the cost differential — a "vision-input premium" tier is a legitimate enterprise SKU.

## Recommended new PRR tasks

1. **PRR-248 — `TenantPolicyOverlay<T>` base model + PhotoUploadPolicy first implementer.** Generic overlay infrastructure usable for future capability flags. v1 ships with `PhotoUploadPolicy` and defaults to `Enabled` for all existing tenants; tenant admin UI deferred to v2. Owner: human-architect. Priority P0 — shipping photo-upload without the overlay is a Phase 2 retrofit on every tenant.
2. **PRR-249 — `Classroom.DefaultAttemptMode` + PRR-236 UI wiring.** Schema, events, PRR-236 dropdown, audit via PRR-220. Owner: kimi-coder. Priority P1.
3. **PRR-250 — Three-layer rubric resolver (Cena base + tenant overlay + classroom overlay), per-criterion merge.** Schema for overlay storage, resolver service, student-facing explain-my-grade surface. Owner: human-architect (ADR needed) then kimi-coder. Priority P1 — blocks humanities launch.
4. **PRR-251 — Tenant-scoped photo-upload rate limits + finops visibility per tenant.** Per-tenant daily/monthly photo-upload caps surfaced to tenant admin, billable. Extends PRR-018 rate-limit pattern. Owner: kimi-coder. Priority P2 for Launch but P1 before broad enterprise GTM.

## Blockers / non-negotiables

- **Blocker (ADR-0001 alignment):** photo-upload endpoint must enforce tenant policy server-side on every request, not via client-hidden UI. A tenant-disabled policy with a missing server check is an indefensible compliance bug. Current `/api/photos/upload` predates the policy shape — audit required before modality ships.
- **Non-negotiable (ADR-0050 Source discriminator):** any rubric override or attempt-mode default authored by a teacher must carry `Source=Classroom` with `AssignedById`, same plumbing as `ExamTarget`. Do not invent a second provenance model for policy overlays.
- **Non-negotiable (no-stubs):** an empty tenant overlay is fine; a "tenant admin UI coming soon" screen that doesn't persist settings is a stub and fails the 2026-04-11 ban. Ship the schema empty or ship it fully; do not ship a lying UI.
- **Non-negotiable (labels match data):** if the tenant disables photo upload, the student-side "Upload photo of your work" button must not render, not merely be disabled-grey. A visible-but-dead button is a lying label to the student.

## Questions back to decision-holder

1. Does the tenant-policy overlay for photo-upload default to **enabled** (opt-out, Israeli public schools get it free) or **disabled** (opt-in, requires positive tenant admin action)? Enterprise-sales answer is opt-in-per-tenant; product-growth answer is opt-out. Pick one before PRR-248 starts.
2. For the three-layer rubric resolver — does the tenant admin or the classroom teacher own the "which criteria can be overridden at classroom level" policy? (Suggest: tenant admin defines the set of overridable criteria; teacher can move the weight within that set. Prevents teacher-level rubric-drift.)
3. Can a student see their teacher's attempt-mode default **before session start**, or only when the first question renders? (UX and consent question — suggest before-start, with "your teacher has set this class to try-first mode" copy.)
4. Cross-institute students (one account, two tenants, both running different photo-upload policies) — whose policy applies? (Suggest: most-restrictive wins, per least-privilege; align with ADR-0001 Phase 2 work.)
