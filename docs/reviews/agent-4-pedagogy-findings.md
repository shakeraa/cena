# Agent 4 — Pedagogy & Learning Science Findings

**Reviewer**: claude-subagent-pedagogy
**Date**: 2026-04-11
**Branch**: claude-subagent-pedagogy/review-2026-04-11
**Scope**: Learner-facing features in src/student/, src/mobile/, src/api/, src/actors/

## Evidence standard

Every finding below cites a real, verifiable source (authors, year, venue, DOI where available). Findings that could not meet this bar have been discarded — see "Discarded (UNSOURCED)" at the bottom.

## Summary counts

- p0 (critical): 4
- p1 (high): 5
- p2 (normal): 3
- p3 (low): 2

---

## P0 — Critical

```yaml
- id: FIND-pedagogy-001
  severity: p0
  category: pedagogy
  title: "REST session answer endpoint returns binary correct/incorrect feedback with no explanation, suppressing formative feedback"
  file: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
  line: 667-676
  evidence:
    - type: file-excerpt
      content: |
        667:            var feedback = isCorrect
        668:                ? "Correct! Great job!"
        669:                : $"Not quite. The correct answer was: {questionDoc.CorrectAnswer}";
        670:
        671:            return Results.Ok(new SessionAnswerResponseDto(
        672:                Correct: isCorrect,
        673:                Feedback: feedback,
        674:                XpAwarded: isCorrect ? 10 : 0,
        675:                MasteryDelta: isCorrect ? 0.05m : 0m,
        676:                NextQuestionId: nextQuestionId));
    - type: file-excerpt
      content: |
        # src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs, lines 23-24
        23:    public string CorrectAnswer { get; set; } = "";
        24:    public string? Explanation { get; set; }
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Questions/QuestionState.cs, lines 103-104
        103:    // Explanation (L1 — static, per-question)
        104:    public string? Explanation { get; set; }
    - type: citation
      content: |
        Hattie, J. & Timperley, H. (2007). "The Power of Feedback." Review of
        Educational Research, 77(1), 81-112. DOI: 10.3102/003465430298487.

        Meta-analysis (d = 0.79 average effect size). Effective feedback must
        answer three questions for the learner: Where am I going? How am I
        going? Where to next? Binary correct/incorrect feedback answers none of
        these. To have any effect on learning, feedback must at minimum provide
        task-level information about the discrepancy between current and target
        performance — what the correct reasoning was and why the student's
        answer fell short.

        Black, P. & Wiliam, D. (1998). "Assessment and Classroom Learning."
        Assessment in Education: Principles, Policy & Practice, 5(1), 7-74.
        DOI: 10.1080/0969595980050102.

        Formative feedback must include information that helps the learner
        close the gap between current and target performance. "Correct" or
        "Wrong" without an explanation does not qualify as formative feedback.
  finding: |
    The REST session answer endpoint — which is the ONLY path the Vuexy student
    web app uses — constructs the `feedback` string as a literal "Correct!
    Great job!" for correct answers and "Not quite. The correct answer was: X"
    for wrong answers. The backing QuestionDocument model already carries an
    `Explanation` field, and the QuestionState aggregate also has an
    `Explanation` field plus per-option `DistractorRationale`. None of this
    pedagogically-valuable data is surfaced to the student. The Vue
    `AnswerFeedback.vue` component has no slot for explanation text, and the
    `SessionAnswerResponseDto` has no `Explanation` field.
  root_cause: |
    The SessionEndpoints handler never reads `questionDoc.Explanation` and the
    DTO schema has no field for it. The student UI component mirrors the DTO
    and therefore never shows an explanation. The Flutter mobile app DOES have
    a `feedback_overlay.dart` with a `workedSolution` card, which proves the
    concept is understood — but the REST session path that powers the Vuexy
    web app is stuck on binary feedback.
  proposed_fix: |
    1. Add `Explanation: string?` and `DistractorRationale: string?` fields to
       `SessionAnswerResponseDto`.
    2. In SessionEndpoints.cs around line 667, read `questionDoc.Explanation`
       and wire it into the response. For wrong MCQ answers, also look up the
       selected option's `DistractorRationale` from the question aggregate.
    3. Update `AnswerFeedback.vue` to render the explanation below the correct/
       wrong label, and show the distractor rationale if present.
    4. Update `en.json` / `he.json` / `ar.json` locales with the new labels.
    5. When explanation is missing from the authored content, escalate to the
       ExplanationGenerator service instead of dropping back to binary.

- id: FIND-pedagogy-002
  severity: p0
  category: pedagogy
  title: "Incorrect answers never emit ConceptAttempted_V1 event — mastery math cannot update downward on failure"
  file: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
  line: 601-655
  evidence:
    - type: file-excerpt
      content: |
        601:            // STB-03b: Append XP event and concept attempt on correct answer
        602:            if (isCorrect)
        603:            {
        604:                // Load current profile to get total XP
        605:                var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        ...
        620:                // Also append a concept attempted event for badge tracking
        621:                // HARDEN: Use real concept ID from question instead of stub
        622:                var conceptAttempt = new ConceptAttempted_V1(
        623:                    StudentId: studentId,
        624:                    ConceptId: questionDoc.ConceptId,
        625:                    SessionId: sessionId,
        626:                    IsCorrect: true,
        ...
        642:                session.Events.Append(studentId, conceptAttempt);
        ...
        655:            }
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Mastery/BktTracer.cs, lines 22-34
        22:        if (isCorrect)
        23:        {
        24:            // P(L|correct) = (1-P_S) * P_L / [(1-P_S)*P_L + P_G*(1-P_L)]
        25:            float numerator = (1f - p.P_S) * currentP_L;
        26:            float denominator = numerator + p.P_G * (1f - currentP_L);
        27:            posterior = denominator > 0f ? numerator / denominator : currentP_L;
        28:        }
        29:        else
        30:        {
        31:            // P(L|incorrect) = P_S * P_L / [P_S*P_L + (1-P_G)*(1-P_L)]
        32:            float numerator = p.P_S * currentP_L;
        33:            float denominator = numerator + (1f - p.P_G) * (1f - currentP_L);
        34:            posterior = denominator > 0f ? numerator / denominator : currentP_L;
        35:        }
    - type: citation
      content: |
        Corbett, A.T. & Anderson, J.R. (1995). "Knowledge Tracing: Modeling
        the Acquisition of Procedural Knowledge." User Modeling and
        User-Adapted Interaction, 4, 253-278. DOI: 10.1007/BF01099821.

        The foundational BKT paper. BKT is defined over a stream of observed
        attempts with both correct AND incorrect outcomes — the update rules
        for P(L|incorrect) are co-equal with P(L|correct). A BKT model that
        only sees correct observations is not Bayesian knowledge tracing; it
        is a monotonically increasing counter.
  finding: |
    The `ConceptAttempted_V1` event is appended inside an `if (isCorrect)`
    block, which means incorrect answers produce zero mastery events. Any
    downstream projection or actor listening to `ConceptAttempted_V1` will
    never see failures — it sees only a stream of "IsCorrect: true" events
    (line 626 even hard-codes `IsCorrect: true`). This directly breaks the
    BktTracer in src/actors/Cena.Actors/Mastery/BktTracer.cs, which was
    designed to update `P(L|incorrect)` on failures. As implemented, the
    REST session flow can only raise mastery, never lower it, and the actor
    BKT pipeline — which is correctly implemented — receives no signal it
    can act on.
  root_cause: |
    The "HARDEN" rewrite of SessionEndpoints conflated XP-awarding (which
    should only fire on correct answers) with mastery-signaling (which must
    fire on every answer). The `ConceptAttempted_V1` append was nested inside
    the same `if` block as the XpAwarded append.
  proposed_fix: |
    1. Move the `ConceptAttempted_V1` emission OUTSIDE the `if (isCorrect)`
       block so it fires on every answer.
    2. Stop hard-coding `IsCorrect: true` on line 626 — pass the real
       `isCorrect` variable computed on line 583.
    3. Populate `ErrorType` via the ErrorClassificationService for wrong
       answers instead of `""` on line 631.
    4. Stop computing `PosteriorMastery` as `prior + 0.05` on line 633; call
       BktService.Update() with the real BktParameters and write the result.
    5. Add an xUnit test that submits a wrong answer through the endpoint and
       asserts `P(L)` decreased via the event stream.

- id: FIND-pedagogy-003
  severity: p0
  category: pedagogy
  title: "Posterior mastery is a hard-coded linear +0.05 increment, bypassing BKT entirely"
  file: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
  line: 632-633
  evidence:
    - type: file-excerpt
      content: |
        632:                    PriorMastery: queue.ConceptMasterySnapshot.GetValueOrDefault(questionDoc.ConceptId, 0.5),
        633:                    PosteriorMastery: Math.Min(1.0, queue.ConceptMasterySnapshot.GetValueOrDefault(questionDoc.ConceptId, 0.5) + 0.05),
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Mastery/BktParameters.cs, lines 13-16
        13:    public readonly record struct BktParameters(float P_L0, float P_T, float P_S, float P_G)
        14:    {
        15:        /// <summary>Default parameters for launch (before trainer calibrates).</summary>
        16:        public static readonly BktParameters Default = new(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);
    - type: citation
      content: |
        Corbett, A.T. & Anderson, J.R. (1995). "Knowledge Tracing: Modeling
        the Acquisition of Procedural Knowledge." User Modeling and
        User-Adapted Interaction, 4, 253-278. DOI: 10.1007/BF01099821.

        BKT posterior updates are non-linear in P(L) and depend on the slip
        (P_S) and guess (P_G) parameters of the knowledge component. A
        constant linear increment (+0.05 per correct) cannot represent the
        Bayesian posterior and guarantees that, for any student, reaching the
        0.85 progression threshold requires exactly ceil((0.85-0.5)/0.05) = 7
        correct answers regardless of the concept difficulty, prior skill, or
        slip/guess characteristics.
  finding: |
    On correct answers, the endpoint writes `PosteriorMastery = PriorMastery
    + 0.05` directly into the ConceptAttempted event. This means any projection
    consuming these events treats mastery as a linear counter, not as a
    Bayesian posterior. The MasteryConstants.ProgressionThreshold of 0.85 can
    never be reached in a way that reflects true probabilistic knowledge — it
    is reached after a deterministic number of correct answers. A student who
    guesses seven questions correctly gets identical mastery to a student who
    reasons through seven questions correctly.
  root_cause: |
    The REST endpoint was implemented as a façade over a Marten event stream
    without wiring the mastery pipeline (BktTracer, HlrCalculator,
    MasteryPipeline, ScaffoldingService) that already exists in
    src/actors/Cena.Actors/Mastery/. The endpoint duplicates an expedient
    "+0.05" stub in lieu of calling the real computation.
  proposed_fix: |
    1. Inject a BktService / MasteryPipeline into the endpoint dependency graph.
    2. Replace line 633 with a real posterior computed from BktTracer.Update(
       currentP_L, isCorrect, BktParameters.AdjustForHints(base, hintCount)).
    3. Also update HalfLifeHours via HlrCalculator on the same write path.
    4. Add a test that verifies mastery trajectory over 10 correct answers
       is non-linear and sensitive to slip/guess parameters.

- id: FIND-pedagogy-004
  severity: p0
  category: pedagogy
  title: "Diagram challenge model has only Hebrew text and Hebrew-only distractor feedback — breaks English-primary strategy"
  file: src/mobile/lib/features/diagrams/models/diagram_models.dart
  line: 298-310
  evidence:
    - type: file-excerpt
      content: |
        297:    @freezed
        298:    class ChallengeOption with _$ChallengeOption {
        299:      const factory ChallengeOption({
        300:        required String id,
        301:        required String textHe,
        302:        required bool isCorrect,
        303:
        304:        /// Shown if student selects this wrong answer
        305:        String? feedbackHe,
        306:      }) = _ChallengeOption;
    - type: file-excerpt
      content: |
        # src/mobile/lib/features/diagrams/challenge_card_widget.dart, lines 61-65
        61:    setState(() {
        62:      _selectedOptionIndex = index;
        63:      _isAnswered = true;
        64:      _isCorrect = option.isCorrect;
        65:      _wrongFeedback = option.isCorrect ? null : option.feedbackHe;
        66:    });
    - type: citation
      content: |
        CENA user memory: "Language Strategy — English primary, Arabic/Hebrew
        secondary, Hebrew hideable outside Israel" (feedback_language_strategy,
        decision 2026). The product stance is that English is the PRIMARY
        locale; Hebrew must be hideable for non-Israel tenants.

        August, D. & Shanahan, T. (Eds.) (2006). "Developing Literacy in
        Second-Language Learners: Report of the National Literacy Panel on
        Language-Minority Children and Youth." Lawrence Erlbaum.
        ISBN: 978-0805860788.

        Research consensus: comprehension feedback must be in the learner's
        language of instruction. Feedback delivered in an unfamiliar language
        provides zero formative value and can actively impede learning by
        adding extraneous cognitive load.
  finding: |
    The `ChallengeOption` model required field `textHe` and optional field
    `feedbackHe` are the ONLY localization points. There is no textEn, textAr,
    feedbackEn, feedbackAr, or a generic map keyed by locale. An
    English-speaking student sees Hebrew question choices and Hebrew feedback
    regardless of their locale preference, violating the project's English-
    primary strategy and delivering feedback in a language they cannot read.
  root_cause: |
    The diagram challenges feature was built Hebrew-first and never
    internationalized. The model shape hard-codes `...He` field suffixes
    rather than using a translation map.
  proposed_fix: |
    1. Replace `textHe` / `feedbackHe` with `Map<String, String> text` and
       `Map<String, String>? feedback` keyed by locale code ('en', 'he', 'ar').
    2. In `challenge_card_widget.dart`, read from the map using the current
       locale with fallback to 'en' → 'he'.
    3. Backfill English translations for all existing diagram challenges
       before re-enabling the feature for non-Hebrew students.
    4. Add a test that asserts the feature is hidden (not rendered in
       Hebrew-only fallback) when locale is 'en' AND no English translation
       is present, to avoid showing Hebrew to English students.
```

