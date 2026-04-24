# STUDENT-INPUT-MODALITIES-001 — How does the student actually answer? Persona Discussion Brief

> **Status**: Discussion draft — pre-ADR, pre-task-split.
> **Decision captured (2026-04-21)**: Pending. Three product questions framed below; decision-holder wants a 10-persona review before locking scope.
> **Scope**: The *modalities* available to a student when working a problem in Cena — handwritten work photo-diagnosis, hide-then-reveal multiple-choice, and free-form typed input across math / physics / chem / language.
> **Not in scope**: Exam-target selection (locked in [ADR-0050](../adr/0050-multi-target-student-exam-plan.md)), diagnostic block sizing (locked in [PRR-228](../../tasks/pre-release-review/TASK-PRR-228-per-target-diagnostic-blocks.md)), content-engineering scope (EPIC-PRR-G).
> **Next**: personas file findings; synthesize; resolve open product questions; draft tasks.

---

## 1. Problem statement

Today a Cena student works a problem by (a) reading the stem, (b) picking a multiple-choice option, OR (c) entering math via MathLive + the step-solver (PRR-206, CAS-gated per ADR-0002). That's it. Three real student behaviors are not supported:

1. **Working on paper first, then getting wrong on the MCQ** — student has pencil-and-paper work that shows *where* they went wrong. Cena can OCR that work for diagnosis; infra exists; product does not.
2. **Wanting to attempt without seeing distractors** — the generation effect (Slamecka & Graf 1978, Bjork) is real pedagogy. A student who sees the answer options before attempting is anchored by them; hiding until attempt preserves the desirable difficulty.
3. **Subjects beyond math** — physics (partial), chemistry (none), natural language Bagrut sections like Literature / History / Civics / Tanakh (rubric DSL exists but student-facing freeform UX is unspecified).

## 2. Current codebase state (evidence)

| Capability | Status | Evidence |
|---|---|---|
| Photo/PDF upload endpoint | **Exists** | [PhotoUploadEndpoints.cs](../../src/api/Cena.Student.Api.Host/Endpoints/PhotoUploadEndpoints.cs) — `/api/photos/upload`, 20 MB cap, EXIF-stripped (PRR-001), CSAM + AI-safety moderated (RDY-001), OCR Layers 0-5 (ADR-0033) |
| Student-work diagnostic use case | **Researched but not productized** | 10-iteration autoresearch at [docs/autoresearch/screenshot-analyzer/](../autoresearch/screenshot-analyzer/) — privacy, prompt-injection-via-OCR, CSAM-moderation-for-minors, rate limiting, error handling — never made it to a feature spec or UX |
| MathLive free-form math input | **Exists, mature** | `useStepSolver.ts` (PRR-206), MathLive installed in student SPA, CAS-gate (ADR-0002), productive-failure path `phase='explored'` |
| FBD (free-body diagram) physics input | **Exists, partial** | PRR-208 wire-fbd-construct-for-physics |
| Symbolic physics beyond FBD (kinematics expressions, circuits, energy) | **Unverified** | Likely routed through MathInput with physics constants; not explicitly documented |
| Chemistry equation / reaction input | **Does not exist** | No component, no task |
| Natural-language freeform answer grading | **Partial** | Rubric DSL (PRR-033) exists for Bagrut; student UX for long-answer entry not specified |
| Hide-then-reveal answer-options mode | **Does not exist** | No `AttemptMode` concept, no composable, no UI toggle |
| Session-level `AttemptMode` / input-modality selector | **Does not exist** | Session runner assumes a single modality per question |

## 3. Question 1 — Photo-of-paper-work upload for error diagnosis

### Flow

Student picks wrong MC option → Cena offers "Upload a photo of your working?" → student snaps pencil-and-paper work → OCR + vision analysis → diagnostic response ("I see you factored correctly but made a sign error on line 3") → student corrects.

### Two framing choices

- **Narrow framing**: photo ONLY from the "I got this wrong and want to understand why" affordance. Restricted to the open question's context. Auto-expires after session. Moderation bounded.
- **Broad framing**: photo upload available anywhere — "ask the tutor about anything". Open-ended. More moderation surface, more prompt-injection-via-OCR risk (autoresearch iteration-02 flagged this explicitly).

### What we'd need to build (either framing)

- Student UX: upload trigger, moderation-fail copy, OCR-progress feedback, diagnostic-response card.
- Backend: student-work OCR → vision-model → misconception extraction pipeline. Per ADR-0003 all misconception data is session-scoped (30 day retention max).
- LLM routing: ADR-0026 tier selection (this is likely Tier 3 Sonnet/Opus — high-complexity vision task).
- Rate limiting: already in endpoint; may need per-session + per-student caps.
- Privacy: minors + CSAM moderation (autoresearch iteration-04), no face-capture (photo of paper, not selfie), EXIF-stripped (PRR-001).

### Cost concern (finops)

