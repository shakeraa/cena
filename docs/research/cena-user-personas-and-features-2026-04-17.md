# Cena User Personas, Daily Workflows & Feature Synthesis

**Date:** 2026-04-17
**Author:** Autoresearch session (claude-code, coordinator)
**Scope:** 10 end-user personas (6 students, 3 parents, 1 teacher) for the Israeli Bagrut context, with concrete daily workflows, pain clusters, and a feature backlog mapped to those pains.
**Method:** Persona synthesis grounded in (a) the existing market research tracks in [docs/research/tracks/](../research/tracks/) — particularly [track-7-israeli-bagrut-market.md](tracks/track-7-israeli-bagrut-market.md), [track-8-dark-patterns-compliance.md](tracks/track-8-dark-patterns-compliance.md), [track-9-socratic-ai-tutoring.md](tracks/track-9-socratic-ai-tutoring.md); (b) the classroom/consumer split in [docs/compliance/classroom-consumer-split.md](../compliance/classroom-consumer-split.md); (c) the pedagogy framing in [docs/design/micro-lessons-design.md](../design/micro-lessons-design.md); (d) the non-negotiable design rules (ADR-0002 SymPy oracle, ADR-0003 session-scoped misconceptions, GD-004 anti-dark-pattern).
**Stance:** These personas are composite, not real individuals. Where I extrapolate (time-of-day patterns, device share), I mark assumptions explicitly. All features are filtered through the three design non-negotiables — anything that violates them is flagged, not shipped.

---

## Part 1 — The 10 Personas

### Student personas (6)

#### P1. Noa Cohen — "The ambitious 5-unit"
- **17, 12th grade, secular Jewish, Tel Aviv, girls high school**
- **Track:** 5-unit math (805), targeting Bagrut score 100+ bonus ("bonus 5") for 8200/Mamram military tech track and later CS admissions
- **Home context:** Two professional parents, one sibling, her own room with a desk, iPad + iPhone + family MacBook
- **Current study stack:** In-person private tutor (₪250/hr, 2×/week), Geva videos on iPad during bus commute, school textbook, printed past-Bagruts
- **Identity:** Self-identified perfectionist. Will retake a Bagrut module to bump 92→96. Anxiety when practice scores slide.
- **Top 3 concerns:** (1) "Did I actually understand this or did I just recognize the pattern?" (2) Time pressure — she has 2 hours a day max after school + practice + orchestra. (3) Losing the 5-unit bonus — scoring 5-unit at <75 is worse than taking 4-unit.
- **Willingness to pay:** High. Parents already spend ~₪2,000/month on tutoring.

#### P2. Amir Haddad — "The scholarship 4-unit"
- **16, 11th grade, Arab Christian, Nazareth, Arabic-language high school**
- **Track:** 4-unit math (804), considering 5-unit for university scholarships
- **Home context:** Three siblings, shared desk area, one family PC, personal Android phone. Arabic is L1, Hebrew L2, English L3.
- **Current study stack:** School teacher + whatsapp study group with 6 classmates + free YouTube (Arabic math channels, inconsistent quality) + occasional paid tutor in town (₪120/hr when family can afford it)
- **Identity:** Pragmatic. Math is a route to university (Technion or TAU) + scholarship. Does not want to "be a tech person" — wants to be a doctor.
- **Top 3 concerns:** (1) Bagrut exam is in Hebrew; his teacher teaches in Arabic — mental translation tax on every question. (2) No Arabic-language equivalent of Yoel Geva exists at quality — he is shopping among weak options. (3) Scholarship thresholds (Atidim, Rashi) require specific Bagrut averages he must hit without failing any subject.
- **Willingness to pay:** Modest. ₪50-80/month feasible; ₪1500 one-shot course is not.

