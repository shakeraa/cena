# LLM-002: Task-to-Model Router with Fallback Chains & Circuit Breaker

**Priority:** P0 — blocks all LLM invocations
**Blocked by:** LLM-001 (FastAPI scaffold)
**Estimated effort:** 3 days
**Contract:** `contracts/llm/acl-interfaces.py` (LLMRouter ABC), `contracts/llm/routing-config.yaml`

---

## Context
The Router is the decision engine that maps every `TaskType` to the optimal `ModelConfig`, manages fallback chains when a model is unavailable, and integrates a per-model circuit breaker. This is the core of the cost-optimization strategy: Kimi K2 handles cheap classification at $0.40/MTok input while Opus handles high-stakes methodology switches at $5.00/MTok input. The router reads `routing-config.yaml` at startup and exposes the `LLMRouter` interface defined in `acl-interfaces.py`.

## Subtasks

### LLM-002.1: Routing Table Loader & ModelConfig Resolution
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/router/config_loader.py` — YAML loader, `ModelConfig` factory
- `src/llm-acl/src/cena_llm/router/__init__.py` — package init

**Acceptance:**
- [ ] Parses `routing-config.yaml` sections 1 (models) and 2 (task_routing) at startup
- [ ] Resolves env var placeholders (`${KIMI_API_BASE_URL}`, etc.) from environment
- [ ] Builds `ModelConfig` for each of the 7 models:
  - `kimi-k2-turbo`: tier=`kimi_fast`, input=$0.40/MTok, output=$2.00/MTok, trusted=false
  - `kimi-k2-0905-preview`: tier=`kimi_standard`, input=$0.40/MTok, output=$2.00/MTok, trusted=false
  - `kimi-k2.5`: tier=`kimi_advanced`, input=$0.45/MTok, output=$2.20/MTok, trusted=false
  - `claude-sonnet-4-6-20260215`: tier=`sonnet`, input=$3.00/MTok, output=$15.00/MTok, trusted=true
  - `claude-sonnet-4-5-20260101`: tier=`sonnet`, input=$3.00/MTok, output=$15.00/MTok, trusted=true
  - `claude-haiku-4-5-20260101`: tier=`sonnet` (degraded), input=$1.00/MTok, output=$5.00/MTok, trusted=true
  - `claude-opus-4-6-20260215`: tier=`opus`, input=$5.00/MTok, output=$25.00/MTok, trusted=true
- [ ] Maps all 10 `TaskType` values to their primary model + fallback chain as defined in `routing-config.yaml` section 2
- [ ] Temperature and max_tokens per-task from YAML: e.g., `socratic_question` temperature=0.4, max_tokens=500; `error_classification` temperature=0.0, max_tokens=200
- [ ] Config reload without restart (SIGHUP or `/admin/reload-config` endpoint)

**Test:**
```python
import os
import pytest
from cena_llm.router.config_loader import load_routing_config

@pytest.fixture
def env_vars(monkeypatch):
    monkeypatch.setenv("KIMI_API_BASE_URL", "https://api.moonshot.test/v1")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-test")
    monkeypatch.setenv("PII_ANONYMIZATION_SALT", "test-salt")

def test_loads_all_seven_models(env_vars):
    config = load_routing_config("contracts/llm/routing-config.yaml")
    assert len(config.models) == 7
    assert "kimi-k2-turbo" in config.models
    assert "claude-opus-4-6-20260215" in config.models

def test_task_routing_maps_all_ten_tasks(env_vars):
    config = load_routing_config("contracts/llm/routing-config.yaml")
    from cena_llm.models import TaskType
    for task in TaskType:
        assert task.value in config.task_routes, f"Missing route for {task}"

def test_kimi_models_marked_untrusted(env_vars):
    config = load_routing_config("contracts/llm/routing-config.yaml")
    for model_key in ["kimi-k2-turbo", "kimi-k2-0905-preview", "kimi-k2.5"]:
        assert config.models[model_key].is_trusted_provider is False

def test_methodology_switch_routes_to_opus(env_vars):
    config = load_routing_config("contracts/llm/routing-config.yaml")
    route = config.task_routes["methodology_switch"]
    assert route.primary_model_id == "claude-opus-4-6-20260215"
    assert route.temperature == 0.3
    assert route.max_tokens == 800

