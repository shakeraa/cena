# Pedagogical Controversy: Automation Bias in AI-Powered Math Tutoring

> **Series**: Student Screenshot Question Analyzer -- Defense-in-Depth Research
> **Type**: Pedagogical controversy analysis (steel-manned)
> **Date**: 2026-04-12
> **Pipeline Context**: Student photo --> Gemini 2.5 Flash --> LaTeX --> CAS validation --> Step-by-step solver
> **Core Question**: Does Cena's AI-verified step-by-step tutoring teach students to trust machines instead of thinking for themselves?

---

## 1. The Objection (Steel-Manned)

The strongest version of this criticism runs as follows.

When a student photographs a math problem and submits it to Cena, every subsequent cognitive act is mediated by a machine. A vision model reads the question. A computer algebra system checks each step. A scaffolding engine decides how much help to give. The student never needs to verify that the system understood the question correctly, never needs to confirm that the CAS judgment is sound, and never needs to decide whether the scaffolding level is appropriate. Over months of daily practice, the student internalizes a simple heuristic: *if the system says my step is correct, it is correct.* The green checkmark becomes the arbiter of mathematical truth, not the student's own reasoning.

This is not a speculative worry. It is the educational instantiation of a phenomenon that Parasuraman and Manzey (2010) documented across decades of human-factors research: *automation bias*, the tendency of humans to defer to automated decision aids even when those aids are wrong, and *automation complacency*, the tendency to reduce monitoring of automated systems over time. Their integrated attentional model demonstrated that these are not personality defects or signs of laziness. They are predictable consequences of how human attention works under cognitive load. When a reliable system handles a subtask, attentional resources are reallocated elsewhere. The monitoring that would catch errors degrades -- not because the human chose to stop watching, but because the architecture of attention makes sustained vigilance over a reliable system metabolically expensive and cognitively unnatural.

The concern is sharpened by Cena's target population: Israeli Bagrut students aged 14--18. These are adolescents in a high-stakes examination context. They have strong extrinsic motivation to practice efficiently, not to develop verification habits. If the system reliably confirms correct steps, the rational student strategy is to trust the confirmation and move on. The student who pauses to independently verify each CAS-checked step is wasting time relative to the student who trusts the machine and solves three more problems in the same session. Cena's own BKT mastery model rewards throughput: more correct steps raise P(learned), which fades scaffolding, which unlocks harder problems, which accelerates progress toward exam readiness. The incentive structure *selects for* automation dependence.

Critics would argue that the end state is a student who can solve problems inside the system but cannot verify solutions outside it -- exactly the wrong preparation for a proctored Bagrut exam where no CAS confirmation exists.

---

## 2. Evidence Supporting the Objection

### 2.1 The Parasuraman-Manzey Framework

Parasuraman and Manzey (2010) published their landmark review "Complacency and Bias in Human Use of Automation: An Attentional Integration" in *Human Factors*. The paper synthesized findings from aviation, medicine, process control, and decision support systems to establish two distinct but related phenomena:

- **Automation complacency**: reduced monitoring of an automated system under multi-task conditions. Complacency emerges even among expert operators and cannot be eliminated by training or explicit instructions to remain vigilant.
- **Automation bias**: using automated cues as a heuristic replacement for independent judgment, leading to *omission errors* (failing to notice when automation misses something) and *commission errors* (following automated advice that is wrong).

Their attentional integration model showed that both phenomena arise from the same mechanism: attention is a limited resource, and reliable automation causes rational reallocation of that resource away from monitoring. The implication for education is direct. A CAS engine that is correct 99.5% of the time will, over hundreds of interactions, train the student to stop checking its output -- precisely because checking is costly and almost never rewarded.

Subsequent reviews have confirmed the robustness of these findings. Goddard, Rouet, and Kendeou (2014) extended the framework to information-processing contexts, and Cummings (2017) documented automation bias in medical decision support systems with high base-rate accuracy, a close analogue to a CAS engine.

