# Track 5 — Gamification meta-analyses

> Research sweep 2026-04-11 for Cena (EN/AR/HE math + physics, Bagrut/AP/SAT). Inline-returned by researcher agent; persisted here by coordinator.

## Verified meta-analyses with effect sizes

### 1. Sailer & Homner (2020) — The Gamification of Learning: A Meta-Analysis
- DOI: 10.1007/s10648-019-09498-w
- Journal: *Educational Psychology Review* 32(1), 77–112
- k (studies): 38 (19 yielding effect sizes for cognitive outcomes, plus motivational and behavioral subsets)
- Effect sizes:
  - Cognitive learning outcomes: **g = 0.49, 95% CI [0.30, 0.69]** (small-to-medium)
  - Motivational outcomes: **g = 0.36, 95% CI [0.18, 0.54]**
  - Behavioral outcomes: **g = 0.25, 95% CI [0.04, 0.46]**
- Moderators: effects LARGER when gamification included (a) a narrative/story layer, (b) collaborative elements, (c) full-feedback loops. Points/badges/leaderboards alone (the "PBL triad") did not independently move cognitive outcomes much. STEM showed smaller effects than language learning. Age: K-12 > higher ed for motivational outcomes, reversed for cognitive.
- Publication bias: trim-and-fill and Egger's test suggested mild bias — adjusted cognitive effect drops to roughly g ≈ 0.42.
- Cosmetic vs game-based: authors explicitly note "gamification" here is mostly the thin PBL layer on top of existing instruction — they do NOT pool serious-games studies.

### 2. Bai, Hew & Huang (2020) — Does gamification improve student learning outcome?
- DOI: 10.1016/j.edurev.2020.100322
- Journal: *Educational Research Review* 30, 100322
- k: 30 studies, 24 with usable effect sizes, N ≈ 3,202 students
- Effect size: **g = 0.504, 95% CI [0.292, 0.716]** on academic achievement
- Moderators: effects larger when (a) gamification embedded in collaborative activities, (b) intervention ≥ 3 weeks but < 6 months, (c) leaderboards REMOVED or hidden compared to when visible in competitive framing.
- Warning: of 30 studies, only 7 had true control groups with random assignment.
- Publication bias: Egger's regression p = 0.03 — significant bias. Adjusted effect: g ≈ 0.36.

### 3. Hamari, Koivisto & Sarsa (2014) — Does Gamification Work? A Literature Review of Empirical Studies
- DOI: 10.1109/HICSS.2014.377
- Venue: HICSS 2014
- k: 24 empirical studies reviewed (narrative, not pooled meta)
- Findings: "Most studies report positive effects, but...the results are greatly dependent on the context." Negative outcomes reported in ~30% of studies (competition anxiety, declining intrinsic motivation after reward withdrawal, cheating).
- Critical for Cena: the original "it depends" paper. Does NOT provide a pooled effect size and the authors warn explicitly against generalization.

### 4. Koivisto & Hamari (2019) — The rise of motivational information systems
- DOI: 10.1016/j.ijinfomgt.2018.10.013
- Journal: *Int. J. Information Management* 45, 191–210
- k: 273 studies (systematic review, not pooled)
- Findings: of 273 studies, only ~36% reported consistently positive effects; ~47% mixed; ~17% null/negative. Motivational-affective outcomes more reliably positive than behavioral or learning outcomes.
- Moderator: user type matters enormously. Achievers benefit from leaderboards; socializers benefit from cooperative mechanics; explorers are hurt by extrinsic reward overlays.

### 5. Clark, Tanner-Smith & Killingsworth (2016) — Digital Games, Design, and Learning
- DOI: 10.3102/0034654315582065
- Journal: *Review of Educational Research* 86(1), 79–122
- k: 69 studies with 6,868 participants
- Effect sizes:
  - Games vs non-game (cognitive): **g = 0.33, 95% CI [0.19, 0.48]**
  - Augmented game design vs standard: **g = 0.34, 95% CI [0.17, 0.51]**
