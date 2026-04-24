# INF-008: AWS Secrets Manager — 7 Secrets, Rotation, ECS Injection, Git Scan

**Priority:** P0 — blocks all service configuration
**Blocked by:** INF-001 (VPC)
**Estimated effort:** 1 day
**Contract:** `contracts/REVIEW_security.md` (H-5: Secret Rotation)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Seven secrets need centralized management: RDS credentials, Redis AUTH, NATS credentials, Kimi API key, Anthropic API key, Firebase service account, PII anonymization salt. All must rotate automatically and inject into ECS without restarts.

## Subtasks

### INF-008.1: Secret Definitions + Rotation Lambdas

**Files to create/modify:**
- `infra/terraform/modules/secrets/main.tf`
- `infra/terraform/modules/secrets/rotation.tf`

**Acceptance:**
- [ ] 7 secrets created: `cena/rds/credentials`, `cena/redis/auth`, `cena/nats/credentials`, `cena/kimi/api-key`, `cena/anthropic/api-key`, `cena/firebase/service-account`, `cena/pii/anonymization-salt`
- [ ] Rotation: RDS every 90 days (Lambda rotator), API keys every 180 days (manual + notification), PII salt every 30 days (per export epoch)
- [ ] Rotation Lambda: dual-key support during rotation window (old + new both valid)
- [ ] ECS task role: `secretsmanager:GetSecretValue` for all 7 secrets
- [ ] Secret versions tracked: `AWSCURRENT` and `AWSPREVIOUS`

**Test:**
```bash
aws secretsmanager list-secrets --filters Key=name,Values=cena/   | jq '.SecretList | length'
# Assert: 7
```

---

### INF-008.2: ECS Secret Injection

**Files to create/modify:**
- `infra/terraform/modules/ecs/secrets-injection.tf`

**Acceptance:**
- [ ] Secrets injected as environment variables in ECS task definition (not baked into image)
- [ ] Secret reference format: `arn:aws:secretsmanager:region:account:secret:cena/rds/credentials`
- [ ] Application reads from environment variables at startup
- [ ] Rotation triggers ECS service update (rolling restart with new secret)

**Test:**
```csharp
[Fact]
public void Configuration_ReadsFromEnvironment()
{
    Environment.SetEnvironmentVariable("RDS_HOST", "test-host");
    var config = new CenaConfiguration();
    Assert.Equal("test-host", config.RdsHost);
}
```

---

### INF-008.3: Git Secret Scanning

**Files to create/modify:**
- `.github/workflows/secret-scan.yml`
- `.gitleaks.toml` — gitleaks configuration

**Acceptance:**
- [ ] Pre-commit hook: `gitleaks protect` blocks commits containing secrets
- [ ] CI pipeline: `gitleaks detect` scans entire repo history
- [ ] Patterns detected: AWS keys, API tokens, connection strings, passwords, PII salt
- [ ] Zero tolerance: any secret in git -> PR blocked, alert to security team
- [ ] `.env` files in `.gitignore`

**Test:**
```bash
echo "AKIAIOSFODNN7EXAMPLE" > /tmp/test-secret.txt
gitleaks detect --source /tmp/test-secret.txt
# Assert: exit code 1 (leak detected)
```

---

## Rollback Criteria
- Secret rotation failure -> keep previous version active, alert ops

## Definition of Done
- [ ] All 7 secrets in Secrets Manager
- [ ] Rotation tested for RDS credentials
- [ ] Git scanning blocks secret commits
- [ ] PR reviewed by architect
