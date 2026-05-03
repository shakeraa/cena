# Iteration 7 (Supplement) — Pedagogical Controversy: Assessment Reform and the Legitimacy of AI-Assisted Learning

**Series**: Screenshot Question Analyzer — Defense-in-Depth Research
**Iteration**: 7-C (Controversy Supplement to iteration-07-academic-integrity.md)
**Date**: 2026-04-12
**Type**: Pedagogical controversy / position paper
**Audience**: Internal product team, education advisors, Bagrut-age students (14--18)

---

## Table of Contents

1. [The Steel-Manned Objection](#1-the-steel-manned-objection)
2. [Evidence For: The Homework-Exam Pipeline Is Broken](#2-evidence-for-the-homework-exam-pipeline-is-broken)
3. [Evidence Against: Exams Exist and Struggle Matters](#3-evidence-against-exams-exist-and-struggle-matters)
4. [Cena's Position: The Step-Solver as Assessment Reform](#4-cenas-position-the-step-solver-as-assessment-reform)
5. [Design Mitigations](#5-design-mitigations)
6. [Open Questions](#6-open-questions)
7. [Sources](#7-sources)

---

## 1. The Steel-Manned Objection

The strongest version of the objection against restricting Cena's screenshot analyzer runs as follows:

> The homework-to-exam pipeline is fundamentally broken. A student is assigned 30 textbook problems, completes them at home with no feedback until the teacher grades them days later, then sits a proctored paper exam that tests recall under pressure. The homework phase produces minimal learning because there is no timely corrective feedback. The exam phase measures test-taking ability as much as mathematical understanding. When a student photographs a problem and works through it step-by-step with Cena's guidance --- receiving scaffolded hints, seeing the solution decomposed into substeps, and being asked to attempt each substep before the next is revealed --- that IS learning. It is arguably *better* learning than struggling alone, getting frustrated, and copying from a classmate or from Photomath without any pedagogical scaffolding. The real problem is not the tool. The real problem is that in 2026, we still assess students with proctored paper exams while every other domain of life has moved to process-over-product evaluation. Change the assessment. Do not cripple the tool.

This objection is not frivolous. It is grounded in decades of learning science, it is aligned with the direction of assessment reform worldwide, and it reflects the lived experience of students who find the current system arbitrary and stressful. Taking it seriously does not mean accepting it uncritically. It means engaging with the evidence on both sides.

---

## 2. Evidence For: The Homework-Exam Pipeline Is Broken

### 2.1 Cooper's Homework Meta-Analyses: The Evidence Is Weaker Than Assumed

Harris Cooper's landmark meta-analyses at Duke University (1989, 2006) are the most frequently cited evidence that homework improves academic achievement. The 2006 study, reviewing research from 1987 to 2003, found a positive correlation between homework and achievement in grades 7--12, with the average student in a homework-assigned class scoring 23 percentile points higher than students in no-homework classes ([Cooper et al., 2006](https://journals.sagepub.com/doi/10.3102/00346543076001001)). But the details complicate the narrative.

First, the effect size was substantially weaker for younger students and strongest for high school seniors --- the population closest to Cena's Bagrut-age users. Second, the relationship showed diminishing returns: for junior high students, benefits plateaued at 1--2 hours per night and then *decreased* ([Duke Today, 2006](https://today.duke.edu/2006/03/homework.html)). Third, and most critically, Cooper's studies measured correlation, not causation. They could not distinguish between "homework causes learning" and "students who do homework are the same students who would learn anyway." As Cooper himself noted, the research supports homework as useful practice but says little about the *quality* of that practice.

Critics including Alfie Kohn, Sara Bennett, and Nancy Kalish have argued that traditional homework marginalizes economically disadvantaged students (who lack quiet study environments and parental help), that teachers receive little training in designing effective assignments, and that the emotional cost of nightly homework battles damages the student-family relationship ([ASCD, 2007](https://ascd.org/el/articles/the-case-for-and-against-homework)). The implication for Cena: if homework's value depends on quality of practice rather than quantity of problems, then a tool that transforms rote repetition into scaffolded guided practice may *increase* homework's pedagogical value rather than undermine it.

### 2.2 Vygotsky's Zone of Proximal Development: Help IS How Learning Works

The theoretical foundation for "learning with help is still learning" comes from Lev Vygotsky's Zone of Proximal Development (ZPD), introduced as part of his sociocultural theory of cognitive development. The ZPD is the distance between what a learner can accomplish independently and what they can accomplish with guidance from a more knowledgeable other ([Vygotsky, 1978](https://www.simplypsychology.org/zone-of-proximal-development.html)).

This is not a marginal theory. It is one of the foundational frameworks in developmental psychology and education. The entire concept of scaffolding --- the temporary support structure that is gradually withdrawn as the learner gains competence --- was built on Vygotsky's work (formalized by Wood, Bruner, and Ross in 1976). The implication is direct: a student working through a problem with Cena's step-by-step guidance, where hints are progressively revealed and the student attempts each substep, is operating exactly within their ZPD. The AI is functioning as the "more knowledgeable other." This is not cheating. This is how constructivist learning theory says learning is supposed to work.

Bloom's (1984) Two Sigma Problem reinforces this. Bloom found that students who received one-on-one tutoring with mastery learning performed two standard deviations above students in conventional classroom instruction --- meaning the average tutored student outperformed 98% of the control group ([Bloom, 1984](https://journals.sagepub.com/doi/10.3102/0013189X013006004)). The "problem" Bloom identified was that one-on-one tutoring is too expensive to scale. An AI tutor that provides individualized, step-by-step guidance with corrective feedback is, in principle, the first technology that could address the Two Sigma Problem at scale.

### 2.3 Israeli Bagrut Reform: The System Is Already Moving

The Israeli Ministry of Education is itself reforming the Bagrut system. In December 2024, Education Minister Yoav Kisch presented a restructured program for humanities subjects (Tanach, history, citizenship, literature) with three components: an external exam worth 35% of the study material, two practical application tasks worth 35%, and a school-assigned grade ([Jerusalem Post, 2024](https://www.jpost.com/israel-news/article-734306); [AACRAO, 2024](https://www.aacrao.org/edge/emergent-news/the-reform-of-the-matriculation-exams-starts-in-israel)). Schools can choose between the old and new programs, and principals can begin some exams in 10th grade to manage workload.

This reform explicitly reduces the weight of sit-down external exams and increases the weight of process-based assessment (practical tasks, school grades). It was formulated in consultation with principals, teachers, supervisors, students, and the Teachers Union. However, it was criticized by the Israeli Parents Association for risking a return to "an old-fashioned way, in which the learning of the subjects for the exams was done by memorizing material and a written exam," and by the National Council of Students and Youth for "sending students back to learning based on memorization, as was customary 50 years ago" ([Jerusalem Post, 2024](https://www.jpost.com/israel-news/article-734306)). The controversy itself demonstrates that Israeli education is in active flux around assessment design --- and that the Bagrut's future is not guaranteed to be the same as its past.

For mathematics and sciences, the Bagrut external exam still dominates. But the humanities reform sets a precedent, and the direction of travel --- process over product, distributed assessment over single high-stakes exams --- is clear.

### 2.4 Global Assessment Reform: Universities Are Leading

The global higher education sector is further ahead. Australia's TEQSA (Tertiary Education Quality and Standards Agency) published "Enacting Assessment Reform in a Time of Artificial Intelligence" in September 2025, outlining three pathways for universities: program-wide assessment reform, unit-level assurance (at least one secure assessment per subject), and hybrid models ([TEQSA, 2025](https://www.teqsa.gov.au/guides-resources/resources/corporate-publications/enacting-assessment-reform-time-artificial-intelligence)). TEQSA's core finding: "AI detection tools can't guarantee integrity and structural redesign is the only sustainable response." In 2026, the emphasis has shifted from assessment plans to actual curriculum redesign ([Teaching@Sydney, 2026](https://educational-innovation.sydney.edu.au/teaching@sydney/navigating-ai-in-higher-education-tasks-ahead-for-2025-and-2026/)).

UNESCO's Dr. Hrishikesh Desai, writing on assessment in the AI age, argued that "many conventional evaluation methods have been poor proxies for meaningful learning all along" and called for a shift from products to processes, from single exams to portfolio-based evaluation, and from rote recall to assessment through dialogue ([UNESCO, 2025](https://www.unesco.org/en/articles/whats-worth-measuring-future-assessment-ai-age)). The UK Quality Assurance Agency's advice emphasizes "principled redesign rather than reactive policies."

Frontiers in Education published research in 2024 on "AI-resistant assessments," arguing that assessment should emphasize tasks requiring students to "deconstruct AI outputs, such as projects where students critique ChatGPT-generated essays and identify logical fallacies or biases" ([Frontiers, 2024](https://www.frontiersin.org/journals/education/articles/10.3389/feduc.2024.1499495/full)). The MDPI journal Education Sciences published a full framework for "Redesigning Assessments for AI-Enhanced Learning" in the generative AI era ([MDPI, 2025](https://www.mdpi.com/2227-7102/15/2/174)).

The pattern is consistent: every major regulatory body and education research institution in the world is moving toward the same conclusion. Detection is a losing strategy. Assessment redesign is the sustainable response.

---

## 3. Evidence Against: Exams Exist and Struggle Matters

### 3.1 The Bagrut Still Exists and Students Must Pass It

Whatever the direction of global reform, the current reality for Cena's users is that the Bagrut mathematics exam --- 5-unit level --- is a proctored, paper-based, high-stakes assessment that determines university admission eligibility. A student who has never solved a problem without AI scaffolding will fail this exam. No amount of philosophical argument about assessment reform will change a student's Bagrut score in 2026.

This is the pragmatic constraint. Cena can advocate for reform while simultaneously preparing students for the system as it exists. The two are not in tension if the product is designed correctly.

### 3.2 Tool Misuse Is Real: The Photomath/Chegg Data

The scale of tool misuse is not hypothetical. Before its Google acquisition, Photomath processed 2 billion math questions per month from 250 million downloads --- the vast majority homework problems where the student had no intent to learn the method ([Fast Company, 2023](https://www.fastcompany.com/90890819/chatgpt-ai-homework-education-stock-drops-chegg-business)). Chegg generates approximately $500 million per year from crowd-sourced homework solutions. One professor reported filing approximately 40 academic integrity violations in a single semester, mostly from students uploading exam questions to Chegg during the exam itself ([Michigan Tech, 2024](https://www.mtu.edu/conduct/integrity-center/pdfs/faculty-guide-to-chegg.pdf)). Photomath is the number-one education app on the App Store, and teachers consistently report the signature pattern: students who "solve" complex problems instantly during homework but fail the in-class quiz.

The lesson: any tool that can show a solution *will* be used by some fraction of students to bypass learning entirely. Design must account for this.

### 3.3 Desirable Difficulties: Learning Without Struggle Does Not Stick

Robert Bjork's (1994) theory of "desirable difficulties" provides the strongest cognitive science argument against frictionless AI assistance. Bjork demonstrated that conditions which make learning *feel* harder during practice --- spacing, interleaving, retrieval practice, variation --- dramatically improve long-term retention and transfer. Effortful learning outperforms easy learning by approximately 60% over the long term ([Bjork & Bjork, 2011](https://bjorklab.psych.ucla.edu/wp-content/uploads/sites/13/2016/04/EBjork_RBjork_2011.pdf)). Active retrieval practice, in particular, is one of the most potent strategies for building durable knowledge.

Manu Kapur's (2014) research on "productive failure" extends this to mathematics specifically. Students who attempted to solve math problems *before* receiving instruction --- and failed --- demonstrated significantly greater conceptual understanding and transfer ability than students who received direct instruction first. The failure activated prior knowledge, exposed knowledge gaps, and produced deeper pattern recognition ([Kapur, 2014](https://onlinelibrary.wiley.com/doi/abs/10.1111/cogs.12107)). Critically, productive failure students matched or exceeded direct-instruction students on procedural fluency while substantially outperforming on conceptual understanding.

The implication for AI tutoring is pointed: if a student can always get a hint before struggling, the desirable difficulty is removed. The student's performance looks good in the moment --- they complete the problem, they feel competent --- but the long-term retention may be weaker than if they had struggled first.

### 3.4 Dan Meyer: "The Tutors Are Too Nice"

Dan Meyer, vice president at Amplify and former chief academic officer at Desmos, has been the most articulate skeptic of AI tutoring's educational claims. In a December 2024 essay, Meyer demonstrated that when he repeatedly answered "IDK" to Khanmigo's scaffolded hints, the tutor responded with cheerful affirmations --- "No problem!" and "No worries!" --- before progressively revealing the complete solution. After eight "IDK" responses, the student had the full answer without having done any work ([Meyer, 2024](https://danmeyer.substack.com/p/these-tutors-are-too-nice)).

Meyer argues that students need "warm demanders" --- educators who are nurturing but who do not lower academic standards. A warm demander might say, "I know you and I know you're capable of more than IDK." Current AI tutors offer what Meyer calls "an abundance of warmth without being demanding in the way that kids need."

Meyer's broader critique is empirical. A Spring 2024 RAND survey found that 82% of math teachers have *never* used AI tools for mathematics teaching. Only 1% use AI "often." At ISTE (the tech-focused education conference), 25% of 2024 sessions addressed AI. At math, English, science, and administrator conferences, the figure was 5% or below ([Meyer, 2025](https://danmeyer.substack.com/p/the-ai-disconnect)). The disconnect between Silicon Valley optimism and classroom reality is wide and growing.

### 3.5 Sal Khan's Own Admission

Even Sal Khan --- the most prominent advocate for AI tutoring --- has revised his position. In an April 2026 interview with Chalkbeat, Khan acknowledged that Khanmigo has not produced the revolution he predicted. "For a lot of students, it was a non-event. They just didn't use it much," Khan said. Khan Academy's chief learning officer, Kristen DiCerbo, stated plainly: "So far I am not seeing the revolution in education." DiCerbo identified a fundamental problem: "Students aren't great at asking questions well." Khan's conclusion: "Our biggest lever is really investing in the human systems" ([Chalkbeat, 2026](https://www.chalkbeat.org/2026/04/09/sal-khan-reflects-on-ai-in-schools-and-khanmigo/)).

This is a significant concession from the person who told TED in 2023 that "we're at the cusp of using AI for probably the biggest positive transformation that education has ever seen." The evidence from the field tempers the theoretical promise.

---

## 4. Cena's Position: The Step-Solver as Assessment Reform

Cena's position synthesizes both sides: the objection is *directionally correct* (assessment reform is needed, learning with help is legitimate, the homework pipeline is broken) but *operationally incomplete* (exams still exist, struggle matters, tool misuse is real). The step-solver is designed to be the assessment reform itself --- not by lobbying the Ministry of Education, but by embodying process-over-product pedagogy in the product's architecture.

### 4.1 Process Over Product

When a student photographs a problem and submits it to Cena, Cena does not return the answer. It returns a decomposition of the problem into substeps, with each substep requiring the student to attempt a solution before the next hint is revealed. The student's interaction trace --- which hints they needed, which substeps they solved independently, how many attempts each substep required --- becomes a rich record of their learning process. This trace is more pedagogically valuable than a correct answer on a homework sheet.

### 4.2 Scaffolded Struggle, Not Frictionless Answers

Cena's scaffolding is designed to preserve desirable difficulty. The system does not reveal the next hint immediately. It prompts the student to try first. If the student's attempt is incorrect, Cena provides targeted feedback on the specific error rather than the complete solution. The scaffolding is graduated: easier problems get fewer hints with larger gaps between them; harder problems get more granular support. This is Vygotsky's ZPD operationalized: the system calibrates its support to keep the student working at the edge of their current ability.

The critical design difference from Photomath and Chegg: those tools optimize for answer delivery speed. Cena optimizes for learning-per-minute. A student who wants only the answer will find Cena slower and more demanding than Photomath. That friction is intentional.

### 4.3 Mastery-Gated Progression

Cena's question engine uses mastery-gated progression: a student cannot advance to the next topic until they demonstrate mastery of the current one, and mastery is measured by independent problem-solving, not by guided-solution completion. A student who uses scaffolded hints to work through a quadratic equation has not "mastered" quadratics --- they have practiced quadratics with support. Mastery requires solving similar problems without hints. The scaffolded session contributes to learning; the unscaffolded assessment confirms it.

---

## 5. Design Mitigations

### 5.1 Exam Mode

Cena implements a teacher-configurable exam mode that can be activated per-class, per-time-window, or globally. When exam mode is active, the screenshot analyzer returns a message explaining that Cena is unavailable during exams and offers to help the student review the material afterward. Exam mode can be triggered by:

- Teacher manual activation (linked to the class calendar)
- Scheduled windows matching known Bagrut exam dates
- Geofence signals (if the student's device is within a known exam venue, subject to privacy constraints documented in iteration-06)

### 5.2 Learning-First Similar-Question Response

When a student photographs a problem that exactly matches a known exam question (detected via the content fingerprinting system documented in iteration-07-academic-integrity.md), Cena does not solve that problem. Instead, it generates a parametrically similar problem and walks the student through that one. The student learns the *method* without receiving the specific answer to the specific question they photographed. This is the pedagogical equivalent of a tutor saying, "I'm not going to do your test for you, but let me show you how to solve a problem just like it."

### 5.3 Teacher Visibility Into Usage Patterns

Cena provides teachers with aggregated, anonymized dashboards showing:

- Per-student hint dependency ratios (what fraction of substeps required hints)
- Time-on-task distributions (students who spend 2 seconds per substep versus 2 minutes)
- Topic-level mastery progression (has the student demonstrated independent solving?)
- Anomaly flags for patterns consistent with answer-harvesting (high volume, low time-per-problem, no independent solving attempts)

These dashboards allow teachers to distinguish between students who are using Cena as a learning tool and students who are using it as a Photomath replacement.

### 5.4 The Homework-Help / Exam-Cheating Distinction in Product Architecture

Cena's architecture enforces a structural distinction between two use cases:

| Dimension | Homework Help (permitted) | Exam Cheating (blocked) |
|-----------|--------------------------|------------------------|
| Timing | Outside scheduled exam windows | During exam windows |
| Response | Scaffolded hints, substeps | Blocked or redirected |
| Content | Method instruction | No answer to detected exam questions |
| Visibility | Full teacher dashboard | Alert to teacher |
| Student record | Learning trace stored | Attempt flagged |
| Similar-question | Generated and guided | N/A (blocked) |

This distinction is not a policy overlay --- it is built into the product's API routing, response generation, and data model.

---

## 6. Open Questions

These questions are genuinely open. They represent tensions that Cena's product team and education advisors must navigate without pretending there are easy answers.

### 6.1 Should Cena Lobby for Bagrut Reform?

The evidence strongly supports moving mathematics assessment toward process-based evaluation. TEQSA, UNESCO, the UK QAA, and the Israeli Ministry of Education itself (in humanities subjects) are all moving in this direction. Cena could advocate for extending the reform model to mathematics Bagrut --- replacing or supplementing the single external exam with portfolio assessment, teacher-evaluated problem-solving sessions, or process-documented mathematical investigations.

Arguments for: Cena has a credible voice as an education technology company serving Bagrut students. The reform would benefit students and would also happen to make Cena's product more valuable. The educational evidence supports the position.

Arguments against: An edtech company lobbying for assessment changes that happen to benefit its business model is a conflict of interest that regulators, parents, and teachers will immediately identify. Cena's credibility as a neutral educational tool depends on not being perceived as manipulating the system it operates within.

**Current position**: Cena should support reform efforts initiated by educators and policymakers but should not lead the lobbying. The product should be designed to work well under both current and reformed assessment regimes.

### 6.2 Is It Cena's Job to Enforce School Policies?

Some schools prohibit all use of AI tools for homework. If a student at such a school uses Cena, is Cena complicit in a policy violation? Cena is not a party to the school's homework policy. It is a learning tool available to students, like a textbook or a calculator. No textbook publisher restricts access based on whether the school allows textbooks for homework.

However: Cena serves minors in an educational context, and its value proposition to schools depends on teacher trust. If teachers perceive Cena as undermining their authority to set homework policies, adoption will fail regardless of the philosophical merits.

**Current position**: Cena should provide schools with the tools to enforce their own policies (exam mode, usage windows, teacher-configurable access controls) without imposing a single policy model. The product respects institutional autonomy.

### 6.3 Where Is the Line Between Scaffolding and Spoon-Feeding?

Bjork and Kapur's research says struggle is necessary for durable learning. Vygotsky's ZPD says support is necessary for learning beyond current ability. Both are correct, which means the calibration of scaffolding intensity is the most important pedagogical design decision Cena makes. Too much scaffolding removes desirable difficulty. Too little scaffolding leaves the student in what Vygotsky called the zone of frustration --- beyond the ZPD, where no amount of effort will produce learning.

Meyer's critique of Khanmigo --- that it capitulates to "IDK" responses --- is a design failure, not a theoretical one. The theory says scaffolding should be graduated and responsive. The implementation failed to hold students accountable for effort. Cena must learn from this failure: the system should escalate its demands before escalating its hints. A student who says "I don't know" to three consecutive substeps should receive motivational redirection and a simpler prerequisite problem, not the answer.

**Current position**: This is an active area of product experimentation. The scaffolding calibration algorithm is not settled and should be treated as a continuously tuned parameter informed by learning outcome data.

### 6.4 What If Assessment Never Reforms?

The Bagrut system has existed since 1954. Reform in education is measured in decades. It is entirely possible that in 2036, Israeli mathematics students will still sit proctored paper exams. If that remains true, Cena's role is to prepare students for that reality while providing better learning experiences along the way. The product cannot be designed solely for a reformed world that may not arrive.

---

## 7. Sources

1. Cooper, H., Robinson, J. C., & Patall, E. A. (2006). "Does Homework Improve Academic Achievement? A Synthesis of Research, 1987--2003." *Review of Educational Research*, 76(1), 1--62. [Link](https://journals.sagepub.com/doi/10.3102/00346543076001001)

2. Duke Today (2006). "Duke Study: Homework Helps Students Succeed in School, As Long as There Isn't Too Much." [Link](https://today.duke.edu/2006/03/homework.html)

3. Kohn, A., Bennett, S., & Kalish, N. — summarized in ASCD (2007). "The Case For and Against Homework." *Educational Leadership*. [Link](https://ascd.org/el/articles/the-case-for-and-against-homework)

4. Vygotsky, L. S. (1978). *Mind in Society: The Development of Higher Psychological Processes*. — summarized at Simply Psychology. [Link](https://www.simplypsychology.org/zone-of-proximal-development.html)

5. Bloom, B. S. (1984). "The 2 Sigma Problem: The Search for Methods of Group Instruction as Effective as One-to-One Tutoring." *Educational Researcher*, 13(6), 4--16. [Link](https://journals.sagepub.com/doi/10.3102/0013189X013006004)

6. Bjork, E. L. & Bjork, R. A. (2011). "Making Things Hard on Yourself, But in a Good Way: Creating Desirable Difficulties to Enhance Learning." [Link](https://bjorklab.psych.ucla.edu/wp-content/uploads/sites/13/2016/04/EBjork_RBjork_2011.pdf)

7. Kapur, M. (2014). "Productive Failure in Learning Math." *Cognitive Science*, 38(5), 1008--1022. [Link](https://onlinelibrary.wiley.com/doi/abs/10.1111/cogs.12107)

8. Meyer, D. (2024). "The Tutors Are Too Nice." *Mathworlds* (Substack). [Link](https://danmeyer.substack.com/p/these-tutors-are-too-nice)

9. Meyer, D. (2025). "The AI Disconnect." *Mathworlds* (Substack). [Link](https://danmeyer.substack.com/p/the-ai-disconnect)

10. Khan, S. — reported in Chalkbeat (2026). "Why Sal Khan is rethinking how AI will change schools." [Link](https://www.chalkbeat.org/2026/04/09/sal-khan-reflects-on-ai-in-schools-and-khanmigo/)

11. Desai, H. (2025). "What's worth measuring? The future of assessment in the AI age." *UNESCO*. [Link](https://www.unesco.org/en/articles/whats-worth-measuring-future-assessment-ai-age)

12. TEQSA (2025). "Enacting Assessment Reform in a Time of Artificial Intelligence." [Link](https://www.teqsa.gov.au/guides-resources/resources/corporate-publications/enacting-assessment-reform-time-artificial-intelligence)

13. Jerusalem Post (2024). "Education Ministry present plans for new Bagrut exams program." [Link](https://www.jpost.com/israel-news/article-734306)

14. AACRAO (2024). "The reform of the matriculation exams starts in Israel." [Link](https://www.aacrao.org/edge/emergent-news/the-reform-of-the-matriculation-exams-starts-in-israel)

15. Frontiers in Education (2024). "AI-resistant assessments in higher education: practical insights from faculty training workshops." [Link](https://www.frontiersin.org/journals/education/articles/10.3389/feduc.2024.1499495/full)

16. MDPI Education Sciences (2025). "Redesigning Assessments for AI-Enhanced Learning: A Framework for Educators in the Generative AI Era." [Link](https://www.mdpi.com/2227-7102/15/2/174)

17. Teaching@Sydney (2026). "Navigating AI in Higher Education: tasks ahead for 2025 and 2026." [Link](https://educational-innovation.sydney.edu.au/teaching@sydney/navigating-ai-in-higher-education-tasks-ahead-for-2025-and-2026/)

18. Michigan Tech (2024). "Faculty Guide to Chegg." [Link](https://www.mtu.edu/conduct/integrity-center/pdfs/faculty-guide-to-chegg.pdf)

19. Fast Company (2023). Reported Photomath statistics. [Link](https://www.fastcompany.com/90890819/chatgpt-ai-homework-education-stock-drops-chegg-business)