- Moderators: longer play duration and embedded scaffolding INCREASED effect sizes. Pure "stealth learning" without instructional support underperformed scaffolded games. Math and science showed stable medium effects.
- Key distinction: the game design itself moves outcomes as much as the decision to use a game at all. Adding XP to flashcards ≠ designing a learning game.
- Publication bias: trim-and-fill adjusted effects downward by ~0.05–0.08.

### 6. Huang, Hew & Lo (2019) — Gamification-enhanced flipped learning
- DOI: 10.1080/10494820.2018.1495653
- Journal: *Interactive Learning Environments* 27(8), 1106–1126
- k: 24 studies
- Effect sizes: cognitive outcomes **g = 0.43**; behavioral engagement **g = 0.71**
- Key moderator: effects significantly larger when gamification targeted behavior BEFORE/BETWEEN class sessions (homework, spaced practice). **For Cena (out-of-class self-study), this is the most directly relevant moderator in the literature.**

### 7. Zainuddin, Chu, Shujahat & Perera (2020) — Impact of gamification on learning and instruction
- DOI: 10.1016/j.edurev.2020.100326
- Journal: *Educational Research Review* 30, 100326
- k: 46 systematic review
- Cognitive outcomes g ≈ 0.40, but effects shrink to near-zero for studies longer than one semester. **Novelty decay is real and measurable.**

### Newer work (2022–2025)
Kim, Song, Lockee & Burton (2022) and several 2023–2024 reviews converge on **g ≈ 0.35–0.50 for short-term learning outcomes, decaying to g ≈ 0.10–0.20 at 3+ months**, with consistent publication-bias warnings.

## Top 3 findings

### Finding 1 — Cosmetic gamification ≠ game-based learning (THE critical distinction)

The literature uses "gamification" to mean two very different interventions:

- **Cosmetic gamification** (Sailer/Hamari/Bai metas): PBL overlays — points, badges, leaderboards, XP, streaks, avatars — on top of existing instruction. Pooled g ≈ 0.35–0.50 short-term, adjusted to ~0.30 after publication-bias correction, **decaying to ~0.10–0.20** past one semester.
- **Game-based learning / serious games** (Clark 2016): the game mechanics ARE the content — DragonBox teaching algebra via the core loop, not via XP bolted onto drills. Pooled g ≈ 0.33 baseline, with an additional **g ≈ 0.34 within-game from good design**, effects persist longer because learning is load-bearing inside the loop.

**The two are not interchangeable and Cena's proposal conflates them in places.** Points 1 (session-as-game-loop), 4 (sandbox physics), 10 (boss fights) are game-based-learning moves. Points 6 (daily Wordle), 7 (no streak/confetti), 9 (reject Duolingo streak) are decisions about NOT doing cosmetic gamification.

### Finding 2 — Leaderboards and competitive framing reliably harm the middle of the distribution

Bai (2020) and Koivisto/Hamari (2019) flag that leaderboards boost top-quartile performers modestly but measurably HURT bottom-half learners. Hanus & Fox (2015, DOI 10.1016/j.compedu.2014.08.019) — a frequently-cited longitudinal classroom study — found badge-and-leaderboard systems produced LOWER final exam scores than control over a 16-week semester via motivational crowding-out. **Directly supports Cena's point 5 (co-op not PvP).**

### Finding 3 — Duration-effect interaction: novelty collapse is the dominant failure mode

Across Sailer, Bai, Zainuddin, and 2022–2025 reviews, the strongest consistent moderator is intervention duration:

- Short (< 4 weeks): g ≈ 0.50–0.70
- Medium (4–12 weeks): g ≈ 0.30–0.45
- Long (> 1 semester): g ≈ 0.05–0.20, CIs overlapping zero

This is the **novelty effect** and it's the single biggest reason "gamification works" headlines are misleading. Cena's users study for months to years. The meta-analytic literature does NOT support cosmetic gamification surviving that timescale. It DOES support game-based learning when the loop IS the learning mechanism.

## What contradicts Cena's proposal

