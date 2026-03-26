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
- [ ] **Test:** `terraform plan` succeeds from clean state

## INF-002: RDS PostgreSQL 16 (Marten Event Store)
**Priority:** P0 | **Blocked by:** INF-001
- [ ] RDS Multi-AZ, db.t4g.medium, GP3 storage
- [ ] Automated snapshots every 1 hour, 7-day retention
- [ ] Connection string in AWS Secrets Manager (not env vars)
- [ ] `cena` database and schema created
- [ ] **Test:** Connect from ECS task role → `SELECT 1` succeeds; restore from snapshot → verify events intact

## INF-003: NATS JetStream (Synadia Cloud)
**Priority:** P0 | **Blocked by:** None
- [ ] Synadia Cloud account, 3-node cluster
- [ ] 11 streams created per `nats-subjects.md` (retention, replicas, max_age)
- [ ] 15 consumer groups configured with correct filter subjects
- [ ] Dead letter queue: `cena.system.dlq.{context}.{event}`
- [ ] Message deduplication: 2-minute window
- [ ] **Test:** Publish 1000 events → verify all consumers receive; kill consumer → verify replay from last ack

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
- [ ] **Test:** Deploy → health check passes; scale to 3 tasks → verify cluster stability

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
- [ ] **Test:** Rotate PostgreSQL password → verify ECS reconnects automatically

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
