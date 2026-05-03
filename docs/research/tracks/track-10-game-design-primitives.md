# Track 10 — Game Design Primitives That Map to Learning

**Scope**: Game-design touchstones cited in the Cena proposal — juice, Octalysis, Monument Valley, Celeste, Hollow Knight, Disco Elysium, Threes / Alto's Odyssey, PhET sandbox, Wordle daily challenge, haptics, study-session sound design, world-map progression. The track asks which of these are empirically grounded for learning transfer and which are aesthetic aspiration that happens to look good in a pitch deck.

**Position**: "Juice" is folk wisdom from commercial game dev with weak, contradictory empirical support in learning contexts. PhET is the only touchstone on the list with a genuine RCT-grade evidence base for transfer. Celeste's Assist Mode and Hollow Knight's map design are useful *interaction patterns* we can steal, not evidence-backed *learning mechanisms*. Octalysis is a taxonomy, not a theory — use it for checklist coverage, never as justification. Wordle's daily-challenge pattern is real and measurable, but the mechanism is loss aversion + social sharing, not pedagogy. Haptics are mostly satisfaction, not retention.

---

## 1. "Juice" — Jonasson & Purho (2012), Juul & Begy (2016)

"Juice It or Lose It" (GDC Europe 2012) is a live-coding demo where Jonasson and Purho add screen shake, particles, squash-and-stretch, audio layers, and "children cheering" SFX to a Breakout clone. It is the canonical craft talk for "game feel." **It is not a study** — no measurements, no RCTs, explicitly framed as craft advice.

The first empirical test of its premise is Juul & Begy, *Good Feedback for Bad Players?* (DiGRA FDG 2016). Two mechanically identical game versions — one minimal, one juiced — with human subjects. Result:

- Subjective quality: **juicy 3.74 vs. minimal 3.26** (juice wins)
- Objective score: **juicy ~40,340 vs. minimal ~49,682** (juice players performed worse)

Juice makes players *like* a game more while *playing it worse*. Cena is not trying to maximize self-reported satisfaction; we are trying to maximize learning outcomes. Kao et al. (CHI 2024) is more charitable — juicy feedback drives curiosity and perceived competence — but still leaves performance and learning as open questions.

**Implication**: juice positive *events* (correct answer, concept mastered, boss defeated). Do not juice the *solving surface*. Screen shake under a physics problem the student is still solving adds extraneous cognitive load (§5). Default: juice transitions and resolutions, keep the workspace calm.

## 2. Octalysis — Yu-kai Chou (2015)

Chou's 8 core drives (Epic Meaning, Accomplishment, Creativity, Ownership, Social Influence, Scarcity, Unpredictability, Loss & Avoidance) are widely cited — but almost all citations use Octalysis as a *taxonomy for organizing interventions*, not as a falsifiable theory. A CEUR workshop paper (2022) notes use is often simplified and the eight drives are hard to operationalize. Systematic gamification reviews find "points, badges, leaderboards" in isolation are weakly motivating, and per Deci & Ryan's SDT can actively undermine intrinsic motivation (overjustification effect, Deci 1971; Ryan & Deci 2000).

**Implication**: treat Octalysis as a *coverage checklist* ("did we hit autonomy/mastery/relatedness, or are we leaning 100% on drive #8 loss-avoidance like Duolingo?"). Do not claim Octalysis "validates" anything. Use SDT for real theory — it *is* empirical and is the better frame for points 1, 6, 7.

## 3. PhET — Wieman, Adams, Perkins

**The only touchstone on the proposal list with RCT-grade evidence for learning transfer.** PhET (CU Boulder, Nobel laureate Carl Wieman) has a 30+ paper peer-reviewed portfolio.

- Wieman et al., *PhET: Simulations That Enhance Learning*, Science 322, 2008.
- Finkelstein et al., *When learning about the real world is better done virtually*, Phys. Rev. PER 2005 — **students using the PhET Circuit Construction Kit outperformed peers using real equipment** on conceptual items. The load-bearing result for "sandbox-first physics."
- Podolefsky, Moore, Perkins, *Guiding without Feeling Guided: Implicit Scaffolding Through Interactive Simulation Design* (2013) — PhET's "implicit scaffolding": constrain the sandbox so productive paths are the easy paths, without popping up instruction.

Two things Cena should steal:

1. **Implicit scaffolding**: lock parameter ranges, starting states, and visible controls so the most salient action is the pedagogically useful one. Don't let students set `g = 0` on screen 1.
2. **Interview-driven iteration**: every PhET sim is tested in 1-on-1 student interviews before ship. Expensive but honest.

