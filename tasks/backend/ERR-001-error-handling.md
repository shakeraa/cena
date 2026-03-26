# ERR-001: Global Error Handling & Correlation

**Priority:** P0 — BLOCKER (without structured errors, debugging distributed failures is impossible)
**Blocked by:** None (foundational infrastructure)
**Blocks:** All API endpoints, all actor message handlers, all gRPC services
**Estimated effort:** 4 days
**Contract:** `contracts/frontend/signalr-messages.ts` (ErrorPayload, SignalRErrorCode), `contracts/backend/nats-subjects.md` (DLQ headers: Cena-Correlation-Id, Cena-Last-Error), `contracts/backend/grpc-protos.proto` (RoutingMetadata)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Cena has 5 communication boundaries: REST API, SignalR WebSocket, gRPC (.NET↔Python), NATS JetStream, and Proto.Actor inter-grain messages. Without a unified error handling strategy, a failure in the Python LLM ACL surfaces as an opaque `500` to the student, the correlation between the SignalR command and the failing gRPC call is lost, and NATS poison messages silently drop into the DLQ without diagnostic context. This task establishes the error hierarchy, error envelopes per protocol, retry policies, and end-to-end correlation ID propagation.

## Subtasks

### ERR-001.1: Error Hierarchy & Machine-Readable Error Codes
**Files:**
- `src/Cena.Contracts/Errors/CenaError.cs` — base error type
- `src/Cena.Contracts/Errors/ErrorCodes.cs` — all error codes (static class)
- `src/Cena.Contracts/Errors/ErrorCategory.cs` — error category enum
- `src/cena_llm/errors/error_codes.py` — Python mirror of error codes

**Acceptance:**
- [ ] Base error record: `CenaError(string Code, string Message, ErrorCategory Category, Dictionary<string, object>? Details, string? CorrelationId)`
- [ ] Error categories: `Validation`, `Authentication`, `Authorization`, `NotFound`, `Conflict`, `RateLimit`, `ExternalService`, `Internal`, `Timeout`
- [ ] Error code format: `CENA_{CONTEXT}_{SPECIFIC}` — e.g., `CENA_AUTH_TOKEN_EXPIRED`, `CENA_LLM_BUDGET_EXHAUSTED`, `CENA_ACTOR_VERSION_CONFLICT`
- [ ] Error codes are exhaustive and documented:
  - Auth: `CENA_AUTH_TOKEN_EXPIRED`, `CENA_AUTH_TOKEN_INVALID`, `CENA_AUTH_INSUFFICIENT_ROLE`
  - Session: `CENA_SESSION_NOT_FOUND`, `CENA_SESSION_ALREADY_ACTIVE`, `CENA_SESSION_EXPIRED`
  - Actor: `CENA_ACTOR_VERSION_CONFLICT`, `CENA_ACTOR_ACTIVATION_FAILED`, `CENA_ACTOR_PASSIVATED`
  - LLM: `CENA_LLM_BUDGET_EXHAUSTED`, `CENA_LLM_PROVIDER_UNAVAILABLE`, `CENA_LLM_TIMEOUT`, `CENA_LLM_CONTENT_FILTER`
  - NATS: `CENA_NATS_PUBLISH_FAILED`, `CENA_NATS_CONSUMER_LAG`
  - Content: `CENA_CONTENT_CONCEPT_NOT_FOUND`, `CENA_CONTENT_QUESTION_RETIRED`
  - Payment: `CENA_PAYMENT_FAILED`, `CENA_PAYMENT_REFUND_FAILED`
- [ ] Mapping to SignalR `SignalRErrorCode` (from signalr-messages.ts): `CENA_SESSION_NOT_FOUND` → `SESSION_NOT_FOUND`, `CENA_AUTH_TOKEN_EXPIRED` → `UNAUTHORIZED`
- [ ] Python error codes mirror .NET exactly (generated from shared spec or manually synced with test)

