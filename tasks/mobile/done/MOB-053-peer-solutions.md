# MOB-053: Peer Solution Replays

**Priority:** P3.5 — High
**Phase:** 3 — Social Layer (Months 5-8)
**Source:** social-learning-research.md Section 1
**Blocked by:** MOB-044 (Class Social Feed)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Bandura's observational learning: watching peers solve problems improves self-efficacy. After a student attempts a question, show how classmates approached it (anonymous, opt-in).

## Subtasks

### MOB-053.1: Solution Collection
- [ ] After correct answer with high quality (response time > 10s, first attempt): offer "Share your approach?"
- [ ] Student's methodology + answer path stored as replay data
- [ ] Anonymous by default ("A classmate solved it this way")

### MOB-053.2: Solution Display
- [ ] "See how others solved it" button on feedback screen (Level 3 disclosure)
- [ ] Show 2-3 peer solutions sorted by methodology diversity
- [ ] Display methodology used, time taken, approach steps
- [ ] Voting: "Was this helpful?" (simple yes/no)

### MOB-053.3: Quality Gate
- [ ] Only show solutions with correct answers
- [ ] Only from students with P(known) > 0.70 on that concept
- [ ] For under-16: teacher approval required before solutions become visible
- [ ] AI pre-filter for text content

### MOB-053.4: Events
- [ ] `PeerSolutionShared_V1`: `{StudentId, ConceptId, QuestionId, MethodologyId}`
- [ ] `PeerSolutionViewed_V1`: `{ViewerStudentId, ConceptId, SolutionId}`

**Definition of Done:**
- Peer solutions shown after student's own attempt
- Anonymous, quality-gated, teacher-approved for under-16
- Diverse methodology representation
- Opt-in sharing with voting

**Test:**
```csharp
[Fact]
public void PeerSolution_RequiresMinimumMastery()
{
    var gate = new PeerSolutionQualityGate();
    var lowMastery = new SolutionCandidate(Mastery: 0.40, Correct: true);
    Assert.False(gate.Qualifies(lowMastery));

    var highMastery = new SolutionCandidate(Mastery: 0.80, Correct: true);
    Assert.True(gate.Qualifies(highMastery));
}
```
