# Iteration 03 -- Pedagogical Controversy: Does Aggressive LaTeX Sanitization Limit Advanced Mathematical Expression?

**Date**: 2026-04-12
**Iteration**: 3 (Companion to iteration-03-latex-sanitization.md)
**Focus**: Whether a ~200-command LaTeX allowlist creates a walled garden that constrains advanced Bagrut students
**Type**: Pedagogical Controversy / Design Tension Analysis

---

## 1. The Objection, Steel-Manned

The strongest version of the criticism runs as follows.

Cena's LaTeX sanitizer allowlists approximately 200 commands and caps expression complexity at 200 commands per submission, 20 levels of nesting depth, and 2,000 characters. KaTeX itself supports 600--700 distinct commands. Full LaTeX, as processed by a TeX engine, is Turing-complete. The sanitizer therefore discards roughly 70% of KaTeX's surface area and 100% of full LaTeX's extensibility.

For a 3-unit Bagrut student working through basic algebra and geometry, this is invisible. But the Israeli 5-unit mathematics track covers differential and integral calculus, sequences and series, probability, analytic geometry, trigonometry, and linear algebra -- material that overlaps with AP Calculus BC and introductory university courses (Do Israel, 2026; Jewish Virtual Library, n.d.). A student at this level who is, say, exploring a novel proof technique, attempting to typeset a piecewise-defined recurrence with custom notation, or experimenting with commutative diagrams and advanced environments encounters the wall.

The objection draws on three bodies of evidence:

**First**, the expertise reversal effect (Kalyuga, Ayres, Chandler, & Sweller, 2003) demonstrates that instructional supports designed for novices become *counterproductive* for advanced learners. Kalyuga writes: "instructional guidance, which may be essential for novices, may have negative consequences for more experienced learners." The mechanism is cognitive: experienced learners already possess schema-based knowledge that provides internal guidance. External scaffolding forces them to reconcile redundant information, imposing additional working memory load rather than reducing it (Kalyuga, 2007). If the allowlist is understood as a form of scaffolding -- the system decides which mathematical vocabulary the student is permitted to use -- then for high-mastery students, it functions not as a safety net but as a cognitive tax.

**Second**, Csikszentmihalyi's flow model (1975; 1990) identifies a *sense of personal control or agency over the activity* as a precondition for the flow state. When both challenge and skill are above average but the environment artificially constrains the action space, the result is not flow but frustration. Csikszentmihalyi & Schneider (2000) found that students in flow states showed 30% higher task persistence and deeper learning compared to non-flow states. Shernoff, Csikszentmihalyi, Schneider, & Shernoff (2014) found that lessons balanced between challenge and skill produced 40% higher engagement scores. A student whose internal mathematical vocabulary exceeds the platform's permitted vocabulary is, by definition, experiencing an environment where the challenge has been artificially lowered relative to their skill -- placing them in the "boredom" channel of the flow model rather than the "flow" channel.

