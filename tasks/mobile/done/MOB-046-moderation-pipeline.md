# MOB-046: Moderation Pipeline (AI Pre-Filter + Teacher Review)

**Priority:** P3.10 — Critical
**Phase:** 3 — Social Layer (Months 5-8)
**Source:** social-learning-research.md Section 13, ethical-persuasion-research.md Section 8
**Blocked by:** MOB-044 (Class Social Feed), MOB-045 (Age Safety)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-046.1: AI Pre-Filter (Tier 1)
- [ ] All UGC scanned before display (< 500ms latency)
- [ ] Use existing `LlmGatewayActor` on Haiku tier for content classification
- [ ] Categories: safe, review-needed, blocked
- [ ] Blocked content: profanity, bullying, PII, inappropriate
- [ ] Auto-block with no notification to poster (prevent gaming the system)

### MOB-046.2: Community Reporting (Tier 2)
- [ ] Report button on all social content
- [ ] 3 reports from different students → auto-hide + escalate to teacher
- [ ] Reporter receives "Thank you" acknowledgment (no details)

### MOB-046.3: Teacher/Admin Review (Tier 3)
- [ ] Teacher moderation queue in admin dashboard
- [ ] 24-hour SLA for review
- [ ] Actions: approve, remove, warn student (private), escalate to admin
- [ ] Moderation audit log (required for COPPA/FERPA)

### MOB-046.4: Rate Limiting
- [ ] Under-13: max 5 social posts/day
- [ ] 13-15: max 15 social posts/day
- [ ] 16+: max 30 social posts/day
- [ ] Escalation rate KPI target: < 5% of UGC

**Definition of Done:**
- 3-tier moderation: AI → community → teacher
- All UGC scanned in < 500ms before display
- Auto-hide on 3 reports
- Teacher moderation queue with 24h SLA

**Test:**
```csharp
[Fact]
public async Task ModerationPipeline_BlocksProfanity()
{
    var pipeline = new ModerationPipeline(llmGateway);
    var result = await pipeline.Evaluate("you are stupid and I hate you");
    Assert.Equal(ModerationResult.Blocked, result.Decision);
}

[Fact]
public async Task ModerationPipeline_EscalatesAfter3Reports()
{
    var content = new SocialContent(id: "post-1", text: "borderline content");
    await pipeline.Report(content, reporterId: "student-1");
    await pipeline.Report(content, reporterId: "student-2");
    await pipeline.Report(content, reporterId: "student-3");
    Assert.True(content.IsHidden);
    Assert.True(content.IsEscalatedToTeacher);
}
```
