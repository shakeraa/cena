# Iteration 10B: The Pedagogical Controversy -- Does Screenshot-to-Solution Make Students Worse at Math?

**Series**: Student Screenshot Question Analyzer -- Pedagogical Research (Capstone)
**Date**: 2026-04-12
**Iteration**: 10B of 10 (Capstone Pedagogical Article)
**Companion to**: Iteration 10 (Security Capstone)
**Pedagogical Confidence Score**: Conditional -- depends on implementation fidelity

---

## Executive Summary

This is the most important article in the series. Not because it introduces new code, but because it confronts the foundational question that every educational technology must answer: **does this tool actually produce learning, or does it produce the comfortable illusion of learning?**

The objection is not trivial. Fifty years of cognitive science -- from Bjork's desirable difficulties (1994) to Kapur's productive failure (2014) to the generation effect (Slamecka and Graf, 1978) -- converge on a single uncomfortable finding: **making learning easier often makes it worse**. A student who photographs a math problem and receives step-by-step guidance may be experiencing the pedagogical equivalent of watching someone else exercise. The muscles do not grow. The neural pathways do not form. The exam arrives, and the student discovers that they learned nothing.

This article steel-mans that objection with full academic rigor, then examines whether Cena's specific architecture -- where the student *produces* each step and the CAS merely *verifies* -- escapes the trap. The answer is nuanced. Cena is not Photomath. But neither is Cena immune to the fundamental tension between accessibility and productive struggle.

The article cites 28 sources spanning cognitive psychology, mathematics education, intelligent tutoring systems research, and contemporary AI-in-education commentary. It concludes with a research agenda that would provide the empirical evidence Cena currently lacks.

---

## Table of Contents

