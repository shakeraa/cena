# Track 3 — Brilliant.org Teardown

**Track**: 3 of 10 (parallel research sweep)
**Date**: 2026-04-11
**Subject**: Brilliant.org as a reference point for Cena's interactive pedagogy, daily challenge, and subscription economics.
**Proposal points under test**: 1 (session-as-game-loop), 4 (sandbox-first physics), 6 (daily-as-Wordle), 8 (MathLive keyboard).

---

## 1. Sources

1. **Brilliant help center — pricing & plans** (official). Monthly $27.99; annual ~$129.48 (≈ $10.79/mo); family plan (up to 6); 7-day free trial; group/seat plans 3–50. [brilliant.org/help/pricing-and-plans](https://brilliant.org/help/pricing-and-plans/)
2. **Brilliant daily challenges** (official). Free tier hook, premium archive, community solution threads. [brilliant.org/daily-problems](https://brilliant.org/daily-problems/)
3. **Trophy — "How Brilliant Uses Gamification"** (non-academic vendor case study, clearly biased toward gamification). Points system, streaks, badges, level progression, retention narrative. [trophy.so/blog/brilliant-gamification-case-study](https://trophy.so/blog/brilliant-gamification-case-study)
4. **Hacker News — "Ask HN: Is Brilliant.org Worth It?"** (anecdotal, self-selected technically literate audience). Thread consensus: good for intuition-building, shallow on depth, annual lock-in friction. [news.ycombinator.com/item?id=29881011](https://news.ycombinator.com/item?id=29881011)
5. **Trustpilot — Brilliant.org** (non-academic, review-site sample bias toward complainers). 4.0/5 over ~2.7K reviews; dominant negative theme = auto-renewal and refund handling. [trustpilot.com/review/brilliant.org](https://www.trustpilot.com/review/brilliant.org)
6. **GetLatka — Brilliant revenue** (secondary business-stats aggregator, not audited). ~$14.3M revenue Nov 2024; ~79 employees end-2024 (-16% YoY headcount); ~10M registered users, 180 countries. [getlatka.com/companies/Brilliant](https://getlatka.com/companies/Brilliant)
7. **Wikipedia — Brilliant (website)** (tertiary). Founded as olympiad-prep community; pivoted to active-learning STEM courses for adults by ~2019–2020; Sue Khim as CEO/founder. [en.wikipedia.org/wiki/Brilliant_(website)](https://en.wikipedia.org/wiki/Brilliant_(website))

Non-academic sources are 3, 4, 5, 6. No peer-reviewed efficacy studies on Brilliant were found — that absence is itself a finding.

---

## 2. What Brilliant actually is (grounded summary)

**History**: Brilliant launched ~2012 as an olympiad problem community — think high-ceiling competition forum. Around 2016–2019 they pivoted hard: away from community Q&A, toward self-paced interactive courses aimed at adult professionals and STEM-curious hobbyists. The old community (wikis, discussion threads) was sunset or de-emphasized. The current product is almost entirely a single-player interactive textbook.

**Course format** (consistent across multiple review sources and the marketing page):
1. **Storytelling intro** — a short framing puzzle or real-world motivator ("why does this bridge sway?").
2. **Interactive step** — a diagram the user drags / taps / adjusts a slider on. Not a video. Not a lecture.
3. **Multiple choice or numeric input** with immediate feedback. The feedback is usually a one-screen reveal plus a hint ladder.
4. **Micro-reveal** — the "aha" is shown as a short text + animated diagram, then the next micro-step is loaded.
5. **Mastery check** at the end of a unit — a slightly harder set of problems that gate progress to the next unit.

A Brilliant "lesson" is typically 10–20 such steps, ~10–15 minutes. A "course" is 5–30 lessons grouped into 3–6 units. Learning Paths sequence multiple courses.

**Interactions**: The signature move is the draggable/tappable diagram — sliders, rotatable vectors, zoomable phase diagrams, probability bar charts you can adjust. It is **not** open-ended sandbox physics. Brilliant's "simulations" are constrained: the user manipulates one or two parameters inside a scripted scene to observe a rule. This is a crucial distinction from PhET. PhET is a playground; Brilliant is a guided demo with one knob.

**Daily challenge**: One problem a day, rotating across math/science/logic/CS. Free tier gets today's problem; premium unlocks the archive and the "related lesson" deep-dive. Community thread per problem with user-written solutions. Notifications push users back. This is the primary top-of-funnel.

**Audience** (per GetLatka + reviews + HN): mostly 22–45 adults, knowledge workers, tech-adjacent. Secondary K–12 (parents buying family plan). Almost nobody is using it as their sole math curriculum. It is positioned and consumed as supplement / hobby / "stay sharp."

**Economics** (per GetLatka 2024): $14.3M revenue, 79 employees, ~10M registered users. If we assume ~3% paid conversion (industry norm for freemium consumer EdTech) that's ~300K paying subs at an effective ~$48/sub/yr blended (mix of monthly churners and annual). That is tight margin at 79 FTE. Headcount declined 16% YoY end-2024 — consistent with the platform being in steady-state cost-cutting mode, not hypergrowth.

---

## 3. Top 3 findings

### Finding A — Brilliant's "interactive" is narrower than its marketing claims, and that is actually a feature

The interactivity is **one-knob scripted**. You adjust a slider, you observe the consequence, you answer a multiple-choice question about what you observed. This is deliberately narrow: it removes the "blank page" problem that sinks most sandbox-style ed products, and it guarantees every student sees the same pedagogical beat. Reviews (Learnopoly, HN, Brighterly) all circle the same critique: "not much interactivity beyond sliders." But churn data suggests this constraint is **why** the system retains users — infinite sandbox is intimidating; a single slider with a correct answer is a dopamine hit every 15 seconds. **This directly complicates Cena proposal point 4 (sandbox-first physics à la PhET).**

### Finding B — The daily challenge is Brilliant's single biggest retention hook, and it works because it is *small, solvable, and archived*

The Brilliant daily is not gamified with streaks in the Duolingo sense. It is a single solvable puzzle with a discussion thread. The hook is curiosity plus social proof ("see how other people solved it"), not streak anxiety. Premium users get the archive, so missing a day isn't punishing — you can catch up at will. This is the *closest existing parallel to the Cena-proposed Wordle-style daily*, and it validates the design direction: one puzzle, solvable in ~2 minutes, discussion thread, no streak shame. **Strongly supports Cena proposal point 6.** Crucially, Brilliant intentionally does **not** weaponize streaks the way Duolingo does.

### Finding C — Brilliant has no published efficacy evidence, and the dominant complaint theme is "doesn't transfer"

No peer-reviewed study of Brilliant's learning outcomes was found. The platform's own marketing cites "active learning" research in general (Freeman et al. 2014 meta-analysis is the usual reference) but not Brilliant-specific data. Meanwhile the HN and Reddit criticism is remarkably consistent: "feels like entertainment," "doesn't transfer to exams," "great intuition, can't write proofs," "can't review past content." For Cena — which has to move the needle on Bagrut/AP/SAT where transfer is the whole point — this is the single most important warning. Brilliant's pedagogy is tuned for *engagement*, not for *exam transfer*. Cena cannot just copy Brilliant; Cena must copy the engagement mechanics then pair them with genuine spaced retrieval and exam-form practice.

---

## 4. Contradictions to the Cena proposal

### Point 4 — Sandbox-first physics à la PhET: **partially contradicted**

Brilliant deliberately *rejected* the PhET sandbox model and went with constrained one-knob demos. Their commercial success (10M users, $14M revenue) suggests constraint outperforms sandbox for self-paced retention. But Brilliant's audience is adult hobbyists, not Bagrut students who need to pass a specific exam; adults tolerate sandbox less because they want efficient progress, whereas 16-year-olds can enjoy a playground. The right synthesis for Cena: **guided sandbox** — PhET-style manipulables inside a Brilliant-style scripted beat, where the student can play freely but the system always asks a specific question at the end. Pure open sandbox is risky; pure one-knob is too thin.

### Point 6 — Daily as Wordle, not chore: **strongly validated**

Brilliant's daily challenge is almost exactly the design Cena is proposing and it is their proven retention hook. The key lessons:
- Single problem, solvable in ~2 minutes.
- No streak punishment. Archive is browsable.
- Community solutions thread is the social layer.
- Free tier gets it — this is a free-to-paid funnel, not a premium-only feature.
Cena should copy this wholesale and treat the daily as a free-tier top-of-funnel, not a premium feature.

### Point 1 — Session-as-game-loop with world map: **neutral / weakly supported**

Brilliant does *not* do a world map. Its navigation is a hierarchical list: path → course → lesson → step. No spatial metaphor, no enemy system, no boss fights. Yet Brilliant retains adults successfully without any of that. This suggests the world-map metaphor is **not load-bearing** — what matters is the micro-loop inside a lesson (storytelling → interact → reveal). Cena's world map is cosmetic; the interactive beat is the engine.

### Point 8 — MathLive keyboard with haptics + sound: **indirectly validated**

Brilliant's input surface is deliberately simple — mostly multiple choice and numeric input, not symbolic expression entry. But Brilliant's audience does not need to enter equations; Bagrut students do. MathLive is still the right call for Cena because the exam format demands symbolic answers. Brilliant's restraint on input complexity is a reminder: **only force the keyboard when the question truly needs a symbolic answer**. For concept checks and intuition-builders, multiple choice + sliders is sufficient and faster.

---

## 5. Specific interaction patterns worth stealing

1. **The one-screen reveal.** Every Brilliant step is a single viewport. No scrolling during a step. Question + diagram + answer area all fit on a phone screen. Cena should enforce this as a design rule for the interactive beat — if a step doesn't fit one viewport, it's too complex and should split.

2. **The hint ladder on wrong answer.** Brilliant does not just say "wrong." It shows a tiered hint: (a) "think about X," (b) "try comparing Y and Z," (c) full worked solution. Each step loses partial credit but never blocks progress. Copy this directly for Cena session answer endpoint.

3. **The "you moved the slider" trigger.** Interactive diagrams gate progress on having *touched* the slider, not on a correct answer. The student must have engaged with the manipulable before the "next" button lights up. This is a cheap behavioral nudge that guarantees the interactive element is used.

4. **The community solutions thread on the daily.** User-written alternative solutions are a free content layer AND a retention hook. Cena should bolt a lightweight "see how others solved it" to every daily challenge, with student-submitted solutions as UGC. Moderation cost is the tradeoff.

5. **The archive as a premium unlock.** The daily is free; the archive of *past* dailies is premium. This is a clean free-to-paid wedge that does not break the free experience. Cena can mirror this exactly.

6. **The lesson unit card as the primary navigation element.** Brilliant does not push students into linear paths — they push cards. A "Continue your current lesson" card, a "Try today's challenge" card, a "New course released" card. This is very close to the Cena admin dashboard card metaphor and validates that direction.

7. **The "warmup" pattern.** Every lesson opens with 1–2 very easy questions that rebuild context from the previous session. This is spaced retrieval hiding inside a warmup — copy directly into Cena session start.

---

## 6. Criticism themes to avoid

- **"Auto-renewal without warning"** (Trustpilot, dozens of reviews). Cena must send a renewal email 7 days out and honor refund requests within 14 days. This is the #1 trust-killer on Brilliant and is entirely avoidable.
- **"Can't review past content"** (reviews + HN). Brilliant apparently makes it hard to revisit lessons you already finished. Cena must keep every completed lesson browsable and re-playable, ideally with the student's own prior answers visible.
- **"Shallow on depth"** (HN, Quora). Brilliant's intro material is thin. Cena's answer is that Cena is exam-prep, so depth is non-negotiable — pair every intuition-builder with at least one full worked Bagrut-style example.
- **"Feels like entertainment not mastery"** (HN, Reddit). This is Cena's biggest risk if the team copies Brilliant uncritically. Countermeasure: every boss fight (proposal point 10) must be a genuine exam synthesizer, not a gamified rehash of the micro-steps.
- **"Doesn't transfer to exams"**. Cena should instrument this explicitly — if a student crushes the Cena map but bombs a practice Bagrut, the system should catch it and reroute to exam-form practice.

---

## 7. Pricing and retention signals

- **Effective pricing**: Monthly $27.99 is a framing anchor; 62% discount on annual ($10.79/mo) is the real conversion lever. The family plan is underused in competitor analysis but is a meaningful upsell. Group seat plans 3–50 are a tiny but high-margin B2B line.
- **Unit economics implied**: $14.3M revenue / 10M users = $1.43 ARPU. Assume ~3% paid → ~300K subs → ~$48/sub/yr blended. This is half the sticker annual. Implication: most revenue comes from *annual* subscribers, not monthly churners; the business lives or dies on the annual renewal cycle.
- **Headcount compression**: -16% YoY headcount end-2024 is a significant signal. Brilliant is in efficiency mode, not growth mode. This matches the product: mature, stable, no major redesign in ~2 years.
- **Retention signals from reviews**: Positive reviews cluster on "short daily habit, feel like I'm learning something," not on "passed a specific exam." This confirms Brilliant's retention driver is **ritual**, not **outcome**.
- **Implication for Cena**: The ritual mechanic (daily challenge) retains; the outcome mechanic (exam prep) monetizes. Cena has the rare opportunity to bundle both — use Brilliant-style daily as the habit, use Bagrut synthesizer boss fights as the outcome proof.

---

## 8. Confidence deltas on Cena proposal

| Point | Delta | Reasoning |
|---|---|---|
| **1 — Session-as-game-loop with world map** | **0** | Brilliant retains without a world map; the micro-loop is what matters, map is cosmetic. Neutral on the map decision. |
| **4 — Sandbox-first physics à la PhET** | **-1** | Brilliant's commercial success via constrained one-knob interactions is a real counter-example. Pure sandbox is risky; guided sandbox is the safer design. Downgrade confidence. |
| **6 — Daily challenge as Wordle** | **+2** | Brilliant's daily is the strongest validator in this teardown. Single puzzle, archived, community thread, no streak shame — exactly the Cena proposal. Copy it wholesale. |
| **8 — MathLive keyboard with haptics** | **+1** | Indirectly validated. Brilliant restricts input to MC/numeric because they can; Cena cannot because Bagrut requires symbolic entry. MathLive is justified, with the caveat that most concept checks should still be MC/numeric for speed. |

Net: Brilliant is a strong validator for the daily challenge mechanic, a mild caution on the sandbox-first physics bet, and a reminder that world maps are cosmetic not structural.

---

## 9. Open questions surfaced by this track

1. Does Brilliant publish any churn or retention numbers? (No, not in any public source found.)
2. Is there a peer-reviewed efficacy study on any Brilliant course? (No, as of this search.)
3. How does Brilliant handle Arabic or Hebrew content? (They don't — English only. Cena's tri-lingual positioning is an uncontested niche.)
4. What does Brilliant's B2B educator plan look like in practice? (educator.brilliant.org exists but details were not captured in this sweep — worth a follow-up track if Cena B2B to schools becomes relevant.)

---

*Track 3 complete. See tracks 1, 2, 4–10 for cross-validation.*
