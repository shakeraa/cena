# INF-008: AWS Secrets Manager — All Secrets, Rotation, and ECS Injection

**Priority:** P0 — blocks all service deployments
**Blocked by:** INF-001 (VPC for Secrets Manager VPC endpoint)
**Estimated effort:** 1.5 days
**Contract:** `docs/operations.md` Section 5

---

## Context

All Cena platform secrets are stored in AWS Secrets Manager, never in environment variables, config files, or hardcoded in source. The architecture defines 7 secrets (`docs/operations.md` Section 5) with specific rotation policies. ECS Fargate tasks reference secrets via the `secrets` block in task definitions — the secret ARN resolves at container start, and no secrets are baked into Docker images.

The security review (`contracts/REVIEW_security.md` H-5) flagged the lack of a rotation contract. This task implements the full secrets lifecycle.

---

## Subtasks

### INF-008.1: Create All 7 Secrets in AWS Secrets Manager

**Files to create/modify:**
- `infra/terraform/modules/secrets/main.tf`
- `infra/terraform/modules/secrets/variables.tf`
- `infra/terraform/modules/secrets/outputs.tf`
- `infra/terraform/environments/staging/secrets.tf`
- `infra/terraform/environments/prod/secrets.tf`

**Acceptance:**

Create all 7 secrets exactly matching `docs/operations.md` Section 5:

| Secret Name | Contents | Rotation | Access (ECS Task Role) |
|-------------|----------|----------|----------------------|
| `cena/db/postgres` | PostgreSQL/Marten connection string (`Host`, `Port`, `Database`, `Username`, `Password`, `SslMode`) | 90-day automatic (AWS Lambda rotator) | `cena-actor-cluster-task-role`, `cena-outreach-task-role` |
| `cena/db/neo4j` | Neo4j AuraDB credentials (`uri`, `username`, `password`) | Manual (AuraDB no auto-rotation support) | `cena-actor-cluster-task-role` |
| `cena/nats/credentials` | NATS JetStream credentials (`url`, `jwt_token`, `nkey_seed`) | 90-day via Synadia Cloud portal (manual trigger) | `cena-actor-cluster-task-role`, `cena-outreach-task-role` |
| `cena/llm/anthropic` | Anthropic API key (`ANTHROPIC_API_KEY`) | Manual rotation; version labeled | `cena-llm-acl-task-role` |
| `cena/llm/moonshot` | Moonshot/Kimi API key (`MOONSHOT_API_KEY`) | Manual rotation; version labeled | `cena-llm-acl-task-role` |
| `cena/outreach/whatsapp` | WhatsApp Business API token | 90-day rotation via Meta Business Manager | `cena-outreach-task-role` |
| `cena/auth/firebase` | Firebase service account credentials (JSON) | Managed by Firebase SDK (auto-refreshed) | All service task roles |

- [ ] All secrets created with JSON structure (not plain strings)
- [ ] Each secret tagged with `Project=cena`, `Environment=staging|prod`, `ManagedBy=terraform`
- [ ] KMS encryption: AWS-managed key (`aws/secretsmanager`) for staging; customer-managed CMK for prod
- [ ] Resource policy on each secret restricting access to specific IAM task roles
- [ ] `.gitignore` blocks `*.pem`, `*.key`, `*.creds`, `*.json` in `infra/secrets/`
- [ ] Terraform creates the secret resource but does NOT store the actual secret value in state — initial values set manually via CLI after `terraform apply`

**Test:**
```bash
# Verify all 7 secrets exist
EXPECTED_SECRETS="cena/auth/firebase cena/db/neo4j cena/db/postgres cena/llm/anthropic cena/llm/moonshot cena/nats/credentials cena/outreach/whatsapp"
ACTUAL_SECRETS=$(aws secretsmanager list-secrets --query 'SecretList[?starts_with(Name, `cena/`)].Name' --output text | tr '\t' '\n' | sort | tr '\n' ' ' | sed 's/ $//')
[ "$ACTUAL_SECRETS" = "$EXPECTED_SECRETS" ] && echo "PASS: all 7 secrets" || echo "FAIL: got $ACTUAL_SECRETS"

# Verify each secret has correct tags
for secret in $EXPECTED_SECRETS; do
  TAGS=$(aws secretsmanager describe-secret --secret-id "$secret" --query 'Tags[?Key==`Project`].Value' --output text)
  [ "$TAGS" = "cena" ] && echo "PASS: $secret tagged" || echo "FAIL: $secret missing Project tag"
done

# Verify resource policies restrict access
for secret in $EXPECTED_SECRETS; do
  POLICY=$(aws secretsmanager get-resource-policy --secret-id "$secret" --query 'ResourcePolicy' --output text 2>/dev/null)
  [ -n "$POLICY" ] && echo "PASS: $secret has resource policy" || echo "WARN: $secret no resource policy (OK for staging)"
done

# Verify no secret values are in Terraform state
terraform show -json | jq '.values.root_module.resources[] | select(.type=="aws_secretsmanager_secret_version")' 2>/dev/null
# Expect: empty (secret values set manually, not via Terraform)
```

