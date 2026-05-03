# INF-006: ECS Fargate — .NET Actor Cluster + Python LLM ACL, Auto-Scaling, Blue-Green

**Priority:** P0 — blocks all deployment
**Blocked by:** INF-001 (VPC), INF-002 (RDS), INF-004 (Redis)
**Estimated effort:** 4 days
**Contract:** `contracts/actors/cluster_config.cs`, `contracts/llm/acl-interfaces.py`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Two ECS services: (1) .NET 9 actor cluster (Proto.Actor with DynamoDB discovery, Marten, NATS), (2) Python FastAPI LLM ACL (Kimi + Claude routing). Both run on Fargate with auto-scaling. Blue-green deployment prevents disruption during updates.

## Subtasks

### INF-006.1: .NET Actor Service Definition

**Files to create/modify:**
- `infra/terraform/modules/ecs/actor-service.tf`
- `infra/terraform/modules/ecs/actor-task-def.tf`
- `docker/Dockerfile.actors`

**Acceptance:**
- [ ] Fargate task: 2 vCPU, 4GB RAM (prod), 0.5 vCPU, 1GB (dev)
- [ ] Health check: `/health/ready` with 30s interval, 3 failures = unhealthy
- [ ] Environment variables from Secrets Manager: RDS, Redis, NATS, DynamoDB
- [ ] gRPC port 8090 exposed for Proto.Actor cluster communication
- [ ] HTTP port 5000 for GraphQL/SignalR/health
- [ ] Stop timeout: 30 seconds (graceful shutdown)
- [ ] Logging: stdout -> CloudWatch Logs -> Grafana Loki

**Test:**
```bash
aws ecs describe-services --cluster cena-prod --services actor-service   | jq '.services[0].desiredCount'
# Assert: >= 2
```

---

### INF-006.2: Python LLM ACL Service Definition

**Files to create/modify:**
- `infra/terraform/modules/ecs/llm-acl-service.tf`
- `docker/Dockerfile.llm-acl`

**Acceptance:**
- [ ] Fargate task: 1 vCPU, 2GB RAM (prod)
- [ ] Health check: `/health` with 15s interval
- [ ] Environment variables: Kimi API key, Anthropic API key (Secrets Manager)
- [ ] HTTP port 8000 for internal gRPC/REST calls from actor cluster
- [ ] No public internet access (internal ALB only)

**Test:**
```bash
curl http://internal-llm-acl:8000/health
# Assert: {"status": "healthy"}
```

---

### INF-006.3: Auto-Scaling Policies

**Files to create/modify:**
- `infra/terraform/modules/ecs/autoscaling.tf`

**Acceptance:**
- [ ] Actor service: scale on CPU > 70%, min 2, max 8 tasks
- [ ] LLM ACL service: scale on request count, min 1, max 4 tasks
- [ ] Scale-in cooldown: 300 seconds (prevent flapping)
- [ ] Scale-out cooldown: 60 seconds (respond quickly to load)

**Test:**
```bash
aws application-autoscaling describe-scaling-policies   --service-namespace ecs --resource-id service/cena-prod/actor-service
# Assert: target tracking policy exists
```

---

### INF-006.4: Blue-Green Deployment

**Files to create/modify:**
- `infra/terraform/modules/ecs/deployment.tf`
- `.github/workflows/deploy.yml`

**Acceptance:**
- [ ] CodeDeploy blue-green deployment for both services
- [ ] Traffic shifts: 10% canary for 5 minutes, then 100%
- [ ] Automatic rollback on CloudWatch alarm (5xx rate > 1%)
- [ ] Zero-downtime deployment verified
- [ ] Deployment takes < 10 minutes

**Test:**
```bash
aws deploy get-deployment --deployment-id $DEPLOY_ID   | jq '.deploymentInfo.status'
# Assert: "Succeeded"
```

---

## Rollback Criteria
- CodeDeploy automatic rollback on alarm
- Manual rollback via `aws deploy stop-deployment`

## Definition of Done
- [ ] Both services running in staging
- [ ] Auto-scaling tested with load generator
- [ ] Blue-green deployment completes without downtime
- [ ] PR reviewed by architect
