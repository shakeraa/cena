# INF-001: AWS Account, VPC, and Network Foundation

**Priority:** P0 — blocks ALL infrastructure work
**Blocked by:** None (first task)
**Estimated effort:** 2 days
**Contract:** `docs/architecture-design.md` Section 13, `docs/operations.md` Section 2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Every Cena service runs on AWS. Before any ECS task, RDS instance, or ElastiCache cluster can be created, the foundational VPC must exist with proper subnet topology, NAT gateways, and security groups. The architecture calls for `eu-west-1` as primary region with `eu-west-2` as passive DR region. This task provisions the primary region only; DR region is a future task triggered by the quarterly DR drill requirement.

The VPC design must support:
- ECS/Fargate tasks (Proto.Actor cluster, Outreach service, Remotion worker, Python FastAPI)
- RDS PostgreSQL (Marten event store) in private subnets
- ElastiCache Redis in private subnets
- DynamoDB (no VPC endpoint required but recommended for cost)
- ALB in public subnets for SignalR/GraphQL ingress
- NAT gateway for private subnet egress (NATS Synadia Cloud, Neo4j AuraDB, LLM APIs)

---

## Subtasks

### INF-001.1: AWS Account Hardening + Terraform Bootstrap

**Files to create/modify:**
- `infra/terraform/bootstrap/main.tf` — S3 backend + DynamoDB lock table
- `infra/terraform/bootstrap/variables.tf` — region, project name
- `infra/terraform/bootstrap/outputs.tf` — bucket ARN, lock table name
- `infra/terraform/environments/staging/backend.tf` — S3 backend config
- `infra/terraform/environments/prod/backend.tf` — S3 backend config

**Acceptance:**
- [ ] AWS Organization with a dedicated `cena-prod` account (or standalone account with SCPs)
- [ ] MFA enforced on root account
- [ ] IAM Identity Center (SSO) configured for developer access — no long-lived access keys
- [ ] Terraform state bucket: `cena-terraform-state-{account_id}` with versioning enabled, SSE-S3 encryption, and `DeletionProtection` lifecycle rule
- [ ] DynamoDB lock table: `cena-terraform-locks` with `LockID` as partition key
- [ ] `terraform init` succeeds against the remote backend from CI
- [ ] `.gitignore` blocks `*.tfstate`, `*.tfstate.backup`, `.terraform/`

**Test:**
```bash
# Verify bootstrap resources exist
aws s3api head-bucket --bucket "cena-terraform-state-$(aws sts get-caller-identity --query Account --output text)" 2>/dev/null
echo "S3 state bucket: $?"

aws dynamodb describe-table --table-name cena-terraform-locks --query 'Table.TableStatus' --output text
echo "DynamoDB lock table: $?"

# Verify state bucket versioning
aws s3api get-bucket-versioning --bucket "cena-terraform-state-$(aws sts get-caller-identity --query Account --output text)" \
  --query 'Status' --output text | grep -q "Enabled" && echo "PASS: versioning enabled" || echo "FAIL"

# Verify state bucket encryption
aws s3api get-bucket-encryption --bucket "cena-terraform-state-$(aws sts get-caller-identity --query Account --output text)" \
  --query 'ServerSideEncryptionConfiguration.Rules[0].ApplyServerSideEncryptionByDefault.SSEAlgorithm' --output text \
  | grep -q "aws:kms\|AES256" && echo "PASS: encryption enabled" || echo "FAIL"
```

**Edge cases:**
- Bucket name collision (account ID suffix prevents this)
- Concurrent `terraform apply` from two developers — DynamoDB lock table handles this
- Forgot to bootstrap before first `terraform init` — document the error and remediation in README

---

### INF-001.2: VPC, Subnets, and NAT Gateway (Terraform)

**Files to create/modify:**
- `infra/terraform/modules/vpc/main.tf`
- `infra/terraform/modules/vpc/variables.tf`
- `infra/terraform/modules/vpc/outputs.tf`
- `infra/terraform/environments/staging/vpc.tf` — module invocation
- `infra/terraform/environments/prod/vpc.tf` — module invocation