---

## P1 — High

```yaml
- id: FIND-pedagogy-005
  severity: p1
  category: pedagogy
  title: "Post-answer feedback auto-dismisses after 1.6 seconds with no way to pause or read"
  file: src/student/full-version/src/pages/session/[sessionId]/index.vue
  line: 79-88
  evidence:
    - type: file-excerpt
      content: |
        77:        feedback.value = resp
        78:
        79:        // Show feedback for ~1.6s then auto-advance
        80:        setTimeout(async () => {
        81:          feedback.value = null
        82:          if (resp.nextQuestionId) {
        83:            await loadCurrentQuestion()
        84:          }
        85:          else {
        86:            await completeSession()
        87:          }
        88:        }, 1600)
    - type: citation
      content: |
        Shute, V.J. (2008). "Focus on Formative Feedback." Review of
        Educational Research, 78(1), 153-189. DOI: 10.3102/0034654307313795.

        Shute's review (synthesizing 50+ years of feedback research) explicitly
        warns that "presenting feedback too quickly may interrupt the student's
        processing and reduce its effectiveness." Effective formative feedback
        requires learner-controlled pacing so the student can read, reflect,
        and integrate the information. A fixed 1.6s auto-dismiss makes
        reflective processing impossible, particularly for wrong answers
        where the student most needs to understand what happened.

        Kulhavy, R.W. & Stock, W.A. (1989). "Feedback in Written Instruction:
        The Place of Response Certitude." Educational Psychology Review, 1(4),
        279-308. DOI: 10.1007/BF01320096.

        The "mindful processing" literature: feedback on errors requires
        substantially more time to process than feedback on correct responses
        (typically 3-5x longer). Uniform short dismiss times fail both cases.
  finding: |
    After the student answers a question, the `AnswerFeedback` component is
    rendered for exactly 1600 milliseconds via a hard-coded `setTimeout`, then
    removed and replaced with the next question. There is no pause, tap-to-
    continue, "read more", or hold gesture. A student who is reading a long
    explanation (if one existed — it currently doesn't, see FIND-pedagogy-001)
    would be yanked to the next question mid-sentence.
  root_cause: |
    A magic number was inlined into the session runner page without a product
    owner sign-off on the formative-feedback dwell time, and without a dismiss
    control in the UI.
  proposed_fix: |
    1. Replace the setTimeout with an explicit "Continue" button that the
       student taps when ready. Keep an optional auto-advance of 8-10 seconds
       as a fallback for correct-answer celebration only.
    2. For wrong answers, REQUIRE a tap to continue — do not auto-advance.
    3. Make the dismiss delay configurable via the student profile
       (accessibility preference: "Slower feedback timing").

- id: FIND-pedagogy-006
  severity: p1
  category: pedagogy
  title: "Vue session flow does not use ScaffoldingService — no hints, no worked examples, no faded examples ever served"
  file: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
  line: 546-678
  evidence:
    - type: file-excerpt
      content: |
        # grep for ScaffoldingService in src/api/Cena.Api.Host — ZERO matches
        # src/actors/Cena.Actors/Mastery/ScaffoldingService.cs exists and is called
        # only from src/actors/Cena.Actors/Sessions/LearningSessionActor.cs and
        # src/actors/Cena.Actors/Students/StudentActor.Commands.cs.
        #
        # The Vue student app calls POST /api/sessions/{id}/answer, which is
        # the endpoint in SessionEndpoints.cs. Nothing in that file references
        # hints, scaffolding, worked examples, or a ScaffoldingLevel.
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Mastery/ScaffoldingService.cs, lines 35-48
        35:    public static ScaffoldingMetadata GetScaffoldingMetadata(ScaffoldingLevel level) => level switch
        36:    {
        37:        ScaffoldingLevel.Full => new(level, "worked-example", ShowWorkedExample: true,
        38:            ShowHintButton: true, MaxHints: 3, RevealAnswer: true),
        39:
        40:        ScaffoldingLevel.Partial => new(level, "faded-example", ShowWorkedExample: false,
        41:            ShowHintButton: true, MaxHints: 2, RevealAnswer: true),
        42:        ...
    - type: citation
      content: |
        Sweller, J., van Merriënboer, J.J.G. & Paas, F. (1998).
        "Cognitive Architecture and Instructional Design." Educational
        Psychology Review, 10(3), 251-296. DOI: 10.1023/A:1022193728205.

        The Worked Example Effect is one of the most replicated findings in
        cognitive load theory: novice learners benefit substantially from
        fully worked examples before being asked to solve problems
        independently. Worked examples should be gradually faded as expertise
        develops (the Expertise Reversal Effect, Kalyuga et al. 2003).

        Renkl, A. & Atkinson, R.K. (2003). "Structuring the Transition From
        Example Study to Problem Solving in Cognitive Skill Acquisition: A
        Cognitive Load Perspective." Educational Psychologist, 38(1), 15-22.
        DOI: 10.1207/S15326985EP3801_3.

        Supports faded-example scaffolding as the correct transition between
        worked examples and independent practice. The `ScaffoldingService`
        implementation (Full → Partial → HintsOnly → None) exactly matches
        the published pedagogical sequence.

        Kalyuga, S., Ayres, P., Chandler, P. & Sweller, J. (2003). "The
        Expertise Reversal Effect." Educational Psychologist, 38(1), 23-31.
        DOI: 10.1207/S15326985EP3801_4.

        Continuing to show scaffolds to experts is counterproductive;
        scaffolds must fade. Exactly what the service models — but only for
        the actor-side path, not the REST path.
  finding: |
    `ScaffoldingService.DetermineLevel(effectiveMastery, psi)` exists in
    src/actors/Cena.Actors/Mastery/ and correctly returns Full → Partial →
    HintsOnly → None levels based on mastery, with worked examples for
    novices. It is called from `LearningSessionActor.cs` and
    `StudentActor.Commands.cs`, but the REST path that serves the Vue student
    app (`SessionEndpoints.cs`) never invokes it. As a result, every student
    using the web app — regardless of whether they are at 5% mastery or 80%
    mastery — gets the same bare multiple-choice question with zero
    scaffolding, zero hints, and zero worked examples.
  root_cause: |
    Two parallel code paths: the actor-based session (LearningSessionActor,
    correctly wired) and the REST-based session in the API Host (wired only
    for raw question delivery). The Vue student app hits only the REST path.
  proposed_fix: |
    1. In the REST `GET /api/sessions/{id}/current-question` endpoint, compute
       ScaffoldingLevel from the student's current mastery on this question's
       concept and include `ScaffoldingMetadata` in `SessionQuestionDto`.
    2. Add fields `workedExample?: string`, `hintsAvailable: int`,
       `hintsRemaining: int` to `SessionQuestionDto`.
    3. Create a `POST /api/sessions/{id}/question/{qid}/hint` endpoint that
       returns progressive hints from the HintGenerator service.
    4. Wire `QuestionCard.vue` to render the worked example block, hint
       button, and hint-remaining counter.
    5. Penalize the BKT update when hints are used (BktParameters.
       AdjustForHints already exists at ScaffoldingService.cs:35).

- id: FIND-pedagogy-007
  severity: p1
  category: pedagogy
  title: "Error type is not captured — `ErrorType: \"\"` hard-coded, blocking the MCM error-type routing pipeline"
  file: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
  line: 631
  evidence:
    - type: file-excerpt
      content: |
        622:                var conceptAttempt = new ConceptAttempted_V1(
        623:                    StudentId: studentId,
        624:                    ConceptId: questionDoc.ConceptId,
        ...
        630:                    MethodologyActive: queue.Mode,
        631:                    ErrorType: "",
        632:                    PriorMastery: ...
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Services/ErrorClassificationService.cs exists
        # and is not referenced from SessionEndpoints.cs.
        # src/actors/Cena.Actors/Graph/McmGraphActor.cs exists and routes on
        # ErrorType. With ErrorType always empty, the MCM graph never fires.
    - type: citation
      content: |
        VanLehn, K. (1988). "Student Modeling." In M.C. Polson & J.J.
        Richardson (Eds.), Foundations of Intelligent Tutoring Systems
        (pp. 55-78). Lawrence Erlbaum. ISBN: 978-0805800531.

        Classic ITS finding: the instructional response to a wrong answer
        depends critically on WHY the student got it wrong. Procedural
        errors (correct concept, wrong execution) need worked examples;
        conceptual errors (misunderstood the idea) need explanation and
        analogy; slips/careless errors need no intervention at all. Without
        error classification, the tutor cannot choose the right remediation.

        Brown, J.S. & VanLehn, K. (1980). "Repair Theory: A Generative
        Theory of Bugs in Procedural Skills." Cognitive Science, 4(4),
        379-426. DOI: 10.1207/s15516709cog0404_3.

        The foundational "buggy algorithms" literature. Error types are a
        first-class signal in intelligent tutoring, not an optional label.
  finding: |
    On every ConceptAttempted_V1 event the endpoint hard-codes `ErrorType:
    ""`. Cena has an ErrorClassificationService and an MCM (Methodology-
    Concept-Methodology) graph that routes pedagogical responses based on
    the detected error type (procedural vs conceptual vs careless vs
    systematic vs transfer). With an empty string on every event, the MCM
    routing in `MethodologySwitchService` and `McmGraphActor` receives no
    signal and cannot distinguish a slip from a deep misconception.
  root_cause: |
    Same architectural gap as FIND-pedagogy-006 — the REST endpoint bypasses
    services that the actor-side flow already uses.
  proposed_fix: |
    1. Inject IErrorClassificationService into the endpoint.
    2. For wrong answers, call the classifier with (studentAnswer,
       correctAnswer, questionType, priorErrors) and populate ErrorType with
       one of: Procedural, Conceptual, Careless, Systematic, Transfer.
    3. For correct answers, set ErrorType: ErrorType.None.
    4. Add a test covering each error type.

- id: FIND-pedagogy-008
  severity: p1
  category: pedagogy
  title: "Per-question LearningObjective/Competency metadata is absent — Bloom level is the only pedagogical tag"
  file: src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs
  line: 12-28
  evidence:
    - type: file-excerpt
      content: |
        12:    public class QuestionDocument
        13:    {
        14:        public string Id { get; set; } = "";
        15:        public string QuestionId { get; set; } = "";
        16:        public string Subject { get; set; } = "";
        17:        public string? Topic { get; set; }
        18:        public string Difficulty { get; set; } = "medium";
        19:        public string ConceptId { get; set; } = "";
        20:        public string Prompt { get; set; } = "";
        21:        ...
    - type: file-excerpt
      content: |
        # grep for LearningObjective / ObjectiveId / Competency in src/ returned
        # zero matches.
    - type: citation
      content: |
        Wiggins, G. & McTighe, J. (2005). "Understanding by Design" (2nd ed.).
        ASCD. ISBN: 978-1416600350.

        Backward design: every assessment item must be traceable to an
        explicit learning goal. Without per-item learning-objective metadata,
        coverage analysis, gap detection, and standards alignment become
        impossible — and teachers cannot see which objectives their students
        have and have not demonstrated.

        Anderson, L.W. & Krathwohl, D.R. (Eds.) (2001). "A Taxonomy for
        Learning, Teaching, and Assessing: A Revision of Bloom's Taxonomy of
        Educational Objectives." Pearson. ISBN: 978-0321084057.

        The revised Bloom's taxonomy explicitly separates the cognitive
        process dimension (remember, understand, apply, analyze, evaluate,
        create) from the knowledge dimension (factual, conceptual,
        procedural, metacognitive). A single `BloomsLevel` integer collapses
        both dimensions into one axis and loses the knowledge-dimension tag.
  finding: |
    `QuestionDocument` classifies items by `Subject`, `Topic`, `ConceptId`,
    `Difficulty`, and `Grade` but carries no explicit `LearningObjective` or
    `Competency` field, and `BloomsLevel` (on QuestionState) is a single
    integer rather than a (cognitive-process, knowledge-type) tuple. This
    limits reporting (teachers cannot see coverage by learning objective),
    standards alignment (no mapping to Bagrut / Common Core objectives), and
    adaptive selection (the item selector can only pick on ConceptId +
    difficulty).
  root_cause: |
    Early-stage design chose ConceptId as the sole pedagogical anchor. This is
    defensible for a concept-graph system but leaves no place to attach the
    specific learning goal that a concept contains multiple of.
  proposed_fix: |
    1. Add a `LearningObjectiveId` field to QuestionDocument and
       QuestionState.
    2. Add a LearningObjective aggregate to the Question/Curriculum context
       with fields (id, description, bloomsCognitiveProcess,
       bloomsKnowledgeType, standardsAlignment).
    3. Extend BloomsLevel to BloomsClassification(cognitiveProcess,
       knowledgeType) per Anderson & Krathwohl 2001.
    4. Backfill existing questions via a one-time migration.

- id: FIND-pedagogy-009
  severity: p1
  category: pedagogy
  title: "Difficulty is a free-text string (\"easy\" | \"medium\" | \"hard\"), not an Elo-calibrated float — adaptive item selection cannot use the 85% rule"
  file: src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs
  line: 18
  evidence:
    - type: file-excerpt
      content: |
        18:    public string Difficulty { get; set; } = "medium"; // easy, medium, hard
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Mastery/ItemSelector.cs, lines 80-96
        80:        for (int i = 0; i < availableItems.Count; i++)
        81:        {
        82:            var item = availableItems[i];
        83:            if (item.ConceptId != conceptId) continue;
        84:
        85:            float expected = item.ExpectedCorrectness > 0
        86:                ? item.ExpectedCorrectness
        87:                : EloScoring.ExpectedCorrectness(studentTheta, item.DifficultyElo);
        88:
        89:            float distance = MathF.Abs(expected - TargetCorrectness);
    - type: citation
      content: |
        Wilson, R.C., Shenhav, A., Straccia, M. & Cohen, J.D. (2019). "The
        Eighty Five Percent Rule for optimal learning." Nature Communications,
        10, 4646. DOI: 10.1038/s41467-019-12552-4.

        The paper that established the "85% rule": for stochastic gradient
        descent-based learners (a good model of human skill acquisition),
        the optimal training error rate that maximizes learning speed is
        approximately 15.87% — i.e., the learner should be succeeding at
        roughly 85% of items. The CENA ItemSelector explicitly targets
        `TargetCorrectness = 0.85f`, which matches this finding — BUT it
        requires a continuous difficulty signal (Elo rating) to compute an
        expected-correctness probability. A 3-bucket enum ("easy"/"medium"/
        "hard") cannot support this.

        Elo, A.E. (1978). "The Rating of Chessplayers, Past and Present."
        Arco Publishing. ISBN: 978-0668047210.

        Elo's original monograph. The Elo system requires continuous ratings
        to compute expected win probabilities; a 3-category scale collapses
        the computation.
  finding: |
    The `QuestionDocument.Difficulty` is a three-valued string ("easy",
    "medium", "hard") with no numerical Elo rating. The actor-side
    `ItemSelector` and `EloScoring.ExpectedCorrectness` are designed to work
    with `item.DifficultyElo` (a continuous float) and to pick items where
    expected correctness is closest to 0.85. With only three buckets, this
    optimization degenerates — an "easy" and a "medium" differ by an unknown
    number of Elo points, and "medium" questions are clumped together
    regardless of whether they are at the student's edge of competence.
  root_cause: |
    The QuestionDocument (used by the REST API) was designed as a simple
    CRUD record, decoupled from the Elo-rich item-rating model the actors
    use. The two representations never converged.
  proposed_fix: |
    1. Add `DifficultyElo: float` to QuestionDocument, seeded from prior
       student response data (or a reasonable per-grade prior).
    2. Keep the string label as a human-readable UI hint, but stop using it
       for any adaptive decision.
    3. Update DifficultyElo online using the student's theta rating and
       response outcome (standard Elo update: D_new = D_old + K*(actual -
       expected)).
    4. Wire the ItemSelector into the REST endpoint's next-question
       selection so the 85% rule actually applies.
```

---

## P2 — Normal

```yaml
- id: FIND-pedagogy-010
  severity: p2
  category: pedagogy
  title: "Mastery threshold constants have no source citation"
  file: src/actors/Cena.Actors/MasteryConstants.cs
  line: 26-39
  evidence:
    - type: file-excerpt
      content: |
        21:    public static class MasteryConstants
        22:    {
        23:        /// <summary>
        24:        /// BKT progression threshold: student has demonstrated sufficient mastery
        25:        /// to proceed to dependent concepts. Used for frontier computation,
        26:        /// review scheduling, ConceptMastered event emission, and item selection.
        27:        /// </summary>
        28:        public const double ProgressionThreshold = 0.85;
        29:
        30:        /// <summary>
        31:        /// Strict prerequisite gate: used for critical prerequisites (edge weight >= 0.9).
        32:        /// Prevents premature progression on foundational dependency chains.
        33:        /// </summary>
        34:        public const double PrerequisiteGateThreshold = 0.95;
    - type: citation
      content: |
        Wilson, R.C., Shenhav, A., Straccia, M. & Cohen, J.D. (2019). "The
        Eighty Five Percent Rule for optimal learning." Nature Communications,
        10, 4646. DOI: 10.1038/s41467-019-12552-4.

        For 0.85 as a progression/item-targeting threshold.

        Corbett, A.T. & Anderson, J.R. (1995). "Knowledge Tracing." UMUAI 4,
        253-278. DOI: 10.1007/BF01099821.

        For 0.95 as a stricter Bayesian mastery certainty threshold; Corbett
        recommends P(L) ≥ 0.95 as the point at which further practice has
        negligible information gain.
  finding: |
    The constants 0.85 (progression) and 0.95 (prerequisite gate) in
    `MasteryConstants.cs` are presented without any research citation in
    code comments. Both values are defensible and have research support,
    but a developer modifying them has no source of truth for why they are
    what they are.
  root_cause: |
    Code was written for brevity; citations were placed in architecture
    docs only, not at the point of use.
  proposed_fix: |
    Add `/// <remarks>Source: Wilson et al. 2019, Nat Commun 10:4646</remarks>`
    to ProgressionThreshold and `/// <remarks>Source: Corbett & Anderson
    1995</remarks>` to PrerequisiteGateThreshold. No functional change; this
    is a comment-only fix.

- id: FIND-pedagogy-011
  severity: p2
  category: pedagogy
  title: "BKT hint-credit curve (0→1.0, 1→0.7, 2→0.4, 3+→0.1) has no cited source"
  file: src/actors/Cena.Actors/Mastery/BktParameters.cs
  line: 31-45
  evidence:
    - type: file-excerpt
      content: |
        29:    /// <summary>
        30:    /// SAI-002: Reduce P_T (learning transition probability) based on hints used.
        31:    /// More hints = less credit for a correct answer. The student demonstrated less
        32:    /// independent mastery, so the learning transition is attenuated.
        33:    /// Credit curve: 0 hints = 1.0x, 1 = 0.7x, 2 = 0.4x, 3+ = 0.1x.
        34:    /// </summary>
        35:    public static BktParameters AdjustForHints(BktParameters baseParams, int hintsUsed)
        36:    {
        37:        float creditMultiplier = hintsUsed switch
        38:        {
        39:            0 => 1.0f,
        40:            1 => 0.7f,
        41:            2 => 0.4f,
        42:            3 => 0.1f,
        43:            _ => 0.1f
        44:        };
    - type: citation
      content: |
        Aleven, V. & Koedinger, K.R. (2000). "Limitations of Student Control:
        Do Students Know When They Need Help?" In Intelligent Tutoring
        Systems: 5th International Conference (pp. 292-303). Springer.
        DOI: 10.1007/3-540-45108-0_33.

        Shows that hinted-help attenuates the evidence of independent mastery
        and that a simple discount (0.5-0.7x for first hint, tapering
        thereafter) is a reasonable default for BKT credit attenuation.
        The specific curve (1.0/0.7/0.4/0.1) in Cena's code is defensible
        but is not from this paper — it is a team-chosen heuristic.
  finding: |
    The hint-credit multipliers are a tunable heuristic with no citation or
    calibration study in the code or docs. The general pattern is supported
    by Aleven & Koedinger 2000 but the specific numbers are team-picked.
  root_cause: |
    Tunable hyperparameter shipped without an empirical calibration step.
  proposed_fix: |
    1. Either cite Aleven & Koedinger 2000 in the comment as the conceptual
       source (not the numerical source), OR
    2. Run a calibration study on 2 months of post-launch data comparing
       mastery predictions with different multiplier sets, and document the
       chosen curve.

- id: FIND-pedagogy-012
  severity: p2
  category: pedagogy
  title: "Home page KPI grid serves 5 cards in one row at tablet viewports — potential extraneous cognitive load"
  file: src/student/full-version/src/pages/home.vue
  line: 107-148
  evidence:
    - type: file-excerpt
      content: |
        107:      <section
        108:        class="home-page__kpis mb-6"
        ...
        117:        <StreakWidget ... />
        121:        <KpiCard label="Minutes today" ... />
        128:        <KpiCard label="Questions" ... />
        135:        <KpiCard label="Accuracy" ... />
        142:        <KpiCard :label="`Level ${level}`" ... />
        ...
        172:      .home-page__kpis {
        173:        display: grid;
        174:        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        175:        gap: 1rem;
        176:      }
    - type: citation
      content: |
        Sweller, J. (1988). "Cognitive load during problem solving: Effects
        on learning." Cognitive Science, 12(2), 257-285.
        DOI: 10.1207/s15516709cog1202_4.

        Foundational paper on cognitive load theory. Extraneous load from
        visual complexity competes with germane load (the processing the
        student needs to do to learn). Dashboard screens with 5+ cards can
        still be fine, but in a learning app they pull working memory away
        from the academic task.

        Sweller, J., Ayres, P. & Kalyuga, S. (2011). "Cognitive Load Theory."
        Explorations in the Learning Sciences, Instructional Systems and
        Performance Technologies, 1. Springer. ISBN: 978-1441981257.

        On landing screens in learning apps: prefer a single "next action"
        focal point with secondary information demoted to a drill-down.
  finding: |
    The home page shows 5 KPI cards (Streak + Minutes + Questions + Accuracy
    + Level) in a single row at tablet+ widths. A student opening the app to
    practice is presented with 5 numeric cards plus navigation before they
    see "what do I do next". This is a minor violation of focal-point design
    for learning apps.
  root_cause: |
    Direct port of a generic admin dashboard KPI pattern (from the Vuexy
    template) without tailoring for the student-learning context.
  proposed_fix: |
    1. Demote 3 of the 5 KPIs into a collapsible "Stats" section.
    2. Promote a single "Start next session" card as the primary focal point.
    3. Streak can stay as a secondary but it should be smaller than the
       primary action.
```

---

## P3 — Low

```yaml
- id: FIND-pedagogy-013
  severity: p3
  category: pedagogy
  title: "Session runner ends with silent auto-navigate to summary — no metacognitive prompt"
  file: src/student/full-version/src/pages/session/[sessionId]/index.vue
  line: 52-56
  evidence:
    - type: file-excerpt
      content: |
        52:    async function completeSession() {
        53:      completing.value = true
        54:      // The summary page calls /complete itself — we just navigate there.
        55:      await router.push(`/session/${sessionId}/summary`)
        56:    }
    - type: citation
      content: |
        Zimmerman, B.J. (2002). "Becoming a Self-Regulated Learner: An
        Overview." Theory Into Practice, 41(2), 64-70.
        DOI: 10.1207/s15430421tip4102_2.

        Self-regulated learning cycle: forethought → performance →
        self-reflection. The self-reflection phase (self-evaluation, causal
        attribution, adaptive inferences) is critical. An app that silently
        dumps the student into a summary page after the last question skips
        the self-reflection affordance and fails to develop metacognitive
        skill.
  finding: |
    On the last question, the session runner calls `completeSession()` which
    immediately routes to the summary page. The student has no prompt to
    self-assess (e.g. "How confident are you on Topic X?") or to reflect on
    what was hard. This is a missed opportunity for metacognitive skill
    development.
  root_cause: |
    The summary page was designed as a post-hoc stats screen, not a
    self-reflection interstitial.
  proposed_fix: |
    Add a brief "self-reflection" interstitial between last question and
    summary: one open-ended prompt ("What felt hardest?") plus a 1-5
    confidence slider per topic practiced. Do not gamify or penalize —
    reflection is its own value.

- id: FIND-pedagogy-014
  severity: p3
  category: pedagogy
  title: "Locale strings in student i18n do not include an `explanation` or `rationale` label"
  file: src/student/full-version/src/plugins/i18n/locales/en.json
  line: 561-568
  evidence:
    - type: file-excerpt
      content: |
        561:      "runner": {
        562:        "questionProgress": "Question {current} of {total}",
        ...
        565:        "submitAnswer": "Submit",
        566:        "correct": "Correct!",
        567:        "wrong": "Not quite.",
        568:        "xpAwarded": "+{xp} XP",
    - type: citation
      content: |
        Butler, A.C. & Roediger, H.L. (2008). "Feedback enhances the positive
        effects and reduces the negative effects of multiple-choice testing."
        Memory & Cognition, 36(3), 604-616. DOI: 10.3758/MC.36.3.604.

        Shows that when multiple-choice feedback includes an explanation of
        the correct answer, students benefit from testing effect without
        learning the distractors as the correct answer. Without explanation,
        multiple-choice testing can reinforce misconceptions.
  finding: |
    The session runner locale block has keys for "correct" / "wrong" /
    "xpAwarded" but no `explanation`, `rationale`, `whyItsWrong`, or
    `correctAnswerBecause` keys. When FIND-pedagogy-001 is fixed, the locale
    block will need these keys added to all three locales (en/he/ar).
  root_cause: |
    The locale was set up for the binary-feedback implementation and has not
    been expanded.
  proposed_fix: |
    Add the following keys to en.json, he.json, ar.json under session.runner:
    - explanation: "Here's why:"
    - hintHeader: "Hint"
    - continueWhenReady: "Tap to continue"
```

---

## Discarded (UNSOURCED — would have been inventions)

- DISCARDED: "Spaced repetition is not actually implemented" — I initially flagged the absence of SRS visuals in the Vue app as a possible P0. DISCARDED because HlrCalculator in `src/actors/Cena.Actors/Mastery/HlrCalculator.cs` cites Settles & Meeder (2016) and correctly implements half-life regression, and MasteryDecayScanner fires MasteryDecayed events. SRS exists in the backend; it is simply not surfaced in the Vue UI — that is a UX finding (Agent 5's remit), not a pedagogy finding.

- DISCARDED: "Gamification undermines intrinsic motivation via XP rewards" — I considered a P1 citing Deci 1971 and Deci, Koestner & Ryan 1999 (the meta-analysis on tangible rewards undermining intrinsic motivation). DISCARDED because the specific claim requires evidence that the XP rewards are (a) task-contingent rather than completion-contingent, AND (b) perceived as controlling. The endpoint awards flat +10 XP per correct answer with no streak multiplier or speed bonus — this is at the "reward completion of free choice" end of the Cameron & Pierce 1994 spectrum, which Deci 1999 acknowledges is less problematic. Without user-research data I cannot prove intrinsic motivation is being undermined, so the claim does not clear the evidence bar.

- DISCARDED: "Cognitive load is high on mobile viewports" — Considered citing Sweller et al. 2011. DISCARDED because the home page grid uses `grid-template-columns: repeat(auto-fit, minmax(200px, 1fr))` which collapses to single column on mobile. The claim is only plausible at tablet+ widths (kept as FIND-pedagogy-012 at p2).

- DISCARDED: "Weighted prerequisite penalty formula in PrerequisiteCalculator is mathematically wrong" — I initially thought the doc comment `product(max(mastery(p)/0.85, 1.0))` described a broken formula. DISCARDED after reading the actual code at lines 68-70 — the code is correct (it only penalizes when factor < 1.0); the doc comment is misleading. This is a doc-vs-code drift issue, not a pedagogy issue, and belongs in an Agent 1 finding.

- DISCARDED: "Primary Bloom's level is flattened to a single integer" — Considered citing Anderson & Krathwohl 2001 for the cognitive-process × knowledge-dimension matrix. Kept the core claim inside FIND-pedagogy-008 (learning-objective metadata gap), but not as a separate finding because there's no evidence that any adaptive logic actually breaks from the single-integer representation — it's a metadata richness issue, not a runtime pedagogy failure.

- DISCARDED: "Difficulty-progression / desirable-difficulty rule is violated by fixed +0.05 mastery increment" — The finding is covered under FIND-pedagogy-003 (BKT bypass). I considered elevating a separate finding tied specifically to Bjork's "desirable difficulties" (Bjork & Bjork, 1992, "A new theory of disuse"). DISCARDED as duplicate — the root cause is the same bypass, and FIND-pedagogy-003 already cites Corbett & Anderson 1995 which is the more directly applicable reference for a BKT-implementation failure.

---

## Notes for the coordinator

1. FIND-pedagogy-001 through -003 are all rooted in the same file
   (`SessionEndpoints.cs`) and should be fixed as a single coordinated PR.
   They represent the bulk of the pedagogical debt in the REST path — the
   actor-side path is in much better shape.

2. The actor-side implementations (BktTracer, HlrCalculator, ScaffoldingService,
   ErrorClassificationService, ItemSelector, MethodologySwitchService) are
   pedagogically sound and well-structured. The problem is architectural:
   the REST API Host exposes a parallel, simplified path that ignores them.

3. FIND-pedagogy-004 is mobile-specific and unrelated to the web app issues.
   Fix the diagram challenge model shape before re-enabling that feature
   for non-Hebrew students.

4. Every P0 finding has a real citation, not a hand-wave. The coordinator
   should check the DOIs I provided against Crossref before merging the
   enqueued tasks into the delivery plan.
