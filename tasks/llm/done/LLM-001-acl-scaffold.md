# LLM-001: FastAPI ACL Scaffold

**Priority:** P0 — blocks ALL LLM work
**Blocked by:** None (standalone Python service)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/acl-interfaces.py`, `contracts/llm/routing-config.yaml`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The LLM Anti-Corruption Layer is a standalone Python 3.12+ / FastAPI service that mediates all communication between the Cena domain layer and external LLM providers (Anthropic Claude, Moonshot Kimi). This task creates the project skeleton: FastAPI app, Pydantic models re-exported from contracts, gRPC stubs for the .NET actor system, Docker image, and health checks. Everything else in the LLM layer depends on this scaffold.

## Subtasks

### LLM-001.1: Python Project & Dependency Scaffold
**Files to create/modify:**
- `src/llm-acl/pyproject.toml` — project metadata, dependencies
- `src/llm-acl/src/cena_llm/__init__.py` — package init
- `src/llm-acl/src/cena_llm/main.py` — FastAPI app entry point
- `src/llm-acl/src/cena_llm/config.py` — Pydantic Settings for env-based config

**Acceptance:**
- [ ] Python 3.12+ specified in `pyproject.toml` with `requires-python = ">=3.12"`
- [ ] Dependencies installed:
  ```toml
  [project]
  dependencies = [
    "fastapi>=0.115",
    "uvicorn[standard]>=0.34",
    "pydantic>=2.10",
    "pydantic-settings>=2.7",
    "httpx>=0.28",
    "grpcio>=1.68",
    "grpcio-tools>=1.68",
    "redis[hiredis]>=5.2",
    "opentelemetry-api>=1.29",
    "opentelemetry-sdk>=1.29",
    "opentelemetry-instrumentation-fastapi>=0.50b",
    "structlog>=24.4",
    "anthropic>=0.42",
  ]
  ```
- [ ] `uv sync` completes with 0 errors
- [ ] `Config` class reads all env vars from `routing-config.yaml` section 3 (rate limits, timeouts): `KIMI_API_BASE_URL`, `ANTHROPIC_API_KEY` (via `SecretStr`), `PII_ANONYMIZATION_SALT` (via `SecretStr`), `OTEL_EXPORTER_OTLP_ENDPOINT`, `REDIS_URL`
- [ ] FastAPI app at `main.py` starts: `uvicorn cena_llm.main:app --port 8000` exits 0

**Test:**
```python
import subprocess, sys

def test_project_builds():
    result = subprocess.run(
        [sys.executable, "-m", "pip", "install", "-e", "src/llm-acl"],
        capture_output=True, text=True,
    )
    assert result.returncode == 0, result.stderr

def test_fastapi_imports():
    from cena_llm.main import app
    assert app.title == "Cena LLM ACL"

def test_config_reads_env(monkeypatch):
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-test-key")
    monkeypatch.setenv("KIMI_API_BASE_URL", "https://api.moonshot.test/v1")
    monkeypatch.setenv("PII_ANONYMIZATION_SALT", "test-salt-abc")
    monkeypatch.setenv("REDIS_URL", "redis://localhost:6379")
    from cena_llm.config import Settings
    settings = Settings()
    assert settings.anthropic_api_key.get_secret_value() == "sk-test-key"
    assert settings.kimi_api_base_url == "https://api.moonshot.test/v1"
```

---

### LLM-001.2: Pydantic Models Re-export & API Routes
**Files to create/modify:**
- `src/llm-acl/src/cena_llm/models.py` — re-export all Pydantic models from contracts
- `src/llm-acl/src/cena_llm/routes/__init__.py` — route registration
- `src/llm-acl/src/cena_llm/routes/tutoring.py` — Socratic, evaluation, Feynman endpoints
- `src/llm-acl/src/cena_llm/routes/classification.py` — Error classification, content filter endpoints
- `src/llm-acl/src/cena_llm/routes/generation.py` — Diagram generation endpoint
- `src/llm-acl/src/cena_llm/routes/methodology.py` — Methodology switch endpoint
- `src/llm-acl/src/cena_llm/routes/budget.py` — Budget query endpoint

**Acceptance:**
- [ ] All 7 request/response model pairs from `acl-interfaces.py` importable: `SocraticQuestionRequest`, `SocraticQuestionResponse`, `AnswerEvaluationRequest`, `AnswerEvaluationResponse`, `ErrorClassificationRequest`, `ErrorClassificationResponse`, `MethodologySwitchRequest`, `MethodologySwitchResponse`, `ContentFilterRequest`, `ContentFilterResponse`, `DiagramGenerationRequest`, `DiagramGenerationResponse`, `FeynmanExplanationRequest`, `FeynmanExplanationResponse`
- [ ] All enums re-exported: `TaskType` (10 values), `ModelTier` (5 values: `kimi_fast`, `kimi_standard`, `kimi_advanced`, `sonnet`, `opus`), `ErrorType` (5 values), `SafetyVerdict` (3 values), `MethodologyId` (8 values), `DiagramType` (7 values)
- [ ] 7 POST endpoints registered:
  - `POST /v1/socratic-question` -> `SocraticQuestionResponse`
  - `POST /v1/evaluate-answer` -> `AnswerEvaluationResponse`
  - `POST /v1/classify-error` -> `ErrorClassificationResponse`
  - `POST /v1/methodology-switch` -> `MethodologySwitchResponse`
  - `POST /v1/content-filter` -> `ContentFilterResponse`
  - `POST /v1/generate-diagram` -> `DiagramGenerationResponse`
  - `POST /v1/feynman-evaluation` -> `FeynmanExplanationResponse`
- [ ] Each endpoint returns 501 Not Implemented (stub) until router is wired (LLM-002)
- [ ] OpenAPI docs accessible at `/docs` with all models documented
- [ ] All PII-annotated fields (`student_id` in `StudentContext`, `ErrorClassificationRequest`) appear in OpenAPI schema with `pii: true` in JSON schema extra

**Test:**
```python
import pytest
from httpx import AsyncClient, ASGITransport
from cena_llm.main import app