### 2.2 The Calculator Wars: What Actually Happened

The closest historical precedent to Cena's situation is the adoption of calculators in K--12 math education from the mid-1970s through the 1990s. The debate was ferocious and strikingly parallel: would calculators make students unable to compute, think, or verify?

Hembree and Dessart (1986) published the definitive meta-analysis in the *Journal for Research in Mathematics Education*, synthesizing 79 studies. Their findings were nuanced:

- Calculator use *improved* problem-solving and conceptual understanding when embedded in curricula designed around the technology.
- Calculator use had *no negative effect* on paper-and-pencil computation when instruction still included it.
- But: studies that simply added calculators to existing curricula without redesigning instruction showed *no improvement* either.

The critical lesson is that the tool was not the variable. The pedagogy around the tool was. Calculators did not destroy arithmetic ability -- but they did not improve it either. What they did was shift what counted as the cognitively valuable part of math, moving emphasis from computation to problem formulation and interpretation.

However, LaCour, Cantu, and Davis (2019) demonstrated in "When Calculators Lie" (*PLOS ONE*) that college students could not detect obviously wrong calculator outputs. When researchers programmed an on-screen calculator to add 15% to certain answers, the vast majority of 482 undergraduates did not notice. When the error was increased to 120%, most still did not notice. Higher numeracy predicted better detection, but incentives for accuracy did not increase detection rates. This is automation bias measured directly, in a mathematical context, decades after calculators became universal.

The implication for Cena: even if CAS verification is beneficial on average, it may erode the verification instinct that students need in CAS-free environments.

### 2.3 ChatGPT and the Over-Reliance Problem

Kasneci et al. (2023) published "ChatGPT for Good? On Opportunities and Challenges of Large Language Models for Education" in *Learning and Individual Differences*, identifying over-reliance on AI-generated outputs as a primary risk. Unlike Cena's CAS engine, ChatGPT produces answers that *look* authoritative but may be wrong. The concern they raised -- that students would accept AI outputs without critical evaluation -- is structurally similar to automation bias, but with the added problem that LLM outputs lack the deterministic guarantees of a CAS.

Jose et al. (2025) deepened this analysis in "The Cognitive Paradox of AI in Education: Between Enhancement and Erosion" (*Frontiers in Psychology*). Their review found that University of Pennsylvania students using ChatGPT solved 48% more problems but scored 17% lower on conceptual understanding tests. The efficiency gain came at the cost of depth. They argued that AI reduces *germane cognitive load* -- the productive struggle that produces durable learning -- while efficiently handling *extraneous load* that does not contribute to understanding.

### 2.4 Cognitive Offloading Research

Risko and Gilbert (2016) published a comprehensive review of cognitive offloading in *Trends in Cognitive Sciences*, defining it as "the use of physical action to alter the information processing requirements of a task so as to reduce cognitive demand." Their metacognitive framework demonstrated that offloading decisions are driven not by objective task difficulty but by the individual's *metacognitive evaluation* of their own abilities -- and these evaluations are frequently wrong. People offload tasks they could handle internally, and the act of offloading further degrades the internal capacity that would have been exercised.

Applied to Cena: when the CAS checks every step, the student's internal verification machinery lies fallow. Over time, metacognitive calibration drifts: the student becomes less accurate at judging whether their own work is correct, because they have outsourced that judgment to the machine.

### 2.5 Recent Evidence on AI in Tutoring

A 2025 Harvard Gazette feature, "Is AI Dulling Our Minds?", reported on a (non-peer-reviewed) MIT Media Lab study finding that "excessive reliance on AI-driven solutions" may contribute to "cognitive atrophy." Harvard education researchers offered a more nuanced framing: Dan Levy (Kennedy School) argued that AI helps learning when it assists thinking rather than replacing it, while Christopher Dede (Graduate School of Education) cautioned against AI that performs auto-completion of cognitive tasks the student should be doing.

