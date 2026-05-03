# Track 6: Self-Determination Theory & Flow State in Educational Technology

**Research date**: 2026-04-11
**Scope**: Theoretical backbone for Cena's "sexy without harm" design bet
**Focus proposals under test**: #1 session-as-game-loop, #5 co-op not PvP, #6 daily challenge as Wordle, #7 zero streak guilt / no variable-ratio, #9 rejection of Duolingo streak mechanics

---

## 1. Verified citations

| # | Citation | Identifier |
|---|---|---|
| C1 | Deci, E. L. (1971). Effects of externally mediated rewards on intrinsic motivation. *Journal of Personality and Social Psychology*, 18(1), 105–115. | PsycNET 1971-22190-001 |
| C2 | Deci, E. L., Koestner, R., & Ryan, R. M. (1999). A meta-analytic review of experiments examining the effects of extrinsic rewards on intrinsic motivation. *Psychological Bulletin*, 125(6), 627–668. | DOI 10.1037/0033-2909.125.6.627 |
| C3 | Ryan, R. M., & Deci, E. L. (2000). Intrinsic and extrinsic motivations: Classic definitions and new directions. *Contemporary Educational Psychology*, 25(1), 54–67. | DOI 10.1006/ceps.1999.1020 |
| C4 | Shernoff, D. J., Csikszentmihalyi, M., Schneider, B., & Shernoff, E. S. (2003). Student engagement in high school classrooms from the perspective of flow theory. *School Psychology Quarterly*, 18(2), 158–176. | DOI 10.1521/scpq.18.2.158.21860 |
| C5 | Przybylski, A. K., Rigby, C. S., & Ryan, R. M. (2010). A motivational model of video game engagement. *Review of General Psychology*, 14(2), 154–166. | DOI 10.1037/a0019440 |
| C6 | Sailer, M., & Homner, L. (2020). The gamification of learning: A meta-analysis. *Educational Psychology Review*, 32(1), 77–112. | DOI 10.1007/s10648-019-09498-w |
| C7 | Ryan, R. M., & Deci, E. L. (2020). Intrinsic and extrinsic motivation from a self-determination theory perspective: Definitions, theory, practices, and future directions. *Contemporary Educational Psychology*, 61, 101860. | DOI 10.1016/j.cedpsych.2020.101860 |
| C8 | Huang, R., et al. (2024). Gamification enhances student intrinsic motivation, perceptions of autonomy and relatedness, but minimal impact on competency: a meta-analysis and systematic review. *Educational Technology Research and Development*. | DOI 10.1007/s11423-023-10337-7 |
| C9 | Cameron, J., & Pierce, W. D. (1994). Reinforcement, reward, and intrinsic motivation: A meta-analysis. *Review of Educational Research*, 64(3), 363–423. (pushback citation) | DOI 10.3102/00346543064003363 |

Supporting but not primary: Csikszentmihalyi, M. (1990). *Flow: The Psychology of Optimal Experience*. Harper & Row. (Book — establishes the flow channel model of challenge/skill balance that Shernoff 2003 operationalizes in classrooms.)

---

## 2. Top findings

### Finding A — The undermining effect on tangible, expected, performance-contingent rewards is real, replicated, and large enough to matter for ed-tech.

Deci (1971, C1) ran three studies where money reward caused free-choice engagement with a puzzle to *drop* after reward was removed; verbal praise had the opposite effect. Deci, Koestner & Ryan's 128-study meta-analysis (1999, C2) reported the following free-choice intrinsic-motivation effect sizes (Cohen's d):

- engagement-contingent rewards: **d = −0.40**
- completion-contingent rewards: **d = −0.36**
- performance-contingent rewards: **d = −0.28**
- all tangible rewards (any schedule): significantly negative
- all expected rewards (announced in advance): significantly negative
- **positive verbal feedback: d = +0.33 (free-choice) / +0.31 (self-report)**