**Acceptance:**
- [ ] VPC CIDR: `10.0.0.0/16` (65,536 addresses — room for growth)
- [ ] 3 Availability Zones in `eu-west-1` (a, b, c)
- [ ] Public subnets: `10.0.1.0/24`, `10.0.2.0/24`, `10.0.3.0/24` (ALB, NAT gateway)
- [ ] Private subnets (app): `10.0.11.0/24`, `10.0.12.0/24`, `10.0.13.0/24` (ECS Fargate tasks)
- [ ] Private subnets (data): `10.0.21.0/24`, `10.0.22.0/24`, `10.0.23.0/24` (RDS, ElastiCache)
- [ ] NAT Gateway: single NAT gateway in AZ-a for cost savings (upgrade to 3x NAT in prod-at-scale)
- [ ] Internet Gateway attached to VPC
- [ ] Route tables: public subnets route `0.0.0.0/0` -> IGW; private subnets route `0.0.0.0/0` -> NAT
- [ ] VPC Flow Logs enabled, sent to CloudWatch Logs (30-day retention)
- [ ] DNS resolution and DNS hostnames enabled (required for RDS, ElastiCache)
- [ ] Tags: `Project=cena`, `Environment=staging|prod`, `ManagedBy=terraform`

**Test:**
```bash
# Validate Terraform plan
cd infra/terraform/environments/staging
terraform validate && echo "PASS: config valid" || echo "FAIL"
terraform plan -out=plan.tfplan
terraform show -json plan.tfplan | jq '.resource_changes | length'
# Expected: ~15-20 resources (VPC, subnets, routes, NAT, IGW, flow logs)

# After apply — verify VPC structure
VPC_ID=$(terraform output -raw vpc_id)
aws ec2 describe-subnets --filters "Name=vpc-id,Values=$VPC_ID" \
  --query 'Subnets[*].[SubnetId,CidrBlock,AvailabilityZone,Tags[?Key==`Name`].Value|[0]]' \
  --output table
# Expect 9 subnets across 3 AZs

# Verify NAT Gateway is active
aws ec2 describe-nat-gateways --filter "Name=vpc-id,Values=$VPC_ID" \
  --query 'NatGateways[*].[State,SubnetId]' --output table
# Expect: State=available

# Verify private subnet can reach internet (via NAT)
# Deploy a test Fargate task in private subnet, curl https://api.ipify.org
```

**Edge cases:**
- AZ capacity constraint in `eu-west-1c` — Terraform will fail; fallback to 2 AZs with adjusted CIDR
- NAT gateway single point of failure — acceptable at <10K users; document upgrade path
- VPC Flow Logs cost — at low traffic, free tier covers it; monitor CloudWatch Logs ingestion

---

### INF-001.3: Security Groups and VPC Endpoints

**Files to create/modify:**
- `infra/terraform/modules/security-groups/main.tf`
- `infra/terraform/modules/security-groups/variables.tf`
- `infra/terraform/modules/security-groups/outputs.tf`

**Acceptance:**
- [ ] `sg-alb`: inbound 443 (HTTPS) from `0.0.0.0/0`; outbound to `sg-ecs-app` on port 5000
- [ ] `sg-ecs-app`: inbound 5000 from `sg-alb`; inbound 5001 (gRPC) from `sg-ecs-app` (cluster inter-node); outbound 5432 to `sg-rds`; outbound 6379 to `sg-redis`; outbound 443 to `0.0.0.0/0` (NATS, Neo4j, LLM APIs via NAT)
- [ ] `sg-rds`: inbound 5432 from `sg-ecs-app` only; no outbound needed
- [ ] `sg-redis`: inbound 6379 from `sg-ecs-app` only; no outbound needed
- [ ] VPC Endpoint for DynamoDB (Gateway type — free, reduces NAT costs)
- [ ] VPC Endpoint for S3 (Gateway type — free, reduces NAT costs for video storage)
- [ ] VPC Endpoint for ECR (Interface type — pulls Docker images without NAT)
- [ ] VPC Endpoint for CloudWatch Logs (Interface type — sends logs without NAT)
- [ ] VPC Endpoint for Secrets Manager (Interface type — fetches secrets without NAT)
- [ ] All security groups use description tags explaining their purpose
- [ ] No security group allows `0.0.0.0/0` on any port except ALB ingress on 443