The 2025 study by Hou et al. in the *British Journal of Educational Technology*, "The Role of Critical Thinking on Undergraduates' Reliance Behaviours on Generative AI in Problem-Solving," found that critical thinking mediates the relationship between AI literacy and reliance behavior. Students with strong critical thinking skills used AI as a tool; students with weak critical thinking skills used it as a crutch. The study did not find that AI *caused* weak critical thinking, but it demonstrated that AI-rich environments disproportionately disadvantage students who lack it.

---

## 3. Evidence Against the Objection (or Nuancing It)

### 3.1 Calculators Did Not Destroy Math Ability

The calculator wars produced a clear empirical verdict: calculators shifted what skills mattered, but they did not produce a generation unable to think mathematically. Hembree and Dessart (1986) found positive effects on problem-solving when calculators were integrated into redesigned curricula. A follow-up meta-analysis by Ellington (2003) in the *Journal for Research in Mathematics Education* confirmed these findings with additional studies from the 1990s.

The key distinction is between *tool dependence* (students cannot compute 7 x 8 without a calculator) and *cognitive dependence* (students cannot reason about multiplication without a calculator). The former happened to some degree. The latter did not. Cena's parallel question is whether CAS verification creates tool dependence (students cannot verify steps without the system) or cognitive dependence (students cannot reason about verification without the system). If the answer is the former, that is a minor problem because the Bagrut exam does not require CAS-level symbolic verification. If the answer is the latter, it is a serious problem.

### 3.2 Step-Based Tutoring Systems Improve Learning

VanLehn (2011) published a comprehensive review in *Educational Psychologist* comparing human tutoring, intelligent tutoring systems (ITS), and conventional instruction. His central finding: step-based ITS achieved an effect size of d = 0.76, nearly matching human tutoring (d = 0.79) and far exceeding answer-based systems (d = 0.31). The difference was entirely attributable to the granularity of feedback -- step-level verification, not answer-level verification.

This is directly relevant to Cena's design. The step-solver does not just confirm final answers; it checks each intermediate step, providing immediate feedback on the algebraic or calculus operation the student just performed. VanLehn's evidence suggests this granularity is precisely what produces learning gains. The automation bias concern must be weighed against the strong evidence that step-level feedback accelerates skill acquisition.

Renkl and Atkinson (2003) added a further nuance with their work on faded worked examples: scaffolding that begins with fully worked solutions and gradually removes support as the student progresses produces effect sizes of d = 0.4--0.6. Cena's three-tier scaffolding model (Full at BKT mastery < 0.20, Partial at 0.20--0.60, Minimal at > 0.60) implements exactly this principle, using Bayesian Knowledge Tracing (Corbett & Anderson, 1995) to time the fading.

### 3.3 The CAS Is Not an LLM

A critical distinction separates Cena's automation bias risk from the ChatGPT over-reliance problem documented by Kasneci et al. (2023) and Jose et al. (2025).

ChatGPT is a stochastic text generator. Its outputs are probabilistic, non-deterministic, and sometimes wrong in ways that are difficult to detect. Trusting ChatGPT for math verification is genuine automation bias because the automation is unreliable.

Cena's CAS engine -- MathNet for in-process operations, SymPy for symbolic equivalence, Wolfram as admin fallback -- is a deterministic proof engine. When it says `(x-2)^2 - 1` is symbolically equivalent to `x^2 - 4x + 3`, that is a mathematical fact, not a probabilistic guess. The CAS does not hallucinate. It does not confabulate. It either proves equivalence or it does not.

"Trusting the CAS" is categorically different from "trusting ChatGPT." Trusting a CAS for symbolic equivalence is no more automation bias than trusting a ruler for length measurement. The student who trusts the CAS's judgment on symbolic equivalence is trusting the correct tool for the job.

