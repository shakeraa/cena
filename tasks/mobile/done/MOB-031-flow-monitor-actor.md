# MOB-031: FlowMonitorActor — Flow-Aware Dynamic Difficulty

**Priority:** P1.2 — Critical
**Phase:** 1 — Foundation (Months 1-3)
**Source:** flow-state-design-research.md Sections 2, 12
**Blocked by:** MOB-030 (Session Flow Arc), ACT-* (Actor system)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Propose a `FlowMonitorActor` as a child of `LearningSessionActor` that computes a real-time flow score and adjusts difficulty targets accordingly. Integrates with existing `FocusDegradationService`, `CognitiveLoadService`, and `DisengagementClassifier`.

## Subtasks

### MOB-031.1: FlowMonitorActor (Backend)
- [ ] Child actor of `LearningSessionActor`, spawned on session start
- [ ] Flow score formula: `0.30 * focus + 0.25 * challengeSkillBalance + 0.20 * consistency + 0.15 * inverseFatigue + 0.10 * voluntaryEngagement`
- [ ] Flow states: `Warming`, `Approaching`, `InFlow`, `Disrupted`, `Fatigued`
- [ ] Emits `FlowEpisodeStarted_V1` when flow score >= 0.70 for 3+ consecutive questions
- [ ] Emits `FlowEpisodeEnded_V1` with duration and trigger

### MOB-031.2: Dynamic Difficulty Targeting
- [ ] `InFlow` → target P(correct) = 0.55-0.65 (stretch zone)
- [ ] `Approaching` → target P(correct) = 0.60-0.70 (sweet spot)
- [ ] `Disrupted` or `Fatigued` → target P(correct) = 0.75-0.85 (recovery)
- [ ] Override `DifficultyGap.cs` classification with flow-aware targets
- [ ] Microbreak suppression when `InFlow` (coordinate with `MicrobreakScheduler`)

### MOB-031.3: Stagnation Integration
- [ ] Absence of flow episodes (0 in last 5 sessions) is a stagnation signal
- [ ] Forward flow metrics to `StagnationDetectorActor`
- [ ] Weight: 0.15 in stagnation composite score

### MOB-031.4: Mobile-Side Flow Indicators
- [ ] Ambient background color temperature shifts by flow state (subtle)
- [ ] No explicit "You're in flow!" notifications (would break flow)
- [ ] Session summary shows flow time percentage

### MOB-031.5: Events & NATS
- [ ] `FlowEpisodeStarted_V1`: `{SessionId, StudentId, FlowScore, QuestionIndex}`
- [ ] `FlowEpisodeEnded_V1`: `{SessionId, DurationSeconds, EndTrigger, QuestionsInFlow}`
- [ ] NATS: `session.{sessionId}.flow.started`, `session.{sessionId}.flow.ended`

**Definition of Done:**
- FlowMonitorActor computes flow score per question and transitions between states
- Difficulty targets adapt to flow state in real-time
- Flow episodes logged as domain events with duration tracking

**Test:**
```csharp
[Fact]
public async Task FlowMonitor_DetectsFlowState_WhenScoreAboveThreshold()
{
    var actor = SpawnFlowMonitor(focusLevel: 0.85, accuracy: 0.65, consistency: 0.90);
    await actor.ProcessQuestion(correct: true, responseTimeMs: 12000);
    await actor.ProcessQuestion(correct: true, responseTimeMs: 11000);
    await actor.ProcessQuestion(correct: false, responseTimeMs: 14000);
    Assert.Equal(FlowState.InFlow, actor.CurrentState);
    Assert.Single(actor.Events.OfType<FlowEpisodeStarted_V1>());
}
```