#### P3. Yael Mizrahi — "The anxious 3-unit"
- **15, 10th grade, secular Jewish, Rishon LeZion, public school**
- **Track:** 3-unit math (801), formal learning accommodations (התאמות) — extended time 25%, separated room, verbal instructions allowed. Dyscalculia diagnosed age 11.
- **Home context:** Single mother, younger brother, small apartment. Phone + school-issued Chromebook.
- **Current study stack:** School remedial track + school counselor + her mother helps with homework despite not being a math person + free online practice she finds frustrating (too fast-paced, no accommodations mode)
- **Identity:** "I'm bad at math." Negative math self-concept. Wants to pass 3-unit without the class knowing she's struggling.
- **Top 3 concerns:** (1) Public humiliation — any UI that shows "your rank in class" or "you're behind" shuts her down for days. (2) Time pressure UI — timers cause panic. (3) Variable notation — she reads משתנה x and 2x differently on bad days, and platforms that assume notation fluency fail her.
- **Willingness to pay:** Very modest. Mother would pay ₪40-60/month if she saw Yael actually use it calmly.

#### P4. Daniel Ben-Ari — "The post-mechina intensive"
- **18, finished pre-military mechina year, preparing to retake 5-unit Bagrut to fix a 78→improve to 90+**
- **Track:** 5-unit math retake (moed chaf), 14-week window
- **Home context:** Living at parents' house in Jerusalem, own room, motivation is "don't waste this year before army." iPhone, laptop, he has 6-8 hours a day to study.
- **Current study stack:** Bought a Bagrut intensive package from GOOL (~₪2,000) + YouTube + friend's old notes
- **Identity:** Gap-year energy. Wants compression — "teach me what I missed, not what I know." Self-disciplined, slightly over-confident.
- **Top 3 concerns:** (1) His knowledge is patchy, not uniformly weak — he needs diagnostic to find the holes, not a 200-hour full-syllabus rerun. (2) Moed chaf is in 14 weeks — tight. (3) He is 18 and out of school — no teacher, no classmates, just him and the platform.
- **Willingness to pay:** High for 3 months, low after. He'd pay ₪150-200/month if the compression actually works.

#### P5. Sarah Levy — "The phone-native 9th grader"
- **14, 9th grade, secular Jewish, Ra'anana, just placed into the advanced math track heading toward 5-unit**
- **Track:** 9th-grade algebra/geometry, pre-Bagrut but pace-setting for 5-unit placement in 11th grade
- **Home context:** Two siblings, iPhone 15 is her primary computing device by a 4:1 ratio over the family iPad. Instagram, TikTok, WhatsApp. Screen time battles with parents.
- **Current study stack:** School homework + occasional older-sibling help + TikTok explanations (actually helpful sometimes) + classroom app the school mandates (boring)
- **Identity:** Digital native, short attention span, tolerates friction poorly. Will abandon an app in 30 seconds if the first screen is ugly or slow.
- **Top 3 concerns:** (1) Desktop-first UX is unusable on her phone — if pinch-zoom is required, she's gone. (2) Boredom. Anything that feels like "school but on a screen" is an immediate no. (3) Her friends use it or they don't — social proof matters.
- **Willingness to pay:** Her parents will, if she actually uses it. That's the whole test.

#### P6. Tariq Abu-Ghanem — "The rural Druze 4-unit"
- **17, 11th grade, Druze village in the Galilee, commutes 1hr each way to a regional school**
- **Track:** 4-unit math (804), Arabic L1, Hebrew L2. School instruction mix varies.
- **Home context:** Older family home, one router, multiple siblings sharing bandwidth, frequent power dips in winter. Android phone on prepaid cellular data plan with a monthly cap.
- **Current study stack:** School teacher + school-provided PDFs + whatever loads on 3G during the bus ride
- **Identity:** Hardworking, time-efficient. If the app doesn't work offline on his commute, it doesn't work for him at all.
- **Top 3 concerns:** (1) Offline support. Cellular data costs. (2) Platforms that assume always-on broadband silently fail for him. (3) Arabic-language first-class, not machine-translated.
- **Willingness to pay:** Low-moderate. Family budget limited; any subscription competes with cellular data itself.

### Parent personas (3)