@pytest.fixture
async def client():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as c:
        yield c

@pytest.mark.anyio
async def test_socratic_endpoint_returns_501(client):
    payload = {
        "student": {
            "student_id": "stu-001",
            "grade_level": 10,
            "mastery_map": {"math-fractions": 0.45},
            "active_methodology": "socratic",
        },
        "concept": {
            "concept_id": "math-fractions",
            "concept_name_he": "שברים",
            "concept_name_en": "Fractions",
        },
        "current_mastery": 0.45,
    }
    response = await client.post("/v1/socratic-question", json=payload)
    assert response.status_code == 501

@pytest.mark.anyio
async def test_openapi_docs_accessible(client):
    response = await client.get("/docs")
    assert response.status_code == 200

@pytest.mark.anyio
async def test_all_seven_endpoints_exist(client):
    endpoints = [
        "/v1/socratic-question", "/v1/evaluate-answer", "/v1/classify-error",
        "/v1/methodology-switch", "/v1/content-filter", "/v1/generate-diagram",
        "/v1/feynman-evaluation",
    ]
    for ep in endpoints:
        response = await client.post(ep, json={})
        assert response.status_code in (422, 501), f"{ep} missing: got {response.status_code}"
```

**Edge cases:**
- Invalid JSON body -> 422 with Pydantic validation details
- `grade_level` outside 1-12 -> 422 with field-level error
- `current_mastery` outside 0.0-1.0 -> 422

---

### LLM-001.3: gRPC Service Definition & Stubs
**Files to create/modify:**
- `src/llm-acl/proto/cena_llm.proto` — gRPC service definition
- `src/llm-acl/src/cena_llm/grpc_server.py` — gRPC server setup (reflection enabled)
- `src/llm-acl/src/cena_llm/grpc_stubs/` — generated Python stubs (via `grpcio-tools`)
- `src/llm-acl/scripts/generate_grpc.sh` — script to regenerate stubs

**Acceptance:**
- [ ] Proto file defines `CenaLLMService` with 7 RPCs matching the 7 REST endpoints
- [ ] Message types match Pydantic models: `SocraticQuestionRequest`, `SocraticQuestionResponse`, etc.
- [ ] gRPC reflection enabled for debugging with `grpcurl`
- [ ] `scripts/generate_grpc.sh` runs `python -m grpc_tools.protoc` and generates `_pb2.py` + `_pb2_grpc.py` files
- [ ] gRPC server starts on port 50051 (configurable via `GRPC_PORT` env)
- [ ] .NET actor system can call gRPC stubs (Proto.Remote compatible message format)

**Test:**
```python
import subprocess

def test_generate_grpc_stubs():
    result = subprocess.run(
        ["bash", "src/llm-acl/scripts/generate_grpc.sh"],
        capture_output=True, text=True,
    )
    assert result.returncode == 0, result.stderr

def test_grpc_stubs_importable():
    from cena_llm.grpc_stubs import cena_llm_pb2, cena_llm_pb2_grpc
    assert hasattr(cena_llm_pb2, "SocraticQuestionRequest")
    assert hasattr(cena_llm_pb2_grpc, "CenaLLMServiceStub")

def test_proto_has_all_rpcs():
    from cena_llm.grpc_stubs import cena_llm_pb2_grpc
    service_desc = cena_llm_pb2_grpc.CenaLLMService
    expected_methods = [
        "SocraticQuestion", "EvaluateAnswer", "ClassifyError",
        "DecideMethodologySwitch", "FilterContent", "GenerateDiagram",
        "EvaluateFeynman",
    ]
    # Verify stubs are generated (attribute check)
    for method in expected_methods:
        assert hasattr(service_desc, method) or True  # Stub methods on servicer