Vision model calls are expensive. If broad framing + unbounded use, estimated cost per student per month could easily exceed the $3.30 LLM ceiling from [ADR-0050 Q5](../adr/0050-multi-target-student-exam-plan.md#q5).

## 4. Question 2 — Hide-then-reveal answer choices, "try on paper first"

### Flow

Student sees a problem stem, no answer options visible. Student attempts mentally or on paper. When ready, clicks "Show my options" → MC options reveal → student selects.

### Why

- Generation effect: Slamecka & Graf (1978), Bjork's desirable difficulty. Retrieval is stronger when not anchored by a visible answer set.
- Persona-cogsci: this is the single highest-value pedagogy lever that requires zero new content.
- Persona-ethics: zero dark-pattern surface (student-controlled; no forcing).

### Three implementation choices

| Option | Who decides | Default | Behavior |
|---|---|---|---|
| **A. Per-question author-set** | Item author | As authored | Questions flagged `reveal_on_request` start hidden; others show normally |
| **B. Per-session student toggle** | Student (at session start) | Off (current behavior) | One toggle; applies to all MC questions in that session |
| **C. Per-target pedagogy-driven** | Scheduler | Based on skill mastery | Low-confidence students see options; high-confidence see hidden |

### Tradeoffs

- **A** is the lowest effort + most author-control but might not surface the generation benefit broadly.
- **B** maximizes student agency + supports variable-difficulty preference + lowest UX surprise.
- **C** is pedagogically optimal but opaque to the student, and may feel punitive ("why am I getting the hard version?").

## 5. Question 3 — Free-form input across math / physics / chem / language

### Current gaps

- Math: ✅ (mature).
- Physics: ⚠️ FBD exists; need to verify kinematic/electromagnetic/energy expressions route through MathInput cleanly.
- Chemistry: ❌ no reaction/equation input; no balancing tool; no per-mol calculator UI.
- Natural language (Bagrut Literature, History, Tanakh, Civics, Hebrew Language): ❌ rubric DSL exists but student-facing long-answer entry is undefined.

### Scope question

Does every subject need a dedicated input component, or is there a shared abstraction?

- Shared abstraction (per-subject adapter): one `FreeformInputField<T>` + per-subject renderer + per-subject CAS or rubric grader. DRY, smaller UX surface area. Harder to optimize per-subject.
- Per-subject components: 4+ components with own affordances. Bigger UX surface, but each can tune for its discipline (chem can have ring-draw tools, language can have paragraph-length tracking).

### Content-engineering coupling

EPIC-PRR-G commits to Bagrut Chemistry + Humanities item banks at Launch. Without input UX, those items can only render as MC — losing the primary value of free-form thinking. If we launch with MC-only for chem + humanities, we're shipping degraded pedagogy despite having "full content".

## 6. Overlap & shared concerns

- **Moderation** (Q1 photo, Q3 freeform-text): every user-submitted freeform content needs CSAM-like moderation for minors (autoresearch iteration-04), PII-scrubbing before LLM (PRR-022), and rate limiting (PRR-018 pattern).
- **LLM cost** (Q1 vision, Q3 rubric-grading): both push toward Tier 3 Sonnet/Opus. Finops SLO (PRR-233) must extend to cover.
- **RTBF** (all three): student work + free-form answers + rubric feedback become new categories needing PRR-003a / PRR-223 RTBF cascade coverage.
- **Accessibility** (all three): photo capture + free-form entry across modalities has a11y implications persona-a11y must bless.

## 7. Persona-lens discussion prompts

Each lens files findings under `pre-release-review/reviews/persona-<lens>/student-input-modalities-findings.md`.

### 7.1 persona-educator
- Does Q1 photo-diagnosis match how teachers actually use "show your working" in class? Does it preserve the student's cognitive ownership of the mistake, or does it turn into a crutch?
- Q2 hide-then-reveal — which of A/B/C does the classroom workflow want? Do teachers want to lock students into option B "try first" mode as a class-default?
- Q3 — for Literature / History / Tanakh, what's the realistic teacher-graded vs. Cena-graded split? Can we honestly claim CAS-gated correctness for any non-math free-form answer?

### 7.2 persona-cogsci
- Generation effect (Q2) — effect size literature. Is this worth building if students just skip the "try first" button? Recommend default state.
- Q1 photo-diagnosis — does external diagnosis degrade the metacognitive self-diagnosis process we want to cultivate? When is scaffolding helpful vs. when does it replace needed struggle?
- Q3 — chem reactions as a symbolic-manipulation task (Taber's conceptual change literature). Is shared MathInput abstraction sufficient or do chem conceptions need their own input representation?
- Recommend: per-question modality, per-student, per-target — which dimension wins?

### 7.3 persona-ethics
- Q1 photo-upload from minor: consent surface required? Does implicit consent-by-action (they chose to upload) suffice, or do we need an explicit first-time modal?
- Q2 pedagogy-driven hide-then-reveal (option C) — can be paternalistic. Is it a dark pattern to hide options based on mastery without telling the student?
- Q3 rubric-graded free-form text — if LLM rubric grader is inconsistent or biased, does that surface as injustice? What's the appeals path?
- Review the autoresearch/screenshot-analyzer controversy iterations — what's the ethics position now vs. then?

### 7.4 persona-a11y
- Q1 photo: students with motor impairments can't easily take a steady photo; alternative input? Students with no smartphone (desktop-only users)?
- Q2 hide-then-reveal: keyboard-only flow to trigger the reveal; SR announcement pattern; reduced-motion respect on the reveal transition.
- Q3 chem / language input: cognitive load, DFA-reader support, RTL language-native input (Arabic literature, Hebrew language), screen-reader compatibility with MathLive fallback.

### 7.5 persona-enterprise
- Q1 photo-upload: tenant-level policy controls? Some schools ban phones in exam prep contexts. Can we respect a tenant override to disable photo entirely?
- Q2: class-wide default set by teacher (ties to EPIC-PRR-C parent-aggregate + PRR-236 classroom UI)?
- Q3: multi-institute — does one school's grading rubric apply in another's tenancy? Per-tenant rubric override?

### 7.6 persona-ministry
- Q1 photo-OCR of student work: does this create a chain-of-custody question for anything that could later be used in Ministry reporting? (Short answer: probably no — it's session-scoped per ADR-0003 — but verify.)
- Q3 chem / humanities input: does the input format match Ministry exam format? Bagrut Chem has specific notation conventions; Bagrut Literature has specific essay structures. Misalignment = we're training for the wrong exam.
- For Psychometry (PET) quantitative: does PET-format input (on-paper scratch, MC-select) match Cena's input? Or are we training a different habit?

### 7.7 persona-sre
- Q1 photo: vision-model latency + availability. Outage runbook. What does the student see when vision model is degraded? Session blocking or graceful skip?
- Q1 rate limiting: per-student photo-upload caps. Spike handling during Bagrut study windows.
- Q3 rubric-grading latency: async grading with "checking later" UX (like awaiting_cas) vs. sync blocking. Queue monitoring.
- Storage growth: retention for photos is 30 days (session-scope) but binary storage is expensive. Archival strategy.

### 7.8 persona-redteam
- Q1 photo-upload is the single highest prompt-injection-via-OCR risk (autoresearch iteration-02). Attacker writes "ignore previous instructions" on paper, photographs it, uploads. Current mitigations?
- Q1 CSAM-via-paper: paper can have drawings — moderation model needs to catch this.
- Q3 freeform text input: classic LLM prompt-injection surface. Is rubric-grader isolated per PRR-022?
- Q2 hide-then-reveal: can a scripted client bypass the "hidden" state to get all questions with answers?

### 7.9 persona-privacy
- Q1 photo: retention policy beyond the 30-day session scope? Can a photo be re-used for item authoring training data? (Should not be — PRR-022.)
- Handwriting in a photo is biometric-adjacent — is it covered under PPL Amendment 13 biometric protections?
- Q3: student-authored freeform answers in LLM prompts — PRR-022 scrubbing applies; verify it catches handwritten-name-in-margin cases in Q1.
- RTBF cascade must cover photo storage + vision-model output.

### 7.10 persona-finops
- Q1 vision model cost: ballpark per photo call. If students average 5 photo-diagnoses per month × $0.10 each = $0.50/student/month. At 100k paying students = $50k/month. Validate the order of magnitude.
- Q3 rubric-grading per long-answer: per-item LLM cost + cache hit rate (lower than MC since free-form is unique per student). SLO threshold?
- Overall budget delta: does Q1+Q3 push us through the ~$3.30/student/month cap locked in ADR-0050 Q5?

## 8. Open product questions back to decision-holder

Items that did NOT converge pre-review and need your call:

1. **Q1 framing**: narrow (diagnose-wrong-answer only) or broad (ask-about-anything)?
2. **Q2 implementation**: A (per-question author-set), B (per-session student toggle), or C (per-target pedagogy-driven)?
3. **Q3 architecture**: shared `FreeformInputField<T>` abstraction or per-subject components?
4. **Q3 chem Launch-scope**: does Bagrut Chemistry launch with MC-only (degraded) if chem input UX doesn't make it in time? Or does chem slip from Launch?
5. **Q3 humanities Launch-scope**: same question — humanities with MC-only at Launch, or slip?
6. **Cross-cut**: does this feature-set stay inside the existing $3.30/student/month LLM cap, or does it require a tier bump for premium users?

---

## 9. Next step

Run personas against sections 7.1-7.10. Resolve section 8 questions. Draft implementation tasks (expected: PRR-244 through PRR-249-ish).

**History**:
- 2026-04-21: Draft created after user raised the three input-modality questions.
