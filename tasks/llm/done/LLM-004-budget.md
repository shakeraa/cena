# LLM-004: Per-Student Token Budget with Redis Backend

**Priority:** P1 — cost control, blocks production at scale
**Blocked by:** LLM-001 (FastAPI scaffold), DATA-007 (Redis key schema)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/cost-tracking.py` (DailyBudget, BudgetManager, CostAggregator), `contracts/data/redis-contracts.ts` (Keys.tokenBudget), `contracts/llm/routing-config.yaml` (section 4: cost_caps)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Every LLM call costs money. Without hard per-student caps, a single misbehaving client or a stagnation loop could burn the entire monthly budget. This task implements the Redis-backed token budget system defined in `cost-tracking.py`: 25,000 output tokens per student per day, midnight Israel-time reset, with alert thresholds at 80% (WARNING) and 150% (CRITICAL). The budget is checked *before* routing to any LLM provider, and consumption is recorded *after* a successful response. Redis keys follow the schema from `redis-contracts.ts`: `cena:budget:tokens:{studentId}:{dateUtc}`.

## Subtasks

### LLM-004.1: Redis-Backed DailyBudget Implementation
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/budget/redis_budget.py` — Redis-backed `DailyBudget` matching `cost-tracking.py` contract
- `src/llm-acl/src/cena_llm/budget/__init__.py` — package init

**Acceptance:**
- [ ] Redis key format: `cena:budget:tokens:{${studentId}}:${dateUtc}` (from `redis-contracts.ts` Keys.tokenBudget)
- [ ] Uses `INCR` for atomic token consumption (no race conditions)
- [ ] First INCR of the day sets TTL to seconds-until-midnight-Israel-time using `ttlUntilMidnightUtc()` logic from `redis-contracts.ts`
- [ ] Israel midnight calculation: UTC+2 (conservative, production uses `zoneinfo` for DST), matching `_next_midnight_israel()` from `cost-tracking.py`
- [ ] Default budget: 25,000 output tokens/day (from `routing-config.yaml` cost_caps.per_student.daily_output_token_limit)
- [ ] Daily cost soft limit: $0.70 (from `routing-config.yaml` cost_caps.per_student.daily_cost_limit_usd)
- [ ] Daily cost hard limit: $1.50 (from `routing-config.yaml` cost_caps.per_student.daily_cost_hard_limit_usd)
- [ ] `can_afford(student_id, estimated_output_tokens)` -> reads current counter from Redis, returns bool
- [ ] `consume(student_id, output_tokens, cost_usd)` -> atomically increments counter, checks thresholds
- [ ] Budget state serialized via `to_dict()` matching the `DailyBudget.to_dict()` contract: includes `student_id_hash` (SHA-256 truncated to 12 hex), `daily_output_limit`, `used_output_tokens`, `remaining_output_tokens`, `usage_percentage`, `used_cost_usd`, `is_exhausted`, `reset_at`
- [ ] Student ID hashed with SHA-256 truncated to 12 hex chars for logging (matching `_hash_student_id()` from `cost-tracking.py`)

**Test:**
```python
import pytest
import fakeredis.aioredis
from cena_llm.budget.redis_budget import RedisDailyBudget

@pytest.fixture
async def budget():
    redis = fakeredis.aioredis.FakeRedis()
    return RedisDailyBudget(redis=redis, daily_output_limit=25_000)

@pytest.mark.anyio
async def test_fresh_student_can_afford(budget):
    assert await budget.can_afford("student-001", estimated_output_tokens=500)

@pytest.mark.anyio
async def test_exhausted_student_cannot_afford(budget):
    await budget.consume("student-001", output_tokens=25_000, cost_usd=0.50)
    assert not await budget.can_afford("student-001", estimated_output_tokens=1)

@pytest.mark.anyio
async def test_consume_increments_atomically(budget):
    await budget.consume("student-001", output_tokens=1000, cost_usd=0.02)
    await budget.consume("student-001", output_tokens=2000, cost_usd=0.04)
    state = await budget.get_state("student-001")
    assert state["used_output_tokens"] == 3000

@pytest.mark.anyio
async def test_budget_resets_at_midnight(budget, monkeypatch):
    # Consume tokens
    await budget.consume("student-001", output_tokens=20_000, cost_usd=0.40)
    # Simulate midnight by clearing the Redis key
    await budget._redis.delete(budget._key("student-001"))
    assert await budget.can_afford("student-001", estimated_output_tokens=25_000)

@pytest.mark.anyio
async def test_student_id_hashed_in_state(budget):
    await budget.consume("student-001", output_tokens=100, cost_usd=0.01)
    state = await budget.get_state("student-001")
    assert state["student_id_hash"] != "student-001"
    assert len(state["student_id_hash"]) == 12  # SHA-256 truncated to 12 hex
```

**Edge cases:**
- Redis unavailable -> budget check fails open (allow request) but logs CRITICAL alert
- Race condition: two concurrent requests for same student both check can_afford -> INCR is atomic, second request may slightly exceed budget (acceptable: soft cap, not hard wall)
- Student with custom budget override -> stored in separate Redis key, checked first
- Clock skew between ACL instances -> TTL is computed per-instance, drift < 1 minute acceptable

