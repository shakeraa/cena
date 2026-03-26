# INF-012: Mutual TLS for gRPC (LLM ACL Boundary)

**Priority:** P0 — BLOCKER (gRPC between .NET silo and Python LLM ACL is unauthenticated)
**Blocked by:** None (can run in parallel with SEC-009)
**Blocks:** Production deployment of LLM ACL services
**Estimated effort:** 3 days
**Contract:** `contracts/backend/grpc-protos.proto` (SocraticTutorService, AnswerEvaluationService, ErrorClassificationService, ContentGenerationService)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The gRPC boundary between the .NET Proto.Actor silo and the Python FastAPI LLM ACL carries sensitive student data (mastery levels, error patterns, anonymized IDs). In production, both sides run in separate ECS tasks within the same VPC. Without mTLS, any process in the VPC can call the LLM ACL and burn token budget. mTLS ensures both client (.NET) and server (Python) authenticate each other with certificates issued by a private CA (AWS ACM PCA).

## Subtasks

### INF-012.1: AWS ACM Private CA Setup
**Files:**
- `config/tls/acm-pca-setup.sh` — CA creation script
- `config/tls/issue-cert.sh` — certificate issuance script
- `config/tls/rotate-cert.sh` — certificate rotation script
- `config/tls/terraform/acm-pca.tf` — Terraform for ACM PCA (optional, for IaC)