**Test:**
```csharp
[Fact]
public void ErrorCode_HasCorrectFormat()
{
    // All error codes follow CENA_{CONTEXT}_{SPECIFIC} pattern
    var codes = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => f.GetValue(null) as string)
        .ToList();

    Assert.All(codes, code =>
    {
        Assert.Matches(@"^CENA_[A-Z]+_[A-Z_]+$", code);
    });
}

[Fact]
public void ErrorCode_MapsToSignalRCode()
{
    Assert.Equal("SESSION_NOT_FOUND", ErrorCodes.ToSignalRCode(ErrorCodes.CENA_SESSION_NOT_FOUND));
    Assert.Equal("UNAUTHORIZED", ErrorCodes.ToSignalRCode(ErrorCodes.CENA_AUTH_TOKEN_EXPIRED));
    Assert.Equal("UNAUTHORIZED", ErrorCodes.ToSignalRCode(ErrorCodes.CENA_AUTH_TOKEN_INVALID));
    Assert.Equal("SESSION_ALREADY_ACTIVE", ErrorCodes.ToSignalRCode(ErrorCodes.CENA_SESSION_ALREADY_ACTIVE));
    Assert.Equal("RATE_LIMITED", ErrorCodes.ToSignalRCode(ErrorCodes.CENA_LLM_BUDGET_EXHAUSTED));
    Assert.Equal("INTERNAL_ERROR", ErrorCodes.ToSignalRCode(ErrorCodes.CENA_ACTOR_ACTIVATION_FAILED));
}

[Fact]
public void CenaError_SerializesWithAllFields()
{
    var error = new CenaError(
        Code: ErrorCodes.CENA_LLM_BUDGET_EXHAUSTED,
        Message: "Daily LLM budget exhausted (25000/25000 tokens used)",
        Category: ErrorCategory.RateLimit,
        Details: new Dictionary<string, object>
        {
            ["tokens_used"] = 25000,
            ["daily_limit"] = 25000,
            ["reset_at"] = "2026-03-27T00:00:00Z"
        },
        CorrelationId: "01JQWX5ABC123"
    );

    var json = JsonSerializer.Serialize(error);
    Assert.Contains("CENA_LLM_BUDGET_EXHAUSTED", json);
    Assert.Contains("01JQWX5ABC123", json);
}
```

```python
# tests/errors/test_error_codes.py
def test_python_error_codes_match_dotnet():
    """Ensure Python error codes mirror .NET exactly."""
    from cena_llm.errors.error_codes import ALL_ERROR_CODES

    expected_codes = [
        "CENA_AUTH_TOKEN_EXPIRED",
        "CENA_AUTH_TOKEN_INVALID",
        "CENA_AUTH_INSUFFICIENT_ROLE",
        "CENA_LLM_BUDGET_EXHAUSTED",
        "CENA_LLM_PROVIDER_UNAVAILABLE",
        "CENA_LLM_TIMEOUT",
        "CENA_LLM_CONTENT_FILTER",
    ]
    for code in expected_codes:
        assert code in ALL_ERROR_CODES, f"Missing error code in Python: {code}"
```

---

### ERR-001.2: Error Envelopes Per Protocol (REST, SignalR, gRPC)
**Files:**
- `src/Cena.Api/Middleware/GlobalExceptionMiddleware.cs` — REST error envelope
- `src/Cena.Api/Hubs/HubExceptionFilter.cs` — SignalR error envelope
- `src/cena_llm/grpc/error_interceptor.py` — gRPC error mapping (Python)
- `src/Cena.Infrastructure/Grpc/GrpcExceptionInterceptor.cs` — gRPC error mapping (.NET client)

**Acceptance:**
- [ ] **REST**: All unhandled exceptions → `{ "error": { "code": "CENA_...", "message": "...", "correlationId": "...", "details": {...} } }` with appropriate HTTP status
  - `ErrorCategory.Validation` → 400
  - `ErrorCategory.Authentication` → 401
  - `ErrorCategory.Authorization` → 403
  - `ErrorCategory.NotFound` → 404
  - `ErrorCategory.Conflict` → 409
  - `ErrorCategory.RateLimit` → 429
  - `ErrorCategory.Timeout` → 504
  - `ErrorCategory.Internal` / `ErrorCategory.ExternalService` → 500
- [ ] **SignalR**: All hub method exceptions → `ErrorEvent` (from signalr-messages.ts) with `correlationId` from the triggering command
  - `HubException` message is machine-readable: `{ "code": "SESSION_NOT_FOUND", "message": "...", "details": {...} }`
  - Internal exceptions never leak stack traces to client
- [ ] **gRPC (Python → .NET)**: gRPC status codes map from Python ACL:
  - `CENA_LLM_BUDGET_EXHAUSTED` → `StatusCode.RESOURCE_EXHAUSTED` with `google.rpc.ErrorInfo` detail
  - `CENA_LLM_PROVIDER_UNAVAILABLE` → `StatusCode.UNAVAILABLE`
  - `CENA_LLM_TIMEOUT` → `StatusCode.DEADLINE_EXCEEDED`
  - `CENA_AUTH_TOKEN_INVALID` → `StatusCode.UNAUTHENTICATED`
  - All others → `StatusCode.INTERNAL` with sanitized message