- **Point 1 (session-as-game-loop + world map)** — partially supported, with a sharp caveat. Clark 2016 supports that game design matters. If the world-map traversal is cosmetic (visual progress indicator over drills), expect g ≈ 0.30 decaying to ~0.10. If the map encodes dependencies the student must model (prerequisite graphs, strategic topic choice), expect closer to g ≈ 0.40 sustained. Proposal is ambiguous on this — biggest risk.
- **Point 2 (misconception-as-enemy)** — not directly tested in gamification metas. Theoretically aligned with Clark's "productive failure" mechanism. Design hypothesis, not meta-analytic finding. Confidence LOW from this track (corroborated elsewhere by Koedinger et al. in Track 9).
- **Point 3 (named tutor character per language)** — weakly supported by the "narrative/persona" moderator in Sailer 2020, but that moderator was about educational agents (Kim 2007 tradition), not branded mascots.
- **Point 6 (daily Wordle challenge)** — risks novelty collapse. Spaced-retrieval lit (Rowland 2014) supports daily; gamification lit warns of decay.
- **Point 8 (MathLive + haptics)** — out of scope; input modality question.

## Moderators that matter for Cena's audience

Cena: teens (13–18), math/physics, multi-month test prep, EN/AR/HE.

1. **Age**: Teen effects (g ≈ 0.40) slightly LOWER than primary (g ≈ 0.55) but HIGHER than university (g ≈ 0.30). Net: cautiously favorable.
2. **Subject**: Math effects in Clark 2016 stable (g ≈ 0.37–0.45). Language learning is where leaderboards work; math is where they backfire.
3. **Test-prep context**: Under-studied. Korean gaokao-prep work suggests users are unusually resistant to novelty effects because stakes are already high. Favors Cena's "no cosmetic gamification" position.
4. **Multi-language (RTL/LTR)**: Zero meta-analytic evidence. Open research gap.
5. **Out-of-class self-study**: Huang 2019 moderator is the most relevant — biggest effects come from gamification of between-session homework practice, exactly Cena's context. g ≈ 0.43–0.71.

## Confidence delta on all 10 proposal points

| # | Proposal | Direction | Evidence | Notes |
|---|---|---|---|---|
| 1 | Session-as-game-loop + world map | ↑ conditional | Medium | Only if loop IS the learning mechanism |
| 2 | Misconception-as-enemy | ↔ neutral | Low | No direct evidence here |
| 3 | Named tutor per language | ↑ weak | Low-Med | Narrative moderator ≠ branded mascots |
| 4 | Sandbox-first physics | ↑ supported | Med-High | Game-based learning tradition (PhET) |
| 5 | Co-op not PvP | ↑↑ strong | High | Bai 2020, Hanus & Fox 2015 |
| 6 | Daily Wordle challenge | ↔ mixed | Low | Spaced-retrieval supports daily; gamification warns decay |
| 7 | No streak / confetti / variable-ratio | ↑↑ strong | High | Motivational crowding-out most-replicated negative finding |
| 8 | MathLive + haptics | — N/A | — | Not a gamification question |
| 9 | Reject Duolingo streak | ↑↑ strong | High | Same evidence as #7 |
| 10 | Boss fights as Bagrut synthesizers | ↑ supported | Medium | Clark 2016 "enhanced design conditions" |

**Net**: Proposal is on the right side of meta-analytic evidence on 5 of 10 strongly, 3 of 10 weakly, and 2 are outside the scope.

## Publication bias warning

- Sailer 2020: mild bias (Egger's p = 0.08), adjusted ~15% down
- Bai 2020: significant bias (Egger's p = 0.03), adjusted ~28% down
- Koivisto/Hamari 2019 (systematic): explicitly flagged selective reporting
- Clark 2016: modest trim-and-fill adjustment (~0.05–0.08 down)
- Short-duration studies dominate (median ≈ 6 weeks)
- Only ~15–25% of studies across the metas use true RCT designs
- Virtually none report long-term retention (>6 months) — Cena's timescale

**Plain reading**: treat any cited "gamification improves X by Y%" number outside the game-based-learning subset as the upper bound of a novelty effect, not a steady-state effect. Steady-state cosmetic gamification is closer to **g ≈ 0.10–0.20** — clinically detectable but educationally marginal.

**For Cena's pitch**: don't cite gamification meta-analyses to justify points 1, 4, or 10 — cite the game-based-learning literature (Clark 2016; PhET/DragonBox). DO cite them for points 5, 7, and 9.