**Edge cases:**
- Terraform destroy would delete secrets — use `lifecycle { prevent_destroy = true }` on prod secrets
- Secret name collision between environments — prefix with environment: `cena/staging/db/postgres` vs `cena/prod/db/postgres`
- Developer needs local access — `aws secretsmanager get-secret-value --secret-id <name>` with MFA; never copy to `.env`

---

### INF-008.2: Automatic Rotation for PostgreSQL and Rotation Procedures

**Files to create/modify:**
- `infra/terraform/modules/secrets/rotation.tf` — Lambda rotation function
- `infra/terraform/modules/secrets/rotation-lambda/index.py` — PostgreSQL rotation logic
- `infra/terraform/modules/secrets/iam.tf` — Lambda execution role

**Acceptance:**

**Automatic Rotation (PostgreSQL):**
- [ ] AWS Secrets Manager rotation Lambda for `cena/db/postgres`
- [ ] Rotation schedule: every 90 days
- [ ] Rotation strategy: alternating users (`cena_app_user_a` / `cena_app_user_b`)
  - Step 1 (`createSecret`): generate new password, store as `AWSPENDING`
  - Step 2 (`setSecret`): `ALTER ROLE cena_app_user_b WITH PASSWORD '<new>'` on RDS
  - Step 3 (`testSecret`): connect with new credentials, run `SELECT 1`
  - Step 4 (`finishSecret`): promote `AWSPENDING` to `AWSCURRENT`
- [ ] Lambda in VPC (private subnet) with access to RDS security group
- [ ] Lambda timeout: 30 seconds
- [ ] Rotation failure triggers CloudWatch alarm -> PagerDuty

**Manual Rotation Procedures (documented):**
- [ ] Neo4j AuraDB: change password in AuraDB console -> update secret -> restart actor cluster
- [ ] Anthropic API key: generate new key in Anthropic console -> store as new version -> verify LLM ACL health -> delete old key from Anthropic
- [ ] Moonshot API key: same procedure via Moonshot AI portal
- [ ] WhatsApp Business API: rotate via Meta Business Manager -> update secret -> verify outreach delivery
- [ ] NATS credentials: rotate via Synadia Cloud portal -> update secret -> rolling restart of services

**Application-Side Credential Refresh:**
- [ ] .NET services: handle `CredentialsExpiredException` by re-fetching from Secrets Manager
- [ ] Npgsql connection pool: configure `ConnectionLifetime=300` (5 min) so pooled connections refresh
- [ ] Python FastAPI: implement credential refresh middleware that re-reads secret on 401 from LLM providers
- [ ] Circuit breaker integration: if API key returns 401, trigger immediate credential refresh before opening circuit

**Test:**
```bash
# Test PostgreSQL rotation Lambda (staging)
aws secretsmanager rotate-secret --secret-id "cena/staging/db/postgres" \
  --rotation-lambda-arn "$(terraform output -raw postgres_rotation_lambda_arn)"

# Wait for rotation to complete (max 60s)
for i in $(seq 1 12); do
  STATUS=$(aws secretsmanager describe-secret --secret-id "cena/staging/db/postgres" \
    --query 'RotationEnabled' --output text)
  VERSIONS=$(aws secretsmanager describe-secret --secret-id "cena/staging/db/postgres" \
    --query 'VersionIdsToStages' --output json)
  echo "Attempt $i: RotationEnabled=$STATUS"
  echo "$VERSIONS" | jq .
  # Check if AWSPENDING is gone (rotation complete)
  echo "$VERSIONS" | grep -q "AWSPENDING" || break
  sleep 5
done

# Verify new credentials work
NEW_CREDS=$(aws secretsmanager get-secret-value --secret-id "cena/staging/db/postgres" \
  --query 'SecretString' --output text)
HOST=$(echo "$NEW_CREDS" | jq -r '.Host')
USER=$(echo "$NEW_CREDS" | jq -r '.Username')
PGPASSWORD=$(echo "$NEW_CREDS" | jq -r '.Password') psql -h "$HOST" -U "$USER" -d cena -c "SELECT 1"
echo "PASS: rotated credentials work"
```

