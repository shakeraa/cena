# MOB-045: Age-Tiered Social Safety Matrix

**Priority:** P3.9 — Critical
**Phase:** 3 — Social Layer (Months 5-8)
**Source:** social-learning-research.md Section 8, ethical-persuasion-research.md Sections 4-5
**Blocked by:** MOB-044 (Class Social Feed)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-045.1: Age Tier Definitions
- [ ] **Tier 1 (6-9):** No social features. Solo learning only.
- [ ] **Tier 2 (10-12):** Class aggregate stats only. Pre-set reactions. No peer comparison.
- [ ] **Tier 3 (13-15):** Optional lateral comparison. Study groups. Moderated text.
- [ ] **Tier 4 (16-18+):** Full social suite. Opt-in leaderboards. Accountability partners.

### MOB-045.2: ComplianceActor (Backend)
- [ ] One per school — manages age gating, consent, data retention
- [ ] Age verification from school enrollment data (not self-reported)
- [ ] Consent state machine: `Pending → ParentalConsentGranted → Active`
- [ ] Under-13: COPPA parental consent flow required before any social feature
- [ ] Data retention: auto-delete after school year unless consent renewed

### MOB-045.3: Feature Gate Middleware
- [ ] Every social API endpoint checks age tier before allowing action
- [ ] Feature gate evaluated at actor level (not just UI hiding)
- [ ] Audit log: all age-gated access attempts logged

### MOB-045.4: Parent Consent Flow
- [ ] Parent receives email/SMS link to approve child's social features
- [ ] Consent is granular: feed (yes), messaging (no), comparison (no)
- [ ] Parent dashboard shows exactly which social features are active
- [ ] Revocable at any time with immediate effect

### MOB-045.5: Anti-Bullying By Design
- [ ] No negative social signals (no dislike, no visible fail states)
- [ ] All UGC moderated before display (AI + teacher queue)
- [ ] Block/report with immediate hide (content hidden, not user notified)
- [ ] Rate limiting: max 5 social actions/day for under-13

**Definition of Done:**
- 4 age tiers with progressively unlocked social features
- ComplianceActor enforces gating at actor level, not just UI
- COPPA parental consent flow for under-13
- Zero negative social signals possible in any tier

**Test:**
```csharp
[Fact]
public void ComplianceActor_BlocksSocialFeatures_ForUnder13WithoutConsent()
{
    var actor = SpawnComplianceActor(studentAge: 11, consentStatus: ConsentStatus.Pending);
    var result = actor.CanAccess(SocialFeature.ClassFeed);
    Assert.False(result.Allowed);
    Assert.Equal("COPPA_CONSENT_REQUIRED", result.Reason);
}
```