However, this defense has a boundary. The CAS verifies *what the student typed*, not *what the student meant*. If a student intends to write `(x-2)^2 - 1` but types `(x+2)^2 - 1`, the CAS correctly reports that this is wrong. But if the student makes a conceptual error that happens to produce a symbolically equivalent expression through a wrong method, the CAS cannot detect the flawed reasoning. Step-level verification catches most such cases (because intermediate steps constrain the reasoning path), but it is not infallible.

### 3.4 Metacognitive Scaffolding Can Prevent Automation Bias

Lim (2025) presented "DeBiasMe: De-biasing Human-AI Interactions with Metacognitive AIED Interventions" at the ACM AIREASONING workshop. The paper proposed three design principles for reducing automation bias in educational AI:

1. **Deliberate friction**: strategic pauses and reflection prompts that force the student to engage actively rather than passively accepting system outputs.
2. **Bi-directional intervention**: addressing bias at both input (how the student formulates the problem) and output (how the student interprets the system's response).
3. **Adaptive scaffolding**: adjusting the intensity of anti-bias interventions based on the student's trust level, confidence, and demonstrated autonomy.

These are not theoretical proposals. They have design-pattern analogues in existing ITS research, and they map directly onto features that Cena could implement (and partially already has, through its scaffolding adaptation system).

---

## 4. Cena's Specific Position

Cena's architecture creates a distinctive automation bias profile that differs from both the ChatGPT over-reliance problem and the calculator dependence problem.

### 4.1 The CAS Verifies Math, Not the LLM

The pipeline is: Student photo --> Gemini 2.5 Flash (vision) --> LaTeX extraction --> CAS validation. The LLM's role is confined to perception (reading the photo) and explanation (generating natural-language feedback). The LLM never decides whether a mathematical step is correct. That determination is made by SymPy's symbolic equivalence engine, which is deterministic and provably correct within its domain.

This means the "automation" the student is trusting is not a probabilistic model but a mathematical proof engine. The automation bias literature primarily concerns unreliable automation -- systems with meaningful error rates. A CAS with correct implementation has an error rate functionally indistinguishable from zero for the operations Cena uses (polynomial algebra, calculus, trigonometric identities). Trusting it is rational.

### 4.2 The Student Produces; the AI Checks

Cena's step-solver inverts the typical AI tutoring dynamic. The student does not receive AI-generated solutions. The student produces each step, and the CAS confirms or rejects it. This is structurally different from automation that *performs* a task on the student's behalf. The cognitive work of solving remains with the student; only the verification is automated.

The API contract makes this explicit:

```
POST /api/sessions/{id}/question/{qid}/step/{stepNum}
Body: { "expression": "LaTeX string" }
Response: { "correct": bool, "feedback": string?, "nextStepUnlocked": bool }
```

The student must formulate the expression. The system only says whether it is equivalent to the expected expression. This is closer to a math teacher saying "yes, that's right" than to a system that solves the problem for the student.

### 4.3 BKT Mastery Tracking Fades Scaffolding

Cena uses Bayesian Knowledge Tracing (Corbett & Anderson, 1995) to estimate per-skill mastery probability. As P(learned) rises above thresholds, scaffolding is systematically withdrawn:

| Mastery | Scaffolding Level | What Changes |
|---------|-------------------|-------------|
| < 0.20 | Full | Instructions per step, faded worked examples, all hints available |
| 0.20--0.60 | Partial | Some steps pre-filled, fewer instructions, hints on request |
| > 0.60 | Minimal | Numbered slots only, student chooses approach, hints delayed |

This means the system progressively *reduces* the level of automated support. A student who achieves high mastery works in an environment closer to the unscaffolded Bagrut exam. The fading is not optional or student-controlled -- it is driven by the mastery model. This directly addresses the concern that students will become dependent on maximum scaffolding.

### 4.4 The Genuine Risk: Vision Model Misreading

Where Cena *does* have a real automation bias exposure is at the input stage. When a student photographs a question and Gemini 2.5 Flash extracts LaTeX, the student may not verify that the extraction is correct. If the vision model reads "2x^2" as "3x^2," and the student does not catch the error, the entire practice session is wasted -- the student solves the wrong problem correctly.

This is the textbook Parasuraman-Manzey scenario: a reliable-but-imperfect automated system whose errors are missed because the human has stopped monitoring. The vision model's error rate is low (competitive VLMs achieve > 95% accuracy on printed math, lower on handwriting), but even a 2--5% error rate on handwritten input means that for every 20--50 photos, a student will silently practice the wrong problem once.

The CAS cannot catch this error. The CAS verifies internal consistency of the extracted problem, not fidelity to the original photo. If the extracted problem is self-consistent but different from the photographed problem, the entire downstream pipeline operates correctly on wrong input.

---

## 5. Design Mitigations

### 5.1 Confidence Indicators on OCR Extraction

When the vision model extracts a question, the system should display the extracted text alongside a confidence indicator and an explicit prompt for student confirmation:

> "We read this as: *Find the roots of f(x) = 3x^2 - 7x + 2*. Is this what your question says? [Yes, continue] [No, let me correct it]"

This addresses the vision-model misreading risk directly. It introduces deliberate friction (Lim, 2025) at exactly the point where automation bias is most dangerous. The student must actively confirm the extraction before CAS verification begins.

Implementation note: Gemini 2.5 Flash provides token-level log-probabilities. Low-confidence tokens (especially coefficients, exponents, and signs -- the elements most likely to be misread and most consequential if wrong) should be visually highlighted.

### 5.2 Student Confirmation Step Before CAS Verification

Beyond OCR confirmation, the step-solver should occasionally require the student to predict whether their own step is correct *before* the CAS checks it:

> "Before I check: do you think this step is correct? [Yes] [Not sure] [No]"

This is a metacognitive calibration prompt. Over time, comparing the student's self-assessment to the CAS result provides data on whether the student is developing independent verification ability or becoming dependent on external confirmation.

If the student consistently says "Yes" and is correct, their verification skills are intact. If the student consistently says "Not sure" and defers to the CAS, automation complacency may be developing.

### 5.3 "Check Your Own Work" Prompts at Mastery Transitions

When a student transitions from Partial to Minimal scaffolding (BKT mastery crossing 0.60), the system should introduce a "verification challenge": a short sequence of problems where CAS feedback is delayed, and the student must self-assess each step before receiving confirmation.

This serves dual purposes:
- It functions as a mastery gate, confirming that the student can verify independently before scaffolding is withdrawn.
- It applies the desirable difficulty principle (Bjork, 1994): reducing feedback frequency during the training phase is uncomfortable but produces superior long-term retention and transfer.

### 5.4 Occasional Deliberate Withholding of Verification

At the Minimal scaffolding level (mastery > 0.60), the system should occasionally withhold step-by-step CAS verification entirely, asking the student to complete the entire solution before checking:

> "You've shown strong mastery of this topic. Try solving this one completely before checking. [Submit full solution for verification]"

This simulates the exam condition (no per-step feedback) and tests whether the student can maintain accuracy without external confirmation. If accuracy drops significantly in withheld-verification mode, it is a signal that automation dependence has developed.

Bjork (1994) documented that reducing feedback frequency during training -- a "desirable difficulty" -- degrades short-term performance but enhances long-term retention and transfer. Delaying or withholding CAS feedback at appropriate mastery levels is a direct application of this principle.

### 5.5 Verification Skill Tracking

The system should maintain a per-student metric: *verification accuracy*, defined as the correlation between the student's self-assessment ("I think this step is correct") and the CAS result. This metric should be tracked alongside BKT mastery and displayed to the student as part of their progress dashboard.

A student with high mastery but low verification accuracy is the automation bias failure case: they can solve problems correctly with CAS support but cannot judge their own correctness independently. This combination should trigger a targeted intervention (more self-check prompts, delayed feedback, verification challenges).

---

## 6. Open Questions

### 6.1 Should Cena Sometimes Show Wrong CAS Results?

The LaCour et al. (2019) "When Calculators Lie" study demonstrated that introducing deliberate errors into calculator output can test (and potentially train) verification skills. Should Cena occasionally present a false CAS rejection -- telling the student a correct step is wrong -- to see if the student pushes back?

**Arguments for**: It would directly train the verification skill that automation bias erodes. Students who learn to question the system's judgment develop stronger metacognitive calibration.

**Arguments against**: Deliberately lying to students violates trust, particularly for minors in a high-stakes exam preparation context. If students learn that the system sometimes lies, they may lose trust in *all* CAS feedback, undermining the pedagogical value of step-level verification. The ethical implications for a K--12 product are substantial.

**Possible middle ground**: Rather than false results, introduce "ambiguity prompts" where the CAS reports that the student's expression is *equivalent but non-standard*, and asks the student to verify whether their approach is the intended one. This maintains truthfulness while prompting verification behavior.

### 6.2 At What Mastery Level Should AI Verification Be Withdrawn?

The current design withdraws scaffolding instructions at BKT mastery > 0.60 but maintains CAS step verification at all levels. Should verification itself be withdrawn at some mastery threshold?

If verification is always available, the student never needs to develop full independence. If verification is withdrawn too early, the student loses the feedback that drives learning.

One approach: maintain CAS verification but delay it. At mastery > 0.80, the student completes the entire solution before receiving per-step CAS results. This preserves the learning signal while requiring the student to exercise independent judgment during the solving process.

### 6.3 How Do You Measure Automation Bias in This Context?

Measuring automation bias requires knowing what the student would have done *without* automation. In a laboratory study, you can compare conditions. In a production tutoring system, you cannot withhold the CAS from a random control group without ethical concerns (denying a potentially beneficial intervention to minors preparing for high-stakes exams).

Possible measurement strategies:

- **Within-student comparison**: compare accuracy on CAS-verified problems vs. "verification withheld" problems at the same mastery level. If accuracy drops sharply without CAS, dependence is indicated.
- **Self-assessment calibration**: track the correlation between student self-assessment and CAS results over time. Declining calibration suggests growing automation complacency.
- **Transfer tests**: administer periodic paper-and-pencil assessments (without CAS) and compare performance to in-system performance. A large gap indicates that in-system skills are not transferring to unassisted contexts.
- **OCR confirmation rates**: track how often students reject or modify the vision model's extraction. If rejection rates decline over time (even as the model's true error rate remains constant), complacency is developing at the input stage.