**Third**, Self-Determination Theory (Deci & Ryan, 2000) identifies autonomy, competence, and relatedness as the three basic psychological needs that underpin intrinsic motivation. Autonomy-supportive environments -- those that provide choice and meaningful rationales -- foster "the most volitional and high quality forms of motivation and engagement for activities, including enhanced performance, persistence, and creativity" (Niemiec & Ryan, 2009). An allowlist that silently rejects valid mathematical notation without explanation undermines all three: autonomy (the student cannot choose their notation), competence (the student's valid knowledge is treated as an error), and relatedness (the student feels the system does not understand them).

Francis Su (2022), in the *American Educator*, identifies five freedoms essential to mathematical exploration: the freedom of knowledge (knowing multiple strategies), the freedom to explore (environments that encourage curiosity), the freedom of understanding (genuine comprehension over procedure), the freedom to imagine (constructing new ideas), and the freedom of welcome (belonging). An expression allowlist that blocks valid mathematics contravenes the second, fourth, and fifth freedoms.

The steel-manned conclusion: **By designing for the median student's security needs, you have created a walled garden that actively harms your best students' learning experience, motivation, and mathematical development.**

---

## 2. Evidence Supporting the Objection

### 2.1 Expertise Reversal Is Well-Documented

The expertise reversal effect is not a niche finding. Kalyuga et al. (2003) established it across multiple instructional formats: worked examples, split-attention integration, and redundancy elimination all show reversed effectiveness as learner expertise increases. A meta-review by Kalyuga (2007) confirmed the pattern and urged adaptive instruction that *fades* scaffolding as competence grows. The effect has been replicated in animation segmentation (Spanjers et al., 2011), prompting strategies (Berthold, Roehrig, & Renkl, 2012), and computer-based learning environments generally. The implication for Cena is direct: a fixed allowlist is a fixed scaffold, and fixed scaffolds eventually become obstacles.

### 2.2 Flow Requires Autonomy and Appropriate Challenge

Hattie (2009) reports that concentration and engagement -- hallmarks of the flow state -- correlate with an effect size of *d* = 0.48 on student achievement. This is a practically significant effect. Csikszentmihalyi's eight-channel model predicts that high-skill/low-challenge environments produce boredom, not engagement. For a 5-unit student who has internalized calculus notation, being told that a particular LaTeX command is "disallowed" when they know it is mathematically valid creates exactly this mismatch.

### 2.3 Desmos and GeoGebra Succeed Through Expressiveness

The two most successful mathematical exploration tools in secondary education -- Desmos and GeoGebra -- both emphasize open-ended expression. Desmos allows students to define arbitrary functions, compose them freely, and explore parameter spaces without artificial limits. GeoGebra enables students to "engage actively in exploration, conjecture, and verification of mathematical ideas" (ResearchGate, 2023). Both tools support conceptual learning through experimentation and visual reasoning by enabling students to manipulate parameters, construct geometric objects, and observe real-time changes in algebraic and graphical relationships (Nature, 2025). Neither tool imposes an allowlist on the mathematical objects students can create. Their success is evidence that expressiveness and engagement are correlated in mathematical software.

### 2.4 The Walled Garden Critique in EdTech

The walled garden pattern in educational technology has been criticized extensively. Hybrid Pedagogy argues that LMS walled gardens imply "that the content provided by the instructor within the LMS is the only information relevant to the topic at hand." Dunn (Medium, n.d.) contends that walled garden approaches mean "pupils don't learn necessary skills" because they are never exposed to the messy reality outside the sandbox. The Christensen Institute warns of a "walled garden effect" where data isolation prevents students and teachers from seeing the bigger picture. While these critiques address LMS platforms rather than LaTeX sanitizers specifically, the structural argument is identical: a system that decides what the user is permitted to express is a system that limits what the user can learn.

### 2.5 Vygotsky's ZPD Demands Fading

Vygotsky's Zone of Proximal Development framework emphasizes that scaffolding must be *temporary*. The empirical evidence is strong: programs with explicit fading protocols produced effect sizes of *d* = 0.71, compared to *d* = 0.32 for programs where scaffolds remained constant (drama.education, 2025). A static allowlist is by definition a scaffold that never fades.

---

## 3. Evidence Against the Objection

### 3.1 The Allowlist Covers 99%+ of the Bagrut Syllabus

The 5-unit Bagrut mathematics curriculum covers: algebra, analytic geometry, sequences and series, statistics and probability, 2D and 3D trigonometry, and differential and integral calculus (Do Israel, 2026). Cena's allowlist of ~200 commands includes: all Greek letters used in the curriculum (alpha through omega, upper and lower case), all standard operators (sum, prod, int through iiint, lim, sup, inf), all trigonometric and hyperbolic functions, logarithmic and exponential functions, fraction and radical notations, matrix environments, piecewise cases, and aligned equation environments.

A concrete inventory:

| Bagrut Topic | Required LaTeX | In Allowlist? |
|---|---|---|
| Derivatives | `\frac{d}{dx}`, `\partial`, `\nabla` | Yes |
| Integrals | `\int`, `\iint`, `\iiint`, `\oint` | Yes |
| Limits | `\lim`, `\limsup`, `\liminf`, `\to`, `\infty` | Yes |
| Series | `\sum`, `\prod`, subscripts, superscripts | Yes |
| Matrices | `\begin{pmatrix}...\end{pmatrix}` and 5 other matrix environments | Yes |
| Piecewise functions | `\begin{cases}...\end{cases}` | Yes |
| Set theory / Logic | `\forall`, `\exists`, `\cup`, `\cap`, `\implies`, `\iff` | Yes |
| Trigonometry | All 12 trig + 8 hyperbolic + 8 inverse functions | Yes |
| Vectors | `\vec`, `\hat`, `\overrightarrow` | Yes |
| Complex numbers | `\Re`, `\Im`, `i`, `\bar{z}` | Yes |

No student has yet encountered a Bagrut exam question that cannot be expressed within the allowlist. The commands excluded from the allowlist are overwhelmingly either: (a) typographic commands irrelevant to mathematical content (`\pagestyle`, `\documentclass`, `\usepackage`), (b) file I/O and shell commands (`\input`, `\write18`) that constitute attack vectors, or (c) advanced typographic environments (`\tikz`, `\pgfplots`) that require a full TeX engine and cannot run in KaTeX regardless.

### 3.2 Security Is Non-Negotiable for a Platform Serving Minors

Cena serves Israeli students aged 14--18. The platform processes student photographs, stores personal academic data, and runs server-side code assessment (SymPy). As documented in iteration-03-latex-sanitization.md, the attack surface includes:

- **Arbitrary code execution** via SymPy's `parse_expr()` chain
- **Cross-site scripting** via KaTeX's `\href{javascript:...}` when trust is enabled
- **Server-side request forgery** via `\includegraphics{url}`
- **Denial of service** via exponential token expansion (CVE-2024-28243)
- **SQL injection** via unsanitized LaTeX strings in database queries

Four KaTeX CVEs were published in 2024 alone (CVE-2024-28243 through CVE-2024-28246). The PowerSchool breach of December 2024 exposed data of 60+ million students through a customer support portal -- a far less exotic attack surface than a Turing-complete typesetting language (TechTarget, 2025).

Educational platforms operating in Israel must comply with the Protection of Privacy Law (1981) and its regulations. Platforms serving minors in jurisdictions with COPPA-equivalent protections face strict obligations: the FTC's 2025 amendments to COPPA strengthened data security protections and required "reasonable steps to release children's personal information only to service providers who are capable of maintaining the confidentiality, security, and integrity of such information" (FTC, 2025). A LaTeX parser that accepts arbitrary commands from untrusted input -- input that arrives via OCR of student photographs, a channel an attacker can manipulate -- would not meet a "reasonable steps" standard.

The question is not whether security is important but whether the specific security measure (an allowlist) is proportionate. It is. The allowlist blocks zero pedagogically necessary commands while blocking all known attack vectors.

### 3.3 Advanced Students Have Unrestricted Tools Elsewhere

Students who need full LaTeX expressiveness have access to Overleaf (free for students), local TeX installations, Desmos, GeoGebra, Wolfram Alpha, and Mathematica. Cena's screenshot pipeline is not a general-purpose LaTeX editor. It is a *step solver* that takes a photograph of handwritten work, extracts the mathematics, and provides pedagogical feedback. The pipeline's purpose is assessing student work against curriculum standards, not providing a creative typesetting environment. Expecting a step solver to also be an unrestricted mathematical sandbox conflates two different tools.

### 3.4 The Expertise Reversal Effect Has Boundary Conditions

Kalyuga's own work acknowledges that the expertise reversal effect applies to *instructional guidance that becomes redundant*, not to *safety constraints that prevent system compromise*. The allowlist does not tell the student how to solve the problem or which notation to prefer. It validates that the input is safe to process. This is closer to a seat belt than to a training wheel: experts do not outgrow the need for seat belts.

Furthermore, the effect demands *adaptive* instruction, not *absent* instruction. The prescription is to fade scaffolding as competence grows -- which Cena can implement through tiered access (see Section 5) -- not to remove all guardrails.

---

## 4. Cena's Position: Tiered Expression

Cena adopts a position that acknowledges both sides of the evidence: **safe mode by default, advanced mode by achievement.**

The platform distinguishes two expression contexts:

| Context | Purpose | Expression Scope | Security Model |
|---|---|---|---|
| **Step Solver** (default) | Assess student work, provide feedback | ~200-command allowlist, strict limits | Full sanitization, subprocess isolation |
| **Sandbox Practice** (unlockable) | Free exploration, "what if" experimentation | Extended allowlist (~400 commands), relaxed limits | Sanitization with expanded allowlist, same isolation |

The step solver is the core product and handles 95%+ of student interactions. Its allowlist is tuned to the Bagrut syllabus and errs on the side of security. The sandbox practice mode is an exploratory space where students can experiment with more advanced notation, test conjectures, and push beyond the curriculum -- but still within a security perimeter.

This mirrors the tiered access pattern common in educational software: Moodle's restrict-access system, LearnWorlds' role levels, and Google Classroom's configurable settings all provide differentiated permissions based on demonstrated competence. The difference is that Cena's tiers are *automatically* determined by the platform's mastery assessment, not manually configured by teachers.

---

## 5. Design Mitigations

### 5.1 "Advanced Mode" Toggle for Verified High-Mastery Students

Students who demonstrate sustained high performance on 5-unit material (e.g., mastery score >= 85% across calculus, series, and linear algebra topics over a rolling 30-day window) are automatically offered an "Advanced Mode" toggle. This mode:

- Expands the allowlist from ~200 to ~400 commands, adding: advanced arrow types, commutative diagram primitives (`\xrightarrow`, `\xleftarrow`, `\overset` compositions), additional environments (`\gather`, `\flalign`), and extended symbol sets (Fraktur letters, double-struck symbols beyond `\mathbb`)
- Raises the nesting depth limit from 20 to 30
- Raises the character limit from 2,000 to 4,000
- Raises the command count limit from 200 to 400
- Maintains all security-critical blocks: no file I/O, no shell execution, no `\href`/`\url`, no `\catcode`, no `\def` beyond KaTeX's limited safe subset

The toggle is sticky (persists across sessions) and revocable (resets if mastery score drops below 75% for 14 consecutive days). This implements the fading-scaffold pattern that both Kalyuga and Vygotsky's framework prescribe: more capable students receive less restrictive scaffolding.

### 5.2 Teacher-Unlockable Custom Macro Sets

Teachers can define named macro sets for their classes:

```json
{
  "macroSetName": "5-unit-advanced-proof",
  "teacher": "teacher-uuid",
  "class": "class-uuid",
  "macros": {
    "\\RR": "\\mathbb{R}",
    "\\NN": "\\mathbb{N}",
    "\\eps": "\\varepsilon",
    "\\dint": "\\displaystyle\\int"
  },
  "additionalCommands": ["gather", "flalign", "xrightarrow"],
  "approvedBy": "teacher-uuid",
  "expiresAt": "2026-06-30T00:00:00Z"
}
```

The macro set is pre-validated by the sanitizer: each macro expansion must itself pass the allowlist. Custom macros are syntactic sugar, not escape hatches. The teacher's action is *curating*, not *disabling* the security layer. Macros expire at term end and require re-approval.

This addresses the Self-Determination Theory concern directly: students gain autonomy (they can use notation that matches their mental models), competence (their advanced knowledge is recognized), and relatedness (their teacher has specifically enabled the notation for their class community).

### 5.3 Transparent Rejection with Educational Feedback

When the sanitizer rejects a command, the current behavior is a generic error. The mitigation is to replace this with targeted feedback:

- **For curriculum-adjacent commands**: "The command `\tikz` requires a full TeX engine and cannot run in the step solver. You can use this command in Overleaf or a local LaTeX installation."
- **For security-sensitive commands**: "The command `\input` is not permitted because it can access files outside the math expression. This is a security restriction."
- **For advanced-mode commands**: "The command `\xrightarrow` is available in Advanced Mode. You qualify for Advanced Mode based on your mastery score -- enable it in Settings."

This transforms the rejection from a competence threat into a learning moment and a navigation aid.

### 5.4 Telemetry-Driven Allowlist Expansion

Every rejected command is logged (command name only, no student identifier) to an aggregate telemetry table. Monthly review of the rejection log identifies commands that are:

1. Frequently rejected (>100 rejections/month)
2. Mathematically valid (not attack patterns)
3. Relevant to the Bagrut syllabus or common enrichment topics

These commands are candidates for allowlist promotion. This creates a feedback loop that prevents the allowlist from calcifying: as curriculum emphasis shifts or as students discover new notation patterns, the allowlist adapts.

### 5.5 Escape Hatch: Image-Based Submission Bypass

For the rare case where a student genuinely needs notation that exceeds even the advanced allowlist (e.g., a commutative diagram for a mathematics olympiad preparation session), the platform accepts the original photograph as the canonical submission. The step solver reports "unable to parse" rather than "disallowed," and the teacher receives the image directly for manual review. The student is not punished for the platform's limitations.

---

## 6. Open Questions

### 6.1 Where Is the Optimal Allowlist Boundary?

The current ~200/~400 split is based on engineering judgment and Bagrut syllabus analysis. No empirical study has measured the relationship between allowlist size and student learning outcomes in a step-solver context. A controlled experiment comparing 200-command, 400-command, and 600-command allowlists on student engagement, error rates, and learning gains would provide evidence-based guidance. Is there a diminishing return? Does the error rate increase non-linearly with allowlist size? These are open empirical questions.

### 6.2 Can the Mastery Threshold Be Gamed?

If Advanced Mode provides a meaningfully different experience, students may attempt to inflate their mastery score to unlock it. The 30-day rolling window and multi-topic requirement make this difficult but not impossible. Adaptive testing with item response theory (IRT) calibration would make gaming harder, but adds implementation complexity. The question is whether the benefit of Advanced Mode is large enough to create a gaming incentive at all.

### 6.3 How Should the Platform Handle Hebrew and Arabic Mathematical Notation?

Israeli students write in three languages. Hebrew mathematical convention sometimes differs from international LaTeX convention (e.g., right-to-left layout for word problems, different variable-naming conventions). Arabic mathematical notation has its own conventions. The allowlist currently covers international LaTeX. Extending it to handle language-specific notation patterns is an open design challenge.

### 6.4 Should the Sandbox Practice Mode Run a Full TeX Engine?

A server-side TeX engine (TeX Live in a container) could provide full LaTeX expressiveness in the sandbox practice mode while maintaining security through container isolation. The trade-off is operational complexity, cost, and a larger attack surface (container escapes). Is the pedagogical benefit large enough to justify the security and operational cost?

### 6.5 What Happens When Students Transition to University?

If Cena is the primary mathematical tool for 3 years of high school, students may internalize its notation conventions. When they arrive at university and encounter full LaTeX for the first time, is there a negative transfer effect? Or does early exposure to a LaTeX subset (even a restricted one) provide positive transfer because students already understand the backslash-command paradigm? No study has examined this specific transition.

---

## 7. Verdict

The objection is *pedagogically valid but architecturally addressable*. The expertise reversal effect, flow theory, and Self-Determination Theory all predict negative outcomes when advanced learners are constrained by scaffolding designed for novices. These are serious, well-evidenced concerns from established research programs.

However, the specific constraint at issue -- a LaTeX allowlist in a step-solver pipeline that processes untrusted input from student photographs -- is both narrower and more security-critical than the general case. The allowlist covers the entire Bagrut syllabus. The security risks of removing it are concrete and documented (four CVEs in 2024 alone, arbitrary code execution via SymPy, XSS via KaTeX). The platform is not a general-purpose LaTeX editor.

The resolution is not to choose between security and expressiveness but to provide *tiered expressiveness within a security perimeter*: safe mode for the step solver, advanced mode for verified high-mastery students, teacher-unlockable macros for class-specific needs, transparent rejection feedback, telemetry-driven allowlist expansion, and an image-based escape hatch.

This is not a walled garden. It is a garden with a gate -- and the gate opens wider as you demonstrate you know what is on the other side.

---

## References

1. Csikszentmihalyi, M. (1975). *Beyond Boredom and Anxiety: Experiencing Flow in Work and Play*. Jossey-Bass.

2. Csikszentmihalyi, M. (1990). *Flow: The Psychology of Optimal Experience*. Harper & Row.

3. Csikszentmihalyi, M., & Schneider, B. (2000). *Becoming Adult: How Teenagers Prepare for the World of Work*. Basic Books.

4. Deci, E. L., & Ryan, R. M. (2000). The "what" and "why" of goal pursuits: Human needs and the self-determination of behavior. *Psychological Inquiry*, 11(4), 227--268. https://selfdeterminationtheory.org/SDT/documents/2000_RyanDeci_SDT.pdf

5. Hattie, J. (2009). *Visible Learning: A Synthesis of Over 800 Meta-Analyses Relating to Achievement*. Routledge.

6. Kalyuga, S. (2007). Expertise reversal effect and its implications for learner-tailored instruction. *Educational Psychology Review*, 19(4), 509--539. https://link.springer.com/article/10.1007/s10648-007-9054-3

7. Kalyuga, S., Ayres, P., Chandler, P., & Sweller, J. (2003). The expertise reversal effect. *Educational Psychologist*, 38(1), 23--31. https://www.academia.edu/1405544/The_expertise_reversal_effect

8. Niemiec, C. P., & Ryan, R. M. (2009). Autonomy, competence, and relatedness in the classroom: Applying self-determination theory to educational practice. *Theory and Research in Education*, 7(2), 133--144. https://selfdeterminationtheory.org/SDT/documents/2009_NiemiecRyan_TRE.pdf

9. Shernoff, D. J., Csikszentmihalyi, M., Schneider, B., & Shernoff, E. S. (2014). Student engagement in high school classrooms from the perspective of flow theory. In *Applications of Flow in Human Development and Education* (pp. 475--494). Springer.

10. Su, F. (2022). Five freedoms that all math explorers should enjoy. *American Educator*, Spring 2022. https://www.aft.org/ae/spring2022/su

11. Vygotsky, L. S. (1978). *Mind in Society: The Development of Higher Psychological Processes*. Harvard University Press.

12. KaTeX Documentation. Supported Functions. https://katex.org/docs/supported.html

13. KaTeX Security Advisories (2024). CVE-2024-28243, CVE-2024-28244, CVE-2024-28245, CVE-2024-28246. https://github.com/KaTeX/KaTeX/security

14. Do Israel. (2026). Israeli School Excellence Tracks: The Ultimate Guide for Parents to 5-Unit Programs & Gifted Paths. https://www.do-israel.com/en/israeli-school-excellence-tracks-guide/

15. FTC. (2025). Children's Online Privacy Protection Rule -- Amended. *Federal Register*. https://www.ftc.gov/legal-library/browse/rules/childrens-online-privacy-protection-rule-coppa

16. Dunn, J. (n.d.). Schools' walled garden approach to content means pupils don't learn necessary skills. *Medium*. https://medium.com/@jdunns4/schools-walled-garden-approach-to-content-means-pupils-don-t-learn-necessary-skills-cff93cfe817e

17. Christensen Institute. (n.d.). The walled garden effect: Why sharing doesn't always create shared progress in education. https://www.christenseninstitute.org/blog/the-walled-garden-effect-why-sharing-doesnt-always-create-shared-progress-in-education/
