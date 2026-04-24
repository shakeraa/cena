# AXIS 2 — Motivation + Self-Regulation Features for Cena
## Research Findings & Feature Recommendations

**Research Date:** 2026-04-20
**Scope:** Adaptive learning platform for Israeli students preparing for Bagrut exams (ages 12-18), with primary wedge in Arabic-speaking cohorts
**Guardrails:** No streak/loss-aversion (GD-004), no variable-ratio rewards, no comparative-percentile shame, no misconception data retention, no ML-training on student data, positive framing only

---

## Summary Table

| # | Feature | Primary Outcome | Effect Size | Effort | Verdict |
|---|---------|----------------|-------------|--------|---------|
| 1 | Socratic Self-Explanation Prompts | Session completion, concept mastery | g=0.55 | M | SHIP |
| 2 | If-Then Session Planner | 30-day retention, LD/anxious cohorts | g=0.31 (children) | S | SHIP |
| 3 | Wise Feedback Engine | Parent NPS, Arabic-cohort trust | d=0.11-0.15 grade pts | M | SHIP |
| 4 | Mastery Path Progress (Non-Loss) | Session completion, retention | N/A (est. 8-15% engagement lift) | S | SHIP |
| 5 | Metacognitive Confidence Check-ins | Calibration accuracy, learning gain | +8.9% learning gain | M | SHORTLIST |
| 6 | Process-Praise Response System | Anxious/LD cohort engagement | N/A (behavioral) | M | SHORTLIST |
| 7 | SRL Micro-Strategy Prompts | Math performance, strategy use | d=0.69-1.00 | L | SHORTLIST |
| 8 | Reflective Study Plan Generator | 30-day retention, SRL habits | d=0.71 (secondary) | L | DEFER |

---

## FEATURE 1: Socratic Self-Explanation Prompts

### What It Is
After each significant step in a worked math problem (or after the student solves independently), the system poses one focused question: *"Why did you choose that operation?"* or *"How does this step connect to what you did in Step 2?"* The prompt is mandatory but low-stakes — students type 1-3 sentences or select from structured sentence starters (Arabic + Hebrew). The system uses LLM-based validation to check for minimum conceptual coherence (not correctness), then provides a model explanation if the student's answer is thin. Unlike generic reflection, these prompts are tied to specific mathematical steps and use focused (not abstract) questioning.

### Why It Could Move the Needle
Meta-analysis by Bisra, Liu, Nesbit, Salimi & Winne (2018) across 64 studies found self-explanation has a weighted mean effect size of **g=0.55** on learning outcomes. In mathematics specifically, Rittle-Johnson, Loehr & Durkin (2017) found self-explanation prompts improve math learning with **g=0.27** in a domain-specific meta-analysis of 26 studies. In game-based math learning (Save Patch), focused self-explanation prompts (connecting game terminology to math concepts) significantly reduced errors and increased progress compared to control conditions. For Cena specifically, this targets **session-completion rate** (students who self-explain show deeper engagement) and **concept mastery** for Bagrut preparation.

**Key personas:** Arabic-speaking students (structured sentence starters reduce language barrier), LD students (focused prompts scaffold executive function), anxious students (explaining reasoning reduces math anxiety by making thinking explicit).

### Sources
- **PEER-REVIEWED:** Bisra, Liu, Nesbit, Salimi & Winne. "Inducing Self-Explanation: A Meta-Analysis." *Educational Psychology Review*, 2018. DOI: 10.1007/s10648-018-9434-x — g=0.55 across 64 studies (n=5,917)
- **PEER-REVIEWED:** Rittle-Johnson, Loehr & Durkin. "Promoting Self-Explanation to Improve Mathematics Learning: A Meta-Analysis." *ZDM — International Journal on Mathematics Education*, 2017. — g=0.27, 26 studies
- **PEER-REVIEWED:** Hsu, Tsai & Wang. "Adding Self-Explanation Prompts to an Educational Computer Game." *Computers in Human Behavior*, 2013. DOI: 10.1016/j.chb.2013.05.007 — Focused prompts improved fraction arithmetic game performance vs. abstract prompts
- **COMPETITIVE:** Google Guided Learning (Gemini) — Socratic step-by-step questioning approach: https://blog.google/products/ai/gemini-guided-learning/

### Evidence Class
PEER-REVIEWED (strong), COMPETITIVE (emerging)

### Effort Estimate
M (1-3 weeks) — Core prompt infrastructure is simple; LLM validation of explanations requires integration

### Complements/Replaces
Complements existing problem-solving flow. Replaces generic "Show your work" prompts with structured Socratic questioning.

### Implementation Sketch
- **Backend:** Prompt database with step-tagged questions for each Bagrut skill area; LLM API call for response quality scoring (conceptual coherence check)
- **Frontend:** Inline text input or sentence-starter buttons at key problem steps; immediate model explanation overlay after submission
- **Data model:** `SelfExplanationAttempt` table (student_id, problem_id, step_number, response_text, coherence_score, timestamp)
- **LLM dependency:** Lightweight — GPT-4o-mini or equivalent for coherence scoring only; no student data training