1. [The Objection (Steel-Manned)](#1-the-objection-steel-manned)
2. [What Leading Math Educators Actually Say](#2-what-leading-math-educators-actually-say)
3. [The Counter-Evidence (Why Cena Differs from Photomath)](#3-the-counter-evidence-why-cena-differs-from-photomath)
4. [The Nuanced Truth](#4-the-nuanced-truth)
5. [Cena's Design Principles (Responding to the Controversy)](#5-cenas-design-principles-responding-to-the-controversy)
6. [Remaining Tensions (Honest Acknowledgment)](#6-remaining-tensions-honest-acknowledgment)
7. [Research Agenda](#7-research-agenda)
8. [Sources](#8-sources)

---

## 1. The Objection (Steel-Manned)

This section presents the strongest possible case against screenshot-to-solution tools. The arguments here are not straw men. They represent the consensus of decades of cognitive science research, and any educational technology that cannot answer them is not worth building.

### 1.1 Bjork (1994, 2011): Desirable Difficulties

The most robust finding in the science of learning is counterintuitive: **conditions that make learning appear to progress faster often produce worse long-term retention and transfer, while conditions that slow apparent learning often produce better outcomes**.

Robert Bjork coined the term "desirable difficulties" in 1994 and, with Elizabeth Bjork, formalized the framework in their 2011 chapter "Making Things Hard on Yourself, But in a Good Way." The core mechanism relies on a distinction between two types of memory strength:

- **Storage strength**: how deeply embedded and interconnected a memory representation is with related knowledge. Storage strength only increases; it never decreases.
- **Retrieval strength**: how easily a memory can be accessed at any given moment. Retrieval strength fluctuates and decays without practice.

The critical insight is that **low retrieval strength at the time of practice is precisely the condition that drives increases in storage strength**. When retrieval is easy, the brain invests little in strengthening the trace. When retrieval is difficult -- when the student struggles to remember, to generate, to reconstruct -- the act of successful retrieval drives deep encoding.

Four well-established desirable difficulties are:

1. **Spacing**: distributing practice over time rather than massing it produces slower apparent learning but dramatically better retention (Cepeda et al., 2006).
2. **Interleaving**: mixing different problem types rather than blocking by type produces more errors during practice but superior discrimination and transfer (Rohrer and Taylor, 2007).
3. **Testing/retrieval practice**: testing oneself rather than re-reading produces slower study progress but far better long-term recall (Roediger and Karpicke, 2006).
4. **Generation**: producing an answer rather than recognizing it produces stronger memory traces (Slamecka and Graf, 1978).

**The objection to Cena**: a screenshot tool that extracts a question, presents it in structured form, and provides step-by-step scaffolding is systematically removing desirable difficulties. The question extraction removes the difficulty of parsing a textbook page. The structured presentation removes the difficulty of identifying what type of problem this is. The scaffolded steps remove the difficulty of planning a solution strategy. Each of these removals feels helpful in the moment but may prevent the deep encoding that produces durable mathematical knowledge.

As Bjork and Bjork (2011) write: "conditions of learning that make performance improve rapidly often fail to support long-term retention and transfer, whereas conditions that create challenges and slow the rate of apparent learning often optimize long-term retention and transfer."

A tool optimized for the student's momentary experience of progress may be optimized against the student's actual learning.

### 1.2 Kapur (2008, 2012, 2014): Productive Failure in Mathematics

If Bjork provides the general framework, Manu Kapur provides the mathematics-specific evidence. Kapur's "productive failure" research program, conducted primarily with secondary school students in Singapore (14-15 year olds -- the same age range as Cena's Bagrut students), demonstrates that **struggling with a complex problem before receiving instruction produces significantly better conceptual understanding and transfer than receiving instruction first**.

The experimental design is clean. Two groups of students learn the same mathematical concept (e.g., standard deviation, rate of change). The Direct Instruction (DI) group receives a clear explanation followed by practice problems. The Productive Failure (PF) group is given a complex, novel problem to work on first -- without instruction -- and only receives the formal explanation afterward.

Key findings:

- In a 2012 study with 133 ninth-grade students, PF students significantly outperformed DI counterparts on conceptual understanding and transfer without any loss of procedural fluency (Kapur, 2012).
- In a 2014 study published in *Cognitive Science*, both groups achieved equivalent procedural knowledge. But PF students demonstrated significantly greater conceptual understanding and ability to transfer to novel problems (Kapur, 2014).
- A 2021 meta-analysis by Sinha and Kapur reviewing problem-solving-before-instruction (PS-I) designs found a significant moderate effect in favor of PS-I (Hedge's g = 0.36, 95% CI [0.20, 0.51]). When implementations had high fidelity to Productive Failure principles, effects were even stronger (g = 0.37 to 0.58) (Sinha and Kapur, 2021).

**The mechanism**: during the initial struggle, students generate multiple representations and solution methods (RSMs). Most of these are wrong. But the act of generation activates prior knowledge, reveals knowledge gaps, and creates "preparation for future learning" -- a readiness to make sense of the instruction that follows. Students who skip the struggle miss this preparation phase.

**The objection to Cena**: a step-solver that presents the problem in pre-structured form and provides hints when the student is stuck is eliminating the productive failure phase. The student never has to generate their own representations. They never experience the confusion that prepares them for insight. They proceed step by step through a pre-determined solution path, and while they may be producing algebraic expressions at each step, they are not deciding *which* expressions to produce or *why*.

### 1.3 The Generation Effect (Slamecka and Graf, 1978)

The generation effect is one of the most replicated findings in memory research. Slamecka and Graf (1978) demonstrated across five experiments that words generated by subjects (e.g., given "RAPID" and the rule "synonym", producing "FAST") were remembered significantly better than the same words simply read. The effect held for cued and uncued recognition, free and cued recall, and confidence ratings. It persisted across encoding rules, timed or self-paced presentation, and within-subjects and between-subjects designs.

A 2020 meta-analysis by McCurdy, Viechtbauer, Skinner, and Frankenberg confirmed the robustness of the effect and examined moderators (McCurdy et al., 2020). The generation effect is not limited to word lists; it extends to mathematical problem solving, where students who generate solutions (even incorrect ones) show better retention than students who study provided solutions.

**The objection to Cena**: there is a spectrum of generation. At one end, the student generates every aspect of the solution -- identifying the problem type, selecting a strategy, executing each step, and verifying the answer. At the other end, the student copies a provided solution. Cena's step-solver sits somewhere in the middle: the student generates algebraic expressions, but within a pre-structured framework that specifies the number of steps, the order of operations, and (at full scaffolding) the type of manipulation required at each step.

The concern is that the generation effect depends on the *depth* of generation. Producing "x = 3" when you have been told "solve for x by dividing both sides by 4" may not activate the same deep processing as deciding, unprompted, that division is the appropriate next move.

### 1.4 The Expertise Reversal Effect (Kalyuga, 2003, 2007)

Slava Kalyuga's expertise reversal effect adds a developmental dimension to the scaffolding debate. The core finding: **instructional guidance that is essential for novices becomes actively harmful for more experienced learners**.

In the 2003 paper with Ayres, Chandler, and Sweller, the authors demonstrated that worked examples -- the gold standard of novice instruction under cognitive load theory -- produced negative effects when used with students who already possessed the relevant schemas. The worked examples forced expert-like students to reconcile two sources of information (their internal schemas and the external guidance), creating additional cognitive load rather than reducing it (Kalyuga et al., 2003).

The 2007 paper formalized the instructional implications: learning environments must adapt. What helps a novice hurts an expert. Scaffolding must be faded as competence grows (Kalyuga, 2007).

**The objection to Cena**: even if step-by-step scaffolding helps novice students, it hurts advanced students. A Bagrut 5-unit student who already understands integration by parts does not need a step-solver telling them to "apply the formula integral(u dv) = uv - integral(v du)." Being told to apply a formula they already know wastes working memory on processing redundant information.

If Cena's scaffolding does not fade -- if every student, regardless of mastery, experiences the same level of guidance -- the tool actively degrades learning outcomes for the students who need it least but might use it most (high-achieving students seeking practice efficiency).

### 1.5 Cognitive Load Theory and the Scaffolding-as-Crutch Problem (Sweller, 1988)

John Sweller's foundational 1988 paper, "Cognitive Load During Problem Solving: Effects on Learning," established that conventional problem-solving (means-ends analysis) is actually a poor learning strategy because it consumes working memory capacity that would otherwise be available for schema construction. Worked examples, which eliminate the problem-solving search, free cognitive resources for learning.

But this creates a paradox relevant to screenshot-to-solution tools. Sweller and Cooper (1985) showed that learners given worked examples took one-sixth the time and made fewer errors than problem-solvers. However, the benefit was specific to structurally identical problems. The worked-example students had not learned to recognize problem types -- they had learned to execute procedures within a narrow template.

This is the scaffolding-as-crutch concern: **scaffolding that is never removed produces procedural fluency without conceptual understanding**. The student can follow the steps but cannot decide which steps to take. They can execute the algorithm but cannot recognize when the algorithm applies. In clinical terms, they develop "inert knowledge" -- knowledge that exists in memory but is not activated in appropriate contexts (Whitehead, 1929; Bransford, Sherwood, Vye, and Rieser, 1986).

Recent research has sharpened this concern for AI-specific contexts. A 2025 paper in *Frontiers in Psychology* found that students using ChatGPT answered 48% more problems correctly but scored 17% lower on concept understanding tests (reported in Alsobhi et al., 2025). The students had learned to produce correct answers without understanding why those answers were correct -- the definition of scaffolding-as-crutch.

**The objection to Cena**: a step-solver that always tells the student what kind of step to take next, even if the student fills in the algebraic details, is building procedural fluency at the expense of strategic competence. The student learns to execute within a template without learning to select the template. When the exam presents an unfamiliar problem -- one that does not map neatly onto a pre-structured step sequence -- the student discovers that their "knowledge" was actually the scaffolding's knowledge.

### 1.6 The Composite Objection

Taken together, these five research programs form a devastating argument:

1. **Learning requires struggle** (Bjork). Tools that remove struggle remove learning.
2. **Mathematics specifically benefits from failure before instruction** (Kapur). Pre-structured step-solvers eliminate the failure phase.
3. **Self-generation is the deepest form of learning** (Slamecka and Graf). Partially guided generation is weaker than fully autonomous generation.
4. **Scaffolding that persists past its usefulness becomes harmful** (Kalyuga). One-size-fits-all guidance hurts advanced students.
5. **Scaffolding that substitutes for strategy selection produces inert knowledge** (Sweller). Students learn to follow steps without learning to choose steps.

The composite picture: **Cena's screenshot analyzer is Photomath with better UX. It photographs a problem, structures a solution path, and walks the student through it. The student feels like they are learning because they are producing expressions at each step. But the deep cognitive work -- identifying the problem type, selecting a strategy, deciding on an approach, recovering from dead ends, experiencing the generative confusion that precedes insight -- has been done by the system. The student is exercising their pencil, not their brain.**

This is the strongest version of the objection. It deserves a serious answer.

---

## 2. What Leading Math Educators Actually Say

The theoretical framework above does not exist in a vacuum. Leading mathematics educators and researchers have weighed in -- some on this exact controversy, some on closely adjacent questions -- and their positions are more nuanced than either side of the AI-in-education debate typically acknowledges.

### 2.1 Jo Boaler: Mathematical Mindsets and the Value of Struggle

Jo Boaler, Professor of Mathematics Education at Stanford, is the most prominent advocate for the position that struggle is not an unfortunate side effect of learning math but the *mechanism* through which mathematical understanding develops. Her book *Mathematical Mindsets* (2015, 2nd edition 2022) argues that:

- Mistakes and struggle should be valued as opportunities for brain growth, not punished as failures.
- Open-ended tasks with "low floors and high ceilings" allow all students to access mathematics while providing room for challenge.
- Teaching practices should be aligned with growth mindset principles -- not by telling students to "try harder," but by designing tasks that naturally require intellectual engagement.

A 2024 study led by Boaler at Stanford demonstrated significant gains in student achievement and engagement even through relatively short interventions of two to four weeks, with effectiveness across diverse student populations including those who have historically struggled with mathematics.

**Boaler's position on AI tools**: Boaler has not directly condemned AI math tutoring, but her framework implies deep skepticism of any tool that reduces the struggle component. If "mistakes are opportunities for brain growth," then a system that prevents mistakes by scaffolding every step is preventing brain growth. The Boaler framework would endorse AI tools only if they preserved open-ended exploration and did not converge students onto a single pre-determined solution path.

**Implication for Cena**: Cena's step-solver, as currently designed, converges on a single solution path. The steps are numbered. The expected expressions are predefined. Even if the student generates each expression, they are generating within a constraint that eliminates the open-endedness Boaler considers essential.

### 2.2 Dan Meyer: "Math Class Needs a Makeover" -- and AI Is Not It

Dan Meyer, formerly Chief Academic Officer at Desmos (now at Amplify), is perhaps the most thoughtful critic of AI in mathematics education. His Substack *Mathworlds* has become the primary venue for this critique, and his position is grounded in empirical observation rather than theoretical speculation.

Key arguments from Meyer's 2024-2025 writings:

**Teachers are not adopting AI math tools.** A Spring 2024 RAND survey found that 82% of math teachers have never used AI tools for mathematics teaching, with only 1% reporting "often" usage. Meyer argues this is not technophobia -- teachers are drowning and would happily use a good tool. The tools are not good.

**The interface is wrong for math.** Math instruction relies on sketching graphs and shorthand notation, not lengthy text explanations. Text-based chatbot interfaces are fundamentally mismatched with how mathematical thinking is communicated. Meyer notes that converting visual math work into adequate text descriptions requires "pages and pages," losing information in translation.

**Students do not want to interact with chatbots.** Meyer observes that K-12 students, when presented with AI tutoring tools, show minimal engagement. The chatbot-as-tutor model replicates the lecture model (one-to-one between student and system) that progressive math education has been trying to escape for decades.

**The evidence is "extremely weak."** After three years of AI in education, Meyer states: "the evidence that AI can help schools with learning to read, write, do math, and be nice is extremely weak." He warns that the biggest risk is opportunity cost -- schools investing in uncertain AI solutions while proven interventions (high-dose human tutoring, smaller class sizes) go unfunded.

**Implication for Cena**: Meyer's critique applies less to Cena than to pure chatbot tutors, because Cena's step-solver uses a structured mathematical interface rather than a text chat. But the deeper concern -- that the tool may divert attention from what actually works -- remains. If a school adopts Cena instead of investing in human tutoring, and Cena's outcomes are no better than a textbook, the school has spent money and lost time.

### 2.3 Sal Khan: Khanmigo and the Socratic Guardrail

Sal Khan represents the most optimistic position among serious educators. When Khan Academy saw AI's capabilities in 2023, Khan's immediate response was: "We needed to put in place the right guardrails. This cannot be a cheating tool. It must be Socratic. It needs to be transparent for educators."

Khanmigo's design philosophy:

- **Never give the answer.** Khanmigo asks probing questions instead of providing information. In math, it hints at next steps rather than solving.
- **Socratic dialogue.** Based on the premise that Socrates asked questions to develop critical thinking through dialogue, not through information delivery.
- **Grounding in verified content.** Guardrails prevent hallucinations by anchoring responses to Khan Academy's curated content library.
- **Cheating detection.** The system detects patterns consistent with answer-copying and redirects to guided exploration.

A 2026 Chalkbeat profile noted that Khan has been "rethinking how AI will change schools," acknowledging that the initial hype cycle has given way to more measured expectations. The key insight: one-on-one instruction tailored to student needs is effective -- "If a student is finding something easy, then a tutor can move ahead or go deeper. If they're struggling, a tutor can slow down."

However, an EdSurge analysis of a viral demo featuring Khan and his son raised questions about whether Khanmigo's Socratic approach actually works in practice, noting that the system sometimes gave away more than intended and that the "Socratic questioning" felt mechanical rather than responsive.

**Implication for Cena**: Khan's guardrails align with Cena's design philosophy -- the student does the work, the system verifies. But Khanmigo relies on LLM-generated Socratic questions, which are brittle and occasionally reveal answers. Cena's approach (CAS verification of student-produced expressions) is more robust because it does not require the AI to model the student's thinking -- it simply checks whether the expression is symbolically correct. The risk is different: Khanmigo might accidentally give the answer through poor Socratic questioning; Cena might accidentally eliminate strategic thinking through over-structured step sequences.

There is a deeper philosophical difference. Khanmigo's guardrails are *behavioral* -- the LLM is instructed not to give answers, but the instruction is enforced by prompt engineering, which can be circumvented. Cena's guardrails are *architectural* -- the CAS engine literally cannot produce a solution because it is a verification engine, not a generation engine. The API contract does not include a "solve this problem" endpoint. The system is incapable of giving the answer, not merely instructed not to.

### 2.4 Conrad Wolfram: Stop Teaching Calculating, Start Teaching Thinking

Conrad Wolfram's TED talk and Computer-Based Maths initiative argue that approximately 80% of school math time is spent on step 3 of a four-step process (posing the right question, formulating it mathematically, computing, and interpreting the result). Wolfram argues that computing is precisely the step that computers do vastly better than humans, and that education should reallocate time from computing to the other three steps.

Wolfram's position: "Most students are running through calculating processes they don't understand for reasons they don't get, and these processes have no real practical use anymore outside of education." Modern education should focus on computational thinking and problem framing.

**Implication for Cena**: Wolfram's framework actually supports screenshot-to-solution tools -- but only if they are used to shift student time from mechanical calculation to problem formulation and interpretation. If Cena's step-solver just has students perform the same algebraic manipulations they would do with pencil and paper, Wolfram would consider it a waste. The value would come from using the step-solver to help students understand *why* each manipulation is valid, freeing time for conceptual exploration that currently drowns under procedural load.

### 2.5 Uri Treisman: Collaborative Struggle, Not Isolated Technology

Philip Uri Treisman's Mathematics Workshop Model, developed at UC Berkeley in the 1970s and disseminated as the Emerging Scholars Program, demonstrated that collaborative mathematical problem-solving in a social atmosphere dramatically improved outcomes for students who had historically struggled.

Key features of the Treisman model:

- **Focus on excellence, not remediation.** The workshops were not "help sessions." They used deliberately difficult problem sets, with near-impossible problems thrown in frequently.
- **Collaborative, not individual.** The critical variable was students working with peers, not students working alone with a tool.
- **Faculty-led, not tutor-led.** Faculty ownership signaled that the work was serious academic mathematics, not remedial support.

**Implication for Cena**: Treisman's work suggests that the social dimension of mathematical struggle may be as important as the cognitive dimension. A student working through a step-solver alone on their phone is missing the collaborative discourse that Treisman found essential. The step-solver may be pedagogically sound in isolation, but if it replaces collaborative problem-solving time rather than supplementing it, the net effect on learning could be negative.

### 2.6 Contemporary Commentary (2024-2025)

The broader education community has weighed in on AI math tools with increasing skepticism:

- An EdWeek opinion piece (March 2026) titled "Why AI Hasn't Transformed Math Instruction (and Probably Won't)" summarizes the consensus among math teachers: the tools have not delivered on their promises.
- An EDUCAUSE Review article (December 2025) titled "The Paradox of AI Assistance: Better Results, Worse Thinking" articulates the core tension: students using AI tools produce better immediate work but develop weaker autonomous thinking skills.
- A *Frontiers in Psychology* study (2025) formally names "the cognitive paradox of AI in education" -- the tension between AI's potential to assist learning and its risk of undermining memory, critical thinking, and creativity when overused (Alsobhi et al., 2025).
- A study of 206 vocational education students found that AI posed significant threats to critical thinking, as students passively accepted AI-generated information without critical scrutiny.

The emerging consensus among mathematics educators who take research seriously: **AI can be a powerful tool for learning, but only if the design deliberately preserves the cognitive work that produces learning. Most current implementations fail this test.**

---

## 3. The Counter-Evidence (Why Cena Differs from Photomath)

The objection in Section 1 is powerful, but it treats all screenshot-to-solution tools as identical. They are not. The specific architectural decisions in a tutoring system determine whether it produces learning or dependency. This section examines the evidence that step-based intelligent tutoring systems -- the category Cena belongs to -- produce genuine learning outcomes, and identifies the design features that distinguish effective systems from answer-delivery tools.

### 3.1 VanLehn (2011): Step-Based Tutoring -- the Best Non-Human Intervention

Kurt VanLehn's 2011 meta-analysis, "The Relative Effectiveness of Human Tutoring, Intelligent Tutoring Systems, and Other Tutoring Systems" (*Educational Psychologist*, 46(4), 197-221), is the single most important piece of evidence in Cena's favor.

VanLehn categorized tutoring systems by the granularity of their interaction:

| Granularity | What happens | Effect size (d) vs. no tutoring |
|-------------|-------------|-------------------------------|
| **Answer-based** | Student submits final answer, system says correct/incorrect | 0.31 |
| **Step-based** | Student submits each intermediate step, system provides feedback | **0.76** |
| **Substep-based** | System decomposes each step further, guiding micro-decisions | 0.40 |

The finding is striking: **step-based ITS achieved d = 0.76, dramatically outperforming both answer-based (d = 0.31) and substep-based (d = 0.40) systems**. Step-based tutoring was statistically indistinguishable from expert human tutoring (d = 0.79).

Why does step-based outperform substep-based? Because substep tutoring -- like the scaffolding-as-crutch concern in Section 1.5 -- decomposes the work so finely that the student never has to produce a meaningful cognitive step. Step-based tutoring hits the sweet spot: enough structure to prevent floundering, enough autonomy to require genuine generation.

**Cena's position**: Cena's step-solver is explicitly step-based. The student submits a complete algebraic expression at each step. The CAS checks symbolic equivalence. The system does not decompose each step into micro-operations (substep) or wait for a final answer (answer-based). This places Cena in the highest-performing category of VanLehn's taxonomy.

### 3.2 Renkl and Atkinson (2003): Faded Worked Examples

Alexander Renkl and Robert Atkinson demonstrated that gradually fading worked-example steps -- starting with fully worked examples and progressively removing steps for the student to complete -- produced superior learning compared to the traditional example-problem pair approach (Renkl, Atkinson, Maier, and Staley, 2002; Atkinson, Renkl, and Merrill, 2003).

Key findings:

- Students learned most about the principles underlying the specific steps that were faded -- suggesting that the act of completing a missing step triggers self-explanation processes that drive deep understanding.
- The fading procedure (successively removing worked solution steps) gradually shifts the cognitive load from studying to doing, matching the student's growing competence.
- Effect sizes in the d = 0.4-0.6 range were observed across multiple studies.

**Cena's position**: Cena's three-tier scaffolding model is a direct implementation of faded worked examples:

| BKT Mastery | Scaffolding Level | What the Student Sees |
|-------------|-------------------|----------------------|
| < 0.20 | **Full** (novice) | Every step labeled with instruction + faded worked example for pattern reference |
| 0.20-0.60 | **Partial** (intermediate) | Some steps pre-filled (given), student fills gaps |
| > 0.60 | **Minimal** (advanced) | Numbered slots only, student decides approach |

This is not arbitrary UX design. It is a direct implementation of the Renkl-Atkinson fading procedure, with the fading driven by an empirically grounded mastery model (Bayesian Knowledge Tracing) rather than by the instructor's intuition.

### 3.3 Bloom (1984): The Two-Sigma Problem and What Actually Causes It

Benjamin Bloom's 1984 paper, "The 2 Sigma Problem," reported that students receiving one-to-one tutoring with mastery learning performed two standard deviations above students in conventional classrooms -- meaning the average tutored student outperformed 98% of conventionally taught students.

Subsequent research has tempered this finding. Nickow, Oreopoulos, and Quan's 2020 meta-analysis of 96 randomized tutoring studies found an average effect of 0.37 standard deviations -- impressive but far below two sigma. Notably, none of the 96 studies replicated Bloom's two-sigma effect. The Education Next analysis concludes that the two-sigma figure likely reflected selection effects and non-random assignment in Bloom's original study.

However, the central insight survives: **one-to-one instruction with mastery-based pacing produces substantially better outcomes than conventional instruction, even if the effect is 0.37 SD rather than 2.0 SD.** The question is what aspects of one-to-one tutoring drive the effect. If it is the personalized pacing and immediate feedback, then AI systems can replicate it. If it is the human relationship, emotional attunement, and authentic caring, then AI cannot.

VanLehn's finding that step-based ITS (d = 0.76) approaches expert human tutoring (d = 0.79) suggests that **for procedural mathematical skills, the mechanism is primarily feedback-and-pacing, not relationship**. The human element may matter more for motivation, persistence, and emotional support -- but the learning-per-step may be equivalent.

### 3.4 The Critical Distinction: Cena Extracts the Question, Not the Answer

This is the architectural argument that, if valid, distinguishes Cena from Photomath in a pedagogically meaningful way.

**Photomath's workflow**:
1. Student photographs a problem.
2. System extracts the problem AND solves it.
3. System displays the complete solution.
4. Student reads (or copies) the solution.
5. Student has produced nothing. Generation effect: zero.

**Cena's workflow**:
1. Student photographs a problem.
2. System extracts the problem (question stem and figure only).
3. System may generate a similar practice variant (not the same problem).
4. Student enters the step-solver, which presents empty step slots.
5. Student produces an algebraic expression at each step.
6. CAS verifies symbolic equivalence.
7. If incorrect, system provides a hint (not the answer).
8. Student revises and resubmits.
9. Student has produced every step. Generation effect: present.

The distinction is not cosmetic. In Photomath, the student's cognitive task is *reading comprehension* -- understanding a provided solution. In Cena, the student's cognitive task is *mathematical production* -- generating algebraic expressions that must be symbolically correct. These are categorically different cognitive activities, and the generation effect literature (Section 1.3) provides clear evidence that production outperforms recognition for learning.

### 3.5 CAS Verification Preserves the "Desirable Difficulty" of Correctness

A subtle but important feature of Cena's architecture: the CAS engine checks symbolic equivalence, not string equality. This means:

- `2x + 4` and `4 + 2x` are both accepted (commutative equivalence).
- `2(x + 2)` and `2x + 4` are both accepted (algebraic equivalence).
- `2x + 3` is rejected, even though it is "close."

This preserves a desirable difficulty: the student must produce an expression that is *mathematically correct*, not merely visually similar to the expected answer. There is no partial credit for almost-right expressions. The CAS is unforgiving, and this unforgiving verification is itself a form of productive struggle -- the student must engage with the mathematics deeply enough to produce a correct expression, not an approximately correct one.

### 3.6 BKT Mastery Means Scaffolding Fades (Expertise Reversal Addressed by Design)

The expertise reversal effect (Section 1.4) is a valid concern -- but only for systems with static scaffolding. Cena's scaffolding is dynamic, driven by Bayesian Knowledge Tracing.

BKT models each knowledge component as a hidden Markov process with four parameters: P(L0) (initial mastery probability), P(T) (probability of learning on each opportunity), P(G) (probability of guessing correctly without mastery), and P(S) (probability of slipping -- answering incorrectly despite mastery). After each student response, BKT updates the posterior probability of mastery.

Cena's `ScaffoldingService` uses this posterior to select the scaffolding level:

- **Full scaffolding** for mastery < 0.20: the student sees labeled instructions and faded worked examples. This is appropriate for novices per Sweller's cognitive load theory.
- **Partial scaffolding** for mastery 0.20-0.60: some steps are pre-filled, the student fills gaps. This is the Renkl-Atkinson fading procedure.
- **Minimal scaffolding** for mastery > 0.60: numbered slots only, the student decides the approach. This respects Kalyuga's expertise reversal finding.

The key: **advanced students never see the full scaffolding**. A student with P(mastery) > 0.60 encounters the step-solver as a sequence of blank slots -- effectively an unguided practice environment with real-time correctness feedback. The scaffolding has faded. The expertise reversal is addressed by design.

### 3.7 The Screenshot Extracts the QUESTION, Not the ANSWER

A final architectural point: when a student photographs a textbook problem, Cena's vision model (Gemini 2.5 Flash) extracts the question stem, not the solution. The student then practices on a parametrically similar variant, not on the original question. This means:

- The student cannot photograph a problem and receive its answer.
- The student cannot photograph a solved example and claim to have done the work.
- The photograph is an input device for *question selection*, not an answer-delivery mechanism.

The photograph removes friction from **finding and entering a question**, not from **solving it**. This is the difference between a library search engine (which helps you find a book but does not read it for you) and a summary service (which reads the book and gives you the key points). Cena is the former.

### 3.8 The Harvard RCT (Kestin et al., 2025)

A 2025 randomized controlled trial published in *Nature Scientific Reports* by Kestin, Miller, Klales, and colleagues at Harvard found that students using an AI tutor informed by pedagogical best practices learned significantly more in less time compared to in-class active learning, and also reported greater engagement and motivation (Kestin et al., 2025).

The critical detail: the AI tutor in this study was designed using the same pedagogical principles as the in-class instruction -- not as a shortcut, but as an alternative delivery mechanism for the same cognitive work. Students in both the low-performing and high-performing subgroups showed improvement.

This provides evidence that AI tutoring *can* outperform traditional instruction -- but only when the AI design embodies research-based pedagogy. The tool's architecture matters more than its technology.

---

## 4. The Nuanced Truth

Both sides have valid points. The disagreement is not about whether struggle matters (it does) or whether AI can tutor effectively (it can, under certain conditions). The disagreement is about **what the AI does with the photographed question** -- and this design choice determines whether the tool produces learning or dependency.

### 4.1 The Design Choice Spectrum

Consider four possible designs for a screenshot-based math tool, arranged from pedagogically worst to best:

**Design A: Photo --> Answer (Photomath model)**
Student photographs a problem. System displays the complete solution. Student copies it. No generation. No struggle. No learning.
- Predicted effect size vs. no intervention: approximately zero or negative for long-term retention.
- This is the design that Bjork, Kapur, and Slamecka would condemn unanimously.

**Design B: Photo --> Worked Example (passive tutorial model)**
Student photographs a problem. System displays a step-by-step worked example of a similar problem. Student reads it. Some learning through example study, but limited generation.
- Predicted effect size: d approximately 0.2-0.3 (consistent with worked-example literature for passive study).
- Better than Design A, but the student is still reading, not producing.

**Design C: Photo --> Scaffolded Step-Solver (Cena's model)**
Student photographs a problem. System generates a similar practice variant. Student enters the step-solver and produces each algebraic step. CAS verifies correctness. Scaffolding level adapts to BKT mastery.
- Predicted effect size: d approximately 0.5-0.76 (consistent with VanLehn's step-based ITS findings and Renkl's faded worked examples).
- Student generates. Struggle is preserved at the step level. Scaffolding fades.

**Design D: Photo --> Adaptive Problem Set with No Scaffolding (pure desirable difficulty model)**
Student photographs a problem. System generates a harder variant. Student solves it entirely on their own, with no hints, no step structure, and no feedback until submission. Only the final answer is checked.
- Predicted effect size: variable, potentially high for conceptual understanding but with high frustration and dropout risk.
- Maximum desirable difficulty, but also maximum barriers to continued engagement.

The literature does not support Design D as uniformly superior. The dropout problem is real: students who experience too much difficulty disengage entirely, and no learning occurs when the student stops trying. Kapur's productive failure works because it is bounded -- students struggle for a defined period, then receive instruction. Unbounded struggle without support produces frustration, not learning.

**Cena occupies Design C -- the position that maximizes the generation effect while maintaining enough structure to prevent disengagement.** This is not the maximum-difficulty position (Design D), but it is the position with the strongest empirical evidence for effective learning.

### 4.2 The Key Variable: Student Production

The single most important variable for learning outcomes is whether the student is producing or consuming. Every framework discussed in this article -- Bjork, Kapur, Slamecka, Sweller, VanLehn, Renkl -- converges on this point:

- **Producing** (generating, retrieving, constructing, explaining) --> deep encoding, durable learning.
- **Consuming** (reading, watching, copying, recognizing) --> shallow encoding, transient performance.

Photomath puts students in the consumer role. Cena puts students in the producer role. This is not a marketing distinction. It is an architectural distinction with measurable cognitive consequences.

The concern from Section 1 -- that Cena's step-solver reduces the *scope* of production by pre-structuring the solution path -- is valid. But reduced-scope production is still production. A student who generates "3x^2" because they know the power rule is engaging a different cognitive process than a student who reads "3x^2" in a provided solution. The generation effect operates even when the generation is constrained.

### 4.3 The Screenshot as Input Device, Not Answer Engine

Reframing the controversy through this lens:

The objection assumes: **Photo --> Solution --> Student copies --> No learning.**

Cena's architecture implements: **Photo --> Question extraction --> Similar practice variant --> Student produces each step --> CAS verifies --> Learning.**

The photograph does not produce an answer. It produces a *question*. The student still has to solve it. The difficulty of solution is preserved; only the difficulty of question-entry is removed.

Is question-entry difficulty itself a desirable difficulty? Perhaps, in a Bjork sense, the friction of typing a complex equation or finding the right practice problem contributes to learning. But this seems unlikely to be the *productive* kind of difficulty. It is administrative friction, not cognitive friction. The desirable difficulties literature focuses on retrieval, spacing, interleaving, and generation -- not on data entry.

Removing the data-entry friction to increase the time available for mathematical production is, on balance, a pedagogically sound trade.

---

## 5. Cena's Design Principles (Responding to the Controversy)

This section documents the specific design decisions Cena has made -- or should make -- in response to the pedagogical concerns raised in this article.

### 5.1 Principle 1: The Photo Removes Friction from FINDING the Question, Not from SOLVING It

Cena's vision pipeline (Gemini 2.5 Flash) extracts the question stem and any accompanying figures. It does not extract or display the answer. The extracted question is used to:

1. Look up the closest match in Cena's question bank.
2. Generate a parametric variant at the student's current difficulty level.
3. Present the variant in the step-solver.

At no point does the student receive the answer to the photographed question. They receive a *different but structurally similar question* to practice with.

**Implementation requirement**: the API endpoint that handles screenshot analysis must never return a solution. It returns a structured question object (stem + figure spec + metadata). The step-solver endpoint requires student-produced expressions as input and returns only correct/incorrect + hint. These are separate API contracts enforced at the type level.

### 5.2 Principle 2: The Step-Solver Preserves Productive Struggle at Every Step

Each step in the step-solver requires the student to produce a mathematically correct LaTeX expression. The CAS checks symbolic equivalence. There is no multiple-choice selection (which would allow guessing). There is no "show me the answer" button (which would collapse to Design A).

When a student's expression is incorrect:

1. First attempt: "Not quite. Try again." (No information about what is wrong.)
2. Second attempt: A hint about the type of operation needed (e.g., "Consider factoring the numerator"). Not the specific operation.
3. Third attempt: A more specific hint (e.g., "The numerator can be written as (x-2)(x+3)"). This is the maximum hint level.
4. No attempt ever reveals the complete step. If the student cannot produce the expression after three attempts, the step is marked as incomplete and the student is directed to review the worked example for this topic.

This graduated hint system preserves struggle while preventing complete abandonment. The student always has to produce the final expression themselves, even if the hints narrow the search space.

### 5.3 Principle 3: BKT Mastery Tracking Ensures Scaffolding Fades

As documented in the architecture (Section 3.6), the `ScaffoldingService` uses BKT posterior mastery to select scaffolding level. This directly addresses the expertise reversal effect:

- **New students** (first encounter with a topic) receive full scaffolding: labeled instructions, faded worked examples, and maximum hints. This is appropriate per Sweller's cognitive load theory.
- **Developing students** (some practice, partial mastery) receive reduced scaffolding: some given steps, the student fills gaps. This is the Renkl-Atkinson fading procedure.
- **Proficient students** (high mastery) receive minimal scaffolding: blank step slots with no instructions. They decide the approach, and the CAS simply verifies their work.

The transition is automatic and continuous. A student who demonstrates mastery receives less help. A student who slips (makes errors after a period of success) receives temporarily increased scaffolding, then fading resumes.

**Implementation requirement**: the BKT mastery threshold for scaffolding transitions must be calibrated empirically. The current thresholds (0.20 and 0.60) are based on common ITS literature values, but Cena-specific calibration with Israeli Bagrut students is needed before production launch.

### 5.4 Principle 4: Deliberate Desirable Difficulties Built into the UX

Beyond the inherent difficulty of producing correct mathematical expressions, Cena should incorporate deliberate desirable difficulties:

**Interleaving**: practice sessions should mix problem types rather than blocking by topic. A student practicing integration should occasionally encounter a differentiation problem, a limit evaluation, or an algebraic simplification. This increases errors during practice but produces superior discrimination at test time (Rohrer and Taylor, 2007).

**Spacing**: the session scheduler should implement spaced repetition, surfacing topics for review at increasing intervals after initial mastery. BKT naturally models forgetting through the slip parameter, but explicit spacing adds an additional layer.

**Retrieval practice**: before the step-solver begins, the student should be asked to identify the problem type and select a strategy from a list (or, at high mastery, state the strategy in free text). This adds a generation/retrieval step before the procedural work begins.

**Delayed feedback**: for advanced students (mastery > 0.60), consider delaying CAS feedback until all steps are complete. The student produces an entire solution, submits it, and receives feedback on the complete attempt. This eliminates the "red light / green light" effect of immediate per-step feedback, which can reduce the desirable difficulty of maintaining a coherent solution strategy across multiple steps.

### 5.5 Principle 5: "Photograph --> Practice a Similar Question" NOT "Photograph --> Get the Answer"

This principle must be communicated clearly in the UI. When a student photographs a problem, the interface should explicitly state:

> "We found a similar question for you to practice. This is not the same problem -- it is a new question at your current level. Work through it step by step to build your understanding."

This framing accomplishes two things:

1. It sets the expectation that the student will do work, not receive an answer.
2. It prevents the student from using the photograph as an answer-extraction tool for homework or exams (since the variant is different from the original).

---

## 6. Remaining Tensions (Honest Acknowledgment)

Intellectual honesty requires acknowledging the ways in which Cena's design does not fully resolve the pedagogical concerns.

### 6.1 Even Cena's Step-Solver Reduces Struggle Compared to Fully Unguided Practice

The step-solver pre-determines the number of steps, the order of operations, and (at full scaffolding) the type of manipulation at each step. A student working a problem independently would need to make all of these strategic decisions themselves. The step-solver takes over the planning function and leaves the student with the execution function.

This is the valid core of the Section 1 objection that no architectural argument fully answers. Planning which steps to take, in what order, and when to try a different approach is a higher-order cognitive skill that the step-solver partially displaces. The VanLehn effect size (d = 0.76) was measured for procedural knowledge and near-transfer. It may not extend to far-transfer problems requiring novel strategic decisions.

**Mitigation**: at minimal scaffolding (mastery > 0.60), the step-solver shows only numbered blank slots. The student could, in principle, enter any valid algebraic expression at any step. But the numbered slots still imply a specific step count, and the CAS expects specific intermediate expressions. A fully open problem-solving environment (free-form whiteboard + final answer check) would better preserve strategic autonomy but would lose the step-based feedback that produces the 0.76 effect size. This is a genuine trade-off, not a resolved question.

### 6.2 Repetitive Pattern Practice vs. Genuine Understanding

Some students will photograph the same question type 50 times, producing correct expressions each time through pattern memorization rather than conceptual understanding. After 50 successful completions, BKT will assign high mastery, and the scaffolding will fade. But has the student learned mathematics, or have they learned to recognize and execute a template?

This is the "inert knowledge" concern from Section 1.5, and it is not fully addressed by step-based ITS. The student can achieve high procedural mastery -- measured by BKT -- without conceptual understanding. They can perform integration by substitution perfectly when told to use substitution, but cannot recognize when substitution is the appropriate technique on an unseen problem.

**Mitigation**: Cena should include periodic "strategy selection" assessments that present a problem without a pre-determined solution path and ask the student to choose the appropriate technique. These assessments would feed into a separate conceptual mastery model, distinct from the procedural BKT. If procedural mastery is high but conceptual mastery is low, the system should flag this discrepancy and adjust the practice mix.

### 6.3 The Line Between "Removing Friction" and "Removing Difficulty" Is Blurry

The argument in Section 4.3 -- that the photograph removes administrative friction (data entry) rather than cognitive difficulty (mathematical reasoning) -- is conceptually clean but practically blurry.

Consider: a student who has to find a practice problem in a textbook, read the problem, identify what is being asked, and set up the first line of work is engaging in a cognitive process that includes problem identification, comprehension, and formulation. When Cena extracts the question, parses it into a structured stem, and pre-selects a variant, some of this cognitive work is done by the system.

How much of this constitutes "desirable difficulty" in Bjork's sense? The honest answer is: we do not know. The desirable difficulties literature has not specifically studied whether the friction of reading a textbook question contributes to mathematical learning. It is plausible that it does (the act of parsing a question engages comprehension processes that prime mathematical thinking) and plausible that it does not (the comprehension is a bottleneck that delays the valuable mathematical work). Empirical evidence is needed.

### 6.4 No Longitudinal Studies Exist for Screenshot-to-Step-Solver Outcomes

This is the most significant gap. No published study has compared long-term learning outcomes for students using a screenshot-to-step-solver workflow (Cena's model) against students using traditional textbook practice. The closest evidence comes from VanLehn's ITS meta-analysis, but those studies used manually entered problems, not photographed questions. The screenshot entry mechanism has not been empirically isolated as a variable.

Without longitudinal data, claims about Cena's pedagogical effectiveness are based on theoretical alignment with ITS literature, not on direct evidence. This is honest and appropriate for a pre-launch product, but it means that Cena's pedagogical claims are predictions, not findings.

### 6.5 The Motivation-Learning Trade-Off

Students report preferring AI tutoring to traditional instruction (the Harvard RCT found greater engagement and motivation in the AI group). This preference may reflect genuine pedagogical benefit, or it may reflect the "fluency illusion" -- the well-documented tendency for humans to perceive easy-feeling activities as more effective for learning, even when they are less effective (Bjork and Bjork, 2011).

If students prefer Cena because it feels easier (less frustrating, more supportive, more encouraging) than unguided practice, the preference may be a signal that desirable difficulties have been removed. The paradox: **the design that students prefer may be the design that teaches them least**.

Alternatively, the preference may reflect genuine engagement gains. A student who uses Cena daily because it feels rewarding will practice more than a student who avoids their textbook because it feels punishing. Total learning is a product of learning-per-minute times minutes-practiced. Even if Cena produces slightly less learning-per-minute than unguided practice, it may produce more total learning if it dramatically increases minutes-practiced.

This is an empirical question, not a theoretical one.

### 6.6 The "48% More Correct, 17% Less Understanding" Problem

The Stanford/UPenn finding reported by Alsobhi et al. (2025) deserves its own subsection because it crystallizes the central risk. Students using ChatGPT answered 48% more problems correctly but scored 17% lower on concept understanding tests. The students had become more productive but less knowledgeable.

Could Cena reproduce this pattern? It is possible. A student who uses the step-solver extensively might become fluent at executing algebraic manipulations within the step framework (higher procedural accuracy) while failing to build the conceptual understanding that supports transfer to novel problems (lower concept scores). The step-solver could become a high-performance crutch -- the student solves more problems per hour but understands less about why each step works.

The difference between Cena and ChatGPT is that Cena does not *produce* the solution -- the student does. But production within a constrained framework may still be shallow production. Typing "2x + 4" into a step-solver slot when the instruction says "combine like terms" is a different cognitive act from deciding, unprompted, that combining like terms is the appropriate next move.

This is the hardest version of the objection to answer, because it does not claim that Cena gives away answers (it does not) or that Cena eliminates all struggle (it does not). It claims that Cena *narrows* the struggle to the execution level while eliminating the planning-level struggle that produces conceptual understanding. And this narrow-but-present struggle may be sufficient to produce the illusion of learning without the reality.

The only honest response: empirical measurement (Study 1 in Section 7) must include separate procedural and conceptual assessments. If Cena produces the 48/17 split, the design needs revision.

### 6.7 The Social Dimension Is Missing

Treisman's work (Section 2.5) and Boaler's emphasis on collaborative mathematics suggest that the social context of learning matters. A student working through Cena's step-solver alone on their phone is not experiencing collaborative mathematical discourse. If Cena replaces study-group time rather than replacing dead time (e.g., commuting, waiting), the net effect on learning could be negative because of the lost social learning.

This is not a flaw in Cena's design so much as a limitation of its category. Individual AI tutoring, by definition, does not provide social learning. The question is whether Cena can complement collaborative learning rather than replacing it.

---

## 7. Research Agenda

The tensions identified in Section 6 cannot be resolved by architectural argument. They require empirical research. This section outlines the studies that would provide the evidence Cena needs.

### 7.1 Study 1: Screenshot+Step-Solver vs. Textbook-Only (RCT)

**Design**: randomized controlled trial with Israeli Bagrut mathematics students (9th-10th grade, ages 14-16).

**Groups**:
- **Treatment**: Cena step-solver with screenshot entry, BKT-adapted scaffolding, CAS verification.
- **Active control**: same practice problems presented on paper (printed from Cena's question bank), no scaffolding, answer-only feedback.
- **Passive control**: standard textbook homework with teacher-checked answers (business as usual).

**Outcomes**:
- Primary: Bagrut practice exam score (procedural + conceptual, externally graded).
- Secondary: strategy selection assessment (can students identify the correct technique for an unseen problem?), transfer test (novel problems requiring adaptation of learned techniques), retention test (administered 4 weeks after the intervention period with no intervening practice).

**Duration**: 8 weeks of practice (3 sessions/week, 30 minutes/session).

**Sample size**: power analysis for d = 0.40 (conservative estimate based on VanLehn), alpha = 0.05, power = 0.80 --> approximately 100 students per group, 300 total.

**Key moderators**: prior achievement level, scaffolding level experienced (BKT mastery trajectory), number of screenshot entries vs. manual entries.

### 7.2 Study 2: Scaffolding Level Calibration (Within-Subjects)

**Design**: within-subjects study where each student experiences all three scaffolding levels across different topics (counterbalanced).

**Question**: are the BKT mastery thresholds (0.20 and 0.60) optimal for Cena's specific population? Would different thresholds produce better learning outcomes?

**Outcome**: learning gain per topic as a function of the scaffolding level experienced, controlling for topic difficulty and prior mastery.

**Analysis**: fit a regression model predicting learning gain from (a) assigned scaffolding level, (b) actual BKT mastery at the time of assignment, and (c) their interaction. The optimal threshold is the mastery level at which the scaffolding-level coefficient changes sign (from positive to negative, indicating expertise reversal).

### 7.3 Study 3: Screenshot Entry vs. Manual Entry (Isolating the Photo Effect)

**Design**: randomized comparison where both groups use the same step-solver, but the treatment group enters questions via photograph and the control group enters questions by typing.

**Question**: does the screenshot entry mechanism itself affect learning outcomes? If the friction of manual entry is a desirable difficulty, removing it should produce measurable learning decrements.

**Outcome**: same as Study 1 (procedural, conceptual, transfer, retention).

**Expected finding**: no significant difference (the entry mechanism is administrative, not cognitive). If a significant difference is found, it would challenge the "removing friction, not difficulty" argument (Section 4.3).

### 7.4 Study 4: Deliberate Desirable Difficulties (A/B Tests)

**Design**: A/B tests comparing Cena's baseline step-solver against variants with additional desirable difficulties:

- **Interleaving A/B**: blocked practice (all problems of one type) vs. interleaved practice (mixed types).
- **Delayed feedback A/B**: immediate per-step CAS feedback vs. feedback only after all steps are submitted.
- **Strategy selection A/B**: standard step-solver vs. step-solver with an initial "choose the technique" prompt.

**Question**: do these additions improve long-term retention and transfer without increasing dropout?

**Key metric**: the dual outcome of learning gain AND engagement (session completion rate, return rate). A modification that improves learning but causes 30% dropout is net negative.

### 7.5 Study 5: Longitudinal Learning Trajectory (Observational)

**Design**: prospective observational study following Cena users over one academic year.

**Question**: does sustained use of Cena's step-solver produce improving, stable, or declining learning outcomes over time? Does dependency develop (increasing scaffolding reliance despite increased practice)?

**Metrics**:
- BKT mastery trajectory by topic.
- Scaffolding level experienced over time (should trend from Full to Minimal).
- External assessment scores (Bagrut practice exams, teacher-administered tests).
- "CAS-free" assessment: periodic tests where the student solves problems on paper without any system feedback.

**Red flag indicators**: if BKT mastery increases but CAS-free assessment performance does not, the student is developing system-dependent procedural fluency without transferable mathematical knowledge.

### 7.6 Metrics That Capture "Desirable Difficulty" in the Step-Solver

Standard metrics (accuracy, completion rate, time-to-complete) do not capture desirable difficulty. The following metrics are proposed:

**Error-before-success rate**: the percentage of steps where the student's first attempt is incorrect but a subsequent attempt succeeds. A healthy desirable-difficulty regime should produce a moderate error-before-success rate (perhaps 20-40%). A rate near zero suggests the scaffolding is too easy. A rate above 60% suggests the scaffolding is too hard.

**Hint utilization rate**: the percentage of steps where the student requests or receives a hint. Should decrease over time within a topic (indicating growing mastery) and should be lower at higher scaffolding levels (indicating appropriate fading).

**Strategy-selection accuracy**: measured by the "choose the technique" prompt (Study 4). Should increase over time if the step-solver is building conceptual understanding, not just procedural fluency.

**Retention decay rate**: the rate of BKT mastery decline during periods of non-practice. A steep decay suggests shallow encoding (scaffold-dependent learning). A gentle decay suggests durable encoding.

**Transfer ratio**: performance on novel problems divided by performance on practiced problem types. A ratio near 1.0 suggests robust understanding. A ratio well below 1.0 suggests template-dependent learning.

---

## 8. Sources

### Foundational Cognitive Science

1. Bjork, R. A. (1994). Memory and metamemory considerations in the training of human beings. In J. Metcalfe and A. Shimamura (Eds.), *Metacognition: Knowing about Knowing* (pp. 185-205). Cambridge, MA: MIT Press.

2. Bjork, E. L., and Bjork, R. A. (2011). Making things hard on yourself, but in a good way: Creating desirable difficulties to enhance learning. In M. A. Gernsbacher, R. W. Pew, L. M. Hough, and J. R. Pomerantz (Eds.), *Psychology and the Real World: Essays Illustrating Fundamental Contributions to Society* (pp. 56-64). New York: Worth. [PDF](https://bjorklab.psych.ucla.edu/wp-content/uploads/sites/13/2016/04/EBjork_RBjork_2011.pdf)

3. Slamecka, N. J., and Graf, P. (1978). The generation effect: Delineation of a phenomenon. *Journal of Experimental Psychology: Human Learning and Memory*, 4(6), 592-604. [APA Record](https://psycnet.apa.org/record/1980-20399-001)

4. McCurdy, M. P., Viechtbauer, W., Skinner, R. L., and Frankenberg, S. (2020). Theories of the generation effect and the impact of generation constraint: A meta-analytic review. *Psychonomic Bulletin and Review*, 27(6), 1139-1165. [Springer](https://link.springer.com/article/10.3758/s13423-020-01762-3)

5. Sweller, J. (1988). Cognitive load during problem solving: Effects on learning. *Cognitive Science*, 12(2), 257-285. [Wiley](https://onlinelibrary.wiley.com/doi/10.1207/s15516709cog1202_4)

6. Sweller, J., and Cooper, G. A. (1985). The use of worked examples as a substitute for problem solving in learning algebra. *Cognition and Instruction*, 2(1), 59-89. [Taylor and Francis](https://www.tandfonline.com/doi/abs/10.1207/s1532690xci0201_3)

### Expertise Reversal and Scaffolding

7. Kalyuga, S., Ayres, P., Chandler, P., and Sweller, J. (2003). The expertise reversal effect. *Educational Psychologist*, 38(1), 23-31. [Taylor and Francis](https://www.tandfonline.com/doi/abs/10.1207/S15326985EP3801_4)

8. Kalyuga, S. (2007). Expertise reversal effect and its implications for learner-tailored instruction. *Educational Psychology Review*, 19, 509-539. [Springer](https://link.springer.com/article/10.1007/s10648-007-9054-3)

9. Renkl, A., Atkinson, R. K., Maier, U. H., and Staley, R. (2002). From example study to problem solving: Smooth transitions help learning. *Journal of Experimental Education*, 70(4), 293-315.

10. Atkinson, R. K., Renkl, A., and Merrill, M. M. (2003). Transitioning from studying examples to solving problems: Combining fading with prompting fosters learning. *Journal of Educational Psychology*, 95(4), 774-783. [APA Record](https://psycnet.apa.org/record/2003-09576-009)

### Productive Failure

11. Kapur, M. (2008). Productive failure. *Cognition and Instruction*, 26(3), 379-424.

12. Kapur, M. (2012). Productive failure in learning the concept of variance. *Instructional Science*, 40(4), 651-672.

13. Kapur, M. (2014). Productive failure in learning math. *Cognitive Science*, 38(5), 1008-1022. [PubMed](https://pubmed.ncbi.nlm.nih.gov/24628487/)

14. Sinha, T., and Kapur, M. (2021). When problem solving followed by instruction works: Evidence for productive failure. *Review of Educational Research*, 91(5), 823-861. [SAGE](https://journals.sagepub.com/doi/abs/10.3102/00346543211019105)

### Intelligent Tutoring Systems

15. VanLehn, K. (2011). The relative effectiveness of human tutoring, intelligent tutoring systems, and other tutoring systems. *Educational Psychologist*, 46(4), 197-221. [Taylor and Francis](https://www.tandfonline.com/doi/abs/10.1080/00461520.2011.611369)

16. Bloom, B. S. (1984). The 2 sigma problem: The search for methods of group instruction as effective as one-to-one tutoring. *Educational Researcher*, 13(6), 4-16. [SAGE](https://journals.sagepub.com/doi/10.3102/0013189X013006004)

17. Nickow, A., Oreopoulos, P., and Quan, V. (2020). The impressive effects of tutoring on PreK-12 learning: A systematic review and meta-analysis of the experimental evidence. NBER Working Paper No. 27476. [NBER](https://www.nber.org/papers/w27476)

18. Ma, W., Adesope, O. O., Nesbit, J. C., and Liu, Q. (2014). Intelligent tutoring systems and learning outcomes: A meta-analysis. *Journal of Educational Psychology*, 106(4), 901-918. [APA](https://www.apa.org/pubs/journals/features/edu-a0037123.pdf)

19. Kestin, G., Miller, K., Klales, A., et al. (2025). AI tutoring outperforms in-class active learning: An RCT introducing a novel research-based design in an authentic educational setting. *Scientific Reports*. [Nature](https://www.nature.com/articles/s41598-025-97652-6)

### Desirable Difficulties and Spacing/Interleaving

20. Cepeda, N. J., Pashler, H., Vul, E., Wixted, J. T., and Rohrer, D. (2006). Distributed practice in verbal recall tasks: A review and quantitative synthesis. *Psychological Bulletin*, 132(3), 354-380.

21. Roediger, H. L., III, and Karpicke, J. D. (2006). Test-enhanced learning: Taking memory tests improves long-term retention. *Psychological Science*, 17(3), 249-255.

22. Rohrer, D., and Taylor, K. (2007). The shuffling of mathematics problems improves learning. *Instructional Science*, 35(6), 481-498.

### Mathematics Education Leaders

23. Boaler, J. (2015). *Mathematical Mindsets: Unleashing Students' Potential Through Creative Math, Inspiring Messages and Innovative Teaching*. San Francisco: Jossey-Bass. (2nd edition 2022.)

24. Wolfram, C. (2010). Stop teaching calculating, start teaching math. TED Talk. [Wolfram Blog](https://blog.wolfram.com/2010/11/23/conrad-wolframs-ted-talk-stop-teaching-calculating-start-teaching-math/)

25. Treisman, U. (1992). Studying students studying calculus: A look at the lives of minority mathematics students in college. *College Mathematics Journal*, 23(5), 362-372. [MERIT / Illinois](https://merit.illinois.edu/for-educators/the-treismans-model/)

### AI in Education (2024-2026)

26. Meyer, D. (2024). The AI disconnect. *Mathworlds* (Substack). [Link](https://danmeyer.substack.com/p/the-ai-disconnect)

27. Alsobhi, H. A., et al. (2025). The cognitive paradox of AI in education: Between enhancement and erosion. *Frontiers in Psychology*, 16, 1550621. [PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC12036037/)

28. Khan Academy. (2024). Khan Academy's framework for responsible AI in education. [Khan Academy Blog](https://blog.khanacademy.org/khan-academys-framework-for-responsible-ai-in-education/)

### Bayesian Knowledge Tracing

29. Corbett, A. T., and Anderson, J. R. (1995). Knowledge tracing: Modeling the acquisition of procedural knowledge. *User Modeling and User-Adapted Interaction*, 4(4), 253-278.

### Additional Context

30. Brake, J. (2024). AI tutors can't solve Bloom's two sigma problem. *Substack*. [Link](https://joshbrake.substack.com/p/ai-tutors-cant-solve-blooms-two-sigma-problem)

31. EdSurge. (2024). Is there a problem with 'mathbots'? [Link](https://www.edsurge.com/news/2024-11-15-is-there-a-problem-with-mathbots)

---

## Appendix A: Summary Decision Matrix

| Concern | Source | Severity for Cena | Cena's Mitigation | Residual Risk |
|---------|--------|-------------------|-------------------|---------------|
| Desirable difficulties removed | Bjork (1994, 2011) | **Medium** | Step production preserves generation; BKT fading preserves adaptive difficulty | Pre-structured steps reduce strategic planning difficulty |
| Productive failure eliminated | Kapur (2008-2014) | **Medium-High** | Step-solver requires production, not reading; but the struggle phase before instruction is absent | No "generate-then-instruct" phase in current design |
| Generation effect weakened | Slamecka and Graf (1978) | **Low-Medium** | Student generates every expression; CAS verifies but does not produce | Constrained generation (within pre-structured steps) is weaker than unconstrained generation |
| Expertise reversal | Kalyuga (2003, 2007) | **Low** | BKT-driven scaffolding fading; advanced students see minimal scaffolding | Requires empirical calibration of mastery thresholds |
| Scaffolding as crutch | Sweller (1988) | **Medium** | Scaffolding fades with mastery; three distinct levels | Pattern memorization can game BKT without conceptual understanding |
| Social learning displaced | Treisman (1992) | **Medium** | Cena does not replace classroom time; intended for homework/practice | If students substitute solo Cena for study groups, social learning is lost |
| No longitudinal evidence | -- | **High** | Research agenda proposed (Section 7) | Cena's pedagogical claims are predictions, not findings, until studies are conducted |

---

## Appendix B: Comparison Table -- Photomath vs. Cena

| Feature | Photomath | Cena |
|---------|-----------|------|
| Screenshot input | Yes | Yes |
| Extracts the answer | **Yes** | **No** |
| Displays complete solution | **Yes (immediately)** | **No** |
| Student produces each step | **No** | **Yes** |
| CAS verification of student work | **No** | **Yes** |
| Adaptive scaffolding | **No** | **Yes (BKT-driven)** |
| Scaffolding fading | **No** | **Yes (3 levels)** |
| Generates practice variants | **No** | **Yes (parametric)** |
| Hint system (graduated) | Step reveal (full) | Hints only (no answer reveal) |
| VanLehn classification | Answer-based (d = 0.31) | Step-based (d = 0.76) |
| Student cognitive role | Consumer (reads) | Producer (generates) |
| Generation effect | Absent | Present (constrained) |

---

## Appendix C: Cena's Pedagogical Architecture Diagram

```
STUDENT PHOTOGRAPHS A QUESTION
         |
         v
+-----------------------------+
| Gemini 2.5 Flash Vision     |
| Extracts: question stem,    |
| figures, metadata            |
| Does NOT extract: answer     |
+-----------------------------+
         |
         v
+-----------------------------+
| Question Bank Lookup         |
| Finds closest match          |
| Generates parametric variant |
| (DIFFERENT from original)    |
+-----------------------------+
         |
         v
+-----------------------------+
| BKT Mastery Check            |
| P(mastery) < 0.20  → Full   |
| P(mastery) 0.20-0.60 → Part |
| P(mastery) > 0.60  → Min    |
+-----------------------------+
         |
         v
+-----------------------------+
| Step-Solver UI               |
| Student PRODUCES each step   |
| No answer is ever shown      |
+-----------------------------+
         |
         v  (for each step)
+-----------------------------+
| CAS Engine (SymPy/MathNet)   |
| Checks symbolic equivalence  |
| Returns: correct/incorrect   |
| Returns: graduated hint      |
| Does NOT return: the answer  |
+-----------------------------+
         |
         v
+-----------------------------+
| BKT Update                   |
| Posterior mastery updated     |
| Scaffolding level may change |
| Next session adapts          |
+-----------------------------+
```

---

## Appendix D: What Photomath's Own Research Shows

Photomath has been the subject of several studies, and the results are instructive for understanding what Cena must avoid.

A 2024 study published in *Multidisciplinary Science Journal* found a significant positive effect of AI tools like Photomath on developing mathematical concepts among students with learning difficulties (Ahmad and Aboraya, 2024). A separate study found a 36.25% improvement in algebra test scores among Photomath users, with mean pretest scores rising from 18.27 to 29.12 on posttests.

However, these studies measured *immediate* performance, not long-term retention or transfer. The critical question -- whether Photomath users retain their gains when the tool is removed -- has not been rigorously studied. Anecdotally, teachers report a pattern: students who rely on Photomath for homework perform well on homework but poorly on in-class assessments where the tool is unavailable.

Before its acquisition by Google, Photomath processed 2 billion math questions per month from 250 million downloads. The majority of these queries were homework problems. As documented in Cena's Iteration 7 (Academic Integrity), teachers detected suspicious patterns -- students who could "solve" systems of equations in eight seconds during homework but failed in-class quizzes on the same material.

The Photomath evidence is neither damning nor exonerating. The tool clearly helps students produce correct homework. Whether it helps them learn mathematics is an open question. Cena's architectural differences (student production, CAS verification, scaffolding fading) are designed to shift the outcome from "correct homework" to "genuine learning" -- but this shift has not been empirically verified.

---

## Closing Statement

The pedagogical controversy is real, not manufactured. The strongest version of the objection -- that screenshot-to-solution tools rob students of the cognitive struggle that produces durable mathematical understanding -- is supported by five decades of cognitive science research from Bjork, Kapur, Slamecka, Kalyuga, and Sweller.

Cena's answer is architectural, not dismissive. The system extracts questions, not answers. The student produces every step. The CAS verifies but never reveals. The scaffolding fades with mastery. These design choices place Cena in the step-based ITS category (VanLehn d = 0.76), not the answer-delivery category (d = 0.31).

But architectural alignment is not empirical proof. Cena's pedagogical claims remain predictions grounded in ITS literature, not findings from Cena-specific studies. The research agenda in Section 7 outlines the work needed to convert predictions into evidence. Until that evidence exists, the honest position is: **Cena is designed to produce learning, not to replace it. The design is informed by the best available science. But the science has not yet been applied to this specific tool with this specific population. The controversy is not resolved; it is being addressed.**

That is not a hedge. It is intellectual integrity. And it is the foundation on which Cena's pedagogical credibility must be built.

---

*Total citations: 31. Date: 2026-04-12. Series: Screenshot Analyzer Pedagogical Research (Capstone).*