def test_error_classification_routes_to_kimi(env_vars):
    config = load_routing_config("contracts/llm/routing-config.yaml")
    route = config.task_routes["error_classification"]
    assert route.primary_model_id == "kimi-k2-0905-preview"
    assert route.temperature == 0.0
```

---

### LLM-002.2: Fallback Chain Resolver
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/router/fallback.py` — fallback chain logic
- `src/llm-acl/src/cena_llm/router/router.py` — `CenaLLMRouter` implementing `LLMRouter` ABC

**Acceptance:**
- [ ] `route(task_type, student)` returns the first available `ModelConfig` in the chain
- [ ] `get_fallback_chain(task_type)` returns the full ordered list from `routing-config.yaml` section 2:
  - `socratic_question`: Sonnet 4.6 -> Sonnet 4.5 -> Haiku 4.5
  - `error_classification`: Kimi K2 0905 -> Kimi K2.5 -> Haiku 4.5 -> Sonnet 4.6
  - `methodology_switch`: Opus 4.6 -> Sonnet 4.6 -> Sonnet 4.5
  - `content_filter`: Kimi K2 Turbo -> Kimi K2 0905 -> Haiku 4.5
  - `diagram_generation`: Kimi K2.5 -> Kimi K2 0905 -> Sonnet 4.6
  - `feynman_explanation`: Sonnet 4.6 -> Sonnet 4.5 -> Haiku 4.5
- [ ] When primary model's circuit is open, router skips to next in chain
- [ ] When ALL models in chain have open circuits, raises `CircuitOpenError` (from `acl-interfaces.py`)
- [ ] Fallback events emit metric `llm_fallback_total` with labels: `task_type`, `from_model`, `to_model`, `reason`
- [ ] Logs every fallback at WARNING level with request_id

**Test:**
```python
import pytest
from cena_llm.router.router import CenaLLMRouter
from cena_llm.models import TaskType, ModelTier

@pytest.fixture
def router(env_vars):
    return CenaLLMRouter.from_config("contracts/llm/routing-config.yaml")

def test_route_returns_primary_when_healthy(router):
    config = router.route(TaskType.SOCRATIC_QUESTION)
    assert config.model_id == "claude-sonnet-4-6-20260215"

def test_route_falls_back_when_primary_open(router):
    # Trip the primary circuit
    for _ in range(5):
        router.report_failure(ModelTier.SONNET, 500)
    config = router.route(TaskType.SOCRATIC_QUESTION)
    # Should fall to Sonnet 4.5 or Haiku
    assert config.model_id in ("claude-sonnet-4-5-20260101", "claude-haiku-4-5-20260101")

def test_all_circuits_open_raises(router):
    from cena_llm.models import CircuitOpenError
    # Open all circuits in the socratic chain
    for tier in [ModelTier.SONNET, ModelTier.SONNET, ModelTier.SONNET]:
        for _ in range(10):
            router.report_failure(tier, 500)
    with pytest.raises(CircuitOpenError):
        router.route(TaskType.SOCRATIC_QUESTION)

def test_fallback_chain_order(router):
    chain = router.get_fallback_chain(TaskType.ERROR_CLASSIFICATION)
    model_ids = [c.model_id for c in chain]
    assert model_ids[0] == "kimi-k2-0905-preview"
    assert model_ids[1] == "kimi-k2.5"
    assert model_ids[2] == "claude-haiku-4-5-20260101"
    assert model_ids[3] == "claude-sonnet-4-6-20260215"
```

**Edge cases:**
- Config YAML missing a task type -> startup validation fails with clear error
- Model referenced in fallback chain but not defined in models section -> startup error
- Env var not set for `api_base_url` -> startup error with var name in message

---

### LLM-002.3: Circuit Breaker
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/router/circuit_breaker.py` — per-model circuit breaker

**Acceptance:**
- [ ] Implements three states: `closed`, `open`, `half_open` (from `LLMRouter.get_circuit_state()`)
- [ ] Default thresholds from `routing-config.yaml` section 5:
  - `failure_threshold`: 5 consecutive failures
  - `success_threshold`: 2 successes in half-open
  - `open_duration_seconds`: 60
  - `half_open_max_requests`: 1
  - `monitored_status_codes`: [500, 502, 503, 429]
  - `exclude_from_failure_count`: [400, 401, 403, 404]
- [ ] Per-model overrides applied:
  - `kimi_k2_turbo`: failure_threshold=8, open_duration=30s
  - `claude_opus_4_6`: failure_threshold=3, open_duration=120s, success_threshold=3
- [ ] `report_failure(model_tier, error_code)` increments failure counter only for monitored codes
- [ ] 400/401/403/404 errors do NOT trip the breaker
- [ ] Circuit transitions emit metric `llm_circuit_breaker_state` (0=closed, 1=half_open, 2=open)
- [ ] Thread-safe: concurrent requests don't corrupt circuit state

**Test:**
```python
import pytest
import time
from cena_llm.router.circuit_breaker import CircuitBreaker

