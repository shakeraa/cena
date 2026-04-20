# Expert Panel Review — User Personas & Feature Synthesis (2026-04-17)

**Subject under review:** [cena-user-personas-and-features-2026-04-17.md](cena-user-personas-and-features-2026-04-17.md)
**Panel:** The 10 professional personas defined in [docs/tasks/pre-pilot/PERSONAS.md](../tasks/pre-pilot/PERSONAS.md) — Dina (enterprise architect, backend), Oren (API & integration), Dr. Nadia (learning science), Dr. Yael (psychometrics), Prof. Amjad (Bagrut curriculum), Tamar (RTL/accessibility), Dr. Lior (UX), Ran (security/compliance), Iman (SRE/ops), Dr. Rami Khalil (adversarial reviewer).
**Moderator note:** This is a simulated panel discussion — each panelist speaks in their persona voice. Rami cross-examines whenever a claim is handwaved. Decisions are not binding; they are surfaced for the human team to accept or reject.

---

## Round 1 — Opening takes (60 seconds each)

**Dina (backend architect):** The persona research is useful but feature-level; what I need to know is which features imply new aggregates, new projections, or new NATS subjects. F7 (compression diagnostic) implies a new `StudyPlan` aggregate with a deadline invariant. F6 (teacher heatmap) implies a cross-classroom read model. F10 (peer group) implies a *new* privacy boundary between student aggregates. None of these are scoped yet. My concern: this synthesis is product-ready but not architecture-ready.

**Oren (API & integration):** F5 (parent WhatsApp digest) is an outbound integration I don't see a plan for — we'll need a WhatsApp Business API provider, rate-limited delivery, opt-in records, and an idempotent retry path. The synthesis says "WhatsApp + email Sunday 20:00" in one bullet. That bullet is a 3-week scope item.

**Dr. Nadia (learning science):** F11 (anxiety-safe hint ladder) and F1 (Socratic explain-it-back) are the two features grounded in actual learning science. Self-explanation effect (Chi 1994, Renkl 1997) is one of the most robust findings in math education research, so F1 is pedagogically sound in principle. My worry: 20% gating on correct answers for F1 is arbitrary — that number should come from a pilot, not a synthesis document. Also, F3 (accommodations) must distinguish between TTS for dyscalculia (helpful) and TTS for reading problems aloud (different — can short-circuit encoding). Accommodations is not one switch; it is a profile with 6-8 dimensions.

**Dr. Yael (psychometrics):** F8 (grade prediction with confidence interval) is the single feature I care about most in this document, and it is the one most likely to be shipped dishonestly. "Predicted Bagrut score: 88 ± 5" implies we have a calibrated IRT model mapping Cena ability θ to Bagrut scale score. We do not have that calibration yet. The synthesis does not flag this. Shipping F8 before calibration would be a lie disguised as a number.

**Prof. Amjad (Bagrut curriculum):** F2 (Arabic-first) is the right bet — nobody else serves the sector at quality. But the synthesis underweights a critical detail: the Bagrut exam is in Hebrew with specific Hebrew mathematical terminology that doesn't always map 1:1 to Arabic textbook terms. If we teach `פולינום` as `متعدد الحدود` consistently, we're fine. If we drift between `كثيرة الحدود` and `متعدد الحدود` depending on the author, students get penalized on the exam. We need a locked Arabic math terminology lexicon before F2 ships.

**Tamar (RTL & accessibility):** F2 says "math is always rendered LTR in `<bdi dir="ltr">`" — good, that's the rule. But the synthesis doesn't discuss mixed-direction hint text (Arabic prose with embedded inline math), which is where rendering bugs hide. Also: F3 claims high-contrast mode and distraction-reduced layout but none of the current Vuexy components pass WCAG AA on the locked primary `#7367F0`. The synthesis should acknowledge that the accommodations mode will need a custom theme override, not just a toggle.

