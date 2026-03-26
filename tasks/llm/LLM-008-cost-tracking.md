# LLM-008: Cost Aggregation, Spike Detection, Monthly Projection

**Priority:** P1 — blocks cost control
**Blocked by:** LLM-001 (ACL scaffold), INF-004 (Redis)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/cost-tracking.py` (ModelPricing, DailyBudget, CostAggregation)

---

## Context

Every LLM invocation records cost. Per-student daily budget: 25K output tokens (hard cutoff). Cost aggregation by student/model/task with spike detection. Monthly projection for budget planning.

## Subtasks

### LLM-008.1: Per-Invocation Cost Recording
- [ ] After each LLM call: compute cost using `ModelPricing.calculate_cost()`
- [ ] Store `CostRecord` in PostgreSQL (append-only)
- [ ] Increment Redis daily budget counter: `INCR cena:budget:tokens:{studentId}:{date}`
- [ ] Budget exhausted -> `BudgetExhaustedError` raised, student sees "Study energy depleted"

### LLM-008.2: Spike Detection
- [ ] Compute rolling 1-hour cost per student
- [ ] Spike: cost > 3x median of last 7 days for same hour -> alert
- [ ] Spike: total platform cost > $500/day -> CRITICAL alert + emergency kill switch
- [ ] Kill switch: disable all LLM calls except content filtering

### LLM-008.3: Monthly Projection + Reporting
- [ ] Daily cost aggregation by model, task type, student cohort
- [ ] Monthly projection: linear extrapolation from last 7 days
- [ ] Report: total cost, cost per student, cost per model, cache savings
- [ ] Dashboard: real-time cost tracker in Grafana

**Test:**
```python
def test_budget_enforcement():
    budget = DailyBudget(student_id="stu-1", daily_limit=25000)
    budget.record_usage(output_tokens=24000)
    assert budget.can_afford(estimated_output_tokens=2000) is False

def test_spike_detection():
    detector = SpikeDetector()
    detector.record_cost(student_id="stu-1", cost_usd=0.50)  # Normal
    detector.record_cost(student_id="stu-1", cost_usd=5.00)  # 10x spike
    assert detector.is_spike("stu-1")
```

---

## Definition of Done
- [ ] Cost tracking for every LLM call
- [ ] Budget enforcement with hard cutoff
- [ ] Spike detection with alerts
- [ ] PR reviewed by architect