**Proposal point 4 (sandbox-first physics) is the best-validated claim in the entire game-design section.**

## 4. Celeste & Assist Mode

No peer-reviewed RCT on Assist Mode and learning — it's a platformer, not ed-tech. But Matt Thorson's design notes (PlayLab Magazine, Tampere Univ., *Too Steep a Hill to Die On*) articulate a philosophy that maps cleanly onto adaptive ed-tech:

- *Additive and granular*: game speed, infinite stamina, invincibility, chapter skip — each independently toggleable.
- *Framed as kindness, not failure*: "Celeste is intended to be a challenging and rewarding experience. If the default game proves inaccessible to you, we hope you can still find that experience with Assist Mode."
- Achievements still unlock in Assist Mode — no punishment for the accessibility path.

**Implication**: dominant ed-tech pattern is "the system chooses difficulty for you" (Knewton, IXL, early Duolingo). Celeste's pattern is *player-configurable assist primitives* (extra time, formula sheet visible, +1 hints, skip misconception-boss). More autonomy-supportive under SDT, and cheaper to build than a black-box adaptive engine.

## 5. Sound design and cognitive load (Sweller)

Proposal point 8 assumes "sound is good." CLT is more careful.

- Sweller's CLT distinguishes intrinsic, extraneous, and germane load. Background media competes for the phonological loop and adds extraneous load.
- Cheah et al., *Background Music and Cognitive Task Performance: A Systematic Review* (Music & Science 2022) — across math/arithmetic studies: **small, mixed, and interacts with task difficulty and personality**. Lyrics are reliably detrimental; hurts *difficult* tasks more than easy ones; hurts introverts more than extroverts.
- Frontiers in Education 2025: preferred instrumental music can improve arithmetic in *low-difficulty* contexts, doesn't replicate for hard problems.

**Implication**: do not auto-play background music over a physics derivation. Event SFX (correct/wrong/complete) are CLT-compatible because they don't overlap the solving window. **Walk back "sound" in point 8 to "event SFX only" by default.** Optional ambient "study mode" (instrumental, user-initiated) is the safe extension.

## 6. Haptics in learning apps

2024 systematic reviews (Hatira et al., *Touch to Learn*, Adv. Intelligent Systems 2024; MDPI Sensors 2024) are cautiously positive but narrow:

- **Strongest evidence**: handwriting, fine motor, geometry/spatial — haptics improve acquisition.
- **Weaker evidence**: conceptual math/physics. Most positive results are motor-skill training, surgical sim, special-needs education.
- **No RCT shows haptics improve long-term retention of a math concept on a phone vs. no-haptic control.**

**Implication**: haptics on MathLive input / correct-answer pulse are defensible as *satisfaction and confirmation*, not retention boosters. Don't claim haptics "help students remember formulas." Claim they make the keyboard feel confident.

## 7. Wordle / daily-challenge pattern

Academic coverage of Wordle is thin, but Duolingo Engineering has published real-world streak data: separating streak mechanics produced **+3.3% Day-14 retention and +19% daily-active-on-streak**. Around day 7, **loss aversion** becomes the primary return driver.

This is the **exact mechanism the proposal rejects in point 7**. Point 6 (Wordle daily) and point 7 (no streak guilt) rely on the same psychological machinery going in opposite directions. Wordle works because you feel bad if you miss a day. A Cena daily with zero continuity penalty may underperform naïve expectations.

**Implication**: a guilt-free daily is probably still fine — Wordle also has social sharing ("🟩🟩🟨⬜⬜") and midnight-refresh curiosity as non-punitive drivers. Do not expect Wordle-level retention. "Missable but not penalized" is the honest midpoint.

## 8. Monument Valley, Hollow Knight, Disco Elysium — UX references

Strongest aesthetic references, weakest empirical ones. None has learning-transfer research. Still useful as interaction patterns:

- **Monument Valley (ustwo)** — designed by an *interaction-design* studio, user-tested on non-gamers who figured it out without a tutorial. The bar for Cena visuals: *no tutorial, no onboarding, first screen teaches itself.*
- **Hollow Knight (Team Cherry)** — map-as-progress: you buy the map from Cornifer *after* wandering an area, then it fills in as you walk. Map is a reward for exploration, not a GPS. For point 1: **don't reveal the whole topic graph up front**. Unlock chapters as the student arrives. Preserves discovery motivation (SDT competence) without fake mystery.
- **Disco Elysium (ZA/UM)** — the 24 "skills" are literal voices in Harry's head, interjecting as domain-specific personalities. Directly transferable to Cena's named-tutor problem (point 3): **the tutor is not a single mascot, it is a cast of inner voices — "the algebra voice," "the geometry voice," "the physics intuition voice"** — that speak when their domain is active. More implementable per LLM prompt than one charismatic voice covering everything.
- **Threes / Alto's Odyssey (Team Alto / Snowman)** — minimal input (tap / long-press), discrete event haptics ("landing a trick, smashing a rock, snapping a vine"), minimalism. Direct template for MathLive keyboard feel and event-SFX rules.