**Dr. Lior (UX):** F11 is my favorite in the document because it operationalizes progressive disclosure correctly — hints are questions, not answers, and mastery decay is invisible. I endorse. My concern is F5 (parent digest) — the synthesis says "2-minute digest." Parents don't read 2 minutes of content. They read 15 seconds. The first line must be the verdict ("Amir is on track" / "Amir is stuck on one topic") and the rest must be optional expansion. If it's a wall of text, it's ignored regardless of language.

**Ran (security & compliance):** F10 (peer study groups) is where this synthesis is at highest compliance risk. "Students can see each other's topic-level mastery" crosses a line the ICO Children's Code explicitly flags — children's data visible to other children requires explicit affirmative consent from both sides AND the guardian, and mastery data derived from BKT is arguably personal data. I need a DPIA addendum before F10 goes past wireframe. F5 (parent digest) is fine as long as it ships topic+mastery only, not misconception codes — the synthesis correctly notes this, and I'll verify the implementation.

**Iman (SRE):** F4 (offline PWA) and F5 (WhatsApp digest) are the two operational landmines. Offline-first means we need a sync conflict-resolution strategy when a student does work offline on the bus and the server-side bank shifted while they were disconnected. WhatsApp delivery at scale requires a queue, a rate-limited sender, dead-letter handling for invalid numbers, and a way to re-send without spamming. Neither is acknowledged in the synthesis as an operational concern. Also: every feature that renders a dashboard (F6, F8, F12) needs a Grafana JSON before it ships — I've seen this team ship dashboards-for-users without dashboards-for-oncall before.

**Dr. Rami Khalil (adversarial):** One thing jumps out. The synthesis claims "F11 (anxiety-safe hints) is the universal baseline — every persona benefits. Ship first." But every panelist here has made a different claim about what should ship first: Oren says F5 is 3 weeks of integration work; Dr. Yael says F8 depends on calibration we don't have; Amjad says F2 depends on a terminology lexicon we don't have; Ran says F10 needs a DPIA. That is not a "ship first" list. That is a "we have six unsequenced dependencies pretending to be a roadmap" list. I'd like the author of this synthesis to defend the sequencing.

---

## Round 2 — Feature-by-feature critique

### F1. Socratic explain-it-back with CAS step checks

**Dr. Nadia:** Pedagogically sound. But the LLM-judge + CAS-invariant approach is non-trivial. The CAS can check "did the student's claimed rule produce the same transformation symbolically?" — yes. The LLM must check "did the student's *natural-language* explanation match the rule they invoked?" That's harder, and getting it wrong will either false-positive ("you're right!" when the student is lucky) or false-negative ("you're wrong!" when the student is right but phrased oddly). I want a ground-truth set of 200 labeled student explanations before F1 enters a pilot.

**Ran:** If the LLM judge stores the student's explanation text for even 30 seconds longer than the response, that's user-generated content from a minor, and it falls under COPPA / GDPR-K. I need the data-flow documented: explanation in → judge processes → judgment out → text deleted within N seconds. Session-scoped per ADR-0003.

**Dr. Rami:** Who owns the LLM-judge system? Is it a separate service or inline in the Actor Host? What's the fallback when the LLM times out — does the student get "we couldn't verify, assumed correct" (false-positive drift) or "please try again" (friction)? The synthesis doesn't say. This is exactly the kind of "one sentence in a synthesis, three months of architecture" feature.

**Dina:** If it's a separate service, it needs circuit-breaker and fallback per Iman's rule. I'd slot it as a sidecar like SymPy.

**Iman:** Seconded. I'll add: LLM judge latency p99 must be < 2.5s or students disengage. If it's > 2.5s, the feature should not show the "explain it back" prompt at all — skip rather than stall.

### F2. Arabic-first UX with Hebrew-Bagrut toggle

**Prof. Amjad:** Strong market wedge. Needs: (1) locked Arabic math terminology lexicon covering 5-unit syllabus, (2) Arabic voice-overs if we do TTS (Egyptian vs Levantine Arabic matters — our target is Levantine), (3) Hebrew-exam-simulation mode must use *Ministry-standard* Hebrew notation which differs slightly from textbook notation. I will personally review the lexicon.