#### P7. Rachel Cohen — "The product-manager mother" (Noa's mother)
- **42, Tel Aviv, senior PM at a fintech, earns well, two kids**
- **Relationship to Cena:** Would be the paying customer. Signs up Noa herself.
- **Top 3 concerns:** (1) Does this actually work? Wants outcome evidence (grade delta, mastery trajectory), not vanity metrics. (2) Time cost for Noa — she doesn't want Cena to add 1 more hour to Noa's already-full day; she wants it to *replace* 1 hour of less-efficient study. (3) Data ethics — she reads TechCrunch. Knows about Edmodo/Byju's. Will ask whether misconception data is used to train models (answer per ADR-0003: no, session-scoped 30 days).
- **What would make her a fan:** A weekly digest that shows "this week Noa worked on integration by parts, hit 70% mastery, got stuck on u-substitution for trigonometric integrands — Cena surfaced 3 targeted problems, now at 85%." Not streaks. Not badges. Signal.
- **What would make her cancel:** Seeing a streak counter. She knows the psych research. She'll assume the product is manipulative.

#### P8. Mahmoud Haddad — "The aspirational father" (Amir's father)
- **48, Nazareth, owns a small auto-parts business. Arabic L1, functional Hebrew, limited English.**
- **Relationship to Cena:** Payer. Wants his son to be the first doctor in the family.
- **Top 3 concerns:** (1) Fairness — he doesn't want an app that secretly favors Hebrew-speaking students in difficulty calibration, hint timing, or UI polish. (2) Price transparency. He's been burned by subscriptions that renew silently. (3) Tangible effort — he wants to see Amir *studying*, not *scrolling a math app*. Parental visibility matters.
- **What would make him a fan:** An Arabic-first product with a WhatsApp-delivered weekly summary he can read in 2 minutes, in Arabic, that says "Amir studied 4 hours this week, here are the topics." Low-friction parental visibility.
- **What would make him cancel:** Hebrew-only parent dashboard. Or a "upgrade to Premium to see your child's progress" paywall — that would feel predatory on an immigrant/minority-language family.

#### P9. Dalia Mizrahi — "The LD-advocate mother" (Yael's mother)
- **39, single parent, part-time bookkeeper, lives in Rishon LeZion**
- **Relationship to Cena:** Guarded. She has been failed by three EdTech apps that claimed to support learning differences and didn't.
- **Top 3 concerns:** (1) Accommodations — extended time, verbal instructions, no humiliating ranking, no speed-based rewards. If the app pressures Yael, Dalia will uninstall it the same day. (2) Communication — she wants a low-lift channel to tell Yael's teacher "this week was hard," and vice versa. (3) Privacy of the disability — Yael's dyscalculia is a documented התאמה; Dalia does not want it advertised in the UI ("You are a slow learner!") and does not want it stored beyond what's needed.
- **What would make her a fan:** An accommodations-aware mode with calm pacing, verbal hints (Yael reads math slowly), and a teacher-contact button that doesn't require Yael's permission each time but respects Yael's agency over what's shared.
- **What would make her cancel:** Any language framing Yael as "behind," "struggling," or "needs more practice than her peers."

### Teacher persona (1)

#### P10. Ofir Shapira — "The overextended math teacher"
- **35, 4-unit math teacher at a large Haifa public school, classes of 32, six periods a day, homeroom responsibilities**
- **Relationship to Cena:** Gatekeeper for school-mode adoption. Classroom classifier per [classroom-consumer-split.md](../compliance/classroom-consumer-split.md) — `InstructorLed` mode inside a `School` institute.
- **Top 3 concerns:** (1) Prep time — he has 0 minutes to author content. If Cena needs him to "tag 50 questions by topic," it's dead on arrival. (2) Signal — he wants to know which 5 kids are stuck on what, ideally by Monday morning from weekend homework, with no grading labor. (3) Trust — he will not assign platform-generated problems to his class unless he can *audit* the problem for Bagrut alignment. (CAS-verified + Prof. Amjad review flow matters here.)
- **What would make him a fan:** A teacher console that shows, for his class, a topic-by-topic heatmap of stuck kids — and lets him assign 15 minutes of Cena work as homework with 0 setup, auto-graded, CAS-verified.
- **What would make him not adopt:** Being asked to be a content creator. Ofir teaches, he doesn't author.

---

## Part 2 — Daily workflows (weekday + weekend for the 6 student personas)

All times local (Asia/Jerusalem). Assumptions flagged.

### Noa (P1) — weekday

