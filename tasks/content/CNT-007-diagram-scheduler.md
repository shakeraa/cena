# CNT-007: Diagram Generation Scheduler

**Priority:** P0 — BLOCKER (without diagrams, visual learners have no content)
**Blocked by:** DATA-004 (Neo4j curriculum graph), INF-001 (NATS cluster)
**Blocks:** Mobile diagram rendering, challenge cards, visual content pipeline
**Estimated effort:** 3 days
**Contract:** `contracts/llm/diagram-generation-pipeline.py` (DiagramPipelineConfig, DiagramGenerationRequest, PipelineRunResult), `contracts/llm/cost-tracking.py` (PRICING, TokenCounter), `contracts/backend/nats-subjects.md` (cena.content.events.ContentPublished)

---

## Context
Cena uses pre-generated SVG diagrams for every concept in the curriculum, generated overnight by Kimi K2.5 (cheapest model at $0.45/MTok input, $2.20/MTok output). The scheduler runs nightly at 2:00 AM Israel time, identifies concepts with stale or missing diagrams, generates them via the batch pipeline, and publishes a `cena.content.events.ContentPublished` NATS event that triggers client-side cache invalidation. Cost control is critical: the `max_cost_per_run_usd` cap of $50 (from `DiagramPipelineConfig`) prevents runaway spend.

## Subtasks

### CNT-007.1: Nightly Scheduler (cron + health monitoring)
**Files:**
- `src/cena_llm/scheduler/diagram_scheduler.py` — main scheduler service
- `src/cena_llm/scheduler/scheduler_config.py` — configuration model
- `src/cena_llm/scheduler/health.py` — health check endpoint for scheduler
- `config/scheduler/crontab` — cron configuration

**Acceptance:**
- [ ] Scheduler runs via APScheduler (or Celery Beat) at `0 2 * * *` (2:00 AM Israel time, per `DiagramPipelineConfig.cron_schedule`)
- [ ] On trigger: loads `DiagramPipelineConfig` from environment/config file
- [ ] Calls `DiagramGenerationPipeline.run_full_pipeline(config)` with loaded config
- [ ] Run is idempotent: if scheduler fires twice (clock drift), second run detects previous run via Redis lock (`diagram-pipeline-lock`, TTL: 4 hours)
- [ ] Health endpoint: `GET /scheduler/health` returns `{ "status": "healthy", "last_run": "...", "next_run": "...", "last_run_result": {...} }`
- [ ] Scheduler logs run start, completion, and cost to structured log
- [ ] On failure: retry once after 30 minutes; if still fails, alert via `cena.system.health.LlmAcl` NATS subject
- [ ] Scheduler can be triggered manually via `POST /scheduler/trigger` (admin-only, authenticated)