**Test:**
```bash
# Verify security group rules
VPC_ID=$(terraform output -raw vpc_id)
for sg_name in sg-alb sg-ecs-app sg-rds sg-redis; do
  SG_ID=$(aws ec2 describe-security-groups --filters "Name=vpc-id,Values=$VPC_ID" "Name=group-name,Values=cena-${sg_name}" \
    --query 'SecurityGroups[0].GroupId' --output text)
  echo "=== $sg_name ($SG_ID) ==="
  aws ec2 describe-security-group-rules --filters "Name=group-id,Values=$SG_ID" \
    --query 'SecurityGroupRules[*].[IsEgress,IpProtocol,FromPort,ToPort,CidrIpv4,ReferencedGroupInfo.GroupId]' \
    --output table
done

# Verify VPC endpoints exist
aws ec2 describe-vpc-endpoints --filters "Name=vpc-id,Values=$VPC_ID" \
  --query 'VpcEndpoints[*].[ServiceName,VpcEndpointType,State]' --output table
# Expect: 5 endpoints (DynamoDB, S3, ECR, CloudWatch, Secrets Manager)

# Verify no overly permissive security groups
aws ec2 describe-security-groups --filters "Name=vpc-id,Values=$VPC_ID" \
  --query 'SecurityGroups[*].IpPermissions[?IpRanges[?CidrIp==`0.0.0.0/0`]].[FromPort,ToPort]' --output text
# Expect: only port 443 (ALB)
```

**Edge cases:**
- ECS tasks failing to pull ECR images — missing ECR VPC endpoint or DNS resolution disabled
- Secrets Manager VPC endpoint cost (~$7/month per interface endpoint) — justified by security posture
- Security group rule limit (60 rules per SG) — not a concern at this scale

---

### INF-001.4: ALB + Route 53 + ACM Certificate

**Files to create/modify:**
- `infra/terraform/modules/alb/main.tf`
- `infra/terraform/modules/alb/variables.tf`
- `infra/terraform/modules/alb/outputs.tf`
- `infra/terraform/modules/dns/main.tf` — Route 53 hosted zone + records

**Acceptance:**
- [ ] ALB in public subnets across 3 AZs
- [ ] HTTPS listener on 443 with ACM certificate for `*.cena.app` (or chosen domain)
- [ ] HTTP listener on 80 redirects to HTTPS (301)
- [ ] Target group: `cena-actor-cluster` on port 5000, health check `GET /health/ready` with 10s interval, 3 healthy threshold, 2 unhealthy threshold
- [ ] Target group: `cena-graphql` (same port, separate path-based routing if needed)
- [ ] WebSocket (SignalR) sticky sessions enabled via `stickiness.enabled=true` with 1-day duration
- [ ] Route 53 hosted zone for `cena.app` (or chosen domain)
- [ ] A-record alias pointing to ALB
- [ ] ACM certificate with DNS validation (auto-renewed by AWS)
- [ ] WAF v2 Web ACL attached to ALB with:
  - AWS Managed Rules: `AWSManagedRulesCommonRuleSet`
  - Rate limiting: 2000 requests/5 minutes per IP
  - Geo-restriction: allow only `IL`, `US`, `GB`, `AE`, `JO`, `EG` (Israel + dev + future MENA)

**Test:**
```bash
# Verify ALB exists and is active
ALB_ARN=$(terraform output -raw alb_arn)
aws elbv2 describe-load-balancers --load-balancer-arns "$ALB_ARN" \
  --query 'LoadBalancers[0].[State.Code,DNSName]' --output text
# Expect: active, <dns-name>

# Verify HTTPS listener
aws elbv2 describe-listeners --load-balancer-arn "$ALB_ARN" \
  --query 'Listeners[*].[Port,Protocol,DefaultActions[0].Type]' --output table
# Expect: 443/HTTPS/forward, 80/HTTP/redirect

# Verify ACM certificate is ISSUED
CERT_ARN=$(terraform output -raw acm_certificate_arn)
aws acm describe-certificate --certificate-arn "$CERT_ARN" \
  --query 'Certificate.Status' --output text
# Expect: ISSUED

# Verify health check endpoint responds
ALB_DNS=$(aws elbv2 describe-load-balancers --load-balancer-arns "$ALB_ARN" \
  --query 'LoadBalancers[0].DNSName' --output text)
curl -sk "https://$ALB_DNS/health/ready" -o /dev/null -w "%{http_code}"
# Expect: 503 (no targets yet) — confirms ALB is routing

# Verify WAF is attached
aws wafv2 list-web-acls --scope REGIONAL --query 'WebACLs[?Name==`cena-waf`].ARN' --output text
```