**Tamar:** RTL story is mostly solved in the codebase per existing work, but hint text with inline math is under-tested. I want the first Arabic-only student pilot to include at least one item per topic with inline math in a hint. Dogfood the hard case.

**Dr. Lior:** Onboarding flow for Arabic students must start in Arabic — language selection should be based on device locale + geolocation hint, not a screen where students tap their flag. Asking Amir to click a flag to choose Arabic is friction that says "you are the secondary audience."

**Dr. Rami:** Who has shipped a Levantine-Arabic educational product before on this team? If the answer is zero, we are at risk of building an Arabic skin on a Hebrew product, which Prof. Amjad has warned about twice in prior sessions. Commit to hiring or contracting at least one native Levantine Arabic pedagogy reviewer before F2 ships, not after.

### F3. Accommodations mode

**Dr. Nadia:** Must be profile-based, not single-toggle. 6-8 dimensions: extended time, reduced items-per-session, TTS for problems, TTS for hints, high-contrast, reduced animation, distraction-free layout, visible progress indicator vs hidden. Each can be independently flipped. Mapping from formal התאמות code (Ministry accommodation codes) to Cena profile should be tabular and documented.

**Tamar:** High-contrast mode requires a full theme override — Vuexy `#7367F0` on white fails WCAG AA in several compound states. I can deliver the accessibility audit for this but it's a real engineering task, not a CSS variable flip.

**Ran:** Accommodations data is sensitive category under GDPR Art 9 in several interpretations (disability status). It must be in a separate consent bucket, opt-in, with a specific data-minimization commitment. Session-scoped per ADR-0003 is the correct default.