### 6.4 Does Scaffolding Fade Fast Enough?

Kalyuga (2003) documented the *expertise reversal effect*: scaffolding that helps novices actively *harms* advanced students by imposing unnecessary cognitive load and preventing the development of autonomous schemas. Cena's current scaffolding thresholds (Full at < 0.20, Partial at 0.20--0.60, Minimal at > 0.60) were chosen based on typical BKT parameters in the ITS literature, but they have not been validated against Bagrut student populations specifically. If scaffolding fades too slowly, advanced students may develop automation dependence precisely because the system does not trust them to work independently.

### 6.5 Cultural Context: Israeli Education and Authority Deference

The automation bias literature is overwhelmingly based on Western (US/European) study populations. Israeli educational culture has distinct features -- including a traditionally less deferential attitude toward authority -- that may modulate automation bias effects. However, the high-stakes nature of the Bagrut and the cultural emphasis on academic achievement in Israeli society may create stronger incentives for efficiency-maximizing behavior (trusting the system) than for verification behavior (questioning the system). No studies have examined automation bias in Israeli K--12 math education specifically.

---

## References

1. Parasuraman, R., & Manzey, D. (2010). Complacency and bias in human use of automation: An attentional integration. *Human Factors*, 52(3), 381--410. https://doi.org/10.1177/0018720810376055