- [ ] **gRPC (.NET client)**: gRPC status codes mapped back to `CenaError` on receipt
- [ ] No stack traces, file paths, or connection strings exposed in any error response (production)
- [ ] Development mode: include stack trace in `details.__debug_stacktrace` (never in production)

**Test:**
```csharp
[Fact]
public async Task REST_ValidationError_Returns400()
{
    var response = await _client.PostAsync("/api/session/start",
        JsonContent.Create(new { subjectId = "" })); // Empty subject

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
    Assert.StartsWith("CENA_", body.Error.Code);
    Assert.Equal(ErrorCategory.Validation.ToString(), body.Error.Category);
    Assert.NotNull(body.Error.CorrelationId);
}

[Fact]
public async Task REST_InternalError_HidesStackTrace()
{
    // Trigger an internal error
    var response = await _client.GetAsync("/api/debug/throw-exception");

    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.DoesNotContain("at Cena.", body); // No stack trace
    Assert.DoesNotContain("NullReferenceException", body); // No exception type
    Assert.Contains("CENA_INTERNAL_", body); // Has error code
}

[Fact]
public async Task SignalR_HubError_SendsErrorEvent()
{
    var errorReceived = new TaskCompletionSource<ErrorPayload>();
    _connection.On<ErrorPayload>("Error", payload => errorReceived.SetResult(payload));

    // Send invalid command (no active session)
    await _connection.InvokeAsync("SubmitAnswer", new SubmitAnswerPayload
    {
        SessionId = "nonexistent-session",
        QuestionId = "q-1",
        Answer = "test"
    });

    var error = await errorReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("SESSION_NOT_FOUND", error.Code);
    Assert.NotNull(error.Message);
    Assert.DoesNotContain("Exception", error.Message);
}

[Fact]
public async Task SignalR_ErrorPreservesCorrelationId()
{
    var correlationId = Guid.NewGuid().ToString();
    var errorReceived = new TaskCompletionSource<MessageEnvelope<string, ErrorPayload>>();
    _connection.On<MessageEnvelope<string, ErrorPayload>>("Error", msg => errorReceived.SetResult(msg));

    await _connection.InvokeAsync("SubmitAnswer", new
    {
        type = "SubmitAnswer",
        correlationId = correlationId,
        payload = new { sessionId = "bad", questionId = "q", answer = "a" }
    });

    var error = await errorReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(correlationId, error.CorrelationId);
}
```

```python
# tests/grpc/test_error_interceptor.py
@pytest.mark.asyncio
async def test_budget_exhausted_returns_resource_exhausted(grpc_stub, exhausted_student):
    with pytest.raises(grpc.aio.AioRpcError) as exc_info:
        await grpc_stub.GenerateSocraticQuestion(SocraticQuestionRequest(
            budget=TokenBudget(anonymized_student_id=exhausted_student),
            concept_id="algebra-1",
        ))

    assert exc_info.value.code() == grpc.StatusCode.RESOURCE_EXHAUSTED
    # Error details contain structured CenaError
    details = exc_info.value.trailing_metadata()
    error_info = dict(details)
    assert "CENA_LLM_BUDGET_EXHAUSTED" in str(error_info)

@pytest.mark.asyncio
async def test_provider_timeout_returns_deadline_exceeded(grpc_stub, slow_provider):
    with pytest.raises(grpc.aio.AioRpcError) as exc_info:
        await grpc_stub.GenerateSocraticQuestion(
            SocraticQuestionRequest(concept_id="algebra-1"),
            timeout=1,  # 1 second timeout
        )

    assert exc_info.value.code() == grpc.StatusCode.DEADLINE_EXCEEDED

@pytest.mark.asyncio
async def test_internal_error_hides_details(grpc_stub, broken_provider):
    with pytest.raises(grpc.aio.AioRpcError) as exc_info:
        await grpc_stub.GenerateSocraticQuestion(
            SocraticQuestionRequest(concept_id="algebra-1"),
        )

    assert exc_info.value.code() == grpc.StatusCode.INTERNAL
    # No Python traceback in error message
    assert "Traceback" not in exc_info.value.details()
    assert "File \"/" not in exc_info.value.details()
```

---