**Acceptance:**
- [ ] AWS ACM Private CA created in `il-central-1` (Israel region) with RSA 2048 or ECDSA P-256
- [ ] Root CA: 10-year validity, stored in ACM PCA
- [ ] Subordinate CA: 3-year validity, used for issuing end-entity certificates
- [ ] End-entity certificates: 90-day validity, auto-renewed by ACM
- [ ] Certificate naming convention: `CN={service-name}.cena.internal, O=Cena, OU={bounded-context}`
- [ ] Certificates for: `dotnet-silo.cena.internal`, `llm-acl.cena.internal`
- [ ] Certificate chain includes root + subordinate CA (full chain validation)
- [ ] Revocation: CRL distribution point configured, OCSP stapling enabled
- [ ] All scripts are idempotent (re-running doesn't create duplicate CAs)

**Test:**
```bash
#!/bin/bash
# test_acm_pca.sh

# Test 1: CA exists and is ACTIVE
CA_STATUS=$(aws acm-pca describe-certificate-authority \
  --certificate-authority-arn "$CA_ARN" \
  --query 'CertificateAuthority.Status' --output text)
[ "$CA_STATUS" = "ACTIVE" ] && echo "PASS: CA is active" || exit 1

# Test 2: End-entity cert is valid
openssl verify -CAfile ca-chain.pem dotnet-silo.pem
echo "PASS: dotnet-silo cert verifies against CA chain"

openssl verify -CAfile ca-chain.pem llm-acl.pem
echo "PASS: llm-acl cert verifies against CA chain"

# Test 3: Certs have correct CN
SILO_CN=$(openssl x509 -in dotnet-silo.pem -noout -subject | grep -o 'CN=[^/,]*')
[ "$SILO_CN" = "CN=dotnet-silo.cena.internal" ] && echo "PASS: correct CN" || exit 1

# Test 4: Cert expiry > 60 days from now
EXPIRY=$(openssl x509 -in dotnet-silo.pem -noout -enddate | cut -d= -f2)
EXPIRY_EPOCH=$(date -d "$EXPIRY" +%s 2>/dev/null || date -j -f "%b %d %T %Y %Z" "$EXPIRY" +%s)
NOW_EPOCH=$(date +%s)
DAYS_LEFT=$(( (EXPIRY_EPOCH - NOW_EPOCH) / 86400 ))
[ "$DAYS_LEFT" -gt 60 ] && echo "PASS: cert has $DAYS_LEFT days remaining" || exit 1
```

---

### INF-012.2: .NET gRPC Client TLS Configuration
**Files:**
- `src/Cena.Infrastructure/Grpc/MtlsGrpcChannelFactory.cs` — mTLS channel factory
- `src/Cena.Infrastructure/Grpc/CertificateLoader.cs` — cert loading from file/ACM
- `src/Cena.Infrastructure/Grpc/GrpcTlsOptions.cs` — configuration model
- `src/Cena.Api/appsettings.json` — TLS config section (paths, not secrets)

**Acceptance:**
- [ ] `MtlsGrpcChannelFactory.CreateChannel(address)` returns a `GrpcChannel` with client cert
- [ ] Client certificate loaded from: (1) PFX file, (2) PEM file pair, (3) AWS ACM via SDK
- [ ] Server certificate validation: trust only certs signed by our private CA (custom `RemoteCertificateValidationCallback`)
- [ ] TLS 1.3 enforced (`SslProtocols.Tls13`), fallback to TLS 1.2 only if server doesn't support 1.3
- [ ] Cipher suites: `TLS_AES_256_GCM_SHA384`, `TLS_CHACHA20_POLY1305_SHA256` preferred
- [ ] Certificate rotation: `CertificateLoader` watches file changes via `FileSystemWatcher`, reloads without restart
- [ ] Health check: `GrpcHealthCheckService` validates TLS handshake succeeds
- [ ] Metrics: `cena.grpc.tls_handshake_ms` histogram, `cena.grpc.cert_days_remaining` gauge

**Test:**
```csharp
[Fact]
public async Task MtlsChannel_ConnectsWithValidCert()
{
    var channel = _channelFactory.CreateChannel("https://localhost:5051");
    var client = new SocraticTutorService.SocraticTutorServiceClient(channel);

    var response = await client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest
    {
        ConceptId = "algebra-1",
        ConceptName = "Linear Equations",
        Language = ContentLanguage.Hebrew,
        Budget = new TokenBudget { AnonymizedStudentId = "abc123", DailyBudgetLimit = 25000 }
    });

    Assert.NotNull(response.QuestionText);
}

[Fact]
public async Task MtlsChannel_RejectsUntrustedServerCert()
{
    // Server uses a self-signed cert not from our CA
    var channel = _channelFactory.CreateChannel("https://untrusted-server:5052");
    var client = new SocraticTutorService.SocraticTutorServiceClient(channel);

    var ex = await Assert.ThrowsAsync<RpcException>(
        () => client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest()));

    Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
}

[Fact]
public async Task MtlsChannel_RejectsWithoutClientCert()
{
    // Connect without presenting a client certificate
    var insecureChannel = GrpcChannel.ForAddress("https://localhost:5051",
        new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() });
    var client = new SocraticTutorService.SocraticTutorServiceClient(insecureChannel);

    var ex = await Assert.ThrowsAsync<RpcException>(
        () => client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest()));

    // Server rejects the TLS handshake
    Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
}

[Fact]
public async Task CertificateRotation_DoesNotBreakExistingConnections()
{
    var channel = _channelFactory.CreateChannel("https://localhost:5051");
    var client = new SocraticTutorService.SocraticTutorServiceClient(channel);

    // Make a call with original cert
    var r1 = await client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest
    {
        ConceptId = "algebra-1", Language = ContentLanguage.Hebrew
    });
    Assert.NotNull(r1.QuestionText);

    // Simulate cert rotation (write new cert to watched path)
    await RotateCertificate("dotnet-silo");

    // Existing connection still works (TLS session resumption)
    var r2 = await client.GenerateSocraticQuestionAsync(new SocraticQuestionRequest
    {
        ConceptId = "algebra-2", Language = ContentLanguage.Hebrew
    });
    Assert.NotNull(r2.QuestionText);

    // New connection uses new cert
    var channel2 = _channelFactory.CreateChannel("https://localhost:5051");
    var client2 = new SocraticTutorService.SocraticTutorServiceClient(channel2);
    var r3 = await client2.GenerateSocraticQuestionAsync(new SocraticQuestionRequest
    {
        ConceptId = "algebra-3", Language = ContentLanguage.Hebrew
    });
    Assert.NotNull(r3.QuestionText);
}
```

---

### INF-012.3: Python FastAPI gRPC Server TLS Configuration
**Files:**
- `src/cena_llm/grpc/tls_config.py` — TLS server configuration
- `src/cena_llm/grpc/cert_loader.py` — certificate loading and rotation
- `src/cena_llm/grpc/server.py` — gRPC server with mTLS

**Acceptance:**
- [ ] gRPC server configured with `grpc.ssl_server_credentials()` requiring client certs
- [ ] `require_client_auth=True` — connections without valid client cert are rejected
- [ ] Server cert and key loaded from: (1) file pair, (2) environment variables, (3) AWS Secrets Manager
- [ ] Client CA trust bundle: only accept certs from our private CA (root + subordinate)
- [ ] TLS 1.3 enforced via `grpc.ssl_server_credentials` options
- [ ] Certificate rotation: `cert_loader.py` uses `watchdog` to detect file changes, triggers graceful server restart
- [ ] Health endpoint (`grpc.health.v1.Health`) accessible without client cert (for ALB health checks) on separate port
- [ ] Metrics: `cena_grpc_tls_handshake_seconds` histogram, `cena_grpc_cert_expiry_days` gauge (Prometheus)

**Test:**
```python
@pytest.mark.asyncio
async def test_mtls_connection_with_valid_client_cert(grpc_mtls_server, client_cert_pair):
    """Client with valid cert from our CA connects successfully."""
    credentials = grpc.ssl_channel_credentials(
        root_certificates=CA_CHAIN,
        private_key=client_cert_pair.key,
        certificate_chain=client_cert_pair.cert,
    )
    channel = grpc.aio.secure_channel("localhost:50051", credentials)
    stub = SocraticTutorServiceStub(channel)

    response = await stub.GenerateSocraticQuestion(SocraticQuestionRequest(
        concept_id="algebra-1",
        language=ContentLanguage.CONTENT_LANGUAGE_HEBREW,
    ))
    assert response.question_text

@pytest.mark.asyncio
async def test_connection_without_client_cert_rejected(grpc_mtls_server):
    """Connection without client cert is rejected."""
    credentials = grpc.ssl_channel_credentials(root_certificates=CA_CHAIN)
    channel = grpc.aio.secure_channel("localhost:50051", credentials)
    stub = SocraticTutorServiceStub(channel)

    with pytest.raises(grpc.aio.AioRpcError) as exc_info:
        await stub.GenerateSocraticQuestion(SocraticQuestionRequest(concept_id="algebra-1"))
    assert exc_info.value.code() == grpc.StatusCode.UNAVAILABLE

@pytest.mark.asyncio
async def test_connection_with_untrusted_client_cert_rejected(grpc_mtls_server, untrusted_cert_pair):
    """Client cert from different CA is rejected."""
    credentials = grpc.ssl_channel_credentials(
        root_certificates=CA_CHAIN,
        private_key=untrusted_cert_pair.key,
        certificate_chain=untrusted_cert_pair.cert,
    )
    channel = grpc.aio.secure_channel("localhost:50051", credentials)
    stub = SocraticTutorServiceStub(channel)

    with pytest.raises(grpc.aio.AioRpcError) as exc_info:
        await stub.GenerateSocraticQuestion(SocraticQuestionRequest(concept_id="algebra-1"))
    assert exc_info.value.code() == grpc.StatusCode.UNAVAILABLE

@pytest.mark.asyncio
async def test_health_check_accessible_without_client_cert(grpc_mtls_server):
    """Health check port does not require client cert (for ALB)."""
    channel = grpc.aio.insecure_channel("localhost:50052")  # Health port
    stub = HealthStub(channel)
    response = await stub.Check(HealthCheckRequest(service="cena.acl.v1.SocraticTutorService"))
    assert response.status == HealthCheckResponse.SERVING

@pytest.mark.asyncio
async def test_cert_rotation_graceful(grpc_mtls_server, client_cert_pair, tmp_path):
    """Cert rotation doesn't drop active connections."""
    credentials = grpc.ssl_channel_credentials(
        root_certificates=CA_CHAIN,
        private_key=client_cert_pair.key,
        certificate_chain=client_cert_pair.cert,
    )
    channel = grpc.aio.secure_channel("localhost:50051", credentials)
    stub = SocraticTutorServiceStub(channel)

    # Call before rotation
    r1 = await stub.GenerateSocraticQuestion(SocraticQuestionRequest(concept_id="algebra-1"))
    assert r1.question_text

    # Rotate server cert
    rotate_server_cert(grpc_mtls_server)
    await asyncio.sleep(2)  # Wait for watchdog to detect

    # Existing channel still works
    r2 = await stub.GenerateSocraticQuestion(SocraticQuestionRequest(concept_id="algebra-2"))
    assert r2.question_text
```

---

## Integration Test (full mTLS chain)

```csharp
[Fact]
public async Task FullMtls_DotNetToFastApi_RoundTrip()
{
    // 1. .NET creates mTLS channel
    var channel = _channelFactory.CreateChannel("https://llm-acl.cena.internal:50051");

    // 2. Call each gRPC service through mTLS
    var socratic = new SocraticTutorService.SocraticTutorServiceClient(channel);
    var question = await socratic.GenerateSocraticQuestionAsync(new SocraticQuestionRequest
    {
        Budget = new TokenBudget { AnonymizedStudentId = "test123", DailyBudgetLimit = 25000 },
        ConceptId = "algebra-1",
        ConceptName = "Linear Equations",
        Language = ContentLanguage.Hebrew,
        DifficultyLevel = "application"
    });
    Assert.NotNull(question.QuestionText);
    Assert.NotNull(question.Routing); // RoutingMetadata populated
    Assert.True(question.Routing.TotalDurationMs > 0);

    var evaluator = new AnswerEvaluationService.AnswerEvaluationServiceClient(channel);
    var eval = await evaluator.EvaluateAnswerAsync(new AnswerEvaluationRequest
    {
        Budget = new TokenBudget { AnonymizedStudentId = "test123", DailyBudgetLimit = 25000 },
        QuestionId = question.QuestionId,
        QuestionText = question.QuestionText,
        StudentAnswer = "x = 5",
        ExpectedAnswer = "x = 5",
        ConceptId = "algebra-1",
        Language = ContentLanguage.Hebrew
    });
    Assert.True(eval.IsCorrect);

    // 3. Verify TLS metrics emitted
    var metrics = await _metricsCollector.GetHistogram("cena.grpc.tls_handshake_ms");
    Assert.True(metrics.Count > 0);
}
```

## Edge Cases
- ACM PCA rate limit hit during cert issuance → retry with exponential backoff, alert ops
- Certificate chain incomplete → connection fails with `SSL_ERROR_UNKNOWN_CA`; health check catches this
- Clock skew between .NET and Python hosts → cert "not yet valid" error; require NTP sync as deployment prerequisite
- gRPC load balancer terminates TLS → mTLS must be end-to-end, not ALB-terminated; use NLB (TCP passthrough) instead

## Rollback Criteria
- If mTLS adds >20ms P95 to gRPC calls: switch to TLS (server-only) + API key header for client auth
- If cert rotation causes >0.1% request failures: extend cert validity to 1 year, rotate manually
- If ACM PCA cost exceeds $400/month: switch to self-signed certs with HashiCorp Vault as CA

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes end-to-end
- [ ] `dotnet test --filter "Category=MtlsGrpc"` → 0 failures
- [ ] `pytest tests/grpc/tls/ -v` → 0 failures
- [ ] No private keys committed to git (verified via `gitleaks detect`)
- [ ] Cert expiry monitoring: alert at 30 days, critical at 14 days
- [ ] TLS handshake P95 < 15ms (after session resumption warmup)
- [ ] Zero unencrypted gRPC traffic between .NET and Python (verified via tcpdump)
- [ ] PR reviewed by architect (you)