---

### LLM-004.2: Alert System & Cost Spike Detection
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/budget/alerts.py` — alert thresholds, cost spike detection matching `CostAggregator.check_cost_spike()` from `cost-tracking.py`

**Acceptance:**
- [ ] Alert thresholds from `routing-config.yaml` section 4:
  - `daily_budget_warning_pct: 80` — WARNING when student uses 80% of daily budget
  - `daily_budget_critical_pct: 150` — CRITICAL when student at 150% (overage)
  - `cost_spike_multiplier: 2.0` — WARNING if today's cost > 2x rolling 7-day average
- [ ] Alert severities match `AlertSeverity` enum from `cost-tracking.py`: `info`, `warning`, `critical`
- [ ] Alert callback protocol matches `AlertCallback` from `cost-tracking.py`: `(severity, message, context) -> None`
- [ ] Cost spike detection implements `CostAggregator.check_cost_spike()` logic:
  - Compare today's cost to rolling 7-day average
  - If today > `spike_multiplier * avg_7d` -> return `CostSpikeAlert` (from `cost-tracking.py`)
  - Alert context includes: `student_id_hash`, `today_cost`, `rolling_avg`, `threshold`
- [ ] Monthly projection implements `CostAggregator.project_monthly_cost()`:
  - Trailing 7-day average extrapolated to 30 days
  - Confidence: "high" (7+ days), "medium" (3-6 days), "low" (<3 days)
  - Returns `MonthlyProjection` dataclass (from `cost-tracking.py`)
- [ ] Alerts emitted as structured log AND optional webhook callback
- [ ] Metric `llm_budget_usage_pct` gauge updated on every consume

**Test:**
```python
import pytest
from datetime import datetime, timezone, timedelta
from cena_llm.budget.alerts import CostAlertManager, AlertSeverity

@pytest.fixture
def alert_manager():
    alerts_received = []
    def callback(severity, message, context):
        alerts_received.append((severity, message, context))
    manager = CostAlertManager(alert_callback=callback)
    manager._alerts = alerts_received
    return manager

def test_warning_at_80_percent(alert_manager):
    alert_manager.check_thresholds(
        student_id="s1", used_output_tokens=20_000,
        daily_limit=25_000, used_cost_usd=0.40,
    )
    assert any(a[0] == AlertSeverity.WARNING for a in alert_manager._alerts)

def test_critical_at_150_percent(alert_manager):
    alert_manager.check_thresholds(
        student_id="s1", used_output_tokens=37_500,
        daily_limit=25_000, used_cost_usd=0.90,
    )
    assert any(a[0] == AlertSeverity.CRITICAL for a in alert_manager._alerts)

def test_no_alert_below_80_percent(alert_manager):
    alert_manager.check_thresholds(
        student_id="s1", used_output_tokens=15_000,
        daily_limit=25_000, used_cost_usd=0.30,
    )
    assert len(alert_manager._alerts) == 0

def test_cost_spike_detected(alert_manager):
    # Simulate 7-day history: avg $0.20/day
    history = [(datetime.now(timezone.utc) - timedelta(days=i), 0.20) for i in range(1, 8)]
    spike = alert_manager.check_cost_spike(
        student_id="s1", today_cost_usd=0.50,
        daily_history=history, spike_multiplier=2.0,
    )
    assert spike is not None
    assert spike.today_cost_usd == 0.50
    assert spike.rolling_avg_daily_usd == pytest.approx(0.20, rel=0.01)

def test_no_spike_when_cost_normal(alert_manager):
    history = [(datetime.now(timezone.utc) - timedelta(days=i), 0.30) for i in range(1, 8)]
    spike = alert_manager.check_cost_spike(
        student_id="s1", today_cost_usd=0.35,
        daily_history=history, spike_multiplier=2.0,
    )
    assert spike is None
```

**Edge cases:**
- New student with no history -> no spike detection (no baseline), only absolute threshold alerts
- Student spends $0 for several days then $0.50 -> spike detected if avg > 0
- Alert callback raises exception -> catch, log ERROR, do not block the LLM request

---

### LLM-004.3: Budget Middleware & API Endpoint
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/budget/middleware.py` — FastAPI middleware: check budget before LLM call, record after
- `src/llm-acl/src/cena_llm/routes/budget.py` (modify) — budget query endpoints

**Acceptance:**
- [ ] Budget middleware wraps all 7 `/v1/*` POST endpoints
- [ ] Before LLM call: `can_afford(student_id, max_output_tokens)` checked
  - If exhausted -> return 429 with JSON: `{"error": "budget_exhausted", "student_id_hash": "...", "used": N, "limit": N, "reset_at": "ISO8601"}`
  - Matches `BudgetExhaustedError` from `acl-interfaces.py`