### ERR-001.3: Retry Policies (Per Boundary)
**Files:**
- `src/Cena.Infrastructure/Resilience/RetryPolicies.cs` — Polly retry policies
- `src/Cena.Infrastructure/Resilience/CircuitBreakerPolicies.cs` — circuit breakers
- `src/cena_llm/resilience/retry_policies.py` — Python retry policies (tenacity)

**Acceptance:**
- [ ] **gRPC calls (.NET → Python LLM ACL)**: 3 retries, exponential backoff (1s, 2s, 4s), jitter ±30%
  - Retry on: `StatusCode.UNAVAILABLE`, `StatusCode.DEADLINE_EXCEEDED`, `StatusCode.INTERNAL`
  - Do NOT retry: `StatusCode.RESOURCE_EXHAUSTED` (budget), `StatusCode.UNAUTHENTICATED`, `StatusCode.INVALID_ARGUMENT`
  - Circuit breaker: open after 5 consecutive failures, half-open after 30s
- [ ] **NATS publish**: 3 retries, linear backoff (500ms, 1s, 2s)
  - Retry on: timeout, connection lost
  - Do NOT retry: permission denied
  - After 3 failures: route to DLQ with `Cena-Last-Error` header (per nats-subjects.md DLQ envelope)
- [ ] **Marten event append**: NO retries for `EventStreamUnexpectedMaxEventIdException` (optimistic concurrency — reload state and re-process)
  - Retry on: transient PostgreSQL errors (`57P01` admin shutdown, `53300` too many connections)
  - 2 retries, 500ms backoff
- [ ] **LLM provider calls (Python → Kimi/Claude)**: 2 retries with fallback chain
  - Primary fails → fallback model (per `RoutingMetadata.fallback_chain` in grpc-protos.proto)
  - Both fail → return cached response if available, else `CENA_LLM_PROVIDER_UNAVAILABLE`
- [ ] All retry attempts logged with `correlationId`, attempt number, and delay

**Test:**
```csharp
[Fact]
public async Task GrpcRetryPolicy_RetriesOnUnavailable()
{
    int callCount = 0;
    _mockGrpcServer.Setup(s => s.GenerateSocraticQuestion(It.IsAny<SocraticQuestionRequest>()))
        .Returns(() =>
        {
            callCount++;
            if (callCount <= 2) throw new RpcException(new Status(StatusCode.Unavailable, ""));
            return Task.FromResult(new SocraticQuestionResponse { QuestionText = "test" });
        });

    var client = CreateClientWithRetryPolicy();
    var response = await client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest());

    Assert.Equal("test", response.QuestionText);
    Assert.Equal(3, callCount); // 2 retries + 1 success
}

[Fact]
public async Task GrpcRetryPolicy_DoesNotRetryOnBudgetExhausted()
{
    int callCount = 0;
    _mockGrpcServer.Setup(s => s.GenerateSocraticQuestion(It.IsAny<SocraticQuestionRequest>()))
        .Returns(() =>
        {
            callCount++;
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Budget exhausted"));
        });

    var client = CreateClientWithRetryPolicy();
    await Assert.ThrowsAsync<RpcException>(
        () => client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest()));

    Assert.Equal(1, callCount); // No retries
}

[Fact]
public async Task CircuitBreaker_OpensAfterConsecutiveFailures()
{
    int callCount = 0;
    _mockGrpcServer.Setup(s => s.GenerateSocraticQuestion(It.IsAny<SocraticQuestionRequest>()))
        .Returns(() =>
        {
            callCount++;
            throw new RpcException(new Status(StatusCode.Unavailable, ""));
        });

    var client = CreateClientWithCircuitBreaker(failureThreshold: 5);

    // First 5 calls hit the server (all fail, exhausting retries)
    for (int i = 0; i < 5; i++)
    {
        await Assert.ThrowsAsync<RpcException>(
            () => client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest()));
    }

    // 6th call should be rejected by circuit breaker (not reaching server)
    var beforeCount = callCount;
    await Assert.ThrowsAsync<BrokenCircuitException>(
        () => client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest()));
    Assert.Equal(beforeCount, callCount); // No new server call
}

[Fact]
public async Task NatsRetry_RoutesToDlqAfterExhaustion()
{
    var dlqMessages = new List<NatsMsg<byte[]>>();
    await _nats.SubscribeAsync<byte[]>("cena.system.dlq.learner.>", msg => dlqMessages.Add(msg));

    // Simulate publish failure (NATS down)
    _nats.SimulateDisconnect();

    var publisher = new ResilientNatsPublisher(_nats, RetryPolicies.NatsPublish);
    await publisher.PublishAsync("cena.learner.events.ConceptAttempted",
        new byte[] { 0x01 },
        headers: new Dictionary<string, string>
        {
            ["Cena-Correlation-Id"] = "corr-123"
        });

    // After 3 retries, message in DLQ
    Assert.Single(dlqMessages);
    Assert.Equal("corr-123", dlqMessages[0].Headers["Cena-Correlation-Id"]);
    Assert.Contains("Connection lost", dlqMessages[0].Headers["Cena-Last-Error"]);
}
```

