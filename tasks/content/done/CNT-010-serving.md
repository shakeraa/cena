# CNT-010: Adaptive Question Serving — Student-Personalized Delivery, ZPD, Focus-Aware

**Priority:** P0 — core student experience
**Blocked by:** CNT-009 (Moderation — needs published items), ACT-001 (Actor system), DATA-003 (Neo4j)
**Estimated effort:** 6 days
**Contract:** `docs/question-ingestion-specification.md` Section 8, `docs/assessment-specification.md`, `docs/intelligence-layer.md`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

Published questions must be served to students in a way that adapts to their individual mastery level, learning pace, focus state, preferred language, and current session goals. This is not random question selection — it is a multi-criteria optimization that considers BKT mastery state, zone of proximal development (ZPD), Bloom's progression, focus degradation signals, and spaced repetition scheduling.

The serving layer is the bridge between the content pipeline and the student experience.

## Subtasks

### CNT-010.1: Question Pool Actor — In-Memory Published Content

**Files to create/modify:**
- `src/Cena.Actors/Content/QuestionPoolActor.cs` — per-subject question pool
- `src/Cena.Actors/Content/QuestionPoolState.cs` — in-memory index

**Acceptance:**
- [ ] One `QuestionPoolActor` per subject, managed by Akka.NET cluster
- [ ] On startup: loads all published items from PostgreSQL into in-memory index
- [ ] Index structure: concept_id → sorted list of items by (bloom_level, difficulty)
- [ ] Hot-reload: subscribes to NATS `cena.serve.item.published` — adds new items without restart
- [ ] Deprecation: subscribes to NATS `cena.serve.item.deprecated` — removes items from pool
- [ ] Memory footprint: ~2,000 concepts x 10 items x 2KB = ~40MB per subject. Fits in actor memory.
- [ ] Health check: `GET /api/health/question-pool` returns item count per subject, last reload timestamp

**Test:**
```csharp
[Fact]
public async Task QuestionPool_HotReloadsOnPublish()
{
    var pool = CreatePool(initialItems: 100);
    Assert.Equal(100, pool.ItemCount);

    await _nats.Publish("cena.serve.item.published", new ItemPublished { ItemId = "new-1" });
    await Task.Delay(100);  // Allow event processing
    Assert.Equal(101, pool.ItemCount);
}
```

---

### CNT-010.2: Student Context — Mastery + Session State

**Files to create/modify:**
- `src/Cena.Actors/Serving/StudentContextBuilder.cs` — aggregates student state for question selection
- `src/Cena.Data/Serving/StudentSessionState.cs` — current session tracking

**Acceptance:**
- [ ] For each question request, build a `StudentContext`:
  ```csharp
  record StudentContext(
      string StudentId,
      string PreferredLanguage,       // "he" or "ar"
      int DepthUnit,                  // 3, 4, or 5 (Bagrut unit level)
      Dictionary<string, double> ConceptMastery,  // concept_id → BKT P(mastery) 0-1
      Dictionary<string, DateTime> LastPracticed,  // concept_id → last practice time
      List<string> ItemsSeenThisSession,           // avoid repeats
      List<string> ItemsSeenLast7Days,             // spaced repetition window
      FocusState CurrentFocus,                     // from FocusDegradationService
      BloomLevel CurrentBloomCeiling,              // highest Bloom's student can handle right now
      double ZpdLower,                             // zone of proximal development bounds
      double ZpdUpper,
      SessionGoal Goal                             // practice | review | challenge | diagnostic
  );
  ```
- [ ] Mastery data from BKT Actor (real-time, per concept)
- [ ] Focus state from FocusDegradationService (response time trends, error patterns)
- [ ] Session history from Redis (items seen this session, items seen last 7 days)
- [ ] ZPD bounds calculated: `lower = current_mastery - 0.1`, `upper = current_mastery + 0.3` (adaptive)

**Test:**
```csharp
[Fact]
public void StudentContext_CalculatesZPD()
{
    var ctx = BuildContext(mastery: new() { { "derivatives", 0.6 } });
    Assert.Equal(0.5, ctx.ZpdLower);
    Assert.Equal(0.9, ctx.ZpdUpper);
}
```

---

### CNT-010.3: Question Selector — Multi-Criteria Adaptive Selection

**Files to create/modify:**
- `src/Cena.Actors/Serving/QuestionSelector.cs` — core selection algorithm
- `src/Cena.Actors/Serving/SelectionCriteria.cs` — criteria definitions
- `src/Cena.Actors/Serving/ConceptPrioritizer.cs` — which concept to practice next
- `config/serving/selection.yaml` — weights, thresholds, exploration rate

