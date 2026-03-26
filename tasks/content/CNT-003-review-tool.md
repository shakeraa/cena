# CNT-003: Expert Review Admin UI — Accept/Edit/Reject, Rejection Rate, Escalation

**Priority:** P1 — blocks content publication
**Blocked by:** CNT-002 (Question Generation)
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/graphql-schema.graphql` (admin mutations)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Generated questions must be reviewed by subject matter experts before entering the production question pool. The review UI allows experts to accept, edit (with diff tracking), or reject questions. Rejection rates per concept are tracked to identify systematic generation issues. High rejection rates trigger escalation to curriculum lead.

## Subtasks

### CNT-003.1: Review Queue API

**Files to create/modify:**
- `src/Cena.Web/GraphQL/Mutations/ContentReviewMutations.cs`
- `src/Cena.Data/Content/ReviewWorkflow.cs`

**Acceptance:**
- [ ] `GET /admin/review/queue` — paginated list of pending questions, filterable by concept/topic/bloom
- [ ] `POST /admin/review/accept` — mark question as approved, set `reviewStatus: "approved"`
- [ ] `POST /admin/review/edit` — update question text, store diff, mark as `"approved_with_edits"`
- [ ] `POST /admin/review/reject` — reject with reason, set `reviewStatus: "rejected"`, reason stored
- [ ] Reviewer identity tracked (Firebase UID with ADMIN role)
- [ ] Batch review: accept/reject multiple questions in single request

**Test:**
```csharp
[Fact]
public async Task ReviewWorkflow_AcceptSetsStatus()
{
    var questionId = await CreatePendingQuestion();
    await _reviewService.Accept(questionId, reviewerUid: "admin-1");
    var q = await _questionStore.GetById(questionId);
    Assert.Equal("approved", q.ReviewStatus);
}
```

---

### CNT-003.2: Rejection Rate Tracking + Escalation

**Files to create/modify:**
- `src/Cena.Data/Content/RejectionTracker.cs`
- `config/content/escalation-thresholds.yaml`

**Acceptance:**
- [ ] Rejection rate computed per concept: `rejected / (approved + rejected)` over rolling 30-day window
- [ ] Threshold: rejection rate > 40% for a concept -> escalate to curriculum lead
- [ ] Escalation: NATS event `cena.content.events.HighRejectionRate` + Slack notification
- [ ] Dashboard: per-concept rejection rate, top-10 problematic concepts, trend over time
- [ ] Rejected question reasons aggregated: top rejection reasons per concept

**Test:**
```csharp
[Fact]
public async Task RejectionTracker_EscalatesHighRate()
{
    // Reject 5 of 10 questions for a concept
    for (int i = 0; i < 5; i++) await _reviewService.Accept(CreateQuestion("concept-1"), "admin");
    for (int i = 0; i < 5; i++) await _reviewService.Reject(CreateQuestion("concept-1"), "admin", "poor distractor");
    var escalations = await GetEscalations("concept-1");
    Assert.Single(escalations);
}
```

---

### CNT-003.3: Review UI (Admin Web)

**Files to create/modify:**
- `src/web-admin/pages/review/ReviewQueue.tsx`
- `src/web-admin/components/QuestionPreview.tsx`

**Acceptance:**
- [ ] Question preview with Hebrew and Arabic side-by-side
- [ ] Math rendering (KaTeX/MathJax) for mathematical expressions
- [ ] Inline editing with diff highlight
- [ ] Keyboard shortcuts: A (accept), R (reject), E (edit), N (next)
- [ ] Batch operations via checkbox selection

**Test:**
```typescript
test('ReviewQueue renders pending questions', async () => {
  renderWithProviders(<ReviewQueue />);
  await waitFor(() => expect(screen.getByText('Pending Review')).toBeInTheDocument());
  expect(screen.getAllByRole('row')).toHaveLength(10); // Default page size
});
```

---

## Rollback Criteria
- If review UI is unavailable, experts can review via exported CSV and bulk import approved list

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] Review queue functional with accept/edit/reject workflow
- [ ] Rejection rate tracking with escalation alerts
- [ ] PR reviewed by architect