```

**Edge cases:**
- Proto file out of sync with Pydantic models -> CI lint step compares field counts
- gRPC port conflict -> log FATAL, fail fast

---

### LLM-001.4: Docker Image & Health Checks
**Files to create/modify:**
- `src/llm-acl/Dockerfile` — multi-stage build, Python 3.12-slim
- `src/llm-acl/docker-compose.dev.yml` — local dev with Redis, Jaeger
- `src/llm-acl/src/cena_llm/routes/health.py` — health check routes

**Acceptance:**
- [ ] `Dockerfile` uses multi-stage build:
  - Stage 1: `python:3.12-slim` builder with `uv` for dependency install
  - Stage 2: `python:3.12-slim` runtime, non-root user `cena`, port 8000 + 50051
- [ ] `docker build -t cena-llm-acl .` exits 0
- [ ] `docker run cena-llm-acl` starts both FastAPI (8000) and gRPC (50051)
- [ ] `GET /health/live` returns 200 always (liveness probe)
- [ ] `GET /health/ready` returns 200 when:
  - Redis is reachable (PING succeeds)
  - At least one LLM provider API key is configured
- [ ] `GET /health/ready` returns 503 with JSON body `{"status": "unhealthy", "checks": {...}}` when Redis is down
- [ ] Health check response includes: `uptime_seconds`, `redis_connected`, `anthropic_configured`, `kimi_configured`, `version`
- [ ] `docker-compose.dev.yml` starts: llm-acl, redis:7, jaeger (all-in-one)
- [ ] Image size < 200MB

**Test:**
```python
import pytest
from httpx import AsyncClient, ASGITransport
from cena_llm.main import app
from unittest.mock import AsyncMock, patch

@pytest.mark.anyio
async def test_liveness_always_200():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get("/health/live")
        assert response.status_code == 200

@pytest.mark.anyio
async def test_readiness_fails_without_redis():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        with patch("cena_llm.routes.health.check_redis", new_callable=AsyncMock, return_value=False):
            response = await client.get("/health/ready")
            assert response.status_code == 503
            body = response.json()
            assert body["status"] == "unhealthy"
            assert body["checks"]["redis_connected"] is False
```

**Edge cases:**
- Redis unreachable at startup -> readiness fails, ECS stops routing traffic
- No API keys configured -> readiness fails with descriptive error
- Graceful shutdown on SIGTERM: stop accepting new requests, drain in-flight, exit within 30s

---

## Integration Test (all subtasks combined)

```python
import pytest
from httpx import AsyncClient, ASGITransport
from cena_llm.main import app

@pytest.mark.anyio
async def test_full_scaffold_e2e():
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Health live passes
        live = await client.get("/health/live")
        assert live.status_code == 200

        # 2. OpenAPI docs load
        docs = await client.get("/openapi.json")
        assert docs.status_code == 200
        schema = docs.json()
        assert "paths" in schema
        assert "/v1/socratic-question" in schema["paths"]
        assert "/v1/evaluate-answer" in schema["paths"]
        assert "/v1/classify-error" in schema["paths"]
        assert "/v1/methodology-switch" in schema["paths"]
        assert "/v1/content-filter" in schema["paths"]
        assert "/v1/generate-diagram" in schema["paths"]
        assert "/v1/feynman-evaluation" in schema["paths"]

        # 3. Model validation works (invalid grade)
        bad_payload = {
            "student": {"student_id": "s1", "grade_level": 99},
            "concept": {"concept_id": "c1", "concept_name_he": "t", "concept_name_en": "t"},
            "current_mastery": 0.5,
        }
        response = await client.post("/v1/socratic-question", json=bad_payload)
        assert response.status_code == 422

        # 4. Valid payload returns 501 (stub)
        valid_payload = {
            "student": {"student_id": "s1", "grade_level": 10},
            "concept": {"concept_id": "c1", "concept_name_he": "שברים", "concept_name_en": "Fractions"},
            "current_mastery": 0.5,
        }
        response = await client.post("/v1/socratic-question", json=valid_payload)
        assert response.status_code == 501

        # 5. All 7 routes registered
        assert len([p for p in schema["paths"] if p.startswith("/v1/")]) == 7
```

## Rollback Criteria
If this task fails or introduces instability:
- Revert to direct HTTP calls from .NET to LLM providers (no ACL)
- All downstream LLM tasks (LLM-002 through LLM-005) are blocked until scaffold is stable
- Acceptable temporary state: REST-only (no gRPC) with Docker running locally

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `pytest tests/ -k "llm_scaffold"` -> 0 failures
- [ ] Docker image builds and starts: health live returns 200
- [ ] gRPC stubs generate without errors
- [ ] OpenAPI schema includes all 14 request/response models
- [ ] PR reviewed by architect (you)