def test_circuit_opens_after_threshold():
    cb = CircuitBreaker(failure_threshold=5, open_duration_seconds=60)
    for _ in range(5):
        cb.record_failure(500)
    assert cb.state == "open"

def test_client_errors_dont_trip_breaker():
    cb = CircuitBreaker(failure_threshold=5, open_duration_seconds=60)
    for _ in range(10):
        cb.record_failure(400)
    assert cb.state == "closed"

def test_circuit_transitions_to_half_open(monkeypatch):
    cb = CircuitBreaker(failure_threshold=3, open_duration_seconds=1)
    for _ in range(3):
        cb.record_failure(500)
    assert cb.state == "open"
    time.sleep(1.1)
    assert cb.state == "half_open"

def test_half_open_recovers_on_success():
    cb = CircuitBreaker(failure_threshold=3, open_duration_seconds=0, success_threshold=2)
    for _ in range(3):
        cb.record_failure(500)
    # Manually transition to half_open
    cb._opened_at = 0  # Force expiry
    cb.record_success()
    cb.record_success()
    assert cb.state == "closed"

def test_opus_override_lower_threshold():
    cb = CircuitBreaker(failure_threshold=3, open_duration_seconds=120, success_threshold=3)
    for _ in range(3):
        cb.record_failure(503)
    assert cb.state == "open"

def test_429_trips_breaker():
    cb = CircuitBreaker(failure_threshold=5, open_duration_seconds=60)
    for _ in range(5):
        cb.record_failure(429)
    assert cb.state == "open"
```

**Edge cases:**
- Race condition: two goroutines both see half_open, both send test request -> only one should pass
- Clock skew in containers -> use monotonic clock for open_duration
- All circuits open simultaneously -> `CircuitOpenError` propagated to caller with list of broken tiers

---

### LLM-002.4: PII Stripper Middleware
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/router/pii_stripper.py` — implements `PIIStripper` ABC from `acl-interfaces.py`

**Acceptance:**
- [ ] Implements `strip()` and `restore()` from the `PIIStripper` interface
- [ ] Inspects Pydantic model field metadata for `json_schema_extra={"pii": True}` (the `PII` annotated type)
- [ ] Strips PII fields before routing to untrusted providers (Kimi: `is_trusted_provider=false`)
- [ ] Token format matches `routing-config.yaml` section 8: `ANON_{field}_{hash8}` where hash8 is SHA-256 of `salt + original_value` truncated to 8 hex chars
- [ ] Anonymization map is consistent within a session (same student_id -> same token)
- [ ] `restore()` replaces anonymized tokens in response text back to original values
- [ ] Does NOT strip PII for trusted providers (Anthropic)
- [ ] Anonymization mapping is NEVER logged in production (`log_anonymization_mapping: false`)

