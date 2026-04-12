# Cena "Sexy Game-Like" Design — Research Validation Synthesis

> Date: 2026-04-11
> Authors: Claude Code coordinator + 10 parallel researcher agents
> Scope: Stress-test the 10-point "sexy game-like" design proposal for Cena (EN/AR/HE math + physics, Bagrut 4-unit/5-unit + AP Calc + SAT) against competitor teardowns, academic literature, and 2022–2026 regulatory enforcement.
> Method: 10 parallel research tracks, each with its own citation target, confidence-delta deliverable, and legal red-flag check. Per-track reports in [`tracks/`](./tracks/).

---

## Executive summary (for the one person who will read only this)

**The proposal survives research with 6 points strengthened, 3 points requiring surgery, and 1 point needing a decision.**

1. **Point 7 (zero streak guilt / confetti / variable-ratio)** is no longer a design posture — it is a **ship-gate requirement**. The FTC 2025 COPPA Final Rule, ICO Reddit £14.47M (Feb 2026), and the Edmodo "Affected Work Product" decree converged this from "brand choice" into "regulatory floor." Elevate this from a principle to an engineering acceptance criterion. Track 8, Track 6, Track 1, Track 5.
2. **Point 6 (daily challenge as Wordle) is internally contradictory with Point 7 and is the single highest-legal-risk point in the proposal.** Wordle's retention is loss aversion. Duolingo's own blog admits the streak adds +19% DA-on-streak because of loss aversion kicking in around day 7. You cannot have daily retention *and* zero loss aversion unless you replace the mechanic — use a shared community puzzle frame (Brilliant's approach), not a personal streak counter. Track 10, Track 8, Track 1.
3. **Point 4 (sandbox-first physics) is the best-empirically-validated claim in the whole proposal.** Finkelstein et al. 2005 (Phys. Rev. PER) — students using PhET Circuit Construction Kit *outperformed peers with real lab equipment* on conceptual items. Steal PhET's *implicit scaffolding* pattern and its *student-interview iteration process*. **But** pivot from pure sandbox to *guided* sandbox: Brilliant's success via one-knob constraint is a real counter-example to throwing students into an unconstrained toybox. Track 10, Track 3.
4. **Point 3 (named tutor character per language) survives only as a delivery wrapper, not as a learning mechanism.** The persona effect (Lester, Moreno & Mayer) is motivational with d<0.1 on learning. Keep the character for brand and localization; drop any claim that the character causes learning. A more research-backed reframe: *cast of inner voices* (Disco Elysium pattern) — multiple named strategies the student can invoke ("the Chief Estimator," "the Sign Checker"), each a specific heuristic. Track 9, Track 10, Track 5.
5. **Point 10 (boss fights as Bagrut synthesizers) is mechanically right but pedagogically neutral.** No evidence for transfer — it's "mixed-topic integrative assessments in costume." That's fine as long as you don't claim it teaches synthesis; it *assesses* synthesis. Khan Academy's Unit Test + Course Challenge is already this pattern at scale. Track 9, Track 2, Track 10.
6. **Point 2 (misconception-as-enemy) has strong pedagogy evidence (Koedinger buggy-rule catalog, d≈0.2–0.4 over generic feedback) and a hard data-retention constraint.** The Edmodo precedent means per-minor misconception profiles cannot be retained past the instructional session or reused to train global ML models. Misconception state must be session-scoped, not profile-scoped. Track 9, Track 8, Track 4.
7. **Point 5 (co-op not PvP) is the single most strongly supported point in the proposal — strengthened by every track it touches.** Schoolhouse.world (174K students, 25K peer tutors, SAT-focused) is the existence proof. Hanus & Fox 2015, Bai 2020, Kahoot speed-axis critique all concur. Ship this. Track 2, Track 4, Track 5, Track 6.
8. **Point 1 (session-as-game-loop with world map)** survives only if the map *is* the curriculum graph, not a loot territory. Prodigy is the world-map game loop that fails — 888 questions per 1 test-point by its own numbers. Clark 2016 supports the mechanism-first framing; the feature itself is neutral.
9. **Point 8 (MathLive + haptics)** needs a sharp restriction: **event SFX and tap haptics only — no continuous background music, and no lyrics under hard math.** There is no RCT showing phone haptics improve retention, and the music-under-cognitive-load literature is consistent: lyrics hurt.
10. **Point 9 (reject Duolingo streak)** is directionally correct but needs tighter wording: reject the **loss-aversion framing**, keep a positive-frame daily cadence signal (Apple Fitness rings style). The underlying habit-engine (spaced repetition + daily cadence + session fit) is empirically sound; the streak-as-debt wrapper is the part that's optional and regulatorily contentious. Track 1, Track 6.

---

## The revised 10-point proposal

Each point below carries: ORIGINAL → RESEARCH-BACKED REVISION → EVIDENCE → NON-NEGOTIABLES.

### 1. Session-as-game-loop with world map
- **Original**: A world map where each node is a session the student can enter.
- **Revised**: A world map where each node is a **topic in the curriculum dependency graph**. Progress unlocks happen by mastery events (Track 2 bidirectional mastery decay pattern), never by XP gates. The map is a navigation metaphor for curriculum topology, not a reward structure. Students can jump around; the map shows prerequisite chains but does not lock them behind loot. Prodigy is the anti-pattern.
- **Evidence**: Clark, Tanner-Smith & Killingsworth 2016 (g ≈ 0.33 base + g ≈ 0.34 from enhanced design); Khan Academy Mastery system; Prodigy cautionary tale (888 questions per standardized-test-point).
- **Non-negotiables**:
  - No XP. No currency. No cosmetic unlocks gated by completion.
  - Map must be a dual of the curriculum skill graph — renaming a math topic must rename the map node.
  - "Locked" nodes show the prerequisite chain honestly, never "unlock at level X."
- **Confidence delta**: **0 → +0.5** — feature is neutral; framing makes or breaks it.

### 2. Misconception-as-enemy
- **Original**: Cena personifies misconceptions as boss characters the student defeats.
- **Revised**: Cena maintains a **buggy-rule catalog** of 100–200 concrete misconceptions per subject (Koedinger tradition). Each misconception has: a counter-example template, a targeted remediation exercise, and a named identity the student can refer to ("the sign-flip error," "the factor-of-two-in-the-kinematic-equation"). The "enemy" is the misconception, never the student. Misconception state lives only inside the current instructional session — session-scoped by design, never profile-scoped.
- **Evidence**: Koedinger Cognitive Tutor, ASSISTments (d≈0.2–0.4 over generic feedback, Track 9); DragonBox as partial positive exemplar (Track 4); Edmodo FTC Affected-Work-Product decree as the retention constraint (Track 8).
- **Non-negotiables**:
  - **Engineering hard constraint**: per-student misconception timelines are session-scoped tables, purged within 24 hours or at session-end, whichever is sooner. Aggregate anonymized stats only at the catalog level.
  - **Never** train a global ML model on per-student misconception traces. This triggers the Edmodo "Affected Work Product" precedent — the FTC can order deletion of the trained model.
  - Misconceptions are fought **in standard mathematical notation**, never in custom game symbols (DragonBox lesson).
  - Pedagogy copy never shames the learner — "this enemy is a pattern people fall into," not "you made a mistake."
- **Confidence delta**: **neutral → +1 for pedagogy, −1 for data architecture complexity** — net positive but expensive.

### 3. Named tutor character per language
- **Original**: A named, illustrated tutor per target language (EN/AR/HE).
- **Revised**: Reframe as a **cast of inner voices**, not a single mascot. Each voice is a named heuristic: "the Chief Estimator" (rough-check first), "the Sign Checker" (did you drop a negative?), "the Units Police" (dimensional analysis). Each language localization uses the same cast with culturally-appropriate names. Brand wrapping only — drop any claim that the character causes learning. Tutor voice is non-evaluative, non-guilting, never "misses" the student.
- **Evidence**: Lester / Moreno & Mayer persona-effect literature (d<0.1 on learning, motivation-only); Disco Elysium pattern (Track 10); SDT autonomy-support (Track 6); Sailer 2020 narrative moderator (Track 5).
- **Non-negotiables**:
  - No push notifications written in the tutor's voice.
  - No "the tutor misses you" loss-aversion copy.
  - Tutor never praises on correctness alone — praise is tied to specific competence information ("you used the discriminant correctly"), not "Correct! +25 XP."
  - RTL/LTR parity — Arabic and Hebrew voices are not afterthoughts.
- **Confidence delta**: **demote from learning mechanism to delivery wrapper**. Still ship, but the rationale shifts from "learning" to "localization + brand."

### 4. Sandbox-first physics (the proposal's best-validated point)
- **Original**: Physics as an unconstrained PhET-style sandbox.
- **Revised**: **Guided sandbox** — PhET-style manipulables inside Brilliant-style scripted beats. One-knob-at-a-time for the learning path; full sandbox available on a "free play" toggle. Steal PhET's *implicit scaffolding* (productive paths are easy paths, no instruction popups). Steal PhET's student-interview iteration protocol as the internal design process.
- **Evidence**: Finkelstein et al. 2005 PhET Circuit Construction Kit RCT — students using the sim outperformed peers with real lab equipment on conceptual items. Wieman et al. 2008 *Science*. Podolefsky, Moore, Perkins implicit-scaffolding paper. Counter-example from Track 3: Brilliant's success via constraint — pure sandbox intimidates. Clark 2016 — scaffolded games outperform stealth-learning games.
- **Non-negotiables**:
  - Every simulation must have a "canonical productive path" that is the easiest path through the UI (implicit scaffolding).
  - Every simulation must be ported through a student-interview iteration cycle (PhET protocol) before shipping. Minimum 8 think-aloud sessions per sim.
  - Free-play mode exists, but it is opt-in — the default flow is guided.
  - No UGC sharing of sandbox states (Track 8 risk — peer-to-peer sharing triggers minor-data flows).
- **Confidence delta**: **+2** — this is the strongest game-design bet in the whole proposal.

### 5. Co-op not PvP (the proposal's most strongly-backed point)
- **Original**: Cooperative play instead of head-to-head competition.
- **Revised**: Ship this exactly as proposed, with three specifics:
  - **No public speed leaderboards.** Ever. (Track 4 Kahoot speed-axis critique.)
  - **Relatedness via shared artifacts**, not shared rankings — joint problem attempts, shared puzzle of the day, peer-tutor certification ladder (Schoolhouse.world pattern).
  - **Peer-tutor certification** as the aspirational mechanic — Schoolhouse proves at 174K scale that mastery-gated peer tutoring works specifically for test prep.
- **Evidence**: Hanus & Fox 2015 (badge-leaderboard stacks produced LOWER final exam scores than control over 16 weeks); Bai 2020 meta; Kahoot Wang & Tahir 2020; Schoolhouse.world (Track 2); Sailer 2020 collaboration moderator; Przybylski Rigby Ryan 2010 SDT relatedness need.
- **Non-negotiables**:
  - Default-on social matchmaking is banned (FTC v. Epic $245M — Track 8).
  - All co-op features require an active opt-in, not a pre-ticked box.
  - No age-mixed chat.
- **Confidence delta**: **+2** — strongest convergent support across 4 tracks.

### 6. Daily challenge (NOT as Wordle, for legal reasons)
- **Original**: Daily challenge as a Wordle clone for retention.
- **Revised**: **Rewrite this point.** Wordle's retention mechanism is loss aversion — the same thing we're rejecting in Point 9. Replace with a **"puzzle of the day" pattern** (Brilliant's actual approach): one solvable puzzle, archived indefinitely, community solutions thread, **no streak counter visible**, **no missed-day notifications**, **no personal-streak display anywhere in the UI**. If a student solves it, they see a shared counter of how many learners world-wide also solved it that day — relatedness via shared artifact, not personal loss.
- **Evidence**: Duolingo's own blog admits +19% DA-on-streak driven by loss aversion kicking in around day 7 — Track 10 explicit contradiction; Brilliant daily-problems page as positive exemplar (Track 3); ICO Reddit Feb 2026 (Track 8); FTC 2022 dark-patterns staff report (Track 1).
- **Non-negotiables**:
  - **Zero streak counter visible anywhere in the UI.**
  - **Zero missed-day notifications.**
  - **Zero personal-rank leaderboard.**
  - Community solutions thread is moderated and scoped to adults / peer tutors — minor-to-minor UGC is out of scope for v1.
- **Confidence delta**: **original point demoted; revised point +1** — only after rewriting.

### 7. Zero streak guilt / confetti / variable-ratio rewards (now a ship-gate)
- **Original**: Design principle.
- **Revised**: **Ship-gate requirement with a specific kill-list (see below).** Reframe as a sales differentiator for school procurement: "Cena is designed under the ICO Children's Code and FTC 2025 COPPA Final Rule; Duolingo, Prodigy, and Kahoot speed-mode are not."
- **Evidence**:
  - Deci/Koestner/Ryan 1999 (d = −0.40 for engagement-contingent rewards, d = −0.36 completion-contingent, d = −0.28 performance-contingent — Track 6)
  - Hanus & Fox 2015 longitudinal classroom (Track 5)
  - FTC v. Epic $245M (2022), FTC v. HoYoverse $20M (2023), FTC v. Edmodo (2023), FTC 2025 COPPA Final Rule, ICO v. Reddit £14.47M (Feb 2026) — Track 8
  - Duolingo repeated backlash cycles (Track 1)
- **Non-negotiables**:
  - No XP points anywhere in the product.
  - No badges that unlock cosmetics.
  - No loot boxes of any kind (Belgian criminal precedent — Track 8).
  - No confetti animation on routine correct answers — reserve for actual mastery events.
  - Verbal feedback must be competence-information ("you used the discriminant correctly"), not "Correct! +25 XP."
- **Confidence delta**: **+2, promoted to P0 engineering acceptance criterion.**

### 8. MathLive keyboard with haptics and micro-sound
- **Original**: MathLive for math entry, with haptic and sound feedback.
- **Revised**: **MathLive yes, with sharp restrictions on haptics and sound.** Tap-only haptics (event SFX on key press), never continuous vibration; sound is event-only (click, submit, error) and can be disabled in one setting; **no background music with lyrics while the student is doing hard math** (music-under-cognitive-load literature is unambiguous). Brilliant's restraint: use MathLive for Bagrut symbolic entry, but use multiple-choice and numeric input for concept checks — not every answer needs a full symbolic keyboard.
- **Evidence**: No RCT on phone haptics for learning retention (Track 10); Sweller cognitive load (extraneous load from juice); Juul & Begy DiGRA 2016 (juicy games rated 3.74 vs 3.26 but players scored 40k vs 50k); Cheah et al. 2022 systematic review on background music and cognitive task performance.
- **Non-negotiables**:
  - Haptics opt-out in onboarding, not buried in settings.
  - Sound mute must persist across sessions.
  - No continuous background music at all in v1.
  - RTL layout parity for MathLive in Arabic and Hebrew (open risk — MathLive's RTL support needs a pre-launch technical spike).
- **Confidence delta**: **−0.5** — still ship, but with heavier restrictions than proposed.

### 9. Reject Duolingo streak (tighten the wording)
- **Original**: Reject Duolingo-style loss-aversion streak.
- **Revised**: Reject the **loss-aversion framing** specifically. Keep a positive-frame daily cadence signal — Apple Fitness rings style — showing *what you did today*, never *what you will lose tomorrow*. Notifications are opt-in, quiet by default, never guilt-copy, and capped at one per day. The habit engine (Settles & Meeder 2016 Half-Life Regression, spaced repetition cadence) is empirically sound and worth copying; the streak-as-debt wrapper is optional and regulatorily contentious.
- **Evidence**: Track 1 Duolingo teardown; Track 6 SDT (d = −0.40); FTC 2022 dark-patterns staff report; ICO Reddit precedent.
- **Non-negotiables**:
  - No "You're about to lose…" copy anywhere in the product.
  - No artificial-urgency countdowns.
  - No streak-freeze currency of any kind (this is the variable-ratio trap).
  - Notification opt-in is default OFF; one daily max if enabled.
- **Confidence delta**: **+2** if tightened as above.

### 10. Boss fights as Bagrut-style synthesizers
- **Original**: End-of-unit boss fights that are Bagrut-style multi-topic synthesis problems.
- **Revised**: Boss fights are **mixed-topic integrative assessments in costume** — that's an honest framing. They do not teach synthesis; they assess it. Khan Academy's Unit Test and Course Challenge is already the pattern at scale. Framed as capstone ("you're ready to try the synthesis problem"), never as gate ("beat it to unlock"). Failing a boss fight is a no-consequence event — no loss of map progress, no loss of XP (there is none), no loss of characters. Scaffolded remediation via the buggy-rule catalog on failure.
- **Evidence**: Khan Academy Mastery + Unit Test + Course Challenge as existing exemplar (Track 2); Clark 2016 "enhanced design conditions"; Track 9 architectural requirement — the LLM tutor must NEVER compute answers; SymPy/CAS is the sole oracle for correctness and step validity; boss-fight steps must be round-tripped through the symbolic engine.
- **Non-negotiables**:
  - **Symbolic math engine (SymPy/CAS) is the sole source of truth for correctness, final answers, and step validity. The LLM explains; it never computes.** This is the Khanmigo 2023 arithmetic-error lesson (Track 9).
  - Boss fights fight in **standard mathematical notation** (DragonBox transfer lesson — Track 4).
  - Answer-leakage guardrails in the tutor LLM: answer-mask at decode, turn budget, solver-as-oracle (Track 9 MathDial 2023 finding: answer leakage within 3 turns ~40% of the time).
  - No punitive consequences on failure.
- **Confidence delta**: **+1** — pattern is proven, pedagogy claim needs honesty.

---

## Kill-list: legally risky mechanics to remove (from Track 8)

These are now P0 removals, not discussion items. Each has a 2022–2026 enforcement precedent.

1. **Visible streak counter that can go to zero** — ICO Children's Code #13, Reddit £14.47M (Feb 2026).
2. **Guilt / loss / shame push notifications** ("Your tutor misses you", "You're about to lose…").
3. **Variable-ratio reward loops** on answer correctness — Belgian Gaming Commission criminal precedent.
4. **Default-on social / co-op matchmaking** — FTC v. Epic $245M administrative order.
5. **Behavioral advertising to anyone under 18** — FTC 2025 COPPA Final Rule.
6. **Self-declaration-only age gate** — ICO v. Reddit £14.47M (Feb 2026) — Cena needs a second factor from day one (parent email, school SSO, or attribute provider).
7. **Retention of per-student misconception profiles past the session or reuse for engagement ML** — FTC v. Edmodo "Affected Work Product" decree.
8. **Loss-aversion mechanics on failed questions** (lose XP, characters, map progress).
9. **Public leaderboards ranking minors** — GDPR Art. 8 profiling + Israel Privacy Protection Law Amendment 13.
10. **Paid randomized rewards of any kind** — Belgian criminal + FTC v. HoYoverse $20M.

---

## Compliance artifacts required before launch (Track 8)

1. DPIA covering all minor data processing (GDPR Art. 35 + ICO #2 + Israel Amendment 13).
2. Child-appropriate multilingual privacy notice (EN/AR/HE, age-banded).
3. Verifiable Parental Consent flow (COPPA + GDPR Art. 8 — up to age 16 in some member states).
4. Age assurance stronger than self-declaration (Reddit precedent).
5. Data Subject Access Request endpoint (export + deletion, parent-triggerable).
6. Explicit per-field data retention policy (FTC 2025 COPPA final rule).
7. Privacy Protection Officer (mandatory under Israel PPL Amendment 13).
8. FERPA school-official agreement template (US schools).
9. Contractual prohibition on out-of-purpose ML training on student data.
10. Split classroom vs. consumer mode in-product (avoid the Edmodo trap).

---

## Architectural non-negotiables (distilled across all 10 tracks)

These belong in an ADR before any model or feature work starts.

1. **Symbolic math engine is the sole source of truth for correctness.** SymPy or equivalent CAS. The LLM tutor explains, it never computes. Every proposed solution step round-trips through the engine for equation-equivalence and validity. Khanmigo 2023 arithmetic-error incident is the precedent. (Track 9)
2. **Tutor reasons at the step level, not the answer level.** Step-based tutors: d ≈ 0.76. Answer-only tutors: d ≈ 0.31. The tutor parses the student's work, aligns to a solver-generated canonical trace, finds the first divergence, and scaffolds at that step. (Track 9)
3. **Faded worked examples as the hint ladder.** Five rungs: full worked example → partial → next-step prompt → targeted hint → reveal (only after engagement gate). Renkl & Atkinson 2003 d≈0.4–0.6; Aleven & Koedinger 2000 help-abuse mitigation. (Track 9)
4. **Buggy-rule catalog** of 100–200 misconceptions per subject, each with counter-example template and targeted remediation. (Tracks 9, 4)
5. **Expertise-reversal handling** — bypass the ladder for high-proficiency students (Sweller 2019). (Track 9)
6. **LLM guardrails**: answer-mask at decode, turn budget, output-language classifier for EN/AR/HE mixed prompts, fingerprinting of known Bagrut-bank items to refuse cheating, style filter against over-cheerful affect. (Track 9)
7. **Misconception data is session-scoped** — not stored beyond the instructional session, not used to train global models. (Tracks 8, 9)
8. **Age assurance requires a second factor beyond self-declaration** — parent email, school SSO, or attribute provider. (Track 8)
9. **MathLive RTL parity** for Arabic and Hebrew is a pre-launch technical spike, not a post-launch fix. (Track 8, Track 7)
10. **World map is a projection of the curriculum skill graph.** Renaming a topic renames the node. No decoupling. (Tracks 1, 2)

---

## Market wedge recommendation (from Track 7)

The Israeli Bagrut prep market is stuck in "course SKU" mode (₪1,150–₪8,737 one-shot payments, 2015-era video-library UX, no adaptive difficulty, no mobile-native product). The Arab-sector market is real but unowned: ~35,000–40,000 Arab-Israeli 12th-graders per year, ~10,000–15,000 serious STEM candidates, **₪6–9M/year TAM at ₪600 ARPU from the Arab sector alone**. Physics is the weakest leg across every player.

**Recommended wedge order:**

1. Arabic-first 5-unit math + 5-unit physics, subscription at ₪69/month (annual ₪590), single-city pilot (Nazareth / Umm al-Fahm / Rahat).
2. Add 5-unit physics with the Sandbox Physics (Point 4) as the differentiator.
3. Ladder down to 4-unit for volume.
4. Only then cross to Hebrew market with full parity.

**Not competing with**: Yoel Geva on teacher brand (build the "cast of inner voices" brand instead); CET / מט"ח on publishing (court them as a year-2 content partner); Duolingo on streak mechanics (the whole point).

**Do**: scrape the MOE official exam archive at `meyda.education.gov.il/sheeloney_bagrut/` for free authentic practice items; apply to the Ministry's approved-vendor list for school-channel sales in year 2.

---

## Ranked competitor list to study hands-on (one evening each)

By priority for Cena's design team:

1. **PhET "Projectile Motion"** (CU Boulder) — 15 minutes unsupervised. Study the implicit scaffolding from the user side. This is Cena's best-evidence reference for Point 4.
2. **Brilliant.org daily challenge page** — Study the retention mechanic that does not use a streak counter. Direct template for Cena's rewritten Point 6.
3. **Khan Academy Unit Test + Course Challenge flow** — Study how mastery is credited and how decay works. Direct template for Cena's Point 10 and map mastery system.
4. **Schoolhouse.world** — Existence proof for Cena's Point 5. Study the peer-tutor certification ladder.
5. **Khanmigo demo (student view)** — Study the 7-step prompt engineering pipeline, "what do YOU think is the first step?" redirect, and the Writing Coach's 15-word-paste detection.
6. **Monument Valley 1 & 2** (ustwo) — First 10 minutes. How a non-gamer figures out a mechanic with zero text. Reference for Cena's onboarding.
7. **Celeste** (Maddy Makes Games) — The Assist Mode opt-in screen and Chapter 1. Exact wording that reframes failure as kindness.
8. **Disco Elysium** (ZA/UM) — The opening morning. 24 personalities interjecting into one monologue. Reference for Cena's "cast of inner voices" (Point 3 reframing).
9. **DragonBox 12+** — Study the misconception-as-enemy mechanic in action, and the transfer-failure failure mode (Dolonen & Kluge 2014). Cena must learn from both sides.
10. **Alto's Odyssey Zen Mode** (Team Alto / Snowman) — Minimalism and event-only haptics. Reference for Cena's restricted Point 8 (haptics/sound).
11. **Hollow Knight** (Team Cherry) — The Cornifer map-purchase scene. Map as earned discovery, not loot. Reference for Point 1 world-map framing.
12. **Baba Is You** (Hempuli) — Rules as movable objects. The bar for "teach a concept by making the concept interactive."

---

## Confidence delta matrix (10 proposal points × 10 research tracks)

Scale: `++` strongly supports, `+` mildly supports, `0` neutral / no signal, `−` mildly contradicts, `−−` strongly contradicts.

| Point | T1 Duo | T2 Khan | T3 Brill | T4 DBox | T5 Meta | T6 SDT | T7 Bagrut | T8 Legal | T9 AI-tutor | T10 GameDesign |
|---|---|---|---|---|---|---|---|---|---|---|
| 1. World map | 0 | 0 | 0 | 0 | + | + | 0 | − | 0 | 0 |
| 2. Misconception-as-enemy | 0 | 0 | 0 | + | 0 | 0 | 0 | −− | ++ | 0 |
| 3. Named tutor per language | 0 | 0 | 0 | 0 | + | 0 | 0 | 0 | − | − |
| 4. Sandbox-first physics | 0 | 0 | − | 0 | + | 0 | + | 0 | 0 | ++ |
| 5. Co-op not PvP | 0 | ++ | 0 | ++ | ++ | ++ | 0 | + | 0 | 0 |
| 6. Daily Wordle | 0 | 0 | ++ | 0 | 0 | + | 0 | −− | 0 | −− |
| 7. No streak / confetti / var-ratio | ++ | ++ | + | ++ | ++ | ++ | 0 | ++ | 0 | + |
| 8. MathLive + haptics | 0 | 0 | + | 0 | 0 | 0 | + | 0 | 0 | − |
| 9. Reject Duo streak | ++ | ++ | + | 0 | ++ | ++ | 0 | ++ | 0 | 0 |
| 10. Boss fights as synthesizers | 0 | + | 0 | 0 | + | + | 0 | 0 | + | 0 |

**Net scoring (−−=−2, −=−1, 0=0, +=+1, ++=+2):**

| Point | Score | Verdict |
|---|---|---|
| 7. No streak / confetti / var-ratio | +14 | **Ship-gate** |
| 5. Co-op not PvP | +9 | **Ship as proposed** |
| 9. Reject Duo streak | +10 | **Ship with tightened wording** |
| 4. Sandbox physics | +4 | **Pivot to guided sandbox** |
| 10. Boss fights | +4 | **Ship with honest framing** |
| 1. World map | +1 | **Ship if tied to skill graph** |
| 2. Misconception-as-enemy | +1 | **Ship with data-architecture constraints** |
| 8. MathLive + haptics | 0 | **Ship with restrictions** |
| 3. Named tutor per language | 0 | **Demote to delivery wrapper** |
| 6. Daily Wordle | −2 | **Rewrite — replace with community puzzle** |

---

## Publication-quality citations (selected, full lists in per-track reports)

- **Vesselinov & Grego (2012)** Duolingo WebCAPE (industry-funded) — `static.duolingo.com/s3/DuolingoReport_Final.pdf`
- **Settles & Meeder (2016)** Half-Life Regression — ACL 2016 — `research.duolingo.com/papers/settles.acl16.pdf`
- **Jiang, Rollinson, Plonsky et al. (2021)** ACTFL outcomes with Duolingo — *Foreign Language Annals* 54(4):974–1002 — DOI 10.1111/flan.12600
- **Rachels & Rockinson-Szapkiw (2018)** Duolingo no-sig-diff in elementary Spanish — *CALL* 31(1–2):72–89
- **Krashen (2014)** methodological rebuttal to Vesselinov & Grego — `sdkrashen.com/content/articles/krashen-does-duolingo-trump.pdf`
- **Decker-Woodrow et al. (2023)** DragonBox RCT — *AERA Open* 9 — DOI 10.1177/23328584231165919
- **Chan et al. (2023)** Conceptual vs procedural DragonBox — *JCAL* 39(3) — DOI 10.1111/jcal.12798
- **Wang & Tahir (2020)** Kahoot! 93-study review — *Computers & Education* 149 — DOI 10.1016/j.compedu.2020.103818
- **Dolonen & Kluge (2014)** UiO Ark&App DragonBox/Kikora — `uv.uio.no/iped/forskning/prosjekter/ark-app/publikasjoner/downloads/rapport-4-case-matematikk-2014-04-11.pdf`
- **Sailer & Homner (2020)** Gamification meta — *Educational Psychology Review* 32(1):77–112 — DOI 10.1007/s10648-019-09498-w
- **Bai, Hew & Huang (2020)** Gamification on learning outcome — *Educational Research Review* 30:100322 — DOI 10.1016/j.edurev.2020.100322
- **Clark, Tanner-Smith & Killingsworth (2016)** Digital games meta — *RER* 86(1):79–122 — DOI 10.3102/0034654315582065
- **Hanus & Fox (2015)** Longitudinal classroom gamification null — *Computers & Education* — DOI 10.1016/j.compedu.2014.08.019
- **Zainuddin et al. (2020)** Gamification impact review — *Educational Research Review* 30:100326 — DOI 10.1016/j.edurev.2020.100326
- **Deci 1971** Original undermining paper — *JPSP* 18:105–115
- **Deci, Koestner & Ryan (1999)** 128-study undermining meta — *Psych. Bulletin* 125:627–668 — DOI 10.1037/0033-2909.125.6.627
- **Ryan & Deci (2000)** SDT definitions — *Contemp. Ed. Psych.* — DOI 10.1006/ceps.1999.1020
- **Ryan & Deci (2020)** 50-year SDT update — DOI 10.1016/j.cedpsych.2020.101860
- **Shernoff et al. (2003)** Flow in HS classrooms — DOI 10.1521/scpq.18.2.158.21860
- **Przybylski, Rigby & Ryan (2010)** SDT applied to video games — DOI 10.1037/a0019440
- **Huang et al. (2024)** Gamification + SDT meta — DOI 10.1007/s11423-023-10337-7
- **VanLehn (2011)** Step-based ITS meta (d≈0.76) — full cite in Track 9
- **Kulik & Fletcher (2016)** ITS meta (g≈0.66) — full cite in Track 9
- **Aleven & Koedinger (2000)** Help abuse in tutors — full cite in Track 9
- **Renkl & Atkinson (2003)** Faded worked examples (d≈0.4–0.6) — full cite in Track 9
- **Macina et al. MathDial (2023)** LLM tutor answer-leakage arXiv — full cite in Track 9
- **Finkelstein et al. (2005)** PhET Circuit Construction Kit — *Physical Review PER* — full cite in Track 10
- **Wieman et al. (2008)** PhET: Simulations That Enhance Learning — *Science* — `science.org/doi/abs/10.1126/science.1161948`
- **Podolefsky, Moore & Perkins** Implicit scaffolding — ResearchGate 259131102
- **Juul & Begy (2016)** Good Feedback for Bad Players? juice empirical — DiGRA FDG 2016 — `jesperjuul.net/text/juiciness.pdf`
- **Cheah et al. (2022)** Background music and cognitive task performance — *Music & Science* — DOI 10.1177/20592043221134392
- **FTC 2022 Dark Patterns Staff Report** — `ftc.gov/system/files/ftc_gov/pdf/P214800 Dark Patterns Report 9.14.2022 - FINAL.pdf`
- **FTC v. Epic Games (2022)** $520M combined — FTC press release
- **FTC v. Edmodo (2023)** "Affected Work Product" decree — FTC press release
- **FTC 2025 COPPA Final Rule** — Federal Register
- **ICO v. Reddit (Feb 2026)** £14.47M Monetary Penalty Notice — ICO website
- **ICO Age Appropriate Design Code (Children's Code)** — `ico.org.uk`
- **Belgian Gaming Commission loot-box ruling (2018)** — Belgian government
- **Israel Privacy Protection Law Amendment 13** — Knesset primary source

Non-peer-reviewed but load-bearing:
- **Fairplay for Kids Prodigy FTC complaint (Feb 2021)** — `fairplayforkids.org/pf/prodigy/`
- **Markey/Castor congressional letter on Prodigy (2021)**
- **Johns Hopkins CRRE ESSA Tier-3 Prodigy correlational study** — Prodigy marketing blog

## Rejected as unverifiable (do not cite)

- Rumoured "2022 *Journal of Behavioral Addictions*" language-app-anxiety study — no DOI locatable.
- "Shatz 2017 *Lingua Sinica*" — appears to be a content-farm hallucination, no DOI locatable.

---

## Per-track reports

| # | Track | File | Key deliverable |
|---|---|---|---|
| 1 | Duolingo deep dive | [`tracks/track-1-duolingo.md`](./tracks/track-1-duolingo.md) | FTC dark-patterns exposure + habit engine worth copying |
| 2 | Khan Academy + Khanmigo | [`tracks/track-2-khan-khanmigo.md`](./tracks/track-2-khan-khanmigo.md) | Schoolhouse.world as co-op existence proof; restrained-gamification winning stack |
| 3 | Brilliant.org teardown | [`tracks/track-3-brilliant.md`](./tracks/track-3-brilliant.md) | Daily-puzzle pattern without streak counter |
| 4 | Prodigy / DragonBox / Kahoot! | [`tracks/track-4-prodigy-dragonbox-kahoot.md`](./tracks/track-4-prodigy-dragonbox-kahoot.md) | DragonBox RCT evidence; Prodigy 888-questions-per-test-point anti-target |
| 5 | Gamification meta-analyses | [`tracks/track-5-gamification-metaanalyses.md`](./tracks/track-5-gamification-metaanalyses.md) | Cosmetic vs game-based distinction; novelty-decay warning |
| 6 | SDT + flow state | [`tracks/track-6-sdt-flow.md`](./tracks/track-6-sdt-flow.md) | 55 years of undermining-effect evidence; informational feedback escape hatch |
| 7 | Israeli Bagrut prep market | [`tracks/track-7-israeli-bagrut-market.md`](./tracks/track-7-israeli-bagrut-market.md) | Arab-sector 5-unit physics wedge; ₪6–9M/year TAM |
| 8 | Dark patterns + child safety | [`tracks/track-8-dark-patterns-compliance.md`](./tracks/track-8-dark-patterns-compliance.md) | Ship-gate kill-list; compliance artifacts required before launch |
| 9 | Socratic AI tutoring | [`tracks/track-9-socratic-ai-tutoring.md`](./tracks/track-9-socratic-ai-tutoring.md) | Step-based tutor architecture; symbolic solver as oracle |
| 10 | Game design primitives | [`tracks/track-10-game-design-primitives.md`](./tracks/track-10-game-design-primitives.md) | PhET as only load-bearing reference; juice-is-double-edged |

---

## Next actions for the coordinator

1. **Create ADR: "Symbolic math engine is the sole source of truth for tutor correctness."** This is non-negotiable across Track 9 and needs to lock before any LLM work on the tutor.
2. **Create ADR: "Misconception state is session-scoped, not profile-scoped."** This is non-negotiable from Track 8 (Edmodo precedent) + Track 9 (buggy-rule catalog).
3. **Rewrite proposal Point 6** — replace Wordle framing with community-puzzle framing. Track 8 legal red flag + Track 10 internal contradiction.
4. **Elevate Point 7 from design principle to engineering acceptance criterion.** Add to review checklist: "any PR introducing XP, streak counter, confetti, variable-ratio reward, or loot box is auto-rejected."
5. **Draft compliance artifact list (Track 8 §10)** as 10 new tasks in the queue for legal/DPO work before v1 launch.
6. **Technical spike**: MathLive RTL parity for Arabic and Hebrew — must happen pre-launch, not post.
7. **Design spike**: PhET student-interview iteration protocol — set up the think-aloud pipeline before any sim ships.
8. **Market decision**: confirm Arabic-first 5-unit physics wedge (Track 7) with product owner before roadmap lock.
9. **Hands-on competitor study week** — book a calendar week for the team to go through the 12-item ranked list above, one evening each.
10. **Update `CLAUDE.md` / memory** with: (a) ship-gate ban on XP/streak/variable-ratio, (b) symbolic-solver-as-oracle ADR reference, (c) misconception data session-scope rule.

---

*End of synthesis. For the raw per-track reports with full methodology, see the `tracks/` subfolder.*