---

### ERR-001.4: Correlation ID Propagation (End-to-End)
**Files:**
- `src/Cena.Infrastructure/Correlation/CorrelationIdMiddleware.cs` — HTTP/SignalR middleware
- `src/Cena.Infrastructure/Correlation/CorrelationIdPropagator.cs` — cross-boundary propagation
- `src/Cena.Infrastructure/Correlation/CorrelationContext.cs` — AsyncLocal<string> context
- `src/cena_llm/correlation/middleware.py` — Python FastAPI correlation middleware

**Acceptance:**
- [ ] Correlation ID source: client-generated UUIDv7 from `MessageEnvelope.correlationId` (per signalr-messages.ts)
- [ ] If no correlation ID provided: server generates one (UUIDv7)
- [ ] Propagation chain: `Client SignalR → .NET Hub → Proto.Actor Message → gRPC Metadata → Python ACL → LLM Provider`
- [ ] Propagation chain: `Client SignalR → .NET Hub → Proto.Actor Message → NATS Headers → Consumer`
- [ ] HTTP response header: `X-Correlation-Id: {id}` on all REST responses
- [ ] SignalR event `correlationId` field matches the triggering command (per `MessageEnvelope`)
- [ ] gRPC metadata: `cena-correlation-id` key (per grpc-protos.proto convention)
- [ ] NATS header: `Cena-Correlation-Id` (per nats-subjects.md §7)
- [ ] All log entries include `CorrelationId` in structured log context (Serilog `.ForContext("CorrelationId", id)`)
- [ ] Python logs include `correlation_id` via `structlog` bound context
- [ ] DLQ messages include `Cena-Correlation-Id` from original event (per nats-subjects.md §4.2)
- [ ] Metric labels include `correlation_id` for high-cardinality tracing (sampled, not all)

**Test:**
```csharp
[Fact]
public async Task CorrelationId_FlowsThroughEntireChain()
{
    var correlationId = "01JQWX5-test-correlation";
    var capturedIds = new ConcurrentDictionary<string, string>();

    // Capture at each boundary
    _grpcInterceptor.OnCall += (metadata) =>
        capturedIds["grpc"] = metadata.Get("cena-correlation-id")?.Value;

    _natsPublisher.OnPublish += (headers) =>
        capturedIds["nats"] = headers["Cena-Correlation-Id"];

    // Send SignalR command with correlation ID
    await _connection.InvokeAsync("SubmitAnswer", new
    {
        type = "SubmitAnswer",
        correlationId = correlationId,
        payload = new { sessionId = _activeSession, questionId = "q-1", answer = "x=5", responseTimeMs = 3000 }
    });

    // Wait for all async processing
    await Task.Delay(TimeSpan.FromSeconds(3));

    // Verify correlation ID at each boundary
    Assert.Equal(correlationId, capturedIds["grpc"]);
    Assert.Equal(correlationId, capturedIds["nats"]);

    // Verify in structured logs
    var logs = _logCapture.GetEntriesWithCorrelationId(correlationId);
    Assert.True(logs.Count >= 3); // At least: hub handler, actor handler, NATS publish
}

[Fact]
public async Task CorrelationId_PreservedInDlq()
{
    var correlationId = "01JQWX5-dlq-test";

    // Publish an event that will fail consumer processing
    await _nats.PublishAsync("cena.learner.events.ConceptAttempted",
        new byte[] { 0xFF }, // Malformed payload
        headers: new NatsHeaders { ["Cena-Correlation-Id"] = correlationId });

    // Wait for DLQ routing (after max retries)
    await Task.Delay(TimeSpan.FromSeconds(10));

    var dlqMessage = await _nats.SubscribeAsync<byte[]>("cena.system.dlq.learner.ConceptAttempted");
    var msg = await dlqMessage.Msgs.ReadAsync();

    Assert.Equal(correlationId, msg.Headers["Cena-Correlation-Id"]);
    Assert.NotNull(msg.Headers["Cena-Last-Error"]);
    Assert.NotNull(msg.Headers["Cena-Delivery-Count"]);
}

[Fact]
public async Task REST_CorrelationId_InResponseHeader()
{
    var correlationId = "01JQWX5-rest-test";
    _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

    var response = await _client.GetAsync("/api/profile");

    Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-Id").First());
}

[Fact]
public async Task CorrelationId_GeneratedIfMissing()
{
    var response = await _client.GetAsync("/api/profile");

    Assert.True(response.Headers.Contains("X-Correlation-Id"));
    var generated = response.Headers.GetValues("X-Correlation-Id").First();
    Assert.Matches(@"^[0-9a-f-]{36}$", generated); // UUIDv7 format
}
```