```python
# pytest: test credential refresh behavior
def test_llm_acl_refreshes_on_401(mock_anthropic, mock_secrets_manager):
    """When Anthropic returns 401, ACL should refresh credentials."""
    mock_anthropic.side_effect = [
        AuthenticationError("invalid api key"),  # First call fails
        SuccessResponse(content="refreshed")     # Second call succeeds
    ]
    mock_secrets_manager.return_value = {"ANTHROPIC_API_KEY": "new-key-123"}

    result = llm_acl.generate_socratic_question(concept="derivatives")

    assert result.content == "refreshed"
    mock_secrets_manager.assert_called_once()  # Credential was refreshed
```

**Edge cases:**
- Rotation Lambda fails mid-rotation (between `setSecret` and `testSecret`) — `AWSPENDING` version exists but RDS password may be changed; Lambda retries handle this
- Application caches old credentials — `ConnectionLifetime` ensures pool refreshes within 5 minutes; worst case: 5 minutes of failed connections during rotation
- Two services reading the same secret during rotation — both get `AWSCURRENT`; only after rotation completes does `AWSCURRENT` change
- RDS Multi-AZ failover during rotation — Lambda must handle connection to new primary

---

### INF-008.3: ECS Task Definition Secret Injection

**Files to create/modify:**
- `infra/terraform/modules/ecs-service/task-definition.tf` — parameterized task definition with `secrets` block
- `infra/terraform/modules/ecs-service/iam.tf` — task execution role with Secrets Manager permissions

**Acceptance:**

**ECS Task Definition `secrets` Block (not `environment`):**
- [ ] Actor cluster task definition:
  ```json
  "secrets": [
    { "name": "POSTGRES_CONNECTION", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/db/postgres" },
    { "name": "NEO4J_CREDENTIALS", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/db/neo4j" },
    { "name": "NATS_CREDENTIALS", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/nats/credentials" },
    { "name": "FIREBASE_CREDENTIALS", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/auth/firebase" }
  ]
  ```
- [ ] LLM ACL task definition:
  ```json
  "secrets": [
    { "name": "ANTHROPIC_API_KEY", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/llm/anthropic" },
    { "name": "MOONSHOT_API_KEY", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/llm/moonshot" },
    { "name": "FIREBASE_CREDENTIALS", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/auth/firebase" }
  ]
  ```
- [ ] Outreach service task definition:
  ```json
  "secrets": [
    { "name": "POSTGRES_CONNECTION", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/db/postgres" },
    { "name": "NATS_CREDENTIALS", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/nats/credentials" },
    { "name": "WHATSAPP_TOKEN", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/outreach/whatsapp" },
    { "name": "FIREBASE_CREDENTIALS", "valueFrom": "arn:aws:secretsmanager:eu-west-1:ACCOUNT:secret:cena/auth/firebase" }
  ]
  ```
- [ ] ECS task execution role has `secretsmanager:GetSecretValue` permission scoped to `cena/*` secrets only
- [ ] ECS task execution role has `kms:Decrypt` permission for the customer-managed CMK (prod only)
- [ ] No secrets in `environment` block of task definition
- [ ] No secrets in Docker images (verified by scanning with `trufflehog`)
- [ ] Secrets Manager VPC endpoint (from INF-001.3) ensures secret fetch does not traverse NAT/internet

**Test:**
```bash
# Verify task definition uses secrets block, not environment for sensitive values
TASK_DEF=$(aws ecs describe-task-definition --task-definition cena-actor-cluster \
  --query 'taskDefinition.containerDefinitions[0]')

# Check secrets block has entries
SECRET_COUNT=$(echo "$TASK_DEF" | jq '.secrets | length')
[ "$SECRET_COUNT" -ge 4 ] && echo "PASS: $SECRET_COUNT secrets injected" || echo "FAIL: only $SECRET_COUNT secrets"

# Check no secrets leaked in environment block
ENV_SECRETS=$(echo "$TASK_DEF" | jq '[.environment[] | select(.name | test("PASSWORD|KEY|TOKEN|SECRET|CREDENTIAL"; "i"))] | length')
[ "$ENV_SECRETS" -eq 0 ] && echo "PASS: no secrets in environment" || echo "FAIL: $ENV_SECRETS secrets in environment block"

# Verify task execution role permissions
ROLE_ARN=$(echo "$TASK_DEF" | jq -r '.executionRoleArn' || terraform output -raw ecs_execution_role_arn)
aws iam simulate-principal-policy \
  --policy-source-arn "$ROLE_ARN" \
  --action-names "secretsmanager:GetSecretValue" \
  --resource-arns "arn:aws:secretsmanager:eu-west-1:*:secret:cena/*" \
  --query 'EvaluationResults[0].EvalDecision' --output text
# Expect: allowed

# Verify role CANNOT access secrets outside cena/ prefix
aws iam simulate-principal-policy \
  --policy-source-arn "$ROLE_ARN" \
  --action-names "secretsmanager:GetSecretValue" \
  --resource-arns "arn:aws:secretsmanager:eu-west-1:*:secret:other-project/*" \
  --query 'EvaluationResults[0].EvalDecision' --output text
# Expect: implicitDeny

# Scan Docker image for leaked secrets
docker pull cena/actor-cluster:latest
trufflehog docker --image cena/actor-cluster:latest --only-verified
# Expect: no findings
```