**Test:**
```python
# tests/scheduler/test_diagram_scheduler.py
@pytest.mark.asyncio
async def test_scheduler_runs_pipeline_at_configured_time(scheduler, mock_pipeline, mock_clock):
    """Scheduler triggers pipeline at 2:00 AM Israel time."""
    mock_clock.set(datetime(2026, 3, 27, 0, 0, 0, tzinfo=timezone.utc))  # Midnight UTC = 2AM Israel

    await scheduler.check_and_run()

    mock_pipeline.run_full_pipeline.assert_called_once()
    config = mock_pipeline.run_full_pipeline.call_args[0][0]
    assert isinstance(config, DiagramPipelineConfig)
    assert config.max_cost_per_run_usd == 50.0

@pytest.mark.asyncio
async def test_scheduler_idempotent_with_redis_lock(scheduler, mock_pipeline, redis_client):
    """Second run within lock TTL is skipped."""
    # First run acquires lock
    result1 = await scheduler.check_and_run()
    assert result1.executed is True

    # Second run within lock TTL is skipped
    result2 = await scheduler.check_and_run()
    assert result2.executed is False
    assert result2.reason == "Previous run still holds lock"

    mock_pipeline.run_full_pipeline.assert_called_once()

@pytest.mark.asyncio
async def test_scheduler_retries_on_failure(scheduler, mock_pipeline):
    """Scheduler retries once after 30 minutes on failure."""
    mock_pipeline.run_full_pipeline.side_effect = [
        Exception("Kimi API timeout"),
        PipelineRunResult(run_id="retry-1", diagrams_generated=10, total_cost_usd=5.0),
    ]

    result = await scheduler.check_and_run()
    assert result.executed is True
    assert result.retry_count == 1
    assert mock_pipeline.run_full_pipeline.call_count == 2

@pytest.mark.asyncio
async def test_scheduler_alerts_on_persistent_failure(scheduler, mock_pipeline, mock_nats):
    """Scheduler publishes health alert after retry exhaustion."""
    mock_pipeline.run_full_pipeline.side_effect = Exception("Persistent failure")

    result = await scheduler.check_and_run()
    assert result.executed is False

    # Health alert published to NATS
    mock_nats.publish.assert_called_with(
        "cena.system.health.LlmAcl",
        pytest.helpers.json_containing({
            "component": "DiagramScheduler",
            "status": "unhealthy",
        }),
    )

@pytest.mark.asyncio
async def test_manual_trigger_requires_admin_auth(app_client, student_token):
    """Manual trigger endpoint rejects non-admin users."""
    response = await app_client.post(
        "/scheduler/trigger",
        headers={"Authorization": f"Bearer {student_token}"},
    )
    assert response.status_code == 403

@pytest.mark.asyncio
async def test_health_endpoint_returns_last_run_info(app_client, scheduler):
    """Health endpoint includes last run metadata."""
    await scheduler.check_and_run()

    response = await app_client.get("/scheduler/health")
    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "healthy"
    assert body["last_run"] is not None
    assert body["last_run_result"]["diagrams_generated"] >= 0
```

---

### CNT-007.2: Concept Selection (Stale/Missing Diagram Detection)
**Files:**
- `src/cena_llm/scheduler/concept_selector.py` — selects concepts needing diagrams
- `src/cena_llm/scheduler/staleness_calculator.py` — diagram freshness logic

**Acceptance:**
- [ ] Queries Neo4j curriculum graph for all active concepts in configured subjects (per `DiagramPipelineConfig.subjects_to_generate`: start with `["math"]`)
- [ ] A concept needs a diagram if:
  1. **Missing**: no diagram exists in S3 for this concept at any Bloom level
  2. **Stale**: diagram was generated before the concept's `last_updated` timestamp in the curriculum graph
  3. **Low confidence**: existing diagram has `confidence < 0.70` (per `DiagramPipelineConfig.reject_threshold`)
  4. **Version mismatch**: diagram was generated for a different curriculum version
- [ ] Priority ordering: missing > stale > low confidence > version mismatch
- [ ] Concepts are batched per `DiagramPipelineConfig.batch_size` (default: 100)
- [ ] For each concept: generate diagrams for all relevant `DiagramType` x `BloomLevel` combinations
  - Math concepts: `FUNCTION_PLOT`, `GEOMETRY`, `WORKED_EXAMPLE` x all 4 Bloom levels
  - Physics concepts: `CIRCUIT`, `PHYSICS_VECTOR`, `WORKED_EXAMPLE` x all 4 Bloom levels
  - CS concepts: `FLOWCHART`, `WORKED_EXAMPLE` x all 4 Bloom levels
- [ ] Skip concepts with active lock (another pipeline run is processing them)
- [ ] Total concept count capped by cost estimate: `estimated_concepts = max_cost_per_run_usd / avg_cost_per_diagram`
  - Average cost per diagram: ~$0.01 (2000 input tokens + 4000 output tokens at Kimi K2.5 rates)
  - $50 budget ≈ 5000 diagrams maximum per run