---

## Top 3 findings

1. **PhET is the only touchstone on the list with a real evidence base for physics learning transfer.** Finkelstein et al. 2005 shows PhET sandbox users *outperforming real-lab users* on conceptual items. Point 4 (sandbox-first physics) is load-bearing and defensible — steal PhET's *implicit scaffolding* pattern, not just the "free simulation" aesthetic.

2. **"Juice" is empirically double-edged.** Juul & Begy 2016 found that juiced games are rated higher in satisfaction but produce worse performance. For Cena this means: juice the *reward and transition* moments (correct answer, chapter complete, boss defeated); **keep the solving surface visually calm**. Do not put screen shake under a physics problem the student is still working on.

3. **Streak-rejection (point 7) and Wordle-daily-challenge (point 6) are in tension.** Wordle's measurable retention power comes from the same loss-aversion mechanism Cena explicitly rejects. Expect point 6 to underperform naïve expectations unless it leans hard on curiosity + social share instead.

## Contradictions / aesthetic aspiration without evidence

- **Point 1 (world-map progression)**: no SDT evidence a visible progress map is net-positive for intrinsic motivation. SDT actually predicts it as a potentially controlling cue — "here's everything you haven't done" can be deflating. Hollow Knight variant (map earned *after* discovery) is safer.
- **Point 2 (misconception-as-enemy)**: no academic validation. It's a re-skin of diagnostic item banks (known to work) wearing a sword. Fine as framing; do not claim novel pedagogy.
- **Point 3 (named tutor per language)**: no evidence named personas outperform unnamed AI tutors. Disco Elysium's cast of inner voices is a stronger model than one mascot.
- **Point 8 (haptics + sound)**: haptics don't reliably improve learning. Lyrics hurt difficult math. **Walk both back to "event SFX + tap haptics, no continuous audio."**
- **Point 10 (boss fights as Bagrut synthesizers)**: no evidence boss framing improves transfer, but reasonable *spaced summative assessments in costume*. Honest framing: "mixed-topic integrative assessments, dressed up."

## Patterns Cena can concretely steal

- **PhET implicit scaffolding** → lock physics-sandbox parameters to pedagogically productive ranges by default; unlock wider ranges as prerequisite concepts are mastered.
- **PhET student-interview iteration** → run 5 student interviews per sandbox/problem type before ship. Not A/B tests — *interviews*. Kill the ones that only work with an interviewer's prompt.
- **Celeste Assist Mode** → player-configurable accessibility toggles (extra time, formula sheet, hint count, skip-boss), framed as kindness, no achievement penalty. Supersedes a black-box adaptive engine.
- **Hollow Knight Cornifer map** → chapter/topic map unlocks as the student arrives, not pre-rendered. Map is earned territory, not GPS.
- **Disco Elysium inner voices** → tutor as a *cast* of domain voices (algebra, geometry, physics intuition, exam-strategy) that speak when their domain is active. Easier to scope per LLM prompt than one mascot.
- **Monument Valley non-gamer testing** → test the first screen with people who've never used ed-tech. If they need a tutorial, the design failed.
- **Threes / Alto's Odyssey microinteractions** → minimal input (tap / long-press), discrete event haptics, no ambient audio, event-only SFX.
- **Juul/Begy juice rule** → juice the *resolution*, never the *work surface*.

## Confidence delta on proposal points

