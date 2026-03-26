# SEC-008: Pre-Launch Security Audit — OWASP Top 10, Dependency Scan, Pen Test, mTLS

**Priority:** P1 — blocks production launch
**Blocked by:** All other SEC tasks
**Estimated effort:** 5 days
**Contract:** `contracts/REVIEW_security.md` (all findings)

---

## Context

Before Cena goes live with Israeli high school students (minors), a comprehensive security audit must verify that all CRITICAL and HIGH findings from the adversarial security review are resolved. This covers OWASP Top 10 against the GraphQL/SignalR surface, dependency vulnerability scanning across .NET/Python/Dart, a focused penetration test, and mTLS for Proto.Actor inter-node gRPC.

## Subtasks

### SEC-008.1: OWASP Top 10 Assessment

**Files to create/modify:**
- `docs/security/owasp-assessment.md` — documented assessment per category
- `scripts/security/owasp-scan.sh` — automated scanning script
- `tests/Security/OwaspTests.cs` — automated checks for top findings

**Acceptance:**
- [ ] A01:Broken Access Control — verified by SEC-002 IDOR tests + RBAC
- [ ] A02:Cryptographic Failures — all PII encrypted (SEC-005), TLS everywhere, no hardcoded secrets
- [ ] A03:Injection — prompt injection (SEC-004), SQL injection (parameterized queries in Marten), GraphQL injection (HotChocolate built-in)
- [ ] A04:Insecure Design — threat model documented for student data flows
- [ ] A05:Security Misconfiguration — no default credentials, Firebase security rules reviewed, CORS restricted
- [ ] A06:Vulnerable Components — dependency scan (SEC-008.2)
- [ ] A07:Auth Failures — Firebase Auth (SEC-001) with MFA-ready, rate-limited sign-in
- [ ] A08:Data Integrity — HMAC-signed offline events (SEC-006), event-sourced audit trail
- [ ] A09:Logging Failures — structured logging with PII redaction, 365-day retention
- [ ] A10:SSRF — no user-controlled URL fetching in backend services
- [ ] Assessment document signed off by security team

**Test:**
```bash
# Run OWASP ZAP against staging
docker run -t owasp/zap2docker-stable zap-baseline.py \
  -t https://staging.cena.edu/graphql \
  -r /tmp/zap-report.html
# Assert: no HIGH or CRITICAL findings
```

---

### SEC-008.2: Dependency Vulnerability Scan

**Files to create/modify:**
- `.github/workflows/dependency-scan.yml` — CI pipeline
- `scripts/security/dep-scan.sh` — manual scan script

**Acceptance:**
- [ ] .NET: `dotnet list package --vulnerable` -> 0 critical/high
- [ ] Python: `pip-audit` -> 0 critical/high
- [ ] Dart/Flutter: `dart pub outdated` + manual CVE check
- [ ] JavaScript (if any): `npm audit` -> 0 critical/high
- [ ] Docker base images: `trivy image` scan -> 0 critical
- [ ] CI blocks merge on critical vulnerability introduction
- [ ] Weekly automated scan with Slack notification for new vulnerabilities
- [ ] SBOM (Software Bill of Materials) generated and stored in S3

**Test:**
```bash
dotnet list package --vulnerable --include-transitive 2>&1 | grep -c "critical\|high"
# Assert: 0

pip-audit --strict --format json 2>&1 | jq '.vulnerabilities | length'
# Assert: 0
```

---

### SEC-008.3: Penetration Test + mTLS for Proto.Actor

**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/RemoteConfiguration.cs` — add mTLS
- `infra/terraform/modules/ecs/tls-certs.tf` — cert generation for actor nodes
- `docs/security/pentest-report.md` — findings and remediation

**Acceptance:**
- [ ] mTLS enabled on Proto.Actor gRPC transport (port 8090)
- [ ] Certificate authority: internal CA (self-signed root, auto-rotated via cert-manager)
- [ ] Each ECS task gets a unique client certificate
- [ ] Connections from non-certified hosts rejected immediately
- [ ] Penetration test scope: GraphQL endpoint, SignalR hub, offline sync endpoint, actor gRPC port
- [ ] Penetration test covers: token forgery, IDOR, injection, rate limit bypass, privilege escalation
- [ ] All findings documented with severity, reproduction steps, and remediation status
- [ ] Zero CRITICAL findings remaining at launch
- [ ] All HIGH findings have documented mitigations or accepted risk with sign-off

**Test:**
```csharp
[Fact]
public async Task GrpcTransport_RejectsUncertifiedConnection()
{
    // Connect without client certificate
    var channel = GrpcChannel.ForAddress("https://localhost:8090", new GrpcChannelOptions {
        HttpHandler = new HttpClientHandler { /* no client cert */ }
    });
    await Assert.ThrowsAsync<RpcException>(() =>
        new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest()));
}
```

---

## Rollback Criteria
If mTLS causes cluster communication failures:
- Disable mTLS, fall back to network-level isolation (security groups)
- Document as accepted risk with compensating control (VPC + security group restriction)

## Definition of Done
- [ ] OWASP assessment document completed and signed off
- [ ] 0 critical/high dependency vulnerabilities
- [ ] mTLS operational on staging cluster
- [ ] Penetration test completed with 0 CRITICAL findings
- [ ] All HIGH findings remediated or risk-accepted with sign-off
- [ ] PR reviewed by architect + security team