```python
# tests/correlation/test_correlation_propagation.py
@pytest.mark.asyncio
async def test_correlation_id_propagated_to_llm_call(app_client, mock_llm_provider):
    response = await app_client.post(
        "/api/v1/evaluate",
        headers={
            "Authorization": f"Bearer {valid_token}",
            "X-Correlation-Id": "corr-py-test-001",
        },
        json={"concept_id": "algebra-1", "answer": "x=5"},
    )
    assert response.status_code == 200

    # Verify correlation ID was passed to LLM provider
    assert mock_llm_provider.last_call_metadata["correlation_id"] == "corr-py-test-001"

    # Verify correlation ID in structured logs
    assert any(
        log["correlation_id"] == "corr-py-test-001"
        for log in captured_logs
    )
```

---

## Integration Test (error across full stack)

```csharp
[Fact]
public async Task Error_PropagatesFromPythonThroughActorToSignalR()
{
    var correlationId = "01JQWX5-full-error-test";
    var errorReceived = new TaskCompletionSource<MessageEnvelope<string, ErrorPayload>>();
    _connection.On<MessageEnvelope<string, ErrorPayload>>("Error", msg => errorReceived.SetResult(msg));

    // Make Python ACL return BUDGET_EXHAUSTED
    _llmAclMock.SetBudgetExhausted("student-err-1");

    // Submit answer → Actor → gRPC → Python → BUDGET_EXHAUSTED → Actor → SignalR Error
    await _connection.InvokeAsync("SubmitAnswer", new
    {
        type = "SubmitAnswer",
        correlationId = correlationId,
        payload = new { sessionId = _activeSession, questionId = "q-1", answer = "x=5", responseTimeMs = 3000 }
    });

    var error = await errorReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

    // Error has correct code
    Assert.Equal("RATE_LIMITED", error.Payload.Code);
    Assert.Contains("budget", error.Payload.Message.ToLower());

    // Correlation ID preserved
    Assert.Equal(correlationId, error.CorrelationId);

    // Logs have full chain
    var logs = _logCapture.GetEntriesWithCorrelationId(correlationId);
    Assert.Contains(logs, l => l.Contains("RESOURCE_EXHAUSTED")); // gRPC layer
    Assert.Contains(logs, l => l.Contains("CENA_LLM_BUDGET_EXHAUSTED")); // Domain layer
}
```

## Edge Cases
- Correlation ID exceeds 256 characters → truncate, log WARNING
- Concurrent requests with same correlation ID → allowed (idempotent operations), log WARNING for different operations
- Python process crashes mid-request → gRPC returns UNAVAILABLE, .NET retries, new correlation context on retry
- NATS consumer processes message after DLQ routing (race) → idempotent consumer detects duplicate

## Rollback Criteria
- If correlation propagation adds >5ms P95 to request latency: make it async (fire-and-forget log enrichment)
- If error code mapping causes silent failures: revert to plain HTTP status codes + freeform messages
- If circuit breaker is too aggressive: increase failure threshold from 5 to 10, half-open delay from 30s to 60s

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes end-to-end
- [ ] `dotnet test --filter "Category=ErrorHandling"` → 0 failures
- [ ] `pytest tests/errors/ tests/correlation/ -v` → 0 failures
- [ ] No stack traces in production error responses (verified by security scan)
- [ ] Correlation ID traceable across all 5 boundaries (SignalR, REST, gRPC, NATS, Proto.Actor)
- [ ] DLQ messages contain full diagnostic headers per nats-subjects.md spec
- [ ] Grafana dashboard: errors by code, errors by boundary, circuit breaker state
- [ ] PR reviewed by architect (you)