| Time  | Activity | Cena touchpoint opportunity |
|-------|----------|----------------------------|
| 06:40 | Wake, coffee, school bag | — |
| 07:20 | Bus to school (30 min) | **Mobile-first 15-min skill drill** on 5-unit calc topic from her weakness map |
| 08:00 | School day (6 classes) | — |
| 14:30 | Bus home (30 min) | **Review mode** — she revisits the morning session's misses |
| 15:00 | Lunch, break | — |
| 16:00 | Tutor session (Wed, Sun) or orchestra (Mon, Tue) or self-study | Tutor session: Cena shows tutor "here's what Noa missed this week" snapshot (consent-gated per classroom-consumer-split) |
| 19:00 | Homework + Cena self-study 45-60 min | **Deep practice** on topics her adaptive bank serves her |
| 21:30 | Wind-down, social, bed | — |

### Noa (P1) — weekend

- **Saturday**: Family day, limited studying; **micro-session** of 15 min max. Quality > duration.
- **Sunday morning**: 2-hour block. Past-Bagrut simulation mode with CAS-graded solutions.

### Amir (P2) — weekday

| Time | Activity | Cena touchpoint |
|------|----------|-----------------|
| 06:00 | Morning chores | — |
| 07:30 | School | — |
| 14:00 | Home, lunch with family | — |
| 15:00 | WhatsApp group homework with classmates | **Group mode?** (future — see feature F10) |
| 16:30 | Paid tutor (when family can afford) OR **Cena 30-45 min** as tutor-replacement | This is the killer slot for Amir |
| 18:00 | Family dinner | — |
| 19:30 | More Cena / YouTube / rest | **Arabic micro-lessons** on concepts he saw in class |
| 22:00 | Sleep | — |

Arabic-first UX is critical here. Hebrew as optional toggle for Bagrut-exam-simulation mode only.

### Yael (P3) — weekday

- Yael does not self-study voluntarily. Her Cena use is triggered by (a) her mother asking, (b) homework assigned.
- **Mother-triggered window**: Wed + Sun evenings, 20 min max, supportive tone required.
- **Homework-triggered**: 15 min immediately after school if teacher assigns.

If the session goes badly once (timer stress, red X, rank display), Yael drops the app for weeks. **Accommodations mode** must be default, not opt-in.

### Daniel (P4) — weekday (gap year, 14-week intensive)

| Time | Activity |
|------|----------|
| 08:00 | 2-hour Cena diagnostic/targeted session |
| 10:30 | Run / gym |
| 12:00 | Lunch |
| 13:00 | 3-hour Cena + past-Bagruts |
| 17:00 | Break, call friends |
| 19:00 | 1.5-hour review session — re-attempt morning misses |

Daniel is a power user. He'll consume 5-6 hours/day. **Compressed diagnostic + targeted weakness drills** is the entire product for him.

### Sarah (P5) — weekday

| Time | Activity | Cena touchpoint |
|------|----------|-----------------|
| 07:30 | School | — |
| 14:30 | Home, social, homework | **Mobile session**, 10-15 min, phone-portrait, swipeable |
| 16:00 | Extracurriculars | — |
| 19:00 | Maybe Cena, maybe TikTok math | **TikTok-adjacent UX** — vertical cards, smooth transitions, no desktop-frame feel |

Sarah's retention test is the first 30 seconds. Onboarding must hook instantly, feel native to her phone, not feel like "school on a screen."

### Tariq (P6) — weekday

- **Bus rides (1hr × 2)**: Offline Cena. Downloaded this morning on school wifi.
- **Home evenings**: Wifi available, but shared with 3 siblings — bandwidth-light mode.
- **Weekend**: 3-4 hours Cena if his data plan allows; otherwise wifi at the village youth center.

Offline-first PWA with preloaded session packs is non-negotiable for him.

---

## Part 3 — Cross-persona pain clusters

When I map the top 3 concerns across all 10 personas, they cluster into 7 themes. Features later map to these cluster codes.