**Test:**
```python
import pytest
from cena_llm.router.pii_stripper import CenaPIIStripper
from cena_llm.models import ErrorClassificationRequest, ConceptInfo, AttemptData

def test_strip_removes_student_id():
    stripper = CenaPIIStripper(salt="test-salt")
    request = ErrorClassificationRequest(
        student_id="real-student-uuid-123",
        concept=ConceptInfo(
            concept_id="math-fractions",
            concept_name_he="שברים",
            concept_name_en="Fractions",
        ),
        attempt=AttemptData(
            question_id="q1", question_type="numeric",
            student_answer="42", expected_answer="7",
            response_time_ms=5000,
        ),
    )
    stripped = stripper.strip(request)
    assert stripped.student_id != "real-student-uuid-123"
    assert stripped.student_id.startswith("ANON_student_id_")
    assert len(stripped.student_id) == len("ANON_student_id_") + 8

def test_strip_preserves_non_pii_fields():
    stripper = CenaPIIStripper(salt="test-salt")
    request = ErrorClassificationRequest(
        student_id="real-id",
        concept=ConceptInfo(
            concept_id="c1", concept_name_he="t", concept_name_en="t",
        ),
        attempt=AttemptData(
            question_id="q1", question_type="numeric",
            student_answer="42", expected_answer="7",
            response_time_ms=5000,
        ),
    )
    stripped = stripper.strip(request)
    assert stripped.concept.concept_id == "c1"
    assert stripped.attempt.student_answer == "42"

def test_consistent_anonymization_within_session():
    stripper = CenaPIIStripper(salt="test-salt")
    token1 = stripper._anonymize("student_id", "same-student")
    token2 = stripper._anonymize("student_id", "same-student")
    assert token1 == token2

def test_no_strip_for_trusted_provider():
    stripper = CenaPIIStripper(salt="test-salt")
    # When routing to Anthropic (trusted), strip should return original
    request = ErrorClassificationRequest(
        student_id="real-id",
        concept=ConceptInfo(concept_id="c1", concept_name_he="t", concept_name_en="t"),
        attempt=AttemptData(
            question_id="q1", question_type="numeric",
            student_answer="42", expected_answer="7",
            response_time_ms=5000,
        ),
    )
    # Pass trusted=True flag
    result = stripper.strip(request, trusted_provider=True)
    assert result.student_id == "real-id"
```

**Edge cases:**
- PII appears in nested model (e.g., `StudentContext.student_id` inside `SocraticQuestionRequest`) -> recursively strip
- Response text contains the anonymized token literally -> `restore()` replaces it back
- Salt not configured -> startup error, refuse to start

---

## Integration Test (all subtasks combined)

```python
import pytest
from cena_llm.router.router import CenaLLMRouter
from cena_llm.router.pii_stripper import CenaPIIStripper
from cena_llm.models import (
    TaskType, ModelTier, StudentContext, ConceptInfo,
    SocraticQuestionRequest, ErrorClassificationRequest, AttemptData,
)

@pytest.fixture
def router(env_vars):
    return CenaLLMRouter.from_config("contracts/llm/routing-config.yaml")

def test_full_routing_pipeline(router):
    # 1. Route socratic question -> Sonnet (trusted, no PII strip needed)
    config = router.route(TaskType.SOCRATIC_QUESTION)
    assert config.model_id == "claude-sonnet-4-6-20260215"
    assert config.is_trusted_provider is True

    # 2. Route error classification -> Kimi (untrusted, PII must be stripped)
    config = router.route(TaskType.ERROR_CLASSIFICATION)
    assert config.model_id == "kimi-k2-0905-preview"
    assert config.is_trusted_provider is False

    # 3. PII strip for Kimi route
    stripper = CenaPIIStripper(salt="test-salt")
    request = ErrorClassificationRequest(
        student_id="real-uuid",
        concept=ConceptInfo(concept_id="c1", concept_name_he="t", concept_name_en="t"),
        attempt=AttemptData(
            question_id="q1", question_type="numeric",
            student_answer="5", expected_answer="7", response_time_ms=3000,
        ),
    )
    stripped = stripper.strip(request)
    assert "real-uuid" not in stripped.student_id

    # 4. Trip Kimi circuit, route falls back to Haiku
    for _ in range(6):
        router.report_failure(ModelTier.KIMI_STANDARD, 500)
    config = router.route(TaskType.ERROR_CLASSIFICATION)
    assert config.model_id != "kimi-k2-0905-preview"

    # 5. Circuit state exposed
    state = router.get_circuit_state(ModelTier.KIMI_STANDARD)
    assert state == "open"
```

## Rollback Criteria
If routing logic is unstable:
- Hardcode all tasks to Claude Sonnet 4.6 as a safe fallback (higher cost, but stable)
- Disable circuit breaker (always route to primary)
- PII stripper can be temporarily disabled for Anthropic-only routing

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `pytest tests/ -k "router"` -> 0 failures
- [ ] All 10 TaskType values resolve to a valid ModelConfig
- [ ] Circuit breaker correctly trips and recovers for every model tier
- [ ] PII fields confirmed stripped for Kimi routes, preserved for Anthropic routes
- [ ] Metrics emitted: `llm_fallback_total`, `llm_circuit_breaker_state`
- [ ] PR reviewed by architect (you)