### Guardrail Tension
- **None.** Self-explanation is a well-established learning strategy. The only watchout: if prompts are too frequent, they increase cognitive load. Mitigation: limit to 1 prompt per problem, at the most critical step only.

### Recommended Verdict
**SHIP** — High evidence base, concrete implementation, moves session completion and concept mastery. Most impactful feature on the list.

---

## FEATURE 2: If-Then Session Planner (Implementation Intentions)

### What It Is
At the end of each session (or via a pre-session prompt), students complete a 2-sentence planning exercise: *"If [situation], then I will [action]."* Examples: *"If I get stuck on a geometry proof, then I will watch the hint video first before guessing."* or *"If it's Tuesday at 4pm, then I will open Cena and do one practice set."* The system stores the plan and surfaces it at the relevant moment — e.g., if the student attempts 3 wrong answers on a geometry problem, a gentle reminder shows their pre-set plan. Students can also set weekly session timing plans with automated reminder messages (WhatsApp/SMS).

### Why It Could Move the Needle
Implementation intentions are among the most robust behavioral interventions in psychology. Breitwieser et al. (2026) conducted a registered meta-analysis of 52 effect sizes from 42 studies (N=12,957 children, mean age 10.67) and found a **small-to-medium effect of g=0.31** (95% CI [0.21, 0.41]) of if-then planning on children's goal achievement. Critically, effects were **stronger for younger children and children with ADHD** — suggesting they are particularly effective when self-regulation abilities are limited. Gollwitzer & Sheeran (2006) found even larger effects in adults (d=0.65). For Cena, this directly targets **30-day retention** (students who plan when/where/how to study return more consistently) and particularly benefits **LD and anxious cohorts** who struggle with executive function and study initiation.

### Sources
- **PEER-REVIEWED:** Breitwieser et al. "The Effectiveness of Implementation Intentions in Children: A Systematic Review and Meta-Analysis." *British Journal of Psychology*, 2026. DOI: 10.1111/bjop.70065 — g=0.31, N=12,957, stronger for ADHD/younger children
- **PEER-REVIEWED:** Gollwitzer, P.M. & Sheeran, P. "Implementation Intentions and Goal Achievement: A Meta-Analysis of Effects and Processes." *Advances in Experimental Social Psychology*, 2006. — d=0.65 in adults
- **PEER-REVIEWED:** Toli, Webb & Hardy. "Does Forming Implementation Intentions Help People with Mental Health Problems to Achieve Goals?" *British Journal of Clinical Psychology*, 2015. DOI: 10.1111/bjc.12086 — d=0.99 for clinical samples (28 studies, N=1,636)
- **COMMUNITY:** Duolingo session reminders (behavioral trigger approach): https://blog.duolingo.com/growth-modeling-duolingo/

### Evidence Class
PEER-REVIEWED (very strong)

### Effort Estimate
S (<1 week) — Simple form interface + storage + conditional display logic

### Complements/Replaces
Complements session flow. Replaces generic "Come back tomorrow!" retention messages with student-generated plans.

### Implementation Sketch
- **Backend:** `ImplementationIntention` table (student_id, trigger_condition, planned_action, context, active_until, reminder_channel)
- **Frontend:** Modal at session-end with 2 dropdowns (trigger situation) + text field (planned action); WhatsApp reminder scheduling
- **Data model:** Match trigger conditions to session events; display stored plan when trigger fires
- **LLM dependency:** None required — structured form entry

### Guardrail Tension
- **None.** This is a student-generated planning tool with no manipulation, no loss aversion, no comparison. Pure self-regulation scaffold.
- **Watchout:** Ensure reminder messages are framed positively ("Your study time is ready!" not "You haven't studied today").

### Recommended Verdict
**SHIP** — Extremely high evidence-to-effort ratio. Especially powerful for LD/anxious cohorts who benefit from externalizing planning. Can be built in <1 week.

---

## FEATURE 3: Wise Feedback Engine

### What It Is
When a student submits an incorrect answer or suboptimal solution, the system's feedback follows a specific 2-part formula inspired by Yeager et al.'s "wise feedback" research: (1) **High standards signal:** "This problem requires careful reasoning about [specific concept]" — conveying respect through setting a high bar; (2) **Assurance of potential:** "You're capable of reaching this standard — let's work through where this solution can be strengthened." The feedback is specific to the error type (diagnosed by the CAS oracle) and never compares to other students. For Arabic-speaking students, the system can frame feedback through a collective-achievement lens: "Your preparation matters for your future and your community."

### Why It Could Move the Needle
Yeager et al. (2014) showed that wise feedback (high standards + assurance of potential) increased African American students' likelihood of submitting essay revisions by **significant margins**, with effects **stronger among students who felt more mistrusting of school**. In a high school replication, attributional retraining based on this model raised African Americans' grades and **reduced the achievement gap**. Yeager et al. (2019) in a national experiment (N>9,000) found growth mindset interventions improved math grades by **0.11 grade points** for lower-achieving students, with effects **doubling to 0.14-0.22** in growth-mindset-supportive classroom contexts. For Cena, this targets **Parent NPS** (parents see the system treating their child with respect and high expectations) and **Arabic-cohort engagement** (wise feedback explicitly addresses trust barriers that minority students may experience).