| # | Pain cluster | Personas hit | Root cause |
|---|--------------|--------------|-----------|
| C1 | **Real vs. simulated understanding** | Noa, Daniel, Ofir | Current Hebrew market is assessment-only; students can't tell if they *understand* or just *pattern-match*. |
| C2 | **Bilingual cognitive load** | Amir, Tariq, Mahmoud | Math content is taught in L1 but tested in L2 (Hebrew Bagrut). No incumbent product solves this. |
| C3 | **Anxiety and shame in UI** | Yael, Dalia, Sarah (low-grade) | Rankings, timers, red X animations, streak pressure — legacy patterns that fail LD students and turn off phone-natives. |
| C4 | **Time scarcity + compression** | Noa, Daniel, Ofir | 5-unit students and gap-year students need targeted weakness-finding, not full-syllabus retreads. |
| C5 | **Connectivity and device reality** | Tariq, Sarah (phone-only), Amir (shared device) | Desktop-first, always-online EdTech silently excludes rural + Arab sector + phone-native users. |
| C6 | **Parent visibility without surveillance** | Rachel, Mahmoud, Dalia | Parents want *honest, ethically-bounded* signal — not a dashboard that pushes "premium to see more." |
| C7 | **Teacher leverage without teacher labor** | Ofir | Teachers will not author content or tag problems; they need formative signal at zero prep cost. |

Cross-cutting: **all 10 personas** lose confidence in a product that feels manipulative (dark patterns), dishonestly labeled, or produces wrong math.

---

## Part 4 — Feature backlog mapped to pain clusters

Each feature: description, pain clusters addressed, ADR alignment, MVP scope, "why now."

### F1. Socratic "Explain-it-back" micro-lessons with CAS-verified step checks

**Description:** After a student gets a problem right, 20% of the time Cena asks "why did step 3 work?" — a free-text explanation that SymPy can't fully grade, but a lightweight LLM judge + CAS invariant check can (did the student identify the rule? did the claimed rule actually apply here symbolically?). This operationalizes the self-explanation effect (Chi 1994, Renkl 1997) — the strongest robust finding in math learning research.