**Edge cases:**
- ACM certificate validation pending — DNS validation requires Route 53 CNAME record; Terraform handles this but propagation takes 5-30 minutes
- SignalR WebSocket upgrade failing through ALB — ALB supports WebSocket natively on HTTP/HTTPS listeners; verify `Connection: Upgrade` header is not stripped
- WAF false positives blocking legitimate traffic — start in COUNT mode for 48 hours before switching to BLOCK
- Domain not yet purchased — use ALB DNS name directly for staging; document domain purchase steps

---

## Integration Test (all subtasks combined)

```bash
#!/bin/bash
set -euo pipefail

echo "=== INF-001 Integration Test ==="

cd infra/terraform/environments/staging

# 1. Terraform validates
terraform validate
echo "PASS: Terraform validates"

# 2. Apply infrastructure
terraform apply -auto-approve

# 3. Verify VPC
VPC_ID=$(terraform output -raw vpc_id)
SUBNET_COUNT=$(aws ec2 describe-subnets --filters "Name=vpc-id,Values=$VPC_ID" --query 'length(Subnets)' --output text)
[ "$SUBNET_COUNT" -eq 9 ] && echo "PASS: 9 subnets" || echo "FAIL: expected 9 subnets, got $SUBNET_COUNT"

# 4. Verify NAT Gateway
NAT_STATE=$(aws ec2 describe-nat-gateways --filter "Name=vpc-id,Values=$VPC_ID" --query 'NatGateways[0].State' --output text)
[ "$NAT_STATE" = "available" ] && echo "PASS: NAT available" || echo "FAIL: NAT state is $NAT_STATE"

# 5. Verify ALB
ALB_STATE=$(aws elbv2 describe-load-balancers --names "cena-alb-staging" --query 'LoadBalancers[0].State.Code' --output text)
[ "$ALB_STATE" = "active" ] && echo "PASS: ALB active" || echo "FAIL: ALB state is $ALB_STATE"

# 6. Verify VPC endpoints
ENDPOINT_COUNT=$(aws ec2 describe-vpc-endpoints --filters "Name=vpc-id,Values=$VPC_ID" --query 'length(VpcEndpoints)' --output text)
[ "$ENDPOINT_COUNT" -ge 5 ] && echo "PASS: $ENDPOINT_COUNT VPC endpoints" || echo "FAIL: expected >=5 endpoints, got $ENDPOINT_COUNT"

# 7. Verify security: no overly permissive SGs
OPEN_PORTS=$(aws ec2 describe-security-groups --filters "Name=vpc-id,Values=$VPC_ID" \
  --query 'SecurityGroups[*].IpPermissions[?IpRanges[?CidrIp==`0.0.0.0/0`]].FromPort' --output text | sort -u)
echo "Open ports from 0.0.0.0/0: $OPEN_PORTS"
# Only 443 should appear

echo "=== INF-001 Integration Test Complete ==="
```

## Rollback Criteria

If this task fails or introduces instability:
- `terraform destroy` tears down all resources cleanly (stateless infrastructure)
- NAT gateway incurs hourly cost ($0.045/hour) — destroy immediately if not proceeding to next tasks
- ALB incurs hourly cost ($0.0225/hour) — same urgency
- VPC itself is free; subnets are free — can be left in place safely

## Definition of Done

- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes in staging
- [ ] `terraform plan` on prod shows identical resource set
- [ ] No security group allows unrestricted access except ALB on 443
- [ ] VPC Flow Logs are being written to CloudWatch
- [ ] WAF Web ACL is attached and in COUNT mode (promoted to BLOCK after 48h observation)
- [ ] Terraform state is stored remotely in S3 with locking
- [ ] PR reviewed by architect
