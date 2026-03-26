# 06 — Infrastructure & DevOps Tasks

**Technology:** AWS (ECS Fargate, RDS, ElastiCache, S3, CloudFront), NATS JetStream, GitHub Actions, Terraform
**Contract files:** `contracts/backend/nats-subjects.md`, `docs/operations.md`
**Stage:** Foundation (Weeks 1-4) + ongoing

---

## INF-001: AWS Account + VPC Setup
**Priority:** P0 | **Blocked by:** None
- [ ] AWS account with MFA, IAM roles (least privilege)
- [ ] VPC: 2 AZs, public/private subnets, NAT gateway
- [ ] Region: eu-west-1 (or il-central-1 when available)
- [ ] Terraform state stored in S3 with DynamoDB locking

**Test:**
```bash
cd infrastructure/
terraform init
terraform plan -out=plan.tfplan
# Assert: exit code 0, no errors
# Assert: plan shows VPC, subnets, NAT gateway, security groups
terraform apply plan.tfplan  # Apply to dev environment
aws ec2 describe-vpcs --filters "Name=tag:Project,Values=cena" | jq '.Vpcs | length'
# Assert: 1
```

## INF-002: RDS PostgreSQL 16 (Marten Event Store)
**Priority:** P0 | **Blocked by:** INF-001
- [ ] RDS Multi-AZ, db.t4g.medium, GP3 storage
- [ ] Automated snapshots every 1 hour, 7-day retention
- [ ] Connection string in AWS Secrets Manager (not env vars)
- [ ] `cena` database and schema created

**Test:**
```bash
# Connection test from ECS task role
PGPASSWORD=$(aws secretsmanager get-secret-value --secret-id cena/rds --query SecretString --output text | jq -r .password)
psql -h $RDS_ENDPOINT -U cena -d cena -c "SELECT 1;"
# Assert: returns 1

# Snapshot restore test
aws rds restore-db-instance-from-db-snapshot \
  --db-instance-identifier cena-restore-test \
  --db-snapshot-identifier $(aws rds describe-db-snapshots --query 'DBSnapshots[-1].DBSnapshotIdentifier' --output text)
# Assert: instance available within 15 minutes
```

## INF-003: NATS JetStream (Synadia Cloud)
**Priority:** P0 | **Blocked by:** None
- [ ] Synadia Cloud account, 3-node cluster
- [ ] 11 streams created per `nats-subjects.md` (retention, replicas, max_age)
- [ ] 15 consumer groups configured with correct filter subjects
- [ ] Dead letter queue: `cena.system.dlq.{context}.{event}`
- [ ] Message deduplication: 2-minute window

**Test:**
```bash
# Publish 1000 events and verify consumer receipt
nats pub cena.learner.events.ConceptAttempted '{"test":true}' --count=1000
nats consumer info LEARNER_EVENTS engagement-consumer
# Assert: num_pending = 1000

# Kill consumer, verify replay
nats consumer delete LEARNER_EVENTS engagement-consumer
nats consumer add LEARNER_EVENTS engagement-consumer --deliver=all
# Assert: redelivers from stream start
```

## INF-004: Redis (ElastiCache)
**Priority:** P1 | **Blocked by:** INF-001
- [ ] ElastiCache Redis 7.x, single-node (upgrade to cluster post-launch)
- [ ] Connection string in Secrets Manager
- [ ] Key namespaces per `redis-contracts.ts`
- [ ] **Test:** SET/GET round-trip from ECS; idempotency SET NX works

## INF-005: S3 + CloudFront (Static Assets + Exports)
**Priority:** P1 | **Blocked by:** INF-001
- [ ] S3 buckets: `cena-diagrams`, `cena-exports`, `cena-web-app`
- [ ] CloudFront distribution for web app and diagrams
- [ ] CORS configured for mobile app domain
- [ ] **Test:** Upload + fetch via CDN URL < 100ms

## INF-006: ECS Fargate Cluster (Actor + LLM ACL)
**Priority:** P0 | **Blocked by:** INF-001, INF-002
- [ ] ECS cluster: 2-3 Fargate tasks (Proto.Actor .NET 9)
- [ ] 1 Fargate task (Python FastAPI LLM ACL)
- [ ] Task definitions with Secrets Manager references (NOT env vars)
- [ ] Auto-scaling: CPU > 70% → add task
- [ ] Blue-green deployment via CodeDeploy

**Test:**

```bash
# Deploy and verify health
aws ecs update-service --cluster cena --service actor-cluster --force-new-deployment
aws ecs wait services-stable --cluster cena --services actor-cluster
curl -f https://api.cena.app/health/ready
# Assert: 200 OK

# Scale test
aws ecs update-service --cluster cena --service actor-cluster --desired-count 3
aws ecs wait services-stable --cluster cena --services actor-cluster
# Assert: 3 running tasks, all healthy
```

## INF-007: CI/CD Pipeline (GitHub Actions)
**Priority:** P1 | **Blocked by:** INF-006
- [ ] `.NET Actor Cluster`: build → test → Docker image → push to ECR → deploy to staging
- [ ] `Python LLM ACL`: build → test → Docker image → push to ECR → deploy to App Runner
- [ ] `Flutter Mobile`: build → test → `flutter build apk/ipa`
- [ ] `React PWA`: build → test → deploy to S3 + CloudFront invalidation
- [ ] Staging auto-deploy on push to main; prod manual promote
- [ ] **Test:** Push to main → verify staging deployment < 10 minutes

## INF-008: Secrets Management (AWS Secrets Manager)
**Priority:** P0 | **Blocked by:** INF-001
- [ ] All 7 secrets from `docs/operations.md` Section 5 stored
- [ ] PostgreSQL: 90-day rotation
- [ ] Anthropic + Moonshot API keys: manual rotation, versioned
- [ ] ECS tasks reference via `secrets` block (not `environment`)
- [ ] `.gitignore` blocks `*.env`, `.env.*`, `secrets/`

**Test:**

```bash
# Verify all secrets exist
for secret in cena/rds cena/nats cena/anthropic cena/moonshot cena/whatsapp cena/firebase cena/neo4j; do
  aws secretsmanager describe-secret --secret-id "$secret" > /dev/null 2>&1 || echo "MISSING: $secret"
done
# Assert: no MISSING output

# Rotation test: rotate RDS password, verify ECS reconnects
aws secretsmanager rotate-secret --secret-id cena/rds
sleep 30
curl -f https://api.cena.app/health/ready
# Assert: 200 OK (ECS re-fetched new credentials)

# Verify no secrets in repo
git log --all -p | grep -i "ANTHROPIC_API_KEY\|sk-ant-\|moonshot" | wc -l
# Assert: 0
```

## INF-009: Monitoring + Alerting (Grafana Cloud)
**Priority:** P1 | **Blocked by:** INF-006
- [ ] Grafana Cloud free tier configured
- [ ] OpenTelemetry collector → Grafana
- [ ] Dashboards: LLM error rate, actor activation latency, NATS consumer lag
- [ ] Alerts: LLM error > 5%, actor activation > 5s, NATS lag > 100
- [ ] **Test:** Trigger alert by injecting high error rate → verify Slack notification

## INF-010: Domain + SSL (CloudFront)
**Priority:** P3 | **Blocked by:** INF-005
- [ ] Domain: cena.app (or cena.co.il)
- [ ] ACM certificate for *.cena.app
- [ ] Route53 DNS records
- [ ] WebSocket URL: wss://api.cena.app
- [ ] **Test:** `curl https://cena.app` returns 200; SSL Labs grade A+