- [ ] After successful LLM call: `consume(student_id, actual_output_tokens, actual_cost_usd)` recorded
- [ ] Cost computed using `ModelPricing.calculate_cost()` from `cost-tracking.py`:
  - `kimi-k2-turbo`: $0.40/MTok input, $2.00/MTok output, $0.10/MTok cached
  - `kimi-k2-0905-preview`: $0.40/MTok input, $2.00/MTok output, $0.10/MTok cached
  - `kimi-k2.5`: $0.45/MTok input, $2.20/MTok output, $0.12/MTok cached
  - `claude-sonnet-4-6-20260215`: $3.00/MTok input, $15.00/MTok output, $0.30/MTok cached
  - `claude-opus-4-6-20260215`: $5.00/MTok input, $25.00/MTok output, $0.50/MTok cached
- [ ] Budget query endpoints:
  - `GET /v1/budget/{student_id}` -> current budget state (to_dict format)
  - `GET /v1/budget/exhausted` -> list of exhausted student_id_hashes
- [ ] Budget check adds < 2ms latency (single Redis RTT)

**Test:**
```python
import pytest
from httpx import AsyncClient, ASGITransport
from cena_llm.main import app
from unittest.mock import AsyncMock, patch

@pytest.mark.anyio
async def test_exhausted_student_gets_429():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        with patch("cena_llm.budget.middleware.budget_manager") as mock_mgr:
            mock_mgr.can_afford = AsyncMock(return_value=False)
            mock_mgr.get_state = AsyncMock(return_value={
                "used_output_tokens": 25_000,
                "daily_output_limit": 25_000,
                "reset_at": "2026-03-27T22:00:00Z",
            })
            payload = {
                "student": {"student_id": "s1", "grade_level": 10},
                "concept": {"concept_id": "c1", "concept_name_he": "t", "concept_name_en": "t"},
                "current_mastery": 0.5,
            }
            response = await client.post("/v1/socratic-question", json=payload)
            assert response.status_code == 429
            body = response.json()
            assert body["error"] == "budget_exhausted"

@pytest.mark.anyio
async def test_budget_endpoint_returns_state():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        with patch("cena_llm.routes.budget.budget_manager") as mock_mgr:
            mock_mgr.get_state = AsyncMock(return_value={
                "student_id_hash": "abc123def456",
                "daily_output_limit": 25_000,
                "used_output_tokens": 5_000,
                "remaining_output_tokens": 20_000,
                "usage_percentage": 0.20,
                "is_exhausted": False,
                "reset_at": "2026-03-27T22:00:00Z",
            })
            response = await client.get("/v1/budget/student-001")
            assert response.status_code == 200
            body = response.json()
            assert body["remaining_output_tokens"] == 20_000
```

**Edge cases:**
- Redis down during budget check -> fail open (allow request), log CRITICAL, alert ops
- Student ID missing from request (some endpoints don't have it in top-level) -> extract from nested `student` object
- Budget consumed but LLM call fails -> tokens already deducted, do not refund (conservative: prevents abuse via intentional failures)
- Content filter requests (source="llm_output") -> no student_id, skip budget check

---

## Integration Test (all subtasks combined)

```python
import pytest
import fakeredis.aioredis
from cena_llm.budget.redis_budget import RedisDailyBudget
from cena_llm.budget.alerts import CostAlertManager, AlertSeverity

@pytest.mark.anyio
async def test_full_budget_lifecycle():
    redis = fakeredis.aioredis.FakeRedis()
    alerts = []

    budget = RedisDailyBudget(
        redis=redis, daily_output_limit=25_000,
        alert_callback=lambda s, m, c: alerts.append((s, m, c)),
    )

    # 1. Fresh student has full budget
    assert await budget.can_afford("student-001", 500)

    # 2. Consume tokens incrementally
    for _ in range(10):
        await budget.consume("student-001", output_tokens=2_000, cost_usd=0.03)

    # 3. 80% threshold triggers WARNING
    assert any(a[0] == AlertSeverity.WARNING for a in alerts)

    # 4. At 20K used, remaining is 5K
    state = await budget.get_state("student-001")
    assert state["used_output_tokens"] == 20_000
    assert state["remaining_output_tokens"] == 5_000

    # 5. Consume remaining
    await budget.consume("student-001", output_tokens=5_000, cost_usd=0.10)
    assert not await budget.can_afford("student-001", 1)

    # 6. State shows exhausted
    state = await budget.get_state("student-001")
    assert state["is_exhausted"] is True

    # 7. Budget reset (simulate midnight)
    await redis.delete(budget._key("student-001"))
    assert await budget.can_afford("student-001", 25_000)
```

## Rollback Criteria
If budget system causes availability issues:
- Disable budget middleware (set env `BUDGET_ENFORCEMENT=disabled`)
- All requests pass through, cost is tracked but not enforced
- Monitor daily spend manually via `CostAggregator` dashboard
- Acceptable temporary state: cost tracking without enforcement

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `pytest tests/ -k "budget"` -> 0 failures
- [ ] Redis key format matches `redis-contracts.ts` exactly
- [ ] Budget resets at midnight Israel time (verified with time-mocked test)
- [ ] Alert thresholds match `routing-config.yaml` section 4 exactly
- [ ] Exhausted students get 429 with reset_at timestamp
- [ ] Budget check latency < 2ms (p99)
- [ ] PR reviewed by architect (you)
