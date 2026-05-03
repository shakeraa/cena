# Iteration 7 — Academic Integrity: Exam Cheating Detection for Photo-Input AI Tutoring

**Series**: Screenshot Question Analyzer — Defense-in-Depth Research
**Iteration**: 7 of 10
**Date**: 2026-04-12
**Security Score Contribution**: 14 / 100 (cumulative with prior iterations)
**Scope**: The tension between accessible AI-powered math tutoring and enabling academic dishonesty when students can photograph exam questions

---

## Table of Contents

1. [The Cheating Problem](#1-the-cheating-problem)
2. [Industry Case Studies](#2-industry-case-studies)
3. [Detection Strategies](#3-detection-strategies)
4. [Cena's Architectural Position](#4-cenas-architectural-position)
5. [Guardrails Implementation](#5-guardrails-implementation)
6. [Policy Framework](#6-policy-framework)
7. [Ethical Analysis](#7-ethical-analysis)
8. [Security Score Breakdown](#8-security-score-breakdown)
9. [Sources](#9-sources)

---

## 1. The Cheating Problem

The core threat model for any AI tutoring platform that accepts photo input is simple: a student points their phone camera at a live exam question and receives the solution. This is not a hypothetical scenario. As of 2026, 46.9% of students use LLMs in their coursework, 39% for answering assessments directly, and 7% to write entire papers ([All About AI, 2026](https://www.allaboutai.com/resources/ai-statistics/ai-cheating-in-schools/)). Student discipline rates for AI-related academic misconduct rose from 48% (2022-23) to 64% (2024-25). A 2025 Inside Higher Ed survey found that 59% of senior administrators believe cheating has increased since AI became widespread ([Inside Higher Ed, 2024](https://www.insidehighered.com/news/tech-innovation/artificial-intelligence/2024/07/29/students-and-professors-expect-more)).

### 1.1 Three Attack Scenarios

**Scenario A: Live Exam Photography**

A student sitting a Bagrut mathematics exam (5-unit level), SAT, or AP Calculus test photographs a question under the desk, sends it to an AI tutoring app, and receives the solution within seconds. The student copies the answer onto the exam paper. The exam is supervised but phone use is difficult to detect at scale when 30+ students sit simultaneously.

This is the highest-severity scenario because it directly undermines high-stakes assessment. Bagrut exams are nationally administered by the Israeli Ministry of Education, with results determining university admission eligibility. Anti-cheating measures during Bagrut include separate seating, teacher proctoring, and leak-prevention protocols ([Haaretz, 2015](https://www.haaretz.com/2015-05-21/ty-article/.premium/math-matriculation-exam-goes-smoothly/0000017f-e7cf-da9b-a1ff-efef66ac0000)), but none of these address a student discretely using a phone app.

**Scenario B: Homework Copy-Paste**

A student photographs every homework problem, submits photos serially, and copies the returned solutions without engaging with the material. Unlike Scenario A, this is not illegal or exam-fraud. However, it defeats the pedagogical purpose entirely. The student builds no skill, accumulates misleading mastery data, and arrives at the actual exam unprepared.

Photomath, before its Google acquisition, processed 2 billion math questions per month from 250 million downloads ([Fast Company, 2023](https://www.fastcompany.com/90890819/chatgpt-ai-homework-education-stock-drops-chegg-business)). The majority of those queries were homework problems where the student had zero intent to learn the method. Teachers reported detecting suspicious patterns --- students who could "solve" systems of equations in eight seconds during class but failed the in-class quiz.

**Scenario C: Standardized Test Preparation Abuse**

A student photographs questions from an SAT or Bagrut practice booklet and uses the AI to generate full worked solutions, which they memorize without understanding. The student then encounters the same or parametrically similar questions on the actual test and reproduces the memorized answer. This is the most difficult scenario to distinguish from legitimate use, because photographing practice problems for study is a valid workflow. The difference is intent --- guided practice versus answer harvesting.

### 1.2 Scale of the Problem

The following statistics frame the scale:

- 45% of students used AI in their classes in the past year; only 15% of instructors reported the same ([EdWeek, 2024](https://www.edweek.org/technology/new-data-reveal-how-many-students-are-using-ai-to-cheat/2024/04))
- Only 7 weeks after ChatGPT launched, 30% of US college students had already used it for written assignments ([CNBC, 2023](https://www.cnbc.com/2023/05/02/chegg-drops-more-than-40percent-after-saying-chatgpt-is-killing-its-business.html))
- 62-68% of teachers report detecting or suspecting AI use in student work (2025), but 73% of student-reported AI detection incidents involve disputed false positives ([All About AI, 2026](https://www.allaboutai.com/resources/ai-statistics/ai-cheating-in-schools/))
- AI tutoring systems that guide rather than give answers show 45% reduction in academic integrity violations compared to answer-lookup platforms ([Chalkbeat, 2025](https://www.chalkbeat.org/2025/11/04/three-theories-on-ai-in-schools-about-cheating-teaching-and-tutoring/))

---

## 2. Industry Case Studies

### 2.1 Photomath: From Camera Calculator to Google Acquisition

**Timeline**: Founded 2014 in Croatia. By 2021, 250 million downloads, 2 billion questions answered per month. Google announced acquisition May 2022; European Commission approved March 2023; deal closed June 2023 at an estimated $550 million ([Wikipedia](https://en.wikipedia.org/wiki/Photomath); [Crunchbase](https://www.crunchbase.com/acquisition/google-acquires-photomath-inc-2--5dcb99bf); [9to5Google, 2024](https://9to5google.com/2024/02/29/photomath-google-app/)).

**The controversy**: Photomath's core feature --- point camera at math problem, receive step-by-step solution --- is precisely the workflow that enables exam cheating. The app itself states its purpose is for students to "learn and check their work" ([MyEngineeringBuddy](https://www.myengineeringbuddy.com/blog/photomath-cheating-guide/)), but the UX makes no distinction between learning mode and answer-lookup mode. There is no friction between the camera capture and the displayed solution.

Educator Alice Keeler argued that "Photomath is NOT cheating" on the grounds that if a problem can be solved by pointing a camera at it, the problem itself is poorly designed --- it tests recall, not understanding ([Alice Keeler, 2022](https://alicekeeler.com/2022/09/29/photomath-is-not-cheating/)). This argument has merit for homework design but collapses for standardized tests where procedural fluency is part of the measured construct.

**School bans**: While comprehensive ban data is not publicly indexed, individual schools and districts have added Photomath to lists of prohibited apps during exams, alongside calculators with CAS capabilities. The fundamental tension remains: the same tool that helps a struggling student practice at home enables a dishonest student to cheat on a test.

**Lesson for Cena**: Photomath's UX is answer-first. Cena's UX must be learning-first. The photo input itself is not the problem --- it is what happens after the photo that determines whether the system is a learning tool or a cheating tool.

### 2.2 Chegg: Stock Crash, Existential Pivot, and the End of Answer Repositories

**Timeline**: Chegg built a $10 billion market-cap business on a simple value proposition: students pay $19.95/month for pre-written answers to textbook questions. When ChatGPT launched in November 2022, students suddenly had a free alternative. On May 2, 2023, Chegg's stock crashed nearly 50% in a single day after the company acknowledged that ChatGPT was hurting its business ([CNBC, 2023](https://www.cnbc.com/2023/05/02/chegg-drops-more-than-40percent-after-saying-chatgpt-is-killing-its-business.html)). By 2025, the stock had fallen 99% from its peak ([Gizmodo, 2025](https://gizmodo.com/chegg-is-on-its-last-legs-after-chatgpt-sent-its-stock-down-99-2000522585)).

**The restructuring**: In October 2025, Chegg slashed 45% of its workforce (approximately 640 employees), reinstated former CEO Dan Rosensweig, and pivoted from student subscriptions toward a professional "skilling platform" targeting the "$40+ billion skilling market" ([Fortune, 2025](https://fortune.com/2025/10/28/chegg-layoffs-ceo-dan-rosensweig-poster-child-ai-slashing-staff-shares/); [CNBC, 2025](https://www.cnbc.com/2025/10/27/chegg-slashes-45percent-of-workforce-blames-new-realities-of-ai.html)). The company projected these skilling-focused businesses would generate approximately $70 million in revenue in 2025.

Chegg also filed a lawsuit against Google in February 2025, alleging that Google's AI-powered search summaries were unlawfully using Chegg's content and siphoning traffic ([Higher Ed Dive, 2025](https://www.highereddive.com/news/chegg-layoffs-strategic-alternatives-google-ai/804192/)).

In Australia, a court found Chegg liable for facilitating cheating --- the first judicial finding of its kind against a homework-help platform ([Times Higher Education](https://www.timeshighereducation.com/news/chegg-facilitated-cheating-australian-court-finds)).

**Lesson for Cena**: The "static answer repository" model is both pedagogically bankrupt and commercially dead. Students do not learn from looking up answers, and ChatGPT can provide the same answers for free. Cena's value proposition must be the learning process itself, not the answer.

### 2.3 Wolfram Alpha: The "Show Steps" Precedent

Wolfram Alpha introduced step-by-step solutions for calculus, algebra, and differential equations --- a feature that immediately raised academic integrity concerns. NYU's Department of Mathematics explicitly states that "copying answers from computational websites like Wolfram Alpha, Symbolab, Mathpix, ChatGPT, or any other similar resource is considered a violation of academic integrity" ([NYU Math, Academic Integrity](https://math.nyu.edu/dynamic/undergrad/ba-cas/academic-integrity/)). However, NYU also acknowledges that "you may use these tools to check your work."

The Wolfram Alpha case illustrates the contextual nature of academic dishonesty. The same "show steps" feature that constitutes cheating during a closed-book exam is a legitimate learning tool during homework. As one analysis noted: "What, to a teacher, is an unethical shortcut can be, to a student, just a better tool" ([Plagiarism Today, 2018](https://www.plagiarismtoday.com/2018/01/11/the-challenge-of-defining-cheating/)).

**Lesson for Cena**: The feature itself is neutral. The context --- when, where, and how it is used --- determines whether it constitutes cheating. This means Cena needs context-aware controls, not feature removal.

### 2.4 Khan Academy Khanmigo: The Guardrails Approach

Khan Academy launched Khanmigo, its GPT-4-based AI tutor, in March 2023 with multiple anti-cheating safeguards built into the architecture ([Fortune, 2023](https://fortune.com/2023/12/11/khan-academy-ai-bots-coach-cheating-guardrails-essays-brainstorm-ai/); [CNN, 2023](https://www.cnn.com/2023/08/21/tech/khan-academy-ai-tutor/index.html)):

1. **Never gives the answer.** Khanmigo uses custom prompts to guide learners Socratically --- asking questions, providing hints, giving feedback --- without revealing the final solution. "Instead of providing answers, Khanmigo asks questions and provides support as a tutor would" ([Khanmigo Review, 2025](https://www.aimodelsrank.com/reviews/khan-academy-khanmigo)).

2. **Teacher visibility.** Chat histories are visible to parents and teachers. A banner at the top of the screen notifies students of this. The system automatically flags problematic questions or comments for teacher review ([CBS News / 60 Minutes, 2025](https://www.cbsnews.com/news/khanmigo-ai-powered-tutor-teaching-assistant-tested-at-schools-60-minutes-transcript/)).

3. **Paste detection.** If a student pastes 15+ words from an external source into the Writing Coach, the section is flagged for teacher review --- a heuristic for detecting outsourced AI-written content.

4. **Teacher control panel.** Teachers can configure what information students can ask the bot, restricting scope to the current unit or topic ([Khan Academy Blog](https://blog.khanacademy.org/navigating-cheating-in-the-age-of-ai/)).

5. **Arithmetic grounding.** After early math errors in GPT-4, Khan Academy added a wrapper that routes arithmetic to a calculator tool and compares final answers against a ground-truth solver (Cena's Track 9 research, 2026-04-11). This is directly analogous to Cena's CAS verification layer.

**Lesson for Cena**: Khanmigo proves that a "never give the answer" architecture is commercially viable and educationally effective. The guardrails are architectural (tool-use routing, answer masking), not just prompt-based --- because research shows LLM tutors leak the answer within 2-3 turns ~40% of the time with prompting alone (Macina et al., 2023, EMNLP Findings).

---

## 3. Detection Strategies

The following six strategies form a layered detection system. No single strategy is sufficient; they must be composed. Each strategy is evaluated on false-positive rate, implementation complexity, and privacy intrusiveness.

### 3.1 Time-of-Day Analysis

**Principle**: Bagrut exams, SAT tests, and school exams occur during predictable windows --- typically 08:00-13:00 on specific dates published by the Ministry of Education. A photo upload during these windows is statistically more likely to be an exam question than a practice problem.

**Implementation**:
```
IF photo_upload.timestamp falls within known_exam_window
   AND student.institute has registered an exam for that date/time
THEN flag = ELEVATED_RISK
     response_mode = EXAM_GUARD (see Section 5)
```

**False-positive rate**: Medium. Students may legitimately study during morning hours. The signal is weak on its own but strong when combined with other indicators.

**Privacy impact**: Low. Requires only timestamp data, which is already captured in standard request logging.

**Precedent**: AI proctoring systems already use time-windowed behavioral analysis as a baseline signal ([Proctor365, 2026](https://www.proctor365.ai/how-ai-based-proctoring-exam-tech-cuts-cheating-by-95/)).

### 3.2 Burst Detection

**Principle**: A student photographing exam questions exhibits a distinctive behavioral pattern: multiple photos in rapid succession (one per exam question), often 5-15 images within a 30-minute window. This is unlike practice behavior, where a student typically photographs one problem, works through it over 10-30 minutes, then moves to the next.

**Implementation**:
```
IF count(photo_uploads, window=30min) > BURST_THRESHOLD (default: 4)
   AND avg_time_between_uploads < 5min
   AND student did NOT interact with step-solver between uploads
THEN flag = HIGH_RISK
     response_mode = EXAM_GUARD
     notify institute_admin (async)
```

**Key metric**: The critical signal is the absence of step-solver engagement between uploads. A legitimate learner uploads a photo, works through steps, then uploads the next problem. A cheating student uploads photos in batch and wants answers returned immediately.

**False-positive rate**: Low, because the "no step-solver engagement" filter eliminates most false positives.

**Privacy impact**: Low. Requires only upload timestamps and session interaction data already captured by the platform.

### 3.3 Question Fingerprinting

**Principle**: Known exam questions from published Bagrut past papers, SAT practice tests, and AP released exams can be fingerprinted --- hashed by their mathematical structure --- and stored in a reference database. When a student uploads a photo that matches a known exam question, the system can determine whether that question is from a past (released) exam or a current (active) exam.

**Implementation approach**:
- Extract the mathematical structure from the OCR output (e.g., "solve quadratic ax^2 + bx + c = 0 for specific a, b, c values")
- Normalize: strip specific numeric coefficients, reduce to problem template
- Compare against fingerprint database using similarity threshold
- If match is to a **released** past exam: allow (legitimate practice)
- If match is to a **current-year** exam question reported by institute: escalate

**Technical challenge**: Question fingerprinting at the structural level requires NLP-level understanding of mathematical problems, not just string matching. Two questions can be parametrically identical ("solve x^2 - 5x + 6 = 0" vs. "solve x^2 - 7x + 12 = 0") but have different fingerprints at the surface level. Effective fingerprinting must operate at the template level.

Research on exam leak detection using AI shows that LLMs can analyze answer patterns and identify similarities between student submissions and known question banks, and ML models can detect anomalies in answer patterns and time-to-completion ([Ajay Verma, Medium](https://medium.com/@ajayverma23/detecting-academic-dishonesty-paper-leak-leveraging-llms-and-ai-for-exam-integrity-7f0bff88fa29)).

**False-positive rate**: Low for exact matches; medium for fuzzy structural matches.

**Privacy impact**: Medium. Requires storing and comparing mathematical content, which could reveal what the student is studying. However, no PII is involved in the fingerprint itself.

### 3.4 Geofencing

**Principle**: If Cena knows the student's institute location and the institute has registered an exam schedule, the system can detect when a student is physically near or inside the school building during an exam window. A photo upload from within the geofence during an exam window is very likely an active exam question.

**Implementation**:
```
IF student.location is within GEOFENCE_RADIUS (default: 200m)
   of student.institute.coordinates
   AND current_time is within institute.exam_schedule
THEN flag = CRITICAL_RISK
     response_mode = EXAM_BLOCK (photo upload disabled)
     log for institute_admin review
```

**Technical requirements**:
- Student app must request location permission (already common for educational apps)
- Institute admin must register exam schedules via the admin dashboard
- Geofence radius must account for GPS accuracy (typically +/- 10-50m)

**Precedent**: Mobile device management (MDM) solutions already use geofencing for campus-based access control. Mobile Guardian provides geofencing to enforce app restrictions based on student location at school ([Mobile Guardian](https://www.mobileguardian.com/features/geofencing)). Indonesia's national examination system used technology-based cheating prevention that reduced test scores by 0.5 standard deviations --- proving that the pre-technology scores were inflated by cheating ([ScienceDirect, 2024](https://www.sciencedirect.com/science/article/pii/S0304387824000567)).

**False-positive rate**: Low when combined with exam schedule data. Without schedule data, the geofence alone is not meaningful (students are at school every day).

**Privacy impact**: High. Location tracking is the most privacy-intrusive detection strategy. Cena must:
- Make location permission opt-in at the institute level
- Never store continuous location trails --- only evaluate geofence membership at the moment of photo upload
- Comply with GDPR-K requirements for minors' location data (session-scoped only, per Cena compliance architecture)
- Disclose location use in terms of service and privacy policy

### 3.5 Session Context Analysis

**Principle**: Distinguish "practice mode" sessions from "exam cramming" sessions based on behavioral patterns within the session. A student in practice mode exhibits steady pacing, uses hints, attempts steps before requesting help, and shows progression across difficulty levels. A student in exam-cramming mode uploads photos rapidly, skips step-solver interaction, and shows no interest in learning --- only in answers.

**Behavioral signals**:

| Signal | Practice Mode | Exam Cramming |
|--------|--------------|---------------|
| Time between photo uploads | > 10 min | < 3 min |
| Step-solver attempts per question | >= 2 | 0 |
| Hint usage | Moderate | None or max |
| Session duration | > 30 min | < 15 min |
| Questions across difficulty levels | Yes | All same level |
| Interaction after receiving guidance | High (tries steps) | Low (moves to next photo) |

**Implementation**: A lightweight ML classifier (logistic regression or decision tree) trained on these behavioral features can assign a session risk score. The risk score feeds into the response mode selection.

**False-positive rate**: Medium. A student legitimately reviewing many problems quickly before an exam could trigger elevated risk. The mitigation is to escalate to softer guardrails (learning-first response) rather than hard blocks.

**Privacy impact**: Medium. Requires behavioral analytics within the session, which is standard for adaptive learning platforms but must be disclosed.

### 3.6 Institute-Level Exam Schedule Integration

**Principle**: The most reliable signal is explicit --- the institute admin registers exam schedules in Cena's admin dashboard, and the platform disables or restricts photo upload for all students at that institute during registered exam windows.

**Implementation**:
```csharp
public sealed record ExamWindow(
    Guid InstituteId,
    string Subject,           // "math-5unit", "physics-5unit"
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    ExamRestrictionLevel Level // DISABLE_PHOTO | LEARNING_ONLY | NO_RESTRICTION
);
```

During an active `ExamWindow` with `DISABLE_PHOTO` restriction:
- The photo upload button is hidden in the student app
- API requests to the photo ingestion endpoint return HTTP 403 with a clear message: "Photo upload is disabled during your institute's exam period"
- The student can still use the platform for practice via text-based question selection

During `LEARNING_ONLY` restriction:
- Photo upload is allowed but always triggers the learning-first response (see Section 5.2)
- The system generates a similar practice question instead of solving the photographed question

**False-positive rate**: Zero for the photo-disable mode (it is a policy decision, not a detection heuristic). Low for learning-only mode.

**Privacy impact**: Low. Requires only the exam schedule data from the institute admin; no student behavioral data is needed for this strategy.

**This is the single most effective anti-cheating measure available to Cena**, because it transforms the problem from detection (statistically unreliable) to prevention (deterministic policy enforcement). An institute that registers its exam schedule in Cena eliminates the Scenario A threat entirely.

---

## 4. Cena's Architectural Position

Cena's existing question engine architecture (documented in `docs/research/cena-question-engine-architecture-2026-04-12.md`) provides natural anti-cheating properties that are absent from tools like Photomath, Chegg, or generic ChatGPT usage.

### 4.1 Cena Is a Learning Platform, Not a Solution Lookup

The fundamental architectural distinction: when a student photographs a question, Cena does not return the solution. The photo is processed through the Path B pipeline (Student photo -> Gemini 2.5 Flash -> LaTeX -> CAS validation), and the output is a **structured learning session**, not an answer.

The step-solver UI requires the student to work through each step:

1. The student sees the problem statement (extracted from the photo)
2. The student must attempt the first algebraic/calculus step
3. The CAS engine (MathNet -> SymPy -> Wolfram fallback) verifies the student's input
4. Only if the student's step is correct does the next step unlock
5. If the student's step is incorrect, the system provides a targeted hint based on the identified misconception (buggy rule)
6. The student must demonstrate understanding at each step before proceeding

This is the inverse of Photomath's workflow. Photomath shows the solution and lets the student scroll through steps passively. Cena requires the student to produce each step actively.

### 4.2 The Step-Solver Never Gives the Answer Directly

The step-solver architecture (Section 7 of the question engine document) implements three scaffolding levels driven by Bayesian Knowledge Tracing (BKT) mastery:

- **Full scaffolding** (mastery < 0.20): Every step has an instruction label and a faded worked example of an *analogous* problem (not the same problem)
- **Partial scaffolding** (mastery 0.20-0.60): Some steps are pre-filled as givens; the student fills gaps
- **Minimal scaffolding** (mastery > 0.60): Numbered slots only; the student decides the approach

At no scaffolding level does the system produce the complete solution. Even at the novice level, the faded worked example is for a *different, analogous* problem --- the student must transfer the pattern, not copy it.

Research confirms this is critical: VanLehn (2011) found step-based ITS achieves d = 0.76 learning gains vs. d = 0.31 for answer-only feedback. The step-level architecture is not just an anti-cheating measure --- it is the reason the system works pedagogically.

### 4.3 CAS Verifies Student Work; It Does Not Produce It

The 3-tier CAS engine (MathNet in-process -> SymPy sidecar -> Wolfram admin fallback) exists to **verify** whether the student's algebraic step is mathematically equivalent to the expected expression. The CAS never generates the solution for the student. The solution steps are pre-authored (for curated questions) or generated by the AI pipeline during question ingestion (and verified by CAS at authoring time).

This is a critical architectural constraint: the CAS engine is a verification oracle, not a solution generator. The student cannot extract the answer from CAS verification responses, because the API returns only `{ correct: true/false, hint?: string }`, never the expected expression.

---

## 5. Guardrails Implementation

### 5.1 Exam Mode: Institute-Controlled Photo Upload Restriction

The admin dashboard (Vuexy Vue 3, `src/admin/full-version/`) exposes an "Exam Schedule" management view. Institute administrators can:

1. Register exam windows with subject, date, start time, end time
2. Select restriction level per exam window:
   - **Full disable**: photo upload is hidden and API-blocked
   - **Learning-only**: photo upload triggers similar-question generation (see 5.2)
   - **No restriction**: platform operates normally (for subjects not being examined)
3. View an audit log of any photo upload attempts during exam windows

The exam schedule integrates with Cena's multi-tenancy model. Each institute manages its own schedule; Cena's platform-level configuration includes the Ministry of Education's published Bagrut exam calendar as a default overlay.

### 5.2 Learning-First Response: Similar Question Generation

When a student uploads a photo and the system is in learning-only mode (or the risk score from detection strategies exceeds a threshold), the response flow changes:

1. The photo is processed normally: OCR, LaTeX extraction, CAS validation
2. Instead of creating a step-solver session for the photographed question, the system:
   a. Identifies the question's template (e.g., "definite integral of polynomial over interval")
   b. Generates a parametric variant using different coefficients/values (Section 4 of question engine architecture)
   c. Creates a step-solver session for the **variant** question
3. The student sees: "Here is a practice problem similar to the one you photographed. Work through it to build your skills."

This approach means that even if a student photographs an active exam question, the returned session is for a **different problem** with different numeric answers. The student cannot copy the solution onto the exam paper.

The parametric variant generation already exists in Cena's architecture (see `docs/research/cena-question-engine-architecture-2026-04-12.md`, Section 4: Parametric Variant Generation). This guardrail reuses existing infrastructure.

### 5.3 Step Lockout: Engagement Gates

The step-solver enforces engagement gates between scaffolding rungs, following Renkl and Atkinson's (2003) faded worked example model:

1. **Attempt gate**: The student must submit at least one attempt for the current step before any hint is unlocked
2. **Hint cooldown**: After viewing a hint, a minimum 30-second cooldown before the next hint tier
3. **Reveal gate**: The step solution is only revealed after the student has made at least 2 attempts and viewed at least 1 hint
4. **Progression gate**: The next step unlocks only after the current step is marked correct (by CAS verification) or revealed (after engagement gate)

These gates make it impossible to rapidly extract a complete solution. Even if a student bypasses the learning-only mode, the step-solver forces a minimum engagement time of approximately 3-5 minutes per question --- far too slow for live exam cheating where each question has a 5-10 minute time allocation.

### 5.4 No Copy-Paste of Final Answers

The step-solver UI renders mathematical expressions as **rendered LaTeX** (via KaTeX or MathJax), not as selectable text. The final answer is never displayed as a standalone copyable string. The student's interaction is through a structured math input component where they build expressions step by step.

Additionally, the API response for each step contains only the verification result (`correct`/`incorrect` + optional hint), never the expected expression. A student inspecting network traffic sees:

```json
{
  "stepNumber": 3,
  "result": "incorrect",
  "hint": "Check the sign when you moved the term across the equation"
}
```

Not:

```json
{
  "stepNumber": 3,
  "expectedExpression": "x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}"
}
```

### 5.5 Pedagogical Scaffolding as Anti-Cheating Architecture

The deepest anti-cheating property of Cena's architecture is that the scaffolding itself makes the answer useless without the understanding. A student who somehow obtains the final answer ("x = 3") still cannot complete the step-solver session, because the session requires them to produce each intermediate step ("factor the quadratic", "set each factor to zero", "solve for x"). The CAS verifies each step independently.

This means the workflow `screenshot -> answer -> copy` is architecturally impossible in Cena. The actual workflow is `screenshot -> practice variant -> attempt step 1 -> verify -> attempt step 2 -> verify -> ... -> complete`. A student who completes this workflow has, by definition, demonstrated understanding of the solution method.

---

## 6. Policy Framework

### 6.1 Terms of Service Language

Cena's terms of service must include explicit provisions:

**Prohibited uses**:
> The Service is designed for learning and practice. You may not use photo upload or any other feature of the Service to obtain solutions to questions on active examinations, standardized tests (including Bagrut, SAT, ACT, AP, or equivalent), or any assessment where external assistance is prohibited by the administering institution. Violation of this provision may result in suspension of access and notification to your educational institution.

**Acceptable uses**:
> You are encouraged to use photo upload to practice problems from textbooks, released past exams, worksheets, and self-study materials. The Service is designed to guide you through the solution process, not to provide answers for submission.

### 6.2 School/Institute Agreement

Each institute that licenses Cena signs an agreement that includes:

1. **Exam schedule registration obligation**: The institute agrees to register exam schedules in the admin dashboard to enable exam-mode restrictions
2. **Teacher dashboard access**: The institute designates teachers who receive access to the activity dashboard (see 6.4)
3. **Incident response protocol**: If Cena's detection systems flag potential exam use, the institute's designated contact is notified within 24 hours
4. **Data handling**: All photo data is session-scoped and not retained beyond the session (per COPPA/GDPR-K/FERPA compliance requirements)

### 6.3 Student Honor Code

Students acknowledge upon account creation:

> I understand that Cena is a learning platform designed to help me build skills through guided practice. I will not use Cena's photo upload feature during active examinations or standardized tests. I understand that my usage patterns are visible to my teachers and that the platform may restrict features during exam periods registered by my school.

The honor code is presented as a clear, non-legalistic statement (not buried in terms of service) during onboarding. Research on student honor codes shows that explicit, visible acknowledgments reduce dishonesty rates, particularly when combined with transparency about monitoring ([TeachAI Toolkit](https://www.teachai.org/toolkit-student); [CU Boulder](https://www.colorado.edu/studentlife/ai-honor-code)).

### 6.4 Teacher Dashboard

The admin dashboard provides teachers with a per-student activity view:

| Data Point | Purpose | Privacy Level |
|-----------|---------|---------------|
| Photo upload timestamps | Detect exam-window usage | Session-scoped |
| Photos per session | Detect burst patterns | Session-scoped |
| Step-solver engagement rate | Distinguish learning from answer-seeking | Aggregated |
| Mastery progression | Verify that photo use correlates with learning | Aggregated |
| Flagged sessions | Review system-flagged potential exam use | Session-scoped |

Teachers see **aggregated engagement metrics**, not individual chat transcripts or photographed questions. This balances oversight with student privacy.

The dashboard answers the teacher's core question: "Is this student using photo upload as a learning tool (practice at home, works through steps, mastery increases) or as an answer-lookup service (burst uploads, no step engagement, flat mastery)?"

---

## 7. Ethical Analysis

### 7.1 The Central Tension: Access vs. Integrity

The ethical core of this problem is a genuine dilemma, not a solvable optimization. AI tutoring that accepts photo input provides two things simultaneously:

1. **Unprecedented access to guided learning** for students who cannot afford private tutors, live in areas without quality instruction, or study in languages underserved by existing platforms. In the Israeli context, Arab-sector students and students in peripheral communities have the least access to quality Bagrut prep (see Track 7 research, 2026-04-11). For these students, photographing a homework problem and receiving Socratic guidance could be the difference between passing and failing the Bagrut.

2. **Unprecedented ease of cheating** on exams. The same phone camera that captures a homework problem for practice can capture an exam question for answer extraction.

Restricting the tool to prevent cheating directly harms the students who need it most. Leaving the tool unrestricted enables cheating that undermines the assessment system that determines university admission.

### 7.2 Digital Equity and the Tutoring Gap

Research shows that AI tutoring is most valuable to socially disadvantaged students who lack access to private tutors or ancillary educational resources ([BERA, 2024](https://www.bera.ac.uk/blog/digital-equity-in-the-age-of-generative-ai-bridging-the-divide-in-educational-technology/); [Springer, 2025](https://link.springer.com/article/10.1007/s11528-025-01088-8)). In the UK, up to 450,000 disadvantaged pupils could benefit from AI tutoring tools being trialed in secondary schools ([Fordham Institute](https://fordhaminstitute.org/national/commentary/how-we-can-use-ai-increase-access-and-equity-science-education)).

The OECD's 2024 report on AI and equity in education warns that if AI-driven systems are trained on biased or incomplete data, they may offer less accurate or less supportive guidance to the students who rely on them most ([OECD, 2024](https://www.oecd.org/content/dam/oecd/en/publications/reports/2024/08/the-potential-impact-of-artificial-intelligence-on-equity-and-inclusion-in-education_0d7e9e00/15df715b-en.pdf)).

In the Israeli context specifically:
- Arabic-language Bagrut prep is thin and fragmented (Track 7 research)
- Peripheral communities have fewer qualified STEM teachers
- Private tutoring costs NIS 150-300/hour, unaffordable for many families
- Cena's target price (NIS 49-89/month) would provide the equivalent of a personal tutor for less than the cost of a single hour of private instruction

Restricting photo upload indiscriminately would disproportionately harm these students, because photo input is the lowest-friction way to get help with a specific problem they are stuck on.

### 7.3 The "Learning Intent" Distinction

The ethical framework that resolves the tension is **learning intent**. The same action (photographing a math problem) has different ethical valences depending on intent:

- **Learning intent**: "I am stuck on this problem and want to understand how to solve it. I will work through the guided steps and build the skill."
- **Cheating intent**: "I want the answer to this problem so I can copy it onto my exam paper / homework without understanding it."

Cena cannot read minds, but Cena's architecture makes cheating intent structurally unrewarding:

1. The system never returns a copyable answer (step-solver architecture)
2. The system returns a practice variant during exam windows (similar-question generation)
3. The system requires engagement at each step (engagement gates)
4. The system logs usage for teacher review (transparency)

A student with cheating intent who encounters Cena will find it useless for cheating --- the answer never appears, and the process takes 3-5 minutes per question with active engagement required. The student will turn to ChatGPT, Photomath, or a friend, which provide instant answers. This is the correct outcome: Cena should not compete to be the fastest cheating tool. Cena should be obviously, architecturally, and irreducibly a learning tool.

### 7.4 The Israeli Regulatory Context

Israel's Ministry of Education declared 2025 the "Year of Artificial Intelligence" and launched initiatives including training 70,000 teachers to use AI in classrooms, bringing in 3,000 training mentors from 400+ tech companies, and launching an AI sandbox for testing AI tools in public schools ([JPost, 2025](https://www.jpost.com/israel-news/article-840424); [JNS, 2025](https://www.jns.org/israel-unveils-groundbreaking-ai-education-plan-for-2025/); [AI Israel, 2025](https://aiisrael.org.il/press_release/israel-launches-ai-sandbox-pilot-for-public-education/)).

Key initiatives include "Q," an AI training bot for responsible usage, and a Google Gemini-based chat assistant tailored for education. The Ministry is actively encouraging responsible AI adoption, not prohibiting it.

This regulatory environment is favorable for Cena's position: the Ministry is not banning AI tutoring tools but is creating frameworks for responsible use. Cena's guardrails architecture aligns with the Ministry's emphasis on responsible AI in education.

In the US, 30 states have issued guidance on AI in education as of September 2025. Common frameworks use a traffic-light system: green for research assistance, amber for conditional uses, and red for exam assistance ([AI for Education, 2025](https://www.aiforeducation.io/ai-resources/state-ai-guidance); [Structural Learning, 2026](https://www.structural-learning.com/post/creating-ai-policy-schools-2025)). Cena's photo upload with learning-first response fits cleanly into the "green/amber" category; Cena during registered exam windows with photo-disable fits cleanly into the "red" enforcement category.

---

## 8. Security Score Breakdown

This iteration contributes **14 points** to the cumulative security robustness score, broken down as follows:

| Control | Points | Rationale |
|---------|--------|-----------|
| Institute exam schedule integration with photo-disable | 4 | Deterministic prevention of the highest-severity threat (live exam photography). Eliminates the attack vector entirely for cooperating institutes. |
| Learning-first response (similar question generation) | 3 | Architectural guarantee that the photographed question's answer is never returned. Requires existing parametric variant infrastructure. |
| Step-solver engagement gates | 2 | Makes rapid answer extraction architecturally impossible. 3-5 minute minimum per question. |
| Burst detection + time-of-day analysis | 2 | Statistical detection layer for non-cooperating institutes. Low false-positive rate when combined. |
| Teacher dashboard with activity visibility | 1 | Transparency-based deterrent. Social pressure against misuse. |
| Student honor code + ToS provisions | 1 | Weak on its own but necessary for legal standing and norm-setting. |
| Question fingerprinting infrastructure | 1 | Long-term investment. Enables matching against active exam databases. |

**Points not awarded** (require further iteration or are out of scope):
- Geofencing: 0 points in this iteration due to high privacy impact and implementation complexity. Evaluated in iteration 6 (privacy-preserving processing) and may be reconsidered with privacy-preserving geofencing designs.
- Network-level detection (device fingerprinting, exam-room WiFi detection): 0 points, too invasive for a learning platform targeting minors.

**Cumulative score context**: Prior iterations 1-6 addressed adversarial image attacks, multimodal prompt injection, LaTeX sanitization, content moderation, rate limiting, and privacy-preserving processing. This iteration adds the academic integrity layer. Remaining iterations 8-10 address accessibility, error handling, and end-to-end verification.

---

## 9. Sources

### Industry and Market

- [All About AI — AI Cheating in Schools: 2026 Global Trends & Bias Risks](https://www.allaboutai.com/resources/ai-statistics/ai-cheating-in-schools/)
- [Chalkbeat — Cheating, Teaching, and Tutoring: How AI Will and Won't Change Schools](https://www.chalkbeat.org/2025/11/04/three-theories-on-ai-in-schools-about-cheating-teaching-and-tutoring/)
- [Inside Higher Ed — Students and Professors Expect More Cheating Thanks to AI](https://www.insidehighered.com/news/tech-innovation/artificial-intelligence/2024/07/29/students-and-professors-expect-more)
- [EdWeek — New Data Reveal How Many Students Are Using AI to Cheat](https://www.edweek.org/technology/new-data-reveal-how-many-students-are-using-ai-to-cheat/2024/04)
- [EdWeek — No, AI Detection Won't Solve Cheating](https://www.edweek.org/technology/opinion-no-ai-detection-wont-solve-cheating/2024/04)

### Photomath

- [Wikipedia — Photomath](https://en.wikipedia.org/wiki/Photomath)
- [9to5Google — Photomath Is Officially Google's Latest App](https://9to5google.com/2024/02/29/photomath-google-app/)
- [Crunchbase — Photomath Acquisition by Google](https://www.crunchbase.com/acquisition/google-acquires-photomath-inc-2--5dcb99bf)
- [MyEngineeringBuddy — Is Photomath Cheating? Engineering Guide](https://www.myengineeringbuddy.com/blog/photomath-cheating-guide/)
- [Alice Keeler — Photomath Is NOT Cheating](https://alicekeeler.com/2022/09/29/photomath-is-not-cheating/)

### Chegg

- [CNBC — Chegg Shares Drop More Than 40% After Company Says ChatGPT Is Killing Its Business](https://www.cnbc.com/2023/05/02/chegg-drops-more-than-40percent-after-saying-chatgpt-is-killing-its-business.html)
- [Gizmodo — Chegg Is On Its Last Legs After ChatGPT Sent Its Stock Down 99%](https://gizmodo.com/chegg-is-on-its-last-legs-after-chatgpt-sent-its-stock-down-99-2000522585)
- [Fortune — Chegg's CEO Once Said He's the 'Poster Child' for AI Shock](https://fortune.com/2025/10/28/chegg-layoffs-ceo-dan-rosensweig-poster-child-ai-slashing-staff-shares/)
- [CNBC — Chegg Slashes 45% of Workforce, Blames 'New Realities of AI'](https://www.cnbc.com/2025/10/27/chegg-slashes-45percent-of-workforce-blames-new-realities-of-ai.html)
- [Higher Ed Dive — Chegg Slashes Nearly Half of Its Workforce as AI Eats Into Business](https://www.highereddive.com/news/chegg-layoffs-strategic-alternatives-google-ai/804192/)
- [Times Higher Education — Chegg Fined by Australian Court for Facilitating Cheating](https://www.timeshighereducation.com/news/chegg-facilitated-cheating-australian-court-finds)

### Wolfram Alpha

- [NYU Department of Mathematics — Academic Integrity](https://math.nyu.edu/dynamic/undergrad/ba-cas/academic-integrity/)
- [Plagiarism Today — The Challenge of Defining Cheating](https://www.plagiarismtoday.com/2018/01/11/the-challenge-of-defining-cheating/)
- [Hacker News — Wolfram Alpha Is Making It Extremely Easy for Students to Cheat](https://news.ycombinator.com/item?id=14718559)

### Khan Academy / Khanmigo

- [Fortune — Khan Academy Founder: AI Has Power to Transcend a Traditional Tutor](https://fortune.com/2023/12/11/khan-academy-ai-bots-coach-cheating-guardrails-essays-brainstorm-ai/)
- [CNN — Meet Khan Academy's AI Tutor](https://www.cnn.com/2023/08/21/tech/khan-academy-ai-tutor/index.html)
- [CBS News / 60 Minutes — Khanmigo AI-Powered Tutor](https://www.cbsnews.com/news/khanmigo-ai-powered-tutor-teaching-assistant-tested-at-schools-60-minutes-transcript/)
- [Khan Academy Blog — Navigating Cheating in the Age of AI](https://blog.khanacademy.org/navigating-cheating-in-the-age-of-ai/)
- [AIModelsRank — Khanmigo Review 2025](https://www.aimodelsrank.com/reviews/khan-academy-khanmigo)

### Exam Proctoring and Detection

- [Proctor365 — How AI Based Proctoring Exam Tech Cuts Cheating by 95%](https://www.proctor365.ai/how-ai-based-proctoring-exam-tech-cuts-cheating-by-95/)
- [Eklavvya — AI Proctoring Guide: Reduce Cheating by 95% in 2026](https://www.eklavvya.com/blog/ai-proctoring-guide/)
- [Open edX — Proctoring in the Age of AI](https://openedx.org/blog/proctoring-in-the-age-of-ai-tackling-modern-cheating-techniques/)
- [Ajay Verma — Detecting Academic Dishonesty/Paper Leak: Leveraging LLMs and AI](https://medium.com/@ajayverma23/detecting-academic-dishonesty-paper-leak-leveraging-llms-and-ai-for-exam-integrity-7f0bff88fa29)
- [ScienceDirect — Using Technology to Prevent Fraud in High-Stakes National Examinations: Evidence from Indonesia](https://www.sciencedirect.com/science/article/pii/S0304387824000567)

### Israel Education Context

- [Haaretz — Israeli Math Matriculation Exam Goes Smoothly as New Anti-Cheating Rules Take Effect](https://www.haaretz.com/2015-05-21/ty-article/.premium/math-matriculation-exam-goes-smoothly/0000017f-e7cf-da9b-a1ff-efef66ac0000)
- [Jewish Virtual Library — Education in Israel: "Bagrut" Matriculation Exams](https://jewishvirtuallibrary.org/quot-bagrut-quot-matriculation-exams)
- [JPost — Israeli Education Ministry Unveils AI-Focused 2025 Curriculum](https://www.jpost.com/israel-news/article-840424)
- [JNS — Israel Unveils Groundbreaking AI Education Plan for 2025](https://www.jns.org/israel-unveils-groundbreaking-ai-education-plan-for-2025/)
- [AI Israel — Israel Launches AI Sandbox Pilot for Public Education](https://aiisrael.org.il/press_release/israel-launches-ai-sandbox-pilot-for-public-education/)

### Policy Frameworks

- [U.S. Department of Education — Guidance on AI Use in Schools](https://www.ed.gov/about/news/press-release/us-department-of-education-issues-guidance-artificial-intelligence-use-schools-proposes-additional-supplemental-priority)
- [AI for Education — State AI Guidance for Education](https://www.aiforeducation.io/ai-resources/state-ai-guidance)
- [CDT — States Focused on Responsible Use of AI in Education During the 2025 Legislative Session](https://cdt.org/insights/states-focused-on-responsible-use-of-ai-in-education-during-the-2025-legislative-session/)
- [Structural Learning — Creating an AI Policy for Schools (2026)](https://www.structural-learning.com/post/creating-ai-policy-schools-2025)
- [TeachAI — Sample Student Agreement](https://www.teachai.org/toolkit-student)
- [CU Boulder — AI and the Honor Code](https://www.colorado.edu/studentlife/ai-honor-code)

### Digital Equity

- [BERA — Digital Equity in the Age of Generative AI](https://www.bera.ac.uk/blog/digital-equity-in-the-age-of-generative-ai-bridging-the-divide-in-educational-technology/)
- [Springer — AI-Based Adaptive Programming Education for Socially Disadvantaged Students](https://link.springer.com/article/10.1007/s11528-025-01088-8)
- [OECD — The Potential Impact of AI on Equity and Inclusion in Education](https://www.oecd.org/content/dam/oecd/en/publications/reports/2024/08/the-potential-impact-of-artificial-intelligence-on-equity-and-inclusion-in-education_0d7e9e00/15df715b-en.pdf)
- [Fordham Institute — How We Can Use AI to Increase Access and Equity in Science Education](https://fordhaminstitute.org/national/commentary/how-we-can-use-ai-increase-access-and-equity-science-education)

### Pedagogical Foundations

- [Brookings — What the Research Shows About Generative AI in Tutoring](https://www.brookings.edu/articles/what-the-research-shows-about-generative-ai-in-tutoring/)
- [AI Competence — AI Socratic Tutors: Teaching The World To Think](https://aicompetence.org/ai-socratic-tutors/)
- [Mobile Guardian — Geofencing for Schools](https://www.mobileguardian.com/features/geofencing)

### Geofencing and Location Technology

- [Mobile Guardian — Geofencing](https://www.mobileguardian.com/features/geofencing)
- [Prey Project — Geofencing for Device Protection on Campus](https://preyproject.com/blog/geofencing-for-device-protection-on-campus)
- [Sweedu — Geofencing Attendance System](https://sweedu.com/blog/geofencing-attendance-system-the-future-of-smart-attendance-in-schools-colleges/)