**Pain clusters:** C1 (real understanding), C4 (compression — self-explanation reveals gaps fast)
**ADR alignment:** ADR-0002 (SymPy verifies the step's symbolic invariant; LLM never grades correctness alone)
**MVP scope:** 10 prompt templates × 3 topic families, gated to 20% of correct answers to avoid fatigue
**Why now:** [micro-lessons-design.md](../design/micro-lessons-design.md) already frames the gap — "Cena today is assessment-only."

### F2. Arabic-first UX with Hebrew-Bagrut exam-simulation toggle

**Description:** Student selects L1 on signup (Arabic, Hebrew, English). Learning, hints, explanations, lesson text, parent/teacher comms all delivered in L1. Hebrew surfaces only in an optional "Bagrut exam simulation" mode that presents items exactly as they will appear on the Ministry exam — so students train the code-switching explicitly, not accidentally. Math is always rendered LTR in `<bdi dir="ltr">` per project rule.

**Pain clusters:** C2 (bilingual cognitive load)
**ADR alignment:** None violated; aligns with Shohamy (2010), Clarkson (2007) research already cited in project docs
**MVP scope:** Arabic + Hebrew UI full parity; English partial; Bagrut-simulation toggle on practice-test mode only
**Why now:** [track-7-israeli-bagrut-market.md](tracks/track-7-israeli-bagrut-market.md) identifies this as the single largest market gap — no incumbent serves Arabic at quality.

### F3. Accommodations mode (default-on detection, opt-out)

**Description:** On signup, students can select accommodations — extended time (no visible timers at all, not "25% more timer"), verbal hints (TTS-read problems + hints in L1), high-contrast mode, distraction-reduced layout (one problem at a time, no sidebar rankings), no comparative stats visible. For students with a parent-linked accommodations profile, the mode is default-on, not opt-in.

**Pain clusters:** C3 (anxiety/shame), C6 (parent-advocate trust)
**ADR alignment:** ADR-0003 (accommodation status is session-scoped; not stored in analytics profile); GD-004 (no dark patterns)
**MVP scope:** 4 accommodations, profile-linked with parent consent, default-on for diagnosed LD cases
**Why now:** Yael and Dalia are representative of ~20% of the Israeli student population with formal התאמות. No Israeli EdTech currently does this well.

### F4. Offline-first PWA with session pre-packs

**Description:** Student on school wifi presses "Download today's session" → 15-30 min of personalized content (problems, hints, lessons, CAS snapshots for grading) packs into the service worker cache. Student runs it offline on the bus, answers sync when wifi returns.

**Pain clusters:** C5 (connectivity)
**ADR alignment:** None violated; consistent with [cena-mobile-pwa-approach.md](cena-mobile-pwa-approach.md)
**MVP scope:** Single-session pre-pack, 30 min max, sync on reconnect
**Why now:** PWA decision is locked (replaced Flutter); no incumbent serves rural + prepaid-cellular users.

### F5. Weekly parent digest — WhatsApp + email, L1, "signal only"

**Description:** Every Sunday night, the parent receives a 2-minute digest in their L1: "This week, [Student] practiced [topics]. Mastery moved from [X] to [Y] on [topic]. One thing they got stuck on: [specific concept]. One thing to celebrate: [specific improvement]." **No** streak claims, **no** "upgrade to see more" paywall, **no** leaderboard.

**Pain clusters:** C6 (parent visibility)
**ADR alignment:** ADR-0003 (digest contains topic + mastery, NOT misconception codes or error-type tags — those stay session-scoped); GD-004 (no streak language, CI scanner enforces)
**MVP scope:** WhatsApp + email, Hebrew + Arabic, Sunday 20:00 local delivery
**Why now:** Rachel, Mahmoud, Dalia all want this. Legacy Hebrew EdTech doesn't do it at all.

### F6. Teacher class-heatmap console (zero-setup homework)

**Description:** Teacher opens the classroom console, sees a topic × student heatmap colored by mastery. Two buttons: "Assign 15 min of targeted work to whole class" (Cena picks each student's top weakness within a teacher-selected topic), and "Assign to specific students" (drag-selection on the heatmap). Results flow back as a class-level summary by Monday morning. No authoring. No tagging.

**Pain clusters:** C7 (teacher leverage)
**ADR alignment:** Consistent with [classroom-consumer-split.md](../compliance/classroom-consumer-split.md) InstructorLed + School model; teacher assignments are teacher-controlled content, curriculum-aligned
**MVP scope:** 4-unit math only for first pilot; heatmap + 2 assignment buttons; results digest Mondays 07:00
**Why now:** Ofir-type teachers are the gatekeeper for `School` institute adoption. Geva and GOOL have no teacher console.

### F7. Diagnostic-driven compression mode ("crash track")

**Description:** On entry, student can select "I have N weeks to moed chaf / final exam." Cena runs a 60-90 min adaptive diagnostic, identifies the top 5-8 weakness clusters, and generates an N-week schedule that prioritizes weaknesses × topic weight on the Bagrut × time available. Schedule adapts weekly based on mastery gains.

**Pain clusters:** C4 (time-scarcity compression), C1 (understanding vs pattern-match, via IRT-calibrated item selection)
**ADR alignment:** None violated; uses existing BKT + (once calibrated) IRT per [PERSONAS.md Dr. Yael lens](../tasks/pre-pilot/PERSONAS.md); ADR-0002 holds for all items
**MVP scope:** Diagnostic for 5-unit first, 14-week schedule, weekly adaptation
**Why now:** Daniel-type gap-year + Noa-type ambitious students will pay premium for this. Hebrew incumbents sell ₪1500 full-syllabus packages — compression is the wedge.

### F8. Transparent grade prediction with confidence interval

**Description:** Student's dashboard shows "Predicted Bagrut score: 88 ± 5 (based on 142 problems, 6 weeks of data)." Confidence interval shrinks as more data comes in. When it's wide, the UI says so: "we need more data to predict accurately."

**Pain clusters:** C1 (real vs simulated understanding), C6 (parent outcome evidence)
**ADR alignment:** Consistent with Dr. Yael's psychometrics lens (confidence intervals, not point estimates); ADR-0003 (prediction is derived from current session + mastery trajectory, not persistent misconception profile)
**MVP scope:** 5-unit first, IRT-based ability estimate + confidence interval, visible to student and parent
**Why now:** Hebrew incumbents show nothing resembling this. Parents (especially Rachel, Mahmoud) need outcome evidence.

### F9. Reference-only Bagrut practice with AI-recreated CAS-verified items

**Description:** "Practice Bagrut 2024 Summer" mode presents items that mirror the 2024 summer exam *structure and difficulty* but are AI-authored and CAS-verified — never raw Ministry exam text (per 2026-04-15 decision: Bagrut reference-only). Each item traces to the Ministry item it mirrors, for teacher review, but students never see the raw Ministry text without Ministry license.

**Pain clusters:** C1 (genuine exam preparation), C7 (teacher trust — items are audit-traceable)
**ADR alignment:** [project_bagrut_reference_only.md](/Users/shaker/.claude/projects/-Users-shaker-edu-apps-cena/memory/project_bagrut_reference_only.md); ADR-0002 (all items CAS-gated)
**MVP scope:** 5 recent Bagrut summer/winter sessions, 5-unit, with teacher audit links
**Why now:** Directly competes with GOOL/BagrutOnline's main SKU (past-Bagrut solutions) at legally-defensible, pedagogically-stronger positioning.

### F10. Peer study group mode (classroom-consumer aware)

**Description:** 3-6 students form a "study group" (not a classroom). They can see each other's topic-level mastery (never item-level), ask for help on a problem (with consent, the problem routes anonymously to a group member who has solved it), and have a shared weekly recap. Crucial guardrail: no ranking within group, no "top student" call-out, no public streak. Group visibility is member-controlled.

**Pain clusters:** C3 (social-without-shame), C2 (WhatsApp study groups already happen — formalize them with privacy-by-design)
**ADR alignment:** GD-004 (no streak/rank/competitive pressure); ADR-0003 (misconception data never cross-person); classroom-consumer split (groups are consumer-context, need parental consent per member)
**MVP scope:** Invite-only groups of up to 6, topic-level mastery visible, no rankings
**Why now:** Amir's WhatsApp group is the current substitute — capture the behavior natively with privacy guardrails.

### F11. Anxiety-safe hint ladder + "I'm stuck" button

**Description:** Instead of multiple-choice hint levels (which students race past), the hint ladder is time-invisible: student presses "I'm stuck," gets a scaffolding prompt that asks a question (not gives an answer), then another question, then an analogous worked example, then the full worked solution. No "you used a hint" penalty. No countdown. Mastery credit is adjusted per Dr. Nadia's lens (assisted credit < unassisted), but invisibly — the student never sees "−20%."

**Pain clusters:** C3 (anxiety), C1 (Socratic scaffolding over answer-reveal)
**ADR alignment:** Dr. Nadia persona lens; GD-004 (no penalty shaming); ADR-0002 (worked examples CAS-verified)
**MVP scope:** 4-level ladder, topic-aware scaffolding questions, mastery decay invisible to student
**Why now:** Existing market reveals answers. Cena's differentiator is *never giving the answer until the student has done the cognitive work.*

### F12. Parent-controlled time budget without dark-pattern reinforcement

**Description:** Parent can optionally set a weekly time budget ("up to 5 hrs/week") via parent console. Cena surfaces time-used vs budget to student in a calm way. Never "you've studied more than 80% of students" — that's comparative shaming. Never "one more session to reach your streak!" Just a calm gauge.

**Pain clusters:** C3 (no manipulation), C6 (parental control without surveillance theater)
**ADR alignment:** GD-004 (CI scanner will reject streak/FOMO language); aligned with ICO Children's Code Standard 11 parental controls
**MVP scope:** Weekly time budget, soft cap with student-chosen behavior at cap
**Why now:** Parental consent flows already exist per [parental-consent.md](../compliance/parental-consent.md); this is the next layer up.

---

## Part 5 — Feature × Persona fit matrix

| Feature | Noa | Amir | Yael | Daniel | Sarah | Tariq | Rachel | Mahmoud | Dalia | Ofir |
|---------|-----|------|------|--------|-------|-------|--------|---------|-------|------|
| F1 Socratic explain-it-back | HIGH | MED | LOW | HIGH | MED | MED | (proxy HIGH) | MED | (proxy LOW) | HIGH |
| F2 Arabic-first UX | LOW | HIGH | — | — | — | HIGH | — | HIGH | — | (teacher lang) |
| F3 Accommodations mode | — | — | HIGH | — | LOW | — | — | — | HIGH | MED |
| F4 Offline PWA | MED | MED | LOW | LOW | MED | HIGH | LOW | MED | LOW | LOW |
| F5 Parent digest | (proxy) | (proxy) | (proxy) | (n/a adult) | (proxy) | (proxy) | HIGH | HIGH | HIGH | LOW |
| F6 Teacher heatmap | — | — | — | — | — | — | — | — | — | HIGH |
| F7 Compression diagnostic | HIGH | MED | LOW | HIGH | LOW | MED | HIGH | MED | LOW | MED |
| F8 Grade prediction + CI | HIGH | HIGH | MED | HIGH | MED | HIGH | HIGH | HIGH | MED | MED |
| F9 AI-recreated Bagrut practice | HIGH | HIGH | LOW | HIGH | LOW | MED | HIGH | HIGH | LOW | HIGH |
| F10 Peer study group | MED | HIGH | LOW | MED | HIGH | MED | MED | MED | LOW | LOW |
| F11 Anxiety-safe hint ladder | HIGH | HIGH | HIGH | HIGH | HIGH | HIGH | HIGH | HIGH | HIGH | HIGH |
| F12 Parental time budget | LOW | MED | MED | (self-managed) | HIGH | MED | HIGH | HIGH | HIGH | — |

**Observations from the matrix:**

- **F11 (anxiety-safe hints)** is the universal baseline — every persona benefits. Ship first.
- **F2 (Arabic-first)** + **F4 (offline PWA)** unlock the Arab-sector + rural market that incumbents entirely miss. Ship these early for competitive wedge.
- **F5 (parent digest)** + **F8 (grade prediction with CI)** are the full parent-trust stack — together they make Rachel a fan and Mahmoud comfortable paying.
- **F6 (teacher heatmap)** is a separate go-to-market path — only needed for `School` institute mode. Time-box until consumer mode is proven.
- **F7 (compression)** + **F9 (Bagrut practice)** are the premium-willing-to-pay wedge — target Daniel and Noa types with a higher-tier subscription.

---

## Part 6 — What not to build (anti-features)

Because the design non-negotiables are locked, these are banned by [shipgate.md](../engineering/shipgate.md) and would fail a persona test:

- **Streaks** of any kind — loses Rachel, Dalia, Yael immediately
- **Leaderboards** within class, school, or platform — loses Yael, Dalia, accidentally loses Amir (Mahmoud's fairness concern)
- **Variable-ratio rewards** (loot box energy) — loses Rachel on product-ethics grounds
- **"Upgrade to see your child's progress" paywalls** — loses Mahmoud permanently
- **Persistent misconception labels** — violates ADR-0003, loses Dalia, creates ML-training liability
- **Timers visible to student** on daily practice — loses Yael, raises Sarah's anxiety floor
- **Hebrew-only parent dashboard** — loses every Arab-sector parent
- **Desktop-first layouts** — loses Sarah in 30 seconds, loses Tariq by default

---

## Part 7 — Next steps (if the team agrees with this synthesis)

1. **Validate personas with 8-10 real interviews** — 5 students across tracks/sectors, 3 parents, 2 teachers. Aim: 2 weeks. Output: which pain clusters were underweighted, which features miss real needs.
2. **Sequence features** against the existing readiness backlog. F11 + F5 + F2 are the three that stack into a compelling alpha demo with the lowest additional engineering cost relative to current Wave 2 state.
3. **Run a dark-pattern adversarial review** on F10 (peer groups) and F12 (time budget) — the two features most at risk of slipping into manipulative territory. Dr. Rami lens from [PERSONAS.md](../tasks/pre-pilot/PERSONAS.md).
4. **Pilot in Arabic-first mode** in 2-3 `School` institutes in the north before turning on Hebrew parity everywhere — it's the market wedge and it stress-tests the hardest path (RTL + Arabic + math LTR + exam-simulation code-switching).
5. **Build F8 (grade prediction with CI)** as soon as IRT calibration has enough response data — it's the single feature that parents will quote when recommending Cena to other parents, which is the only free marketing channel that matters in this market.