**Acceptance:**
- [ ] **Step 1 — Concept selection**: Pick the best concept to practice based on:
  - BKT mastery probability (prioritize concepts with P(mastery) between 0.3-0.7 — maximum learning gain)
  - Prerequisite readiness (all prerequisites must have P(mastery) > 0.6)
  - Spaced repetition schedule (HLR model: concepts due for review get priority boost)
  - Session goal weighting: `practice` = new concepts, `review` = due concepts, `challenge` = stretch, `diagnostic` = uniform sampling
- [ ] **Step 2 — Bloom's level selection**: Based on student's mastery phase for the selected concept:
  - P(mastery) < 0.3 → remember, understand
  - 0.3-0.6 → understand, apply
  - 0.6-0.8 → apply, analyze
  - 0.8+ → analyze, evaluate
- [ ] **Step 3 — Difficulty selection**: Within the selected Bloom's level, pick difficulty in the student's ZPD:
  - Target difficulty: `P(correct) = 0.65-0.75` (optimal challenge point)
  - Adjust based on focus state: if focus degrading, reduce difficulty by 0.1
  - If focus is strong, increase difficulty by 0.05 (productive challenge)
- [ ] **Step 4 — Item selection**: From the filtered pool, select the best item:
  - Exclude items seen this session
  - Prefer items NOT seen in last 7 days (spaced repetition)
  - Prefer items with higher quality scores
  - Balance item exposure (don't over-serve popular items)
  - Exploration: 10% chance of selecting a random qualifying item (prevents exploitation-only loops)
- [ ] **Step 5 — Localization**: Serve item in student's preferred language. Math expressions identical in both.
- [ ] Selection latency: < 10ms from request to item selected (in-memory operations only)

**Test:**
```csharp
[Fact]
public void Selector_PicksConceptWithHighestLearningGain()
{
    var ctx = BuildContext(mastery: new()
    {
        { "algebra_basics", 0.95 },  // mastered — low priority
        { "derivatives", 0.5 },       // in learning zone — highest priority
        { "integrals", 0.1 }          // not ready — prerequisites not met
    });
    var selection = _selector.SelectConcept(ctx);
    Assert.Equal("derivatives", selection.ConceptId);
}

[Fact]
public void Selector_ReducesDifficultyOnFocusDegradation()
{
    var ctx = BuildContext(focus: FocusState.Degrading, mastery: 0.5);
    var item = _selector.SelectItem(ctx, concept: "derivatives");
    Assert.True(item.Classification.Difficulty < 0.6);
}

[Fact]
public void Selector_RespectsBloomProgression()
{
    var ctx = BuildContext(mastery: new() { { "derivatives", 0.35 } });
    var item = _selector.SelectItem(ctx, concept: "derivatives");
    Assert.Contains(item.Classification.BloomLevel, new[] { "understand", "apply" });
}

[Fact]
public void Selector_NeverRepeatsWithinSession()
{
    var ctx = BuildContext(itemsSeen: new[] { "item-1", "item-2" });
    for (int i = 0; i < 50; i++)
    {
        var item = _selector.SelectItem(ctx, concept: "derivatives");
        Assert.DoesNotContain(item.ItemId, ctx.ItemsSeenThisSession);
        ctx.ItemsSeenThisSession.Add(item.ItemId);
    }
}

[Fact]
public void Selector_UnderTenMs()
{
    var ctx = BuildContext();
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 1000; i++)
        _selector.SelectItem(ctx, concept: "derivatives");
    sw.Stop();
    Assert.True(sw.ElapsedMilliseconds / 1000.0 < 10);  // <10ms per selection
}
```

---

### CNT-010.4: Focus-Aware Adaptation

**Files to create/modify:**
- `src/Cena.Actors/Serving/FocusAdapter.cs` — adapts serving based on focus signals
- Integrates with `FocusDegradationService` (from focus-degradation-research.md)

**Acceptance:**
- [ ] Monitor focus signals during session: response time trend, error rate trend, time-between-interactions
- [ ] Focus states: `strong`, `stable`, `declining`, `degrading`, `critical`
- [ ] Adaptation rules:
  - `strong` → normal selection, allow stretch questions
  - `stable` → normal selection
  - `declining` → reduce difficulty by 10%, prefer shorter questions, suggest break in 5 minutes
  - `degrading` → reduce difficulty by 20%, switch to review/reinforce mode, show break prompt
  - `critical` → pause new content, offer microbreak (90 seconds), switch to easy wins
- [ ] After microbreak: reassess focus. If improved, resume normal. If still degrading, suggest ending session.
- [ ] Focus state changes logged for analytics (student dashboard, parent dashboard)
- [ ] Focus adaptation is transparent: student sees "Let's review something you know well" not "Your focus is declining"

**Test:**
```csharp
[Fact]
public void FocusAdapter_ReducesDifficultyOnDecline()
{
    var criteria = new SelectionCriteria { BaseDifficulty = 0.7 };
    var adapted = _adapter.Adapt(criteria, FocusState.Declining);
    Assert.Equal(0.6, adapted.BaseDifficulty, precision: 1);
}

[Fact]
public void FocusAdapter_SuggestsBreakOnCritical()
{
    var result = _adapter.Adapt(new SelectionCriteria(), FocusState.Critical);
    Assert.True(result.SuggestBreak);
    Assert.Equal(90, result.BreakDurationSeconds);
}
```

---

### CNT-010.5: Session Goal Modes

**Files to create/modify:**
- `src/Cena.Actors/Serving/SessionGoalRouter.cs` — routes question selection based on session goal
- `src/Cena.Web/Controllers/SessionController.cs` — session start with goal selection

**Acceptance:**
- [ ] Session goals (student selects at session start, or system recommends):
  - **Practice** (תרגול / تمارين): Focus on new concepts near mastery frontier. Bloom's: apply-analyze.
  - **Review** (חזרה / مراجعة): Spaced repetition of previously mastered concepts due for review. Bloom's: remember-understand.
  - **Challenge** (אתגר / تحدي): Stretch concepts above current mastery. Higher difficulty. Bloom's: analyze-evaluate.
  - **Diagnostic** (אבחון / تشخيص): Uniform sampling across all concepts to assess overall mastery. Used for initial placement and periodic assessment.
  - **Exam Prep** (הכנה לבגרות / تحضير للبجروت): Simulate Bagrut exam conditions. Timed. Question types and difficulty distribution match real exam.
- [ ] System recommendation: based on days until Bagrut exam, current mastery profile, and last session type
  - > 8 weeks to exam → practice + review mix
  - 4-8 weeks → practice + challenge
  - < 4 weeks → exam prep + review
  - Just after a failed concept → review mode for that concept
- [ ] `POST /api/session/start` body: `{ student_id, subject, goal, language, duration_minutes? }`
- [ ] Goal can change mid-session (student taps "switch to review")
- [ ] Each goal mode adjusts the concept prioritizer weights and Bloom's ceiling differently

**Test:**
```csharp
[Fact]
public void ExamPrepMode_MatchesBagrutDistribution()
{
    var session = StartSession(goal: SessionGoal.ExamPrep);
    var questions = SelectQuestions(session, count: 20);
    var bloomDist = questions.GroupBy(q => q.BloomLevel).ToDictionary(g => g.Key, g => g.Count() / 20.0);
    // Bagrut distribution: ~15% recall, 25% comprehension, 35% application, 20% analysis, 5% synthesis
    Assert.InRange(bloomDist["application"], 0.25, 0.45);
}

[Fact]
public void ReviewMode_OnlyDueConcepts()
{
    var ctx = BuildContext(lastPracticed: new()
    {
        { "algebra", DateTime.Now.AddDays(-1) },    // practiced yesterday — not due
        { "derivatives", DateTime.Now.AddDays(-8) }  // 8 days ago — due for review
    });
    var session = StartSession(goal: SessionGoal.Review, ctx);
    var concept = _selector.SelectConcept(session);
    Assert.Equal("derivatives", concept.ConceptId);
}
```

---

### CNT-010.6: Delivery API — REST + SignalR

**Files to create/modify:**
- `src/Cena.Web/Controllers/QuestionDeliveryController.cs` — REST endpoints
- `src/Cena.Web/Hubs/SessionHub.cs` — SignalR real-time push (extends existing hub)

**Acceptance:**
- [ ] `GET /api/session/{id}/next-question` — returns next personalized question:
  ```json
  {
    "item_id": "sha256:a3f8c2...",
    "item_type": "function_investigation",
    "content": {
      "stem": "נתונה הפונקציה {math:g}",   // localized to student's language
      "math_expressions": { "g": "g(x) = 2x^3 + 3x^2 - 12x + 1" },
      "sub_parts": [
        { "id": "a", "stem": "מצא את נקודות הקיצון של הפונקציה.", "depends_on": null }
      ]
    },
    "interaction": {
      "type": "open_expression",
      "expected_format": "latex",
      "time_limit_seconds": null,
      "hints_available": 2
    },
    "metadata": {
      "concept": "derivatives_critical_points",
      "bloom_level": "apply",
      "difficulty": 0.55,
      "reason": "practicing_new_concept"    // why this question was selected
    }
  }
  ```
- [ ] `POST /api/session/{id}/answer` — submit answer, receive evaluation:
  ```json
  {
    "item_id": "sha256:a3f8c2...",
    "student_answer": "x = -2, x = 1",
    "time_spent_ms": 45000
  }
  → Response:
  {
    "correct": true,
    "score": 1.0,
    "feedback": { "he": "כל הכבוד! נקודות הקיצון נמצאות ב-x = -2 ו-x = 1." },
    "mastery_update": { "derivatives_critical_points": 0.65 },
    "next_action": "continue"    // or "break_suggested", "session_complete"
  }
  ```
- [ ] SignalR push: `SessionHub.SendQuestion(sessionId, question)` for real-time collaborative sessions
- [ ] Response time tracked per answer (used by FocusDegradationService)
- [ ] Hint system: `POST /api/session/{id}/hint` — returns progressive hint (first hint general, second more specific)
- [ ] Question reporting: `POST /api/session/{id}/report` — student reports issue with question. Routes to moderator queue.

**Test:**
```csharp
[Fact]
public async Task NextQuestion_ReturnsLocalizedContent()
{
    var session = await StartSession(language: "ar");
    var response = await _client.GetAsync($"/api/session/{session.Id}/next-question");
    var question = await response.Content.ReadFromJsonAsync<QuestionResponse>();
    Assert.Contains("أوجد", question.Content.Stem);  // Arabic phrasing
    Assert.Contains("g(x)", question.Content.MathExpressions.Values.First());  // Same math
}

[Fact]
public async Task Answer_UpdatesMastery()
{
    var before = await GetMastery("student-1", "derivatives");
    await SubmitCorrectAnswer("student-1", "derivatives_q1");
    var after = await GetMastery("student-1", "derivatives");
    Assert.True(after > before);
}

[Fact]
public async Task Answer_TracksFocusSignals()
{
    await SubmitAnswer("student-1", timeSpentMs: 3000);   // fast
    await SubmitAnswer("student-1", timeSpentMs: 15000);  // slow
    await SubmitAnswer("student-1", timeSpentMs: 25000);  // very slow
    var focus = await GetFocusState("student-1");
    Assert.Equal(FocusState.Declining, focus);
}
```

---

### CNT-010.7: Serving Quality Gates + Cool-Off

**Files to create/modify:**
- `src/Cena.Actors/Serving/ServingQualityGate.cs` — pre-serving validation
- `config/serving/quality_gates.yaml` — thresholds

**Acceptance:**
- [ ] Before any item is served to a student, verify:
  - `status == 'published'` (mandatory, no exceptions)
  - `quality_scores.math_correctness == 1.0` (mandatory)
  - `quality_scores.overall >= 0.80` (configurable)
  - Item has been published for >= 24 hours (cool-off period for spot-check)
  - Item has not been flagged by >= 2 student reports (auto-quarantine threshold)
- [ ] Cool-off period configurable per source: `bagrut_exam` = 0 hours (trusted), `recreated` = 24 hours, `batch_generated` = 12 hours
- [ ] Quarantined items removed from pool, sent back to moderation queue with student reports attached
- [ ] `GET /api/admin/serving/quarantined` — list quarantined items with report details

**Test:**
```csharp
[Fact]
public void QualityGate_BlocksUnpublished()
{
    var item = new Item { Status = "approved" };  // approved but not yet published
    Assert.False(_gate.CanServe(item));
}

[Fact]
public void QualityGate_BlocksDuringCoolOff()
{
    var item = new Item { Status = "published", PublishedAt = DateTime.UtcNow.AddHours(-12) };
    Assert.False(_gate.CanServe(item));  // 24h cool-off not elapsed
}

[Fact]
public void QualityGate_QuarantinesOnReports()
{
    _reports.Add("item-1", "math_error");
    _reports.Add("item-1", "math_error");  // 2nd report
    Assert.False(_gate.CanServe(GetItem("item-1")));
}
```