**Edge cases:**
- ECS task fails to start because secret ARN is wrong — ECS logs `ResourceNotFoundException`; verify ARN format includes the random suffix
- Secret value is JSON but application expects plain string — use `valueFrom` with `::key` suffix to extract individual JSON keys (e.g., `arn:...:cena/db/postgres::Password`)
- Secrets Manager throttling during large deployment (many tasks starting simultaneously) — VPC endpoint helps; ECS retries secret fetch
- Task execution role vs task role confusion — execution role fetches secrets at startup; task role is for runtime AWS API calls

---

## Integration Test (all subtasks combined)

```bash
#!/bin/bash
set -euo pipefail

echo "=== INF-008 Integration Test ==="

# 1. All 7 secrets exist
SECRET_COUNT=$(aws secretsmanager list-secrets --query 'length(SecretList[?starts_with(Name, `cena/`)])' --output text)
[ "$SECRET_COUNT" -ge 7 ] && echo "PASS: $SECRET_COUNT secrets" || echo "FAIL: expected >=7, got $SECRET_COUNT"

# 2. PostgreSQL rotation is enabled
ROTATION=$(aws secretsmanager describe-secret --secret-id "cena/db/postgres" --query 'RotationEnabled' --output text)
[ "$ROTATION" = "True" ] && echo "PASS: PostgreSQL rotation enabled" || echo "FAIL: rotation not enabled"

# 3. All ECS task definitions use secrets block
for service in cena-actor-cluster cena-llm-acl cena-outreach; do
  SECRETS=$(aws ecs describe-task-definition --task-definition "$service" \
    --query 'taskDefinition.containerDefinitions[0].secrets | length(@)' --output text 2>/dev/null || echo "0")
  [ "$SECRETS" -gt 0 ] && echo "PASS: $service has $SECRETS secrets" || echo "SKIP: $service not yet deployed"
done

# 4. No secrets in environment blocks
for service in cena-actor-cluster cena-llm-acl cena-outreach; do
  LEAKED=$(aws ecs describe-task-definition --task-definition "$service" \
    --query 'taskDefinition.containerDefinitions[0].environment[?contains(name, `KEY`) || contains(name, `PASSWORD`) || contains(name, `SECRET`)] | length(@)' \
    --output text 2>/dev/null || echo "0")
  [ "$LEAKED" -eq 0 ] && echo "PASS: $service no leaked secrets" || echo "FAIL: $service has $LEAKED secrets in env"
done

# 5. Trigger test rotation (staging only)
aws secretsmanager rotate-secret --secret-id "cena/staging/db/postgres" 2>/dev/null \
  && echo "PASS: rotation triggered" || echo "SKIP: rotation already in progress or not staging"

echo "=== INF-008 Integration Test Complete ==="
```

## Rollback Criteria

If this task fails or introduces instability:
- Secrets in Secrets Manager persist independently of Terraform — `terraform destroy` can be configured to skip them (`prevent_destroy`)
- If rotation breaks PostgreSQL access: manually set the password in RDS and update the secret to match
- If ECS tasks fail to start due to secret issues: temporarily use `environment` block with hardcoded values (staging only, never prod) and fix the secret configuration
- Never roll back by exposing secrets in source code or environment variables

## Definition of Done

- [ ] All 7 secrets created in Secrets Manager with proper JSON structure
- [ ] PostgreSQL automatic rotation working on 90-day schedule
- [ ] Manual rotation procedures documented for all 6 remaining secrets
- [ ] All ECS task definitions use `secrets` block (not `environment`) for sensitive values
- [ ] Task execution roles scoped to `cena/*` secrets only
- [ ] No secrets in Docker images (Trufflehog scan passes)
- [ ] Credential refresh tested: services recover from rotated credentials within 5 minutes
- [ ] PR reviewed by architect