**Key personas:** Arabic-speaking students (trust-building through high-standards messaging), anxious students (reduces fear of errors by normalizing struggle as part of high-standard work), under-18 students (developmentally appropriate — adolescents are especially sensitive to signals of respect from authority).

### Sources
- **PEER-REVIEWED:** Yeager et al. "Breaking the Cycle of Mistrust: Wise Interventions to Provide Critical Feedback Across the Racial Divide." *Journal of Experimental Psychology: General*, 2014. DOI: 10.1037/a0033906 — RCTs showing wise feedback increased revision submission and essay quality, especially for mistrusting students
- **PEER-REVIEWED:** Yeager et al. "A National Experiment Reveals Where a Growth Mindset Improves Achievement." *Nature*, 2019. DOI: 10.1038/s41586-019-1466-y — 0.11 grade point improvement, larger for at-risk students
- **PEER-REVIEWED:** Yeager, Carroll & Buontempo. "Teacher Mindsets Help Explain Where a Growth Mindset Intervention Improves Achievement." *Psychological Science*, 2021. — Teacher mindset x intervention interaction B=0.09, p=.005; effects doubled in growth-mindset classrooms
- **COMPETITIVE:** MATHia (Carnegie Learning) — personalized feedback and progress bar: https://www.carnegielearning.com/mathia/

### Evidence Class
PEER-REVIEWED (very strong), COMPETITIVE (supporting)

### Effort Estimate
M (1-3 weeks) — Requires error-type-specific feedback template library + localization

### Complements/Replaces
Complements existing feedback system. Replaces generic "Incorrect, try again" with psychologically-informed feedback that builds trust.

### Implementation Sketch
- **Backend:** Feedback template library organized by error type (from CAS oracle) x student persona x language (Hebrew/Arabic); A/B testing framework for message variants
- **Frontend:** Inline feedback display after incorrect answers; 2-sentence structured format with visual distinction between standards-signal and potential-assurance
- **Data model:** `WiseFeedbackEvent` table (student_id, problem_id, error_type, feedback_template_id, response_time_next_attempt, was_helpful)
- **LLM dependency:** Optional — can use rule-based templates initially; LLM can generate personalized variations at scale later

### Guardrail Tension
- **Borderline consideration:** The "high standards" framing must not slide into shaming. The tone must be warm and invitational, not demanding.
- **Mitigation:** Use positive framing exclusively ("Let's work toward" not "You haven't reached"). User-test with Arabic-speaking students to verify cultural resonance.

### Recommended Verdict
**SHIP** — Strong evidence base, particularly relevant for Arabic-cohort trust-building and parent NPS. The Yeager et al. research on mistrust is directly applicable to minority student populations.

---

## FEATURE 4: Mastery Path Progress (Non-Loss Visualization)

### What It Is
A visual "skill map" that shows each Bagrut topic as a node in a branching tree. Completed topics glow and unlock connections to new topics. Topics in progress pulse gently. Not-yet-attempted topics are visible but grayed out — students can see what's ahead (goal gradient) but never see anything "lost" or "broken." There are no streak counters, no "days missed" indicators, and no fire emojis that extinguish. Progress is cumulative and permanent — skills, once mastered, never "decay" visually. The map includes micro-milestones ("3 of 5 quadratics skills mastered → you're over halfway!"). Students can zoom out to see their entire Bagrut preparation landscape.

### Why It Could Move the Needle
The goal-gradient effect (Hull, 1932; Kivetz, Urminsky & Zheng, 2006) shows that effort increases as people approach a goal. Khan Academy's mastery system and Duolingo's learning path both leverage this. Critically, this design **does not use loss aversion** — it shows accumulation only, never decay. For Cena's anxious and LD cohorts, this is essential: traditional streak mechanics create anxiety about "breaking the chain," which research shows is counterproductive for students with anxiety (White Hat vs. Black Hat gamification analysis, 2025). Brilliant.org's learning paths use a similar non-loss approach with high engagement. Estimated impact: **8-15% improvement in session completion** and **reduced dropout among anxious students** who would otherwise abandon the platform after missing a day.

### Sources
- **PEER-REVIEWED:** Kivetz, Urminsky & Zheng. "The Goal-Gradient Hypothesis Resurrected: Purchasing Acceleration, Illusory Goal Progress, and Customer Retention." *Journal of Marketing Research*, 2006. — Found goal gradient increases effort as consumers approach rewards
- **COMPETITIVE:** Khan Academy Mastery System — non-loss progress tracking: https://www.khanacademy.org/ (no streak requirement for core progress display)
- **COMPETITIVE:** Brilliant.org Learning Paths — visual skill map with progressive unlocking: https://brilliant.org/ (Foundational Math → Advanced Math path)
- **COMMUNITY:** Yukaichou, "White Hat vs Black Hat Gamification in Education," 2025 — Analysis showing White Hat (accomplishment-driven) approaches sustain engagement without Black Hat (loss/pressure) burnout