2. Hembree, R., & Dessart, D. J. (1986). Effects of hand-held calculators in precollege mathematics education: A meta-analysis. *Journal for Research in Mathematics Education*, 17(2), 83--99. https://doi.org/10.2307/749255

3. Kasneci, E., Sessler, K., Kuchemann, S., Bannert, M., Dementieva, D., Fischer, F., ... & Kasneci, G. (2023). ChatGPT for good? On opportunities and challenges of large language models for education. *Learning and Individual Differences*, 103, 102274. https://doi.org/10.1016/j.lindif.2023.102274

4. Risko, E. F., & Gilbert, S. J. (2016). Cognitive offloading. *Trends in Cognitive Sciences*, 20(9), 676--688. https://doi.org/10.1016/j.tics.2016.07.002

5. VanLehn, K. (2011). The relative effectiveness of human tutoring, intelligent tutoring systems, and other tutoring systems. *Educational Psychologist*, 46(4), 197--221. https://doi.org/10.1080/00461520.2011.611369

6. LaCour, M., Cantu, T., & Davis, T. (2019). When calculators lie: A demonstration of uncritical calculator usage among college students and factors that improve performance. *PLOS ONE*, 14(10), e0223736. https://doi.org/10.1371/journal.pone.0223736

7. Corbett, A. T., & Anderson, J. R. (1995). Knowledge tracing: Modeling the acquisition of procedural knowledge. *User Modeling and User-Adapted Interaction*, 4(4), 253--278. https://doi.org/10.1007/BF01099821