**Dalia (from the user persona side, via Dr. Lior's advocacy):** The language framing in the UI matters. "Accommodations mode" must not appear in the student's UI as "slow mode" or any variant. Internal label ≠ student-facing label.

### F4. Offline PWA

**Iman:** This is my single biggest concern in this document. Offline-first means: (a) a service-worker cache of problems + hints + assets, (b) a local answer queue that syncs on reconnect, (c) conflict resolution when the server-side bank shifted, (d) a "last sync" indicator so students know their state is stale, (e) fallback UX when cache is empty. The synthesis mentions none of (b)-(e). I'd scope this at 4-6 engineering weeks, not 1.

**Dina:** Conflict resolution is the interesting architecture question. If Tariq did problem X offline and got it wrong, and meanwhile the system recalibrated problem X's IRT difficulty, do we apply the old or new calibration? I vote: freeze the item at the time it was served, regardless of recalibration. Event-sourcing makes this natural.

**Oren:** Sync API needs to be idempotent and order-tolerant. Offline events arriving out of order (three sessions batched up) cannot corrupt mastery state.

**Dr. Rami:** The synthesis says "single-session pre-pack, 30 min max." What happens when Tariq finishes in 20 min and wants more? The current scope says: too bad, reconnect tomorrow. Is that acceptable? If yes, say so. If no, scope creeps immediately.

### F5. Weekly parent digest

**Oren:** WhatsApp Business API has per-template-message approval, strict session windows for free-form replies, rate limits per recipient per day, and quality-based sender-reputation ratings. This is not "just send a WhatsApp message." Also: not every parent uses WhatsApp — Mahmoud does, but Rachel might prefer email, Dalia might prefer SMS. Delivery channel preference per parent, configurable, with opt-out per channel.

**Ran:** The digest content must pass a ship-gate check — a CI scanner that greps the rendered digest for banned terms (streak, record, bonus, limited-time, etc.) before it goes out. This is already a pattern we have for the student UI; extend it to outbound parent comms.

**Dr. Lior:** First line is the verdict. Subsequent lines are expandable. Mobile-first because parents read this on WhatsApp. 3 lines or 300 characters, whichever is shorter, for the default view.

**Dr. Nadia:** The digest must NOT say "Amir is 2 weeks behind the class." That is comparative shaming, and it violates the non-dark-pattern rule. It must say "Amir worked on X. Mastery went from A to B. One thing to celebrate, one thing to practice next week." Absolute, self-referential, non-comparative.

**Dr. Rami:** What happens when the week was bad? Amir practiced 0 hours because he was sick. Does the parent get no digest (signal loss) or a "zero hours this week" digest (shame)? My suggestion: send the digest with "Amir took a break this week — that's fine. Here's what he can pick up next week." Compassionate framing is a pedagogy decision, not a product decision.

### F6. Teacher class-heatmap console

**Dina:** This is a cross-student, per-classroom read model. Marten projection from student events into a classroom-level `ClassMasteryHeatmapProjection`. I need this to be idempotent and rebuild-from-scratch-safe.

**Oren:** Teacher console auth is `School` institute scoped. The classroom-consumer split already handles this. Endpoint: `GET /api/v1/institutes/{instituteId}/classrooms/{classroomId}/mastery-heatmap`. Scoping enforcement via middleware already in place.

**Ofir's voice (via Dr. Lior):** Ofir will use this 2 minutes per day. It must load in under 1 second and be scannable in 15 seconds. Not a dashboard with 20 widgets.

**Prof. Amjad:** Topic grouping must match the Ministry's syllabus topic hierarchy, not some internal taxonomy. Ofir teaches "Integrals" on week 14; if the heatmap calls it something else, he won't use it.

**Dr. Yael:** Cell color should encode mastery with a confidence indicator — 0.8 mastery from 50 attempts is different from 0.8 from 3 attempts. Use opacity or a second channel for sample size.

### F7. Compression diagnostic

**Dr. Yael:** Diagnostic design is an IRT problem. With a cold-start student, the diagnostic should use maximum-information item selection (MIS) with exposure control. I estimate 20-30 well-chosen items gives θ within ±0.3 for most students. 60-90 minutes in the synthesis is generous; 40 minutes is more realistic for high-stakes attention.

**Dr. Nadia:** For Daniel (post-mechina), diagnostic is motivation-boosting — "you already know 60% of the 5-unit syllabus, we're only drilling 40%." For Yael (3-unit anxious), a diagnostic could be devastating — "here is everything you don't know." Do not force diagnostic onto anxious students. Opt-in with framing.

**Dina:** `StudyPlan` aggregate with deadline, topic list, weekly targets, weekly adaptation. Event-sourced, obviously.

**Prof. Amjad:** The adaptation algorithm must respect curriculum sequencing. You cannot skip prerequisite topics even if the diagnostic says the student is weak elsewhere — weaknesses in advanced topics often trace to weaknesses in prerequisites.

**Dr. Rami:** "Cena generates an N-week schedule that prioritizes weaknesses × topic weight × time available" — how is topic weight computed? Is it Ministry-published topic weighting for recent Bagruts? Is it expert judgment? Is it mined from past Bagrut items? The synthesis says it like it's a solved problem. It isn't.

### F8. Grade prediction with confidence interval

**Dr. Yael:** I will repeat my opening: we do not have a calibrated mapping from Cena θ to Bagrut scaled score. Shipping this before calibration is dishonest. Calibration requires either (a) a cohort of students who used Cena AND sat a real Bagrut, or (b) a concordance study linking our items to calibrated Ministry items via common-item equating. We have neither. Until we do, the best we can do is "Cena mastery level: high / medium / low with 80% confidence" — not a predicted score. Don't lie with numbers.

**Dr. Nadia:** Seconded. Mastery-level framing is pedagogically healthier anyway. Point-estimate predictions cause anxiety spikes when they fluctuate.

**Rachel (via Dr. Lior):** Rachel wants signal, not a number. Mastery-trajectory is fine. "Predicted Bagrut: 88" is not a real signal if it's not calibrated.

**Dr. Rami:** Agreement. Rename F8 from "Grade prediction" to "Mastery trajectory with confidence framing." That is both honest and what the parents actually want.

### F9. AI-recreated Bagrut practice (reference-only)

**Prof. Amjad:** This is the right implementation of the ADR-0002 + reference-only decision. The key is the trace — every generated item must point to the Ministry item it mirrors and pass an expert-review gate (mine or a qualified substitute). Without the review gate, generated items drift in difficulty, terminology, or scope, and students train on the wrong thing.

**Dr. Yael:** The generated items must be IRT-calibrated to match the target difficulty. Right now, generated items are AI-authored + CAS-verified but not difficulty-calibrated. Without calibration, "Practice Bagrut 2024" is a misnomer — it's "Items that sort of look like Bagrut 2024."

**Ran:** Legal framing matters. Items must be clearly AI-authored originals, not Ministry-derived. I want the item metadata to explicitly say "recreation, reference only, not Ministry-published." If we ever get challenged, the documentation must be clean.

**Dr. Rami:** The synthesis says "5 recent Bagrut summer/winter sessions, 5-unit, with teacher audit links." How many items per session? Bagrut 5-unit has ~7-12 questions per session of varying weight. 5 sessions × 10 items × CAS-verified × expert-reviewed = 50 items manually reviewed. Who reviews them? At what rate? What's the review SLA?

### F10. Peer study group mode

**Ran:** I've already flagged this. DPIA addendum required. Two-sided consent required (student + guardian, for each member). Mastery data visible to peers is a policy question, not a product question. The synthesis should be revised to make the compliance scope explicit.

**Dr. Nadia:** Peer learning effects (Vygotsky ZPD, peer tutoring) are positive *if designed right*. Anonymous help-request routing is pedagogically sound. Named rankings are not. The synthesis correctly bans rankings; I endorse.

**Dr. Lior:** Groups form naturally on WhatsApp today (Amir's case). The feature is justified by real behavior. But the product must not duplicate WhatsApp's job — our value-add is the mastery-linked routing, not the chat. Keep the chat minimal or punt it entirely.

**Iman:** Help-request routing at scale means message delivery, notification delivery, cross-student state management. Non-trivial. I'd want a detailed design doc before build.

**Dr. Rami:** The synthesis says "invite-only groups of up to 6." What happens when a group member leaves? What happens when a group member gets suspended for dark-pattern reports? What's the moderator model? These are questions that decide whether F10 is a small feature or a medium one.

### F11. Anxiety-safe hint ladder

**Dr. Nadia:** Universal endorsement. This is Socratic scaffolding as the research actually supports it — questions, not answers, with guided progression and no shame.

**Dr. Lior:** Universal endorsement. This is progressive disclosure as the research supports it.

**Ran:** No compliance concerns.

**Dina:** No architecture concerns beyond standard hint state tracking.

**Dr. Rami:** If this is so universal, why isn't it already shipped? That's not a challenge — it's a real question. If F11 is low-risk and high-value, it should be Wave 2 or Wave 3, not "ship first" in a 2026-04-17 synthesis. Team: is there a blocker I'm missing?

### F12. Parent-controlled time budget

**Dr. Nadia:** The research on imposed time budgets for children is mixed. External motivation crowding-out intrinsic motivation (Deci & Ryan SDT) is the core risk. If the parent sets a 5-hour cap and Yael hits it and wants to keep going, stopping her might harm motivation more than letting her continue would harm wellbeing.

**Dr. Lior:** The cap should be soft and framed in the student's UI as a shared family agreement, not a lockout. "You and your parent agreed to 5 hours this week — you're at the end. Take a break, or continue and we'll log it."

**Ran:** Parental controls fit ICO Children's Code Standard 11. Ship-gate scanner must check that the budget UI doesn't use FOMO or scarcity framing ("ONLY 10 MIN LEFT!" = banned).

**Dr. Rami:** "Calm gauge" is a synthesis phrase that could hide a lot of design crimes. I want wireframes reviewed before F12 builds.

---

## Round 3 — Priority sequencing (the panel's rewritten roadmap)

The synthesis says "F11 first." The panel disagrees — some features are dependency-bound. Here is the panel's proposed sequence.

### Wave A (ship-ready after 4-6 weeks)

1. **F11 — anxiety-safe hint ladder.** Universally endorsed, no dependencies, high pedagogy value. Dr. Nadia leads design.
2. **F3 — accommodations mode (core 4 of 8 dimensions).** Tamar + Ran co-lead. Ship with a partial dimension set; expand later.
3. **F5 — parent digest (email only, no WhatsApp yet).** Dr. Lior leads UX, Oren leads delivery plumbing. WhatsApp in Wave B.

### Wave B (after Wave A validates, 4-6 weeks more)

4. **F2 — Arabic-first UX (5-unit first, limited lexicon).** Prof. Amjad leads lexicon, Tamar leads RTL, Dr. Lior leads onboarding.
5. **F5 expansion — WhatsApp digest.** Oren leads integration.
6. **F6 — teacher heatmap console.** Dina leads projection, Dr. Yael leads mastery+confidence encoding.

### Wave C (after calibration data exists)

7. **F8 — mastery trajectory (not grade prediction until calibrated).** Dr. Yael leads.
8. **F9 — AI-recreated Bagrut practice with review gate.** Prof. Amjad leads review, Dr. Yael leads difficulty calibration.
9. **F7 — compression diagnostic.** Dr. Nadia + Dr. Yael co-lead.

### Wave D (after policy + design work)

10. **F1 — Socratic explain-it-back.** Needs LLM-judge architecture + ground-truth set.
11. **F4 — offline PWA.** Needs sync conflict-resolution design + Iman's ops readiness.
12. **F10 — peer study group.** Needs DPIA + moderator model + two-sided consent flow.
13. **F12 — parent time budget.** Needs wireframe review + dark-pattern scan.

---

## Round 4 — Rami's final cross-exam

**Dr. Rami:** Three questions to the team before any of this becomes a roadmap.

1. **Honest metrics.** The synthesis has zero success metrics for any feature. How do we know F11 worked? Hint-usage-without-abandonment rate? Mastery gain per hint used? Satisfaction survey? Without a metric per feature, "ship F11 first" is aspirational, not measurable.
2. **Who owns unglamorous work.** F5 (parent digest) is three weeks of integration. F4 (offline PWA) is six weeks of sync plumbing. These have no panelist champion — everyone wants to do F1 and F11. Who is actually writing the WhatsApp webhook idempotency tests?
3. **Pilot ethics.** The synthesis proposes a northern Arabic-first pilot with 2-3 schools. What is the pilot consent flow? What is the exit strategy if the pilot shows negative learning effect for the Arabic cohort? What is the baseline comparison? Without an answer, we are piloting on children without a control.

**Panel response (consolidated):** These three questions go to the coordinator as action items before the synthesis is upgraded to a roadmap.

---

## Closing — panel verdict

The synthesis is a strong persona-grounded starting point. It correctly identifies the Arab-sector + rural market wedge, the anti-dark-pattern discipline, and the parent-trust stack. Its primary weaknesses are:

- **Unscoped dependencies** (F5 integration, F8 calibration, F2 lexicon, F10 DPIA)
- **Missing metrics** per feature
- **Optimistic sequencing** that ignores (a) dependency order and (b) who will actually do the unglamorous work
- **One honesty issue** (F8 as "grade prediction" implies calibration we don't have)

Recommended next step: the coordinator revises the synthesis with (a) the panel's proposed wave sequencing, (b) explicit per-feature success metrics, (c) dependency annotations, (d) rename F8 to "mastery trajectory" until calibration exists. Then schedule a 30-minute pilot-design session with Ran, Prof. Amjad, and Dr. Nadia for the Arabic-first pilot.

---

*Panel adjourned. Next review: when the synthesis is revised per above.*