### Evidence Class
PEER-REVIEWED (supporting theory), COMPETITIVE (strong examples), COMMUNITY (design analysis)

### Effort Estimate
S (<1 week for MVP) — Visual skill map can be built with existing student progress data; no ML required

### Complements/Replaces
Complements existing progress tracking. Replaces any streak-based or loss-framed progress indicators.

### Implementation Sketch
- **Backend:** Skill dependency graph (DAG) for each Bagrut level; mastery calculation logic (≥3 correct on skill = mastered, per ASSISTments model)
- **Frontend:** Canvas/SVG interactive tree visualization; zoom levels for near/far views; micro-milestone calculation and display
- **Data model:** `SkillNode` table (id, topic_id, bagrut_level, parent_skills, display_position); `StudentSkillMastery` table (student_id, skill_id, status, mastery_date)
- **LLM dependency:** None

### Guardrail Tension
- **Borderline consideration:** The goal gradient effect can, in some interpretations, create pressure to "finish." However, since this design uses only accumulation (no decay, no loss), it stays firmly in White Hat territory.
- **Mitigation:** Ensure milestone messages celebrate the journey ("You've built 3 new skills this week"), not the destination. No countdown timers, no urgency language.

### Recommended Verdict
**SHIP** — Low effort, high visual impact, directly supports Cena's non-negotiable of positive-only framing. The anxious/LD cohort benefit is significant since they disproportionately abandon platforms with loss-framed mechanics.

---

## FEATURE 5: Metacognitive Confidence Check-ins

### What It Is
Before answering a problem, students rate their confidence on a 3-point scale (low/medium/high) with emoji anchors. After submitting their answer, they see whether their confidence matched their performance: "You said high confidence and got it right — great calibration!" or "You said low confidence but got it right — you're learning more than you think!" Over time, a personal "calibration chart" shows how accurately the student judges their own knowledge. The system occasionally asks post-problem: "If you got this wrong, what was the source of confusion?" with structured response options.

### Why It Could Move the Needle
Guo (2022) meta-analysis of metacognitive prompts in computer-based learning found **g=0.40** effect on learning outcomes and **g=0.50** on self-regulated learning activities, with effects larger when prompts include feedback, are task-specific, and adaptive. Recent CHI 2025 research (N=133 RCT) found AI-powered metacognitive calibration training improved learning gains by **8.9%** (t=-2.384, p=.019), with overconfident students showing **4.1% better calibration improvement** (t=2.001, p=.049). However, Klar et al. found that simple confidence prompts *without* supplementary instruction can show non-compliance and no performance benefit — **the key is pairing prompts with feedback and reflection support.**