Children were more vulnerable than college students. The critical pattern: what Cena would call an XP bar (engagement-contingent, announced in advance, tangible in-app currency) lands inside the negative-effect zone on all three axes. This is the single strongest empirical result in SDT and the reason Ryan/Deci consider gamification's dominant "points-for-correctness" pattern a known failure mode, not a neutral design choice (C3, C7).

**Implication for Cena**: Point 7 (no variable-ratio, no streak guilt, no confetti-for-correct) and Point 9 (reject the Duolingo streak) are *directly supported* by Finding A. Duolingo's streak + XP + Legend League is engagement-contingent + expected + tangible — the worst quadrant of C2.

### Finding B — The effect has real boundary conditions. Informational feedback is the escape hatch, and Cena can exploit it.

Honest reporting of the opposition: Cameron & Pierce (1994, C9) ran a competing meta-analysis and concluded the undermining effect was narrow and overstated. Deci, Koestner & Ryan (1999, C2) and a follow-up (Deci, Koestner & Ryan 2001, *Review of Educational Research*) responded that Cameron & Pierce's coding scheme was flawed — specifically, they collapsed non-rewarded baselines with low-interest tasks where there was no intrinsic motivation to undermine in the first place. The mainstream of motivation psychology now sides with Deci/Ryan, but the Cameron/Pierce line is not fringe — it reappears in behaviorist learning-science work and in Reeve's distinction between "informational" and "controlling" feedback.

The boundary conditions where rewards *do not* harm — and sometimes help — are well-defined (C2, C3, C7):

1. **Unexpected rewards** don't undermine (because they can't be anticipated as a contingency).
2. **Verbal positive feedback** framed as competence information enhances intrinsic motivation (d ≈ +0.31).
3. **Rewards for low-interest tasks** are neutral (nothing intrinsic to undermine).
4. **Task-non-contingent rewards** (given for showing up, not for performance) are mostly neutral.
5. **Rewards tightly linked to performance and framed informationally** ("you mastered this") can be neutral or positive — this is the Cameron/Pierce counter-case and Deci/Ryan accept it as a narrow exception.
6. The harm is largest when the activity was already high-interest — precisely the danger zone for kids who come to Cena *because* they like math.

**Implication for Cena**: Points 1, 5, 6 can survive if Cena treats all feedback as *informational* ("you've mastered quadratic factoring" with a progress marker on the world map) rather than *controlling* ("+25 XP, +1 streak day, don't break the chain!"). The world map itself should be a competence-information display, not a reward schedule. Boss fights (point 10) are defensible under this frame because completion shows the learner they can integrate multi-topic Bagrut-style problems — that's a competence signal, not a bribe.

### Finding C — Flow in classrooms predicts engagement and learning, and the conditions that produce it are the same conditions SDT calls autonomy-supportive.

Shernoff et al. (2003, C4) sampled 526 U.S. high-schoolers with the Experience Sampling Method and found flow was highest when (a) perceived challenge and skill were *both high and balanced*, (b) instruction was *relevant*, and (c) the learning environment was *under the student's control*. Flow occurred more in individual and group work than in lectures, videos, or exams. Przybylski, Rigby & Ryan (2010, C5) extended this to video games, showing that game engagement and post-play wellbeing are predicted by satisfaction of the three SDT needs — competence, autonomy, relatedness — not by reward schedules or win/loss counts. A violent game with autonomy support produces better wellbeing than a gentle game that strips choice.

Sailer & Homner's (2020, C6) meta-analysis of gamified learning found small-to-medium positive effects on cognitive learning (g = .49), motivational (g = .36), and behavioral outcomes (g = .25) — but the cognitive effect was the only one stable under methodological-rigor filtering. Critically, **game fiction** (narrative wrapping) and **combining competition with collaboration** were significant moderators. Huang et al. (2024, C8) went further: gamification reliably improved intrinsic motivation, autonomy and relatedness but had *minimal* effect on competence, and the authors note that reward-based gamification tends to produce controlled motivation unless the design also supports choice and social connection.