**Test:**
```python
# tests/scheduler/test_concept_selector.py
@pytest.mark.asyncio
async def test_selects_missing_diagrams_first(selector, neo4j_session, s3_client):
    """Concepts with no diagrams are selected before stale ones."""
    # Setup: 3 concepts — 1 missing, 1 stale, 1 current
    await seed_concepts(neo4j_session, [
        {"id": "missing-1", "name": "Quadratic Formula", "subject": "math", "updated_at": "2026-03-01"},
        {"id": "stale-1", "name": "Chain Rule", "subject": "math", "updated_at": "2026-03-15"},
        {"id": "current-1", "name": "Limits", "subject": "math", "updated_at": "2026-01-01"},
    ])
    await seed_diagrams(s3_client, [
        {"concept_id": "stale-1", "generated_at": "2026-03-01", "confidence": 0.95},  # Before concept update
        {"concept_id": "current-1", "generated_at": "2026-03-20", "confidence": 0.96},  # After concept update
    ])
    # "missing-1" has no diagram in S3

    selected = await selector.select_concepts(config=DiagramPipelineConfig(subjects_to_generate=["math"]))

    assert selected[0].concept_id == "missing-1"  # Missing first
    assert selected[1].concept_id == "stale-1"    # Stale second
    assert "current-1" not in [c.concept_id for c in selected]  # Current skipped

@pytest.mark.asyncio
async def test_selects_low_confidence_diagrams(selector, neo4j_session, s3_client):
    """Diagrams below reject threshold are re-generated."""
    await seed_concepts(neo4j_session, [
        {"id": "low-conf-1", "name": "Derivatives", "subject": "math", "updated_at": "2026-01-01"},
    ])
    await seed_diagrams(s3_client, [
        {"concept_id": "low-conf-1", "generated_at": "2026-03-20", "confidence": 0.65},  # Below 0.70
    ])

    selected = await selector.select_concepts(config=DiagramPipelineConfig())
    assert any(c.concept_id == "low-conf-1" for c in selected)

@pytest.mark.asyncio
async def test_respects_cost_cap(selector, neo4j_session):
    """Total selected concepts do not exceed cost budget estimate."""
    # Seed 10,000 concepts (all missing diagrams)
    await seed_concepts(neo4j_session, [
        {"id": f"concept-{i}", "name": f"Concept {i}", "subject": "math", "updated_at": "2026-01-01"}
        for i in range(10_000)
    ])

    config = DiagramPipelineConfig(
        max_cost_per_run_usd=50.0,
        max_tokens_per_diagram=4000,
    )
    selected = await selector.select_concepts(config=config)

    # At ~$0.01 per diagram, $50 budget ≈ 5000 diagrams max
    # Each concept generates ~3 diagram types × 4 bloom levels = 12 diagrams
    # So ~416 concepts max
    estimated_diagrams = len(selected) * 12  # Rough estimate
    estimated_cost = estimated_diagrams * 0.01
    assert estimated_cost <= 55.0  # Allow 10% margin

@pytest.mark.asyncio
async def test_skips_locked_concepts(selector, neo4j_session, redis_client):
    """Concepts being processed by another run are skipped."""
    await seed_concepts(neo4j_session, [
        {"id": "locked-1", "name": "Integration", "subject": "math", "updated_at": "2026-01-01"},
        {"id": "unlocked-1", "name": "Differentiation", "subject": "math", "updated_at": "2026-01-01"},
    ])
    # Lock one concept
    await redis_client.set("diagram-lock:locked-1", "run-xyz", ex=3600)

    selected = await selector.select_concepts(config=DiagramPipelineConfig())
    concept_ids = [c.concept_id for c in selected]
    assert "locked-1" not in concept_ids
    assert "unlocked-1" in concept_ids

@pytest.mark.asyncio
async def test_generates_correct_diagram_types_per_subject(selector, neo4j_session):
    """Math concepts get function_plot + geometry + worked_example, not circuit."""
    await seed_concepts(neo4j_session, [
        {"id": "math-1", "name": "Algebra", "subject": "math", "updated_at": "2026-01-01"},
        {"id": "physics-1", "name": "Ohms Law", "subject": "physics", "updated_at": "2026-01-01"},
    ])

    selected = await selector.select_concepts(config=DiagramPipelineConfig(
        subjects_to_generate=["math", "physics"]
    ))

    math_concept = next(c for c in selected if c.concept_id == "math-1")
    physics_concept = next(c for c in selected if c.concept_id == "physics-1")

    assert DiagramType.FUNCTION_PLOT in math_concept.diagram_types
    assert DiagramType.CIRCUIT not in math_concept.diagram_types
    assert DiagramType.CIRCUIT in physics_concept.diagram_types
    assert DiagramType.FUNCTION_PLOT not in physics_concept.diagram_types
```