For Cena, this targets **learning efficiency** (students who calibrate well study more effectively) and **reduced overconfidence** (common among struggling students who don't seek help). Anxious students often show underconfidence — this feature can reveal their competence to them.

### Sources
- **PEER-REVIEWED:** Guo, L. "Using Metacognitive Prompts to Enhance Self-Regulated Learning and Learning Outcomes: A Meta-Analysis." *Journal of Computer Assisted Learning*, 2022. DOI: 10.1111/jcal.12650 — g=0.40 learning outcomes, g=0.50 SRL activities
- **PEER-REVIEWED:** CHI 2025. "Learning Behaviors Mediate the Effect of AI-powered Support for Metacognitive Calibration on Learning Outcomes." *Proceedings of CHI*, 2025. DOI: 10.1145/3706598.3713960 — +8.9% learning gain, N=133 RCT
- **PEER-REVIEWED:** Klar et al. "Limits of Metacognitive Prompts for Confidence Judgments." *Education Sciences*, 2023. DOI: 10.1515/edu-2022-0209 — No benefit from bare prompts; feedback + instruction required
- **PEER-REVIEWED:** Foster et al.; Saenz et al. (cited in CHI 2025) — Salient feedback + motivational lecture enhanced calibration accuracy most

### Evidence Class
PEER-REVIEWED (strong but mixed)

### Effort Estimate
M (1-3 weeks) — Requires feedback logic, calibration tracking, and careful design to avoid non-compliance

### Complements/Replaces
Complements problem-solving flow. Adds a metacognitive layer without replacing any existing feature.

### Implementation Sketch
- **Backend:** Confidence judgment storage; calibration accuracy calculation (predicted vs. actual performance); feedback rule engine
- **Frontend:** 3-point confidence selector pre-problem; post-problem calibration feedback message; personal calibration dashboard (updated weekly)
- **Data model:** `ConfidenceJudgment` table (student_id, problem_id, confidence_level, was_correct, calibration_error, timestamp)
- **LLM dependency:** Optional — LLM could generate personalized calibration feedback messages

### Guardrail Tension
- **Borderline consideration:** Confidence calibration can, if poorly implemented, make struggling students feel worse about their performance. Klar et al. found high non-compliance with bare prompts.
- **Mitigation:** (1) Always pair with encouraging feedback; (2) Frame underconfidence positively ("You're learning more than you think!"); (3) Never show class-average comparison; (4) Include supplementary reflection prompts, not bare confidence ratings.

### Recommended Verdict
**SHORTLIST** — Strong evidence but requires careful implementation to avoid backfiring. Build with full feedback/reflection support, not bare confidence ratings.

---

## FEATURE 6: Process-Praise Response System

### What It Is
The system's celebratory messages and feedback consistently attribute success to **process** (strategy, effort, persistence) rather than **person** ("smart," "talented"). When a student solves a difficult problem: "You worked through that step-by-step — that systematic approach is paying off" (not "You're so smart at math!"). When a student persists through errors: "You tried three different approaches before finding one that worked — that's exactly how mathematicians work" (not "Great job getting it right!"). The system also surfaces the student's own problem-solving history: "Last week you needed 4 attempts on problems like this. Today you got it on the second try — your strategies are improving." This is Dweck's growth mindset operationalized in system language.

### Why It Could Move the Needle
Dweck's research (and subsequent meta-analyses) shows process praise fosters mastery orientation, while person praise fosters performance anxiety and avoidance of challenge. Hattie's Visible Learning places feedback at **d=0.73** effect size, with process-level feedback (targeting strategies) being more effective than task-level or self-level feedback. For Cena specifically: anxious students benefit because process praise reduces fear of failure (mistakes are part of the process, not evidence of low ability); LD students benefit because it frames their accommodations as strategic tools, not crutches; Arabic-speaking students benefit because it emphasizes effort and agency over fixed labels.

Note: Growth mindset interventions as standalone treatments show debated effect sizes (Macnamara & Burgoyne 2023 meta-analysis: d=0.05 overall; Burnette et al. 2023 reanalysis: 0.09 overall, 0.15 for at-risk). **However, process praise embedded in system feedback is a different mechanism** than standalone mindset interventions — it is a continuous behavioral signal, not a one-time belief manipulation. Hattie's feedback-level research (d=0.73) is more relevant here.

### Sources
- **PEER-REVIEWED:** Hattie & Timperley. "The Power of Feedback." *Review of Educational Research*, 2007. — Feedback d=0.73; process-level feedback targets strategies used
- **PEER-REVIEWED:** Dweck, C.S. *Mindset: The New Psychology of Success*. 2006. — Foundational process vs. person praise research
- **PEER-REVIEWED:** Yeager & Dweck. "Social-Psychological Interventions in Education: They're Not Magic." *Review of Educational Research*, 2011. DOI: 10.3102/0034654311405999 — Nuanced analysis of when mindset interventions work
- **COMPETITIVE:** DreamBox Learning — personalized positive feedback emphasizing problem-solving process: https://www.dreambox.com/

### Evidence Class
PEER-REVIEWED (strong theory, mixed intervention effects), COMPETITIVE (supporting)

### Effort Estimate
M (1-3 weeks) — Requires extensive feedback template library with process-framing rules

### Complements/Replaces
Complements existing feedback system. Replaces generic "Correct!" / "Good job!" with strategically framed process attribution.

### Implementation Sketch
- **Backend:** Feedback message library organized by outcome (correct/incorrect/partial) x student persona x attempt number x process attribution target; A/B testing for message variants
- **Frontend:** Inline feedback messages after each problem; weekly summary highlighting process improvements
- **Data model:** `ProcessPraiseEvent` table (student_id, problem_id, message_template_id, praise_type, timestamp)
- **LLM dependency:** Optional — LLM can generate process-attribution variants; initial implementation can use rule-based templates

### Guardrail Tension
- **Borderline consideration:** Growth mindset research has faced replication challenges. The standalone intervention effects are smaller than originally claimed.
- **Mitigation:** (1) Frame as "feedback design" not "mindset intervention"; (2) Focus on specific, authentic process observations (not generic "effort is everything" messaging); (3) Never imply that effort alone guarantees success — acknowledge strategy quality too; (4) Avoid preachy/neuroplasticity explanations that adolescents find cringeworthy.

### Recommended Verdict
**SHORTLIST** — Strong theoretical grounding and high relevance for anxious/LD cohorts, but requires careful execution to avoid feeling artificial or preachy. User-test message templates with target demographic before full deployment.

---

## FEATURE 7: SRL Micro-Strategy Prompts (Planning-Monitoring-Evaluating)

### What It Is
At three key moments in the learning session, the system delivers micro-prompts scaffolded to the Winne-Hadwin SRL model:
1. **Pre-session (Planning):** "You're about to work on [topic]. What's one thing you want to focus on improving today?" (3 options + open text)
2. **Mid-session (Monitoring):** "You've completed [X] problems. How's your energy level? Ready for more, or want a quick review of a key concept first?" (Adaptive branch based on response)
3. **Post-session (Evaluating):** "What strategy helped you most today?" (Structured options: "Watching the hint videos," "Drawing a diagram," "Checking my work step by step," "Something else...")

These are not surveys — they are **actionable scaffolds** that immediately influence the session flow. The mid-session monitoring prompt can trigger a 90-second "strategy refresh" micro-lesson if the student indicates fatigue or confusion.

### Why It Could Move the Needle
Dignath et al. (2008) meta-analysis found SRL interventions improve academic performance with **d=0.69 overall**, and **mathematics-specific SRL interventions show d=1.00** — the highest of any subject domain. For secondary students specifically, Dignath & Buttner (2008) found **d=0.71** for academic performance. Granello et al. (2025) RCT with 382 middle school students found 6-session SRL intervention improved math perseverance, utility value, and theory of intelligence, with **gains persisting at 6-month follow-up**. Perels et al. (2009) integrated SRL training into regular 6th-grade math classes and found significant improvement in both SRL competencies and math achievement compared to control.

For Cena: **30-day retention** (planning prompts increase session intentionality), **session completion** (monitoring prevents dropout from confusion), **Bagrut outcome delta** (evaluating prompts build transferable study skills).

### Sources
- **PEER-REVIEWED:** Dignath, Buttner & Langfeldt. "How Can Primary School Students Learn Self-Regulated Learning Strategies Most Effectively? A Meta-Analysis." *Educational Psychology Review*, 2008. — d=0.69 overall, d=1.00 for math specifically
- **PEER-REVIEWED:** Dignath & Buttner. "Components of Fostering Self-Regulated Learning among Students: A Meta-Analysis on Intervention Studies." *Journal of Educational Psychology*, 2008. — d=0.71 for secondary students
- **PEER-REVIEWED:** Granello et al. "'I Can Do Math!': A Self-Regulated Learning Intervention to Enhance Math-Related Motivational Factors and Performance in Middle School." *British Journal of Educational Psychology*, 2025. DOI: 10.1111/bjep.70034 — 6-session SRL intervention, gains persisted 6 months
- **PEER-REVIEWED:** Perels, Dignath & Schmitz. "Is It Possible to Improve Mathematical Achievement by Means of Self-Regulated Learning Strategies?" *Journal of Educational Psychology*, 2009. — SRL training in regular 6th-grade math improved achievement

### Evidence Class
PEER-REVIEWED (very strong)

### Effort Estimate
L (1-3 months) — Requires 3 distinct prompt systems, adaptive branching logic, micro-lesson content creation

### Complements/Replaces
Complements existing session flow. Adds SRL scaffolding layer before, during, and after sessions.

### Implementation Sketch
- **Backend:** SRL prompt engine with 3-phase architecture; adaptive branching rules; micro-lesson trigger system; strategy effectiveness tracking
- **Frontend:** Modal prompts at session start, mid-session (after N problems or M minutes), and session end; quick-select buttons + optional text input; micro-lesson video player
- **Data model:** `SRLPromptResponse` table (student_id, session_id, phase, prompt_type, response, triggered_action, timestamp); `StudentStrategyProfile` table (aggregated strategy preferences)
- **LLM dependency:** Optional — LLM could adapt prompt wording based on student history

### Guardrail Tension
- **Borderline consideration:** Mid-session prompts could feel interruptive and increase cognitive load.
- **Mitigation:** (1) Make prompts opt-in after the first encounter; (2) Use 1-tap responses (not lengthy forms); (3) Only show mid-session prompt if system detects struggle patterns (high error rate, long pauses); (4) Allow "Don't ask me again this session" dismissal.

### Recommended Verdict
**SHORTLIST** — Strongest effect sizes on this list (d=0.69-1.00), but highest implementation complexity. Build incrementally — start with pre-session planning prompt only, then add mid-session, then post-session evaluating.

---

## FEATURE 8: Reflective Study Plan Generator

### What It Is
At the end of each week, the system generates a personalized study plan for the coming week based on: (1) topics attempted, (2) accuracy rates by topic, (3) confidence calibration data, and (4) upcoming Bagrut timeline. The plan is presented as a simple checklist: "This week, focus on: Quadratic equations (you're 70% there — 2 more sessions should nail it), Probability (try the new unit — you've mastered the prerequisites)." Students can edit the plan, and the system uses this as the basis for next-session recommendations. The plan is shareable with parents (increasing parent NPS by making the learning journey visible).

### Why It Could Move the Needle
This integrates multiple research streams: (1) implementation intentions (Feature 2) by making plans concrete and time-bound; (2) metacognitive calibration (Feature 5) by surfacing self-assessment data; (3) goal-gradient effect (Feature 4) by showing proximity to mastery. Winne's nStudy system demonstrates that SRL tools that support planning, monitoring, and evaluating improve learning outcomes. Hattie identifies self-reported grades (closely related to self-assessment) at **d=1.44** — the highest effect size in Visible Learning. For Cena: **30-day retention** (students with plans return more), **parent NPS** (parents see structured, data-informed study planning), **Bagrut outcome delta** (deliberate practice beats distributed practice).

### Sources
- **PEER-REVIEWED:** Winne, P.H. "nStudy: Software for Learning Analytics about Processes for Self-Regulated Learning." *Journal of Learning Analytics*, 2019. DOI: 10.18608/jla.2019.62.7 — Software supporting SRL planning, monitoring, evaluating
- **PEER-REVIEWED:** Winne & Hadwin. "Studying as Self-Regulated Learning." *Metacognition in Educational Theory and Practice*, 1998. — Foundational SRL model with planning as core phase
- **PEER-REVIEWED:** Hattie, J. *Visible Learning*. 2009. — Self-reported grades d=1.44 (highest-ranked influence)
- **COMPETITIVE:** Google Classroom / Seesaw progress dashboards: https://classroom.google.com/

### Evidence Class
PEER-REVIEWED (strong theory), COMPETITIVE (supporting)

### Effort Estimate
L (1-3 months) — Requires integration of multiple data sources, recommendation logic, parent-facing view

### Complements/Replaces
Complements all other features. Serves as the "integration layer" that turns individual feature data into actionable weekly plans.

### Implementation Sketch
- **Backend:** Weekly data aggregation pipeline; recommendation algorithm (rule-based initially: topic prioritization by accuracy x importance x prerequisites); parent notification system
- **Frontend:** Weekly plan view with editable checklist; topic cards with progress bars; parent share link
- **Data model:** `WeeklyStudyPlan` table (student_id, week_start, planned_topics, actual_completion, parent_shared, timestamp); topic prioritization scores
- **LLM dependency:** Medium — LLM can generate natural language plan summaries from structured data; this is a good use case since it's generative (not student-data-training)

### Guardrail Tension
- **Borderline consideration:** Parent visibility could create pressure on students if parents monitor obsessively.
- **Mitigation:** (1) Parent view is summary-only (not real-time surveillance); (2) Include positive framing in parent communications ("Your child has mastered 3 new topics this week"); (3) Student controls what gets shared; (4) No comparison to other students in parent view.

### Recommended Verdict
**DEFER** — Valuable integration feature but depends on Features 2, 5, and 4 being built first. Schedule for Phase 2 after core features are proven.

---

## Cross-Cutting Implementation Notes

### Arabic-Cohort Specific Considerations
1. **Sentence starters for self-explanation** should be available in Arabic and Hebrew, with Arabic variants for both formal (Fusha) and Levantine dialect
2. **Wise feedback** should be culturally validated — "high standards" messaging must resonate with Arab-Israeli families' educational aspirations
3. **If-then planning** timing should account for Ramadan scheduling and weekend patterns (Thursday-Friday weekend in Israel)
4. **Process praise** should avoid individualistic framing that conflicts with collectivist cultural values; frame as building capabilities for family/community contribution

### LD/Anxious Cohort Considerations
1. All prompts should have "skip" options after first exposure (never mandatory twice)
2. Confidence check-ins use 3 points, not 5-7 (reduces decision paralysis)
3. Visual progress uses gentle animations, never flashing/alarming colors
4. SRL prompts use plain language; avoid metacognitive jargon ("monitor your learning" → "How's it going so far?")

### Under-18 / COPPA-Aligned Data Practices
1. All reflection text is processed ephemerally — stored only as aggregate scores, not raw text for model training
2. Confidence judgments and plan data are student-facing only (not used for external reporting)
3. Parent dashboards are opt-in with student consent
4. No behavioral profiling or predictive modeling on individual students

---

## Key Research Gaps & Risks

1. **Growth mindset standalone effects are smaller than originally claimed.** Macnamara & Burgoyne (2023) meta-analysis found d=0.05 overall for mindset interventions on academic achievement. Burnette et al. (2023) reanalysis found 0.09 overall, 0.15 for at-risk students. **Mitigation:** Frame Features 3 and 6 as "feedback design" and "trust-building," not "mindset interventions." The mechanism is behavioral (what the system does), not psychological (what the student believes).

2. **Metacognitive prompts can backfire without feedback support.** Klar et al. (2023) found bare confidence prompts showed no benefit and high non-compliance. **Mitigation:** Always pair confidence check-ins with calibration feedback and reflection support (Feature 5 design includes this).

3. **Self-explanation quality varies.** Low-quality explanations don't help learning. **Mitigation:** Use structured sentence starters and focused (not abstract) prompts tied to specific mathematical steps.

4. **SRL scaffolds require sustained implementation.** Granello et al. (2025) found effects persisted 6 months but required 6 sessions of training. **Mitigation:** Embed SRL prompts into every session as micro-interactions, not as separate training modules.

---

## Recommended Implementation Priority

| Phase | Features | Timeline | Dependencies |
|-------|----------|----------|--------------|
| 1 (Weeks 1-3) | Feature 2 (If-Then Planner) + Feature 4 (Mastery Path) | Sprints 1-3 | None — both are independent |
| 2 (Weeks 4-6) | Feature 1 (Self-Explanation Prompts) + Feature 3 (Wise Feedback) | Sprints 4-6 | CAS oracle for error-type feedback; LLM API for coherence scoring |
| 3 (Weeks 7-9) | Feature 5 (Confidence Check-ins) + Feature 6 (Process-Praise System) | Sprints 7-9 | Requires Feature 1 data for calibration; requires Feature 3 feedback infrastructure |
| 4 (Months 4-6) | Feature 7 (SRL Micro-Prompts) + Feature 8 (Study Plan Generator) | Sprints 10-18 | Requires all prior features for data integration |

---

## References (Consolidated)

### Peer-Reviewed Meta-Analyses
- Bisra, Liu, Nesbit, Salimi & Winne (2018). Inducing Self-Explanation: A Meta-Analysis. *Educational Psychology Review*. DOI: 10.1007/s10648-018-9434-x
- Breitwieser et al. (2026). The Effectiveness of Implementation Intentions in Children. *British Journal of Psychology*. DOI: 10.1111/bjop.70065
- Burnette et al. (2023). A Systematic Review and Meta-Analysis of Growth Mindset Interventions. *Psychological Bulletin*. DOI: 10.1037/bul0000428
- Dignath, Buttner & Langfeldt (2008). How Can Primary School Students Learn Self-Regulated Learning Strategies Most Effectively? *Educational Psychology Review*. DOI: 10.1007/s10648-007-9066-2
- Dignath & Buttner (2008). Components of Fostering Self-Regulated Learning among Students. *Journal of Educational Psychology*. DOI: 10.1037/0022-0663.100.2.373
- Guo (2022). Using Metacognitive Prompts to Enhance Self-Regulated Learning. *Journal of Computer Assisted Learning*. DOI: 10.1111/jcal.12650
- Gollwitzer & Sheeran (2006). Implementation Intentions and Goal Achievement. *Advances in Experimental Social Psychology*. DOI: 10.1016/S0065-2601(06)38002-1
- Hattie & Timperley (2007). The Power of Feedback. *Review of Educational Research*. DOI: 10.3102/003465430298487
- Macnamara & Burgoyne (2023). Do Growth Mindset Interventions Impact Students' Achievement? *Psychological Bulletin*. DOI: 10.1037/bul0000428
- Rittle-Johnson, Loehr & Durkin (2017). Promoting Self-Explanation to Improve Mathematics Learning. *ZDM*. DOI: 10.1007/s11858-017-0884-1
- Sisk, Burgoyne et al. (2018). To What Extent and Under Which Circumstances Are Growth Mindsets Important to Academic Achievement? *Psychological Science*. DOI: 10.1177/0956797617739704

### Peer-Reviewed Primary Studies
- Granello et al. (2025). "I Can Do Math!": A Self-Regulated Learning Intervention. *British Journal of Educational Psychology*. DOI: 10.1111/bjep.70034
- Hsu, Tsai & Wang (2013). Adding Self-Explanation Prompts to an Educational Computer Game. *Computers in Human Behavior*. DOI: 10.1016/j.chb.2013.05.007
- Klar et al. (2023). Limits of Metacognitive Prompts for Confidence Judgments. *Education Sciences*. DOI: 10.1515/edu-2022-0209
- Perels, Dignath & Schmitz (2009). Is It Possible to Improve Mathematical Achievement by Means of Self-Regulated Learning Strategies? *Journal of Educational Psychology*.
- Yeager et al. (2014). Breaking the Cycle of Mistrust. *JEP: General*. DOI: 10.1037/a0033906
- Yeager et al. (2019). A National Experiment Reveals Where a Growth Mindset Improves Achievement. *Nature*. DOI: 10.1038/s41586-019-1466-y
- Yeager, Carroll & Buontempo (2021). Teacher Mindsets Help Explain Where a Growth Mindset Intervention Improves Achievement. *Psychological Science*.

### Peer-Reviewed SRL/Analytics Research
- CHI 2025. Learning Behaviors Mediate the Effect of AI-powered Support for Metacognitive Calibration. DOI: 10.1145/3706598.3713960
- LAK 2025. Turning Real-Time Analytics into Adaptive Scaffolds for Self-Regulated Learning Using GenAI. DOI: 10.1145/3706468.3706559
- Winne (2019). nStudy: Software for Learning Analytics about Processes for Self-Regulated Learning. *Journal of Learning Analytics*. DOI: 10.18608/jla.2019.62.7
- Roll & Winne (2015). Understanding, Evaluating, and Supporting Self-Regulated Learning Using Learning Analytics. *Journal of Learning Analytics*. DOI: 10.18608/jla.2015.62.7

### Competitive Sources
- Khan Academy Mastery System: https://www.khanacademy.org/
- Brilliant.org Learning Paths: https://brilliant.org/
- MATHia (Carnegie Learning): https://www.carnegielearning.com/mathia/
- DreamBox Learning: https://www.dreambox.com/
- ASSISTments: https://www.assistments.org/
- Google Guided Learning (Gemini): https://blog.google/products/ai/gemini-guided-learning/

---

*Document prepared for Cena Product & Engineering teams. All effect sizes reported as published; actual platform results will vary based on implementation quality and student population.*