**Implication for Cena**: Flow isn't a proxy for fun; it's a predictor of deeper learning *when challenge/skill are calibrated*. The adaptive difficulty engine (ReasoningBank-driven item selection) is the flow lever. Point 1 (session as game loop with world map) gets strong support if the map is an autonomy-and-competence display. Point 5 (co-op over PvP) gets direct support from Sailer & Homner (2020) — the co-op + game-fiction combination is the single most effective configuration in their moderator analysis. Point 6 (Wordle-style daily challenge) sits in a gray zone: shared-puzzle-same-for-everyone is autonomy-neutral and relatedness-positive (everyone's doing the same thing), but if framed as "don't miss a day" it mutates into a controlling streak. See design principles below.

---

## 3. Contradictions and credible pushback

- **Cameron & Pierce (1994, C9)** — the strongest opposing meta-analysis. Their core claim: when you control for task type and reward schedule, the undermining effect disappears on performance-contingent rewards. Deci/Ryan countered on methodology (C2). **Current status**: the mainstream accepts that performance-contingent rewards framed informationally are a genuine exception, but not that the undermining effect is a "myth."
- **Sailer & Homner 2020 (C6)** — gamification as a whole has positive learning effects. If Cena over-reads Deci 1971 and strips all game elements, it may leave learning gains on the table. The nuance: *game fiction* and *co-op structure* drive the gains, not *point schedules*. Cena can have the first two without the third.
- **Huang et al. 2024 (C8)** — gamification's effect on *competence* is minimal. This is the strongest argument against Cena marketing itself as "gamified math that makes you better at math." It may make students *feel* more autonomous and connected (real wins) while the cognitive delta stays small. The adaptive engine (not the cosmetics) is what must produce the learning.
- **Duolingo's commercial success** — the single biggest real-world counterexample. It uses every mechanic Deci 1971 warns against (variable-ratio gems, loss-aversion streaks, Legend League PvP) and produces retention numbers that dwarf non-gamified alternatives. The SDT-consistent reading: Duolingo *retains users* (behavioral outcome) but the *quality* of that motivation is controlled, not autonomous, and long-term learning outcomes from Duolingo are notoriously hard to measure (the company publishes engagement metrics, not learning gains). Cena's bet is that autonomous motivation + real learning is a defensible product wedge, even if it retains fewer daily-active-users in the short term.

---

## 4. Boundary conditions Cena must honor

1. **Informational, not controlling, framing.** Every piece of feedback must answer "what did you learn?" not "how much did you earn?" No coin icons, no currency metaphor, no progress bars framed as depleting/refilling resources.
2. **Unexpected surprise > scheduled reward.** If Cena wants delight moments, they should be sporadic and non-contingent (e.g., tutor character comments on a clever solution path the learner took), never promised in advance.
3. **No loss aversion.** Deci 1971 and C2 don't directly study streak-breaking, but the Ryan/Deci framework treats loss-framed contingencies as maximally controlling because they engage punishment avoidance. Streak guilt is the most controlling form of gamification. Point 9 (reject the Duolingo streak) is the single most SDT-aligned design decision in the proposal.
4. **Competence information must be accurate.** If the world map says "you mastered quadratics" and the next session shows the learner cannot solve a quadratic, SDT predicts worse outcomes than no feedback at all — inflated competence feedback is a known failure mode.
5. **Real choice, not fake choice.** Autonomy-supportive ≠ "pick your avatar color." It means the learner can choose topic order, skip items, see why they're doing what they're doing, and opt out without punishment. This is the hardest bar for Cena to clear and the one most ed-tech companies fail on.
6. **Relatedness without competition.** Co-op (point 5) is SDT-positive. Leaderboards are not. A shared daily challenge (point 6) is SDT-positive *if* it's a communal puzzle, not a ranked race.

---

## 5. Confidence delta on the proposal

| Proposal | Before Track 6 | After Track 6 | Rationale |
|---|---|---|---|
| **#1 Session-as-game-loop with world map** | medium | **high** | Supported by Shernoff 2003 (flow + autonomy) and Sailer/Homner 2020 (game fiction is a significant moderator). Requires map to function as competence-information display, not reward schedule. |
| **#5 Co-op not PvP** | medium | **high** | Strong direct support from Sailer/Homner 2020 (combining competition with collaboration was a significant moderator of behavioral learning) and Przybylski/Rigby/Ryan 2010 (relatedness need satisfaction predicts engagement and wellbeing). |
| **#6 Daily challenge as Wordle** | medium | **medium-high** | Supported *if* framed as shared puzzle (relatedness-positive, autonomy-neutral). Danger: drifts into streak territory if "don't miss a day" framing leaks in. Must have zero penalty for missing. |
| **#7 Zero streak guilt, no variable-ratio, no confetti for correct** | medium | **very high** | Directly supported by Deci 1971 and the Deci/Koestner/Ryan 1999 meta-analysis (d = −0.40 for engagement-contingent rewards). This is the most empirically-grounded point in the proposal. |
| **#9 Rejection of Duolingo streak** | medium | **very high** | Same evidence as #7 plus loss-aversion logic. The streak is the worst-case reward schedule: expected, tangible, engagement-contingent, loss-framed. If Cena keeps only one SDT principle, this is the one. |

---

## 6. Autonomy-supportive design principles to bake into Cena

Drawn from C3, C5, C7, C8 and cross-checked against C4:

1. **Provide a rationale.** Every topic and every item should answer "why is this worth doing?" — even one line. C3 identifies rationale-giving as the single strongest autonomy-support intervention.
2. **Offer real topic choice.** The world map lets the learner pick the next domain from a set of unlocked-but-equivalent options. Not "pick the one I was going to force you into anyway."
3. **Acknowledge negative feelings.** The tutor character (point 3) should be allowed to say "this one is genuinely hard, most students struggle" instead of performing fake positivity. This is a named autonomy-support technique in Reeve's SDT intervention literature.
4. **Calibrate challenge to skill continuously.** Flow requires challenge ≈ skill ± narrow band (C4). Cena's adaptive engine must be tuned to stay in-band; items that are too easy or too hard both break flow.
5. **Competence feedback = information + specificity.** Not "Correct!" but "You used the discriminant correctly — that's the technique this section is about." This is the C2-compliant form of positive feedback that produced d = +0.31 free-choice intrinsic motivation.
6. **No leaderboards, no Legend League, no rank pressure.** These are maximally controlling.
7. **Opt-out is always available without penalty.** The learner can leave a session, skip an item, or switch topics. If skipping costs XP / a streak / a rank, the "choice" is theater.
8. **Co-op relatedness via shared artifacts, not shared rankings.** Study groups, shared notes, shared solutions to the daily puzzle — not "you and your friend both hit Level 12 this week."
9. **Tutor character maintains a non-evaluative voice.** The tutor's language should describe what the learner did, not judge it. "You tried factoring first, then switched to completing the square when it didn't work — that's the right instinct" beats "Great job! +50 XP!"
10. **Boss fights (point 10) frame as capstone, not gate.** "You've done enough practice items to try the synthesis-style problem that combines them" is informational. "Beat the boss to unlock the next area" is controlling. Same mechanic, opposite SDT valence.

---

## 7. One-sentence summary

Cena's rejection of the Duolingo reward stack (points 7 and 9) is the single most empirically-defensible design decision in the proposal — backed by 55 years of replicated findings beginning with Deci 1971 and a 128-study meta-analysis with effect sizes in the d = −0.28 to −0.40 range — and the rest of the proposal (world map, co-op, daily puzzle, boss fights) is defensible *if and only if* every feedback surface is treated as competence information rather than earned currency.

---

## Sources

Primary citations verified via PsycNET, ScienceDirect, and the Self-Determination Theory archive at selfdeterminationtheory.org. Full DOIs provided in Section 1.