---

### CNT-007.3: NATS DiagramsPublished Event + Cache Invalidation
**Files:**
- `src/cena_llm/scheduler/event_publisher.py` — NATS event publisher
- `src/cena_llm/scheduler/models.py` — event payload models

**Acceptance:**
- [ ] After pipeline completes: publish `cena.content.events.ContentPublished` to NATS for each batch of diagrams
- [ ] Event payload per `nats-subjects.md`:
  ```json
  {
    "event_id": "UUIDv7",
    "event_type": "DiagramsPublished",
    "timestamp": "2026-03-27T02:15:00Z",
    "run_id": "pipeline-run-123",
    "concept_ids": ["algebra-1", "calculus-chain-rule", ...],
    "diagrams_generated": 150,
    "diagrams_auto_approved": 142,
    "diagrams_pending_review": 5,
    "diagrams_rejected": 3,
    "total_cost_usd": 1.85,
    "cdn_urls": {"algebra-1": "https://cdn.cena.app/diagrams/v1/math/2026-03/algebra-1.svg", ...}
  }
  ```
- [ ] NATS headers set per `nats-subjects.md` §7: `Nats-Msg-Id`, `Cena-Correlation-Id`, `Cena-Schema-Version`
- [ ] Batch publishing: one event per 100 concepts (avoid single massive message)
- [ ] Client-side consumer (mobile/web) receives event → invalidates diagram cache for affected `concept_ids`
- [ ] CDN cache invalidation: CloudFront invalidation request for updated paths (`/diagrams/v1/{subject}/*`)
- [ ] `PipelineRunResult` (from contract) populated and returned to scheduler
- [ ] If NATS publish fails: retry 3 times, then log ERROR (diagrams are still in S3, just clients won't know until next manual check)

**Test:**
```python
# tests/scheduler/test_event_publisher.py
@pytest.mark.asyncio
async def test_publishes_diagrams_published_event(publisher, mock_nats):
    """Publishes NATS event after successful pipeline run."""
    run_result = PipelineRunResult(
        run_id="run-001",
        started_at=datetime(2026, 3, 27, 2, 0, 0),
        completed_at=datetime(2026, 3, 27, 2, 15, 0),
        concepts_processed=50,
        diagrams_generated=150,
        diagrams_auto_approved=142,
        diagrams_pending_review=5,
        diagrams_rejected=3,
        total_cost_usd=1.85,
    )
    concept_cdn_map = {
        "algebra-1": "https://cdn.cena.app/diagrams/v1/math/2026-03/algebra-1.svg",
        "calculus-1": "https://cdn.cena.app/diagrams/v1/math/2026-03/calculus-1.svg",
    }

    await publisher.publish_diagrams_published(run_result, concept_cdn_map)

    mock_nats.publish.assert_called_once()
    call_args = mock_nats.publish.call_args
    assert call_args[0][0] == "cena.content.events.ContentPublished"

    payload = json.loads(call_args[0][1])
    assert payload["event_type"] == "DiagramsPublished"
    assert payload["diagrams_generated"] == 150
    assert payload["total_cost_usd"] == 1.85
    assert "algebra-1" in payload["concept_ids"]

    # Headers set correctly
    headers = call_args[1]["headers"]
    assert "Nats-Msg-Id" in headers
    assert "Cena-Correlation-Id" in headers
    assert headers["Cena-Schema-Version"] == "1"

@pytest.mark.asyncio
async def test_batches_large_concept_lists(publisher, mock_nats):
    """Publishes multiple events for >100 concepts."""
    concept_cdn_map = {
        f"concept-{i}": f"https://cdn.cena.app/diagrams/v1/math/2026-03/concept-{i}.svg"
        for i in range(250)
    }
    run_result = PipelineRunResult(
        run_id="run-002",
        concepts_processed=250,
        diagrams_generated=750,
        total_cost_usd=8.50,
    )

    await publisher.publish_diagrams_published(run_result, concept_cdn_map)

    # 250 concepts / 100 per batch = 3 events
    assert mock_nats.publish.call_count == 3

@pytest.mark.asyncio
async def test_cdn_invalidation_called(publisher, mock_cloudfront):
    """CloudFront invalidation triggered for updated paths."""
    concept_cdn_map = {
        "algebra-1": "https://cdn.cena.app/diagrams/v1/math/2026-03/algebra-1.svg",
    }

    await publisher.publish_diagrams_published(
        PipelineRunResult(run_id="run-003", diagrams_generated=3, total_cost_usd=0.03),
        concept_cdn_map,
    )

    mock_cloudfront.create_invalidation.assert_called_once()
    paths = mock_cloudfront.create_invalidation.call_args[1]["paths"]
    assert "/diagrams/v1/math/*" in paths

@pytest.mark.asyncio
async def test_cost_cap_respected(scheduler, mock_pipeline, mock_cost_tracker):
    """Pipeline stops when cost cap is reached."""
    # Configure low cost cap
    config = DiagramPipelineConfig(max_cost_per_run_usd=5.0)

    # Mock pipeline to track cumulative cost
    diagrams_processed = 0
    async def mock_run(cfg):
        nonlocal diagrams_processed
        diagrams_processed = 0
        while True:
            diagrams_processed += 1
            cost_so_far = diagrams_processed * 0.01
            if cost_so_far >= cfg.max_cost_per_run_usd:
                break
        return PipelineRunResult(
            run_id="capped",
            diagrams_generated=diagrams_processed,
            total_cost_usd=cost_so_far,
        )

    mock_pipeline.run_full_pipeline = mock_run

    result = await scheduler.run_with_config(config)
    assert result.total_cost_usd <= 5.50  # Allow 10% overshoot (batch in-flight)
    assert result.total_cost_usd >= 4.50  # But close to cap

@pytest.mark.asyncio
async def test_nats_publish_failure_retries(publisher, mock_nats):
    """Retries NATS publish on transient failure."""
    mock_nats.publish.side_effect = [
        ConnectionError("NATS timeout"),
        ConnectionError("NATS timeout"),
        None,  # Third attempt succeeds
    ]

    await publisher.publish_diagrams_published(
        PipelineRunResult(run_id="retry-test", diagrams_generated=1, total_cost_usd=0.01),
        {"concept-1": "https://cdn.cena.app/diagrams/v1/math/concept-1.svg"},
    )

    assert mock_nats.publish.call_count == 3
```

---

## Integration Test (full scheduler pipeline)

```python
# tests/integration/test_diagram_scheduler_integration.py
@pytest.mark.asyncio
@pytest.mark.integration
async def test_full_scheduler_pipeline(
    scheduler, neo4j_session, s3_client, nats_client, redis_client
):
    """End-to-end: scheduler detects stale concepts, generates diagrams, publishes events."""

    # 1. Seed curriculum with 5 concepts (3 missing diagrams, 2 current)
    await seed_concepts(neo4j_session, [
        {"id": "int-test-1", "name": "Quadratic Formula", "subject": "math", "updated_at": "2026-03-25"},
        {"id": "int-test-2", "name": "Chain Rule", "subject": "math", "updated_at": "2026-03-25"},
        {"id": "int-test-3", "name": "Limits", "subject": "math", "updated_at": "2026-03-25"},
        {"id": "int-test-4", "name": "Derivatives", "subject": "math", "updated_at": "2026-01-01"},
        {"id": "int-test-5", "name": "Integrals", "subject": "math", "updated_at": "2026-01-01"},
    ])
    await seed_diagrams(s3_client, [
        {"concept_id": "int-test-4", "generated_at": "2026-03-20", "confidence": 0.96},
        {"concept_id": "int-test-5", "generated_at": "2026-03-20", "confidence": 0.97},
    ])

    # 2. Subscribe to NATS for published events
    received_events = []
    sub = await nats_client.subscribe("cena.content.events.ContentPublished")

    # 3. Run scheduler
    config = DiagramPipelineConfig(
        max_cost_per_run_usd=50.0,
        subjects_to_generate=["math"],
        s3_bucket="cena-diagrams-test",
    )
    result = await scheduler.run_with_config(config)

    # 4. Verify results
    assert result.concepts_processed == 3  # Only missing concepts
    assert result.diagrams_generated > 0
    assert result.total_cost_usd <= 50.0
    assert result.total_cost_usd > 0

    # 5. Verify diagrams in S3
    for concept_id in ["int-test-1", "int-test-2", "int-test-3"]:
        objects = await s3_client.list_objects(Bucket="cena-diagrams-test", Prefix=f"v1/math/")
        concept_objects = [o for o in objects if concept_id in o["Key"]]
        assert len(concept_objects) > 0, f"No diagram for {concept_id}"

    # 6. Verify NATS event published
    msg = await asyncio.wait_for(sub.next_msg(), timeout=5.0)
    payload = json.loads(msg.data)
    assert payload["event_type"] == "DiagramsPublished"
    assert set(payload["concept_ids"]) == {"int-test-1", "int-test-2", "int-test-3"}

    # 7. Verify current concepts were NOT re-generated
    assert "int-test-4" not in payload["concept_ids"]
    assert "int-test-5" not in payload["concept_ids"]

    # 8. Verify pipeline result matches PipelineRunResult contract
    assert isinstance(result, PipelineRunResult)
    assert result.run_id is not None
    assert result.completed_at is not None
```

## Edge Cases
- Kimi K2.5 returns confidence 0.0 for a diagram → reject, do not upload to S3, increment `diagrams_rejected`
- Neo4j connection fails during concept selection → abort run, alert, retry next night
- S3 upload fails mid-batch → diagrams already uploaded are valid; resume from last successful concept on retry
- Curriculum graph updated during pipeline run → some diagrams may be immediately stale; next nightly run will catch them
- Cost exceeds cap mid-batch → stop after current batch completes (don't interrupt mid-generation)

## Rollback Criteria
- If Kimi K2.5 generates poor quality diagrams (>30% rejection rate): switch to Claude Sonnet with $150 cost cap
- If scheduler fails 3 nights in a row: manual trigger via admin endpoint, investigate root cause
- If NATS publish failure prevents cache invalidation: force client cache clear via app version bump

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes end-to-end
- [ ] `pytest tests/scheduler/ -v` → 0 failures
- [ ] Nightly run generates diagrams for all missing/stale concepts in configured subjects
- [ ] Stale concepts correctly identified (before `last_updated` in curriculum graph)
- [ ] Current concepts correctly skipped
- [ ] Cost cap ($50) respected — no run exceeds cap
- [ ] `cena.content.events.ContentPublished` NATS event published with correct payload
- [ ] Pipeline run metadata logged and available via health endpoint
- [ ] PR reviewed by architect (you)