8. Bjork, R. A. (1994). Memory and metamemory considerations in the training of human beings. In J. Metcalfe & A. Shimamura (Eds.), *Metacognition: Knowing about knowing* (pp. 185--205). MIT Press.

9. Renkl, A., & Atkinson, R. K. (2003). Structuring the transition from example study to problem solving in cognitive skill acquisition: A cognitive load perspective. *Educational Psychologist*, 38(1), 15--22. https://doi.org/10.1207/S15326985EP3801_3

10. Kalyuga, S. (2003). The expertise reversal effect. *Educational Psychologist*, 38(1), 23--31. https://doi.org/10.1207/S15326985EP3801_4

11. Jose, B., Cherian, J., Verghis, A. M., Varghise, S. M., Mumthas, S., & Joseph, S. (2025). The cognitive paradox of AI in education: Between enhancement and erosion. *Frontiers in Psychology*, 16, 1550621. https://doi.org/10.3389/fpsyg.2025.1550621

12. Lim, C. (2025). DeBiasMe: De-biasing human-AI interactions with metacognitive AIED interventions. In *Proceedings of the 2025 ACM Workshop on Human-AI Interaction for Augmented Reasoning (AIREASONING)*, Yokohama, Japan.

13. Ellington, A. J. (2003). A meta-analysis of the effects of calculators on students' achievement and attitude levels in precollege mathematics classes. *Journal for Research in Mathematics Education*, 34(5), 433--463. https://doi.org/10.2307/30034795

14. Hou, Y., et al. (2025). The role of critical thinking on undergraduates' reliance behaviours on generative AI in problem-solving. *British Journal of Educational Technology*, 56(2). https://doi.org/10.1111/bjet.13613

15. Cummings, M. L. (2017). Automation bias in intelligent time critical decision support systems. *Decision Making in Aviation* (pp. 289--294). Routledge.

---

**Verdict**: The automation bias objection is *partially valid*. The CAS-verification component of Cena is categorically different from the LLM-over-reliance problem -- trusting a deterministic proof engine is rational, not biased. But the vision-model input stage and the absence of verification-skill tracking create genuine automation bias risks that require explicit design mitigations. The five mitigations proposed above (confidence indicators, self-assessment prompts, mastery-transition verification challenges, occasional feedback withholding, and verification-skill tracking) address the real risk without abandoning the pedagogically validated step-based tutoring model.