| # | Point | Prior conf. | Post-research | Delta | Reason |
|---|-------|-------------|---------------|-------|--------|
| 1 | Session-as-game-loop / world map | Medium | Medium | 0 | No evidence for or against; borrow Hollow Knight's *map earned by exploration*, not GPS. |
| 3 | Named tutor per language | High | Medium-low | **−** | No empirical backing. Reframe as "cast of inner voices" (Disco Elysium pattern) — stronger and more implementable. |
| 4 | Sandbox-first physics (PhET) | High | **Very high** | **+** | Only point on the list with RCT-grade support (Finkelstein 2005). Steal *implicit scaffolding*, not just the sandbox aesthetic. |
| 6 | Daily challenge as Wordle | High | Medium | **−** | Wordle's retention mechanism is loss aversion, which point 7 explicitly forbids. Expect softer retention; lean on curiosity + social share. |
| 8 | MathLive + haptics + sound | High | Medium | **−** | Haptics ≈ satisfaction, not retention. Background music hurts difficult math. Restrict to event SFX + tap haptics only. |
| 10 | Boss fights as Bagrut synthesizers | Medium | Medium | 0 | No evidence bosses help, but they're reasonable mixed-topic summatives in costume. Framing is honest if we admit that internally. |

## Aesthetic references for designers to actually watch/play

Pass this list to the design team as a homework assignment — one evening each, not a full playthrough. Watch the opening 20 minutes and one middle scene.

1. **Monument Valley 1 & 2** (ustwo) — first 10 minutes. *How a non-gamer figures out a mechanic with zero text.*
2. **Hollow Knight** (Team Cherry) — the Cornifer-map-purchase scene in Forgotten Crossroads. *Map as earned reward.*
3. **Celeste** (Maddy Makes Games) — the Assist Mode opt-in screen, then Chapter 1 with and without assist. *The exact wording that reframes failure as kindness.*
4. **Disco Elysium** (ZA/UM) — the opening morning. *How 24 personalities interject into one character's monologue.*
5. **Alto's Odyssey** (Team Alto / Snowman) — the Zen Mode screen. *What minimalism and event-only haptics feel like.*
6. **PhET "Projectile Motion" sim** (CU Boulder) — 15 minutes unsupervised. *What implicit scaffolding looks like from the user side.*
7. **Baba Is You** (Hempuli) — optional, for the design team specifically. *Rules as movable objects — the bar for "teaching a concept by making the concept interactive."*
8. **Threes** (Sirvo) — 5 minutes. *The "one input, infinite depth" template for the MathLive keyboard.*

## Citations

**Peer-reviewed empirical (load-bearing):**

1. Finkelstein, N. D., et al. *When learning about the real world is better done virtually: A study of substituting computer simulations for laboratory equipment.* Physical Review Special Topics - Physics Education Research, 2005. — **The PhET RCT.**
2. Wieman, C., Adams, W. K., Perkins, K. K. *PhET: Simulations That Enhance Learning.* Science 322, 2008.
3. Podolefsky, N. S., Moore, E. B., Perkins, K. K. *Guiding without Feeling Guided: Implicit Scaffolding Through Interactive Simulation Design.* 2013.
4. Juul, J., Begy, J. S. *Good Feedback for Bad Players? A Preliminary Study of 'Juicy' Interface Feedback.* DiGRA/FDG 2016. — **The empirical test of "juice".**
5. Kao, D., et al. *How does Juicy Game Feedback Motivate? Testing Curiosity, Competence, and Effectance.* CHI 2024.
6. Cheah, Y., et al. *Background Music and Cognitive Task Performance: A Systematic Review of Task, Music, and Population Impact.* Music & Science, 2022.
7. Hatira, A., et al. *Touch to Learn: A Review of Haptic Technology's Impact on Skill Development and Enhancing Learning Abilities for Children.* Advanced Intelligent Systems, 2024.
8. Ryan, R. M., Deci, E. L. *Self-Determination Theory and the Facilitation of Intrinsic Motivation, Social Development, and Well-Being.* American Psychologist, 2000. — Relevant to points 1, 6, 7 (why the undermining effect punishes streak-as-punishment designs).

**Game-design theory / industry (marked clearly as not science):**

9. Jonasson, M., Purho, P. *Juice It or Lose It.* GDC Europe 2012. — Canonical craft talk. Not a study.
10. Chou, Y. *Actionable Gamification: Beyond Points, Badges and Leaderboards.* Octalysis Media, 2015. — Taxonomy, not theory.
11. Thorson, M. et al. *Celeste* Assist Mode design notes (in-game and dev interviews); PlayLab Magazine, Tampere Univ., *Too Steep a Hill to Die On.*
12. Team Cherry. *Hollow Knight* level design retrospective, Game Developer. — Map-as-progress as a shipped pattern, not a study.
13. ustwo. *Monument Valley* post-mortem interviews, Design Week / Game Developer. — Non-gamer UX testing as process.
14. Duolingo Engineering. *How the Duolingo Streak Uses Habit Research.* Duolingo Blog, 2023. — First-party retention data; not peer reviewed but useful for calibrating point 6 vs 7.
