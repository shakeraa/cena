# INF-007: CI/CD Pipelines — GitHub Actions for All Services

**Priority:** P1 — blocks automated deployment
**Blocked by:** INF-001 (VPC/ALB), INF-002 (ECS cluster), INF-008 (secrets for ECR push)
**Estimated effort:** 3 days
**Contract:** `docs/operations.md` Section 4

---

## Context

The Cena platform is polyglot: .NET 9 (actor cluster, outreach service), Python FastAPI (LLM ACL), Node.js (Remotion worker), React Native (mobile), and React (web PWA). Each service needs its own CI/CD pipeline with the environment topology defined in `docs/operations.md` Section 4.1:

```
dev (feature branches) → staging (main branch, auto-deploy) → prod (manual promote)
```

All pipelines use GitHub Actions. Docker images push to ECR. Backend services deploy to ECS/Fargate or App Runner. Mobile apps deploy via Fastlane to TestFlight/Play Console. The web PWA deploys to S3 + CloudFront.

---

## Subtasks

### INF-007.1: .NET Actor Cluster Pipeline

**Files to create/modify:**
- `.github/workflows/actor-cluster.yml`
- `.github/workflows/reusable/dotnet-build.yml` — reusable build step
- `src/Cena.Actors.Host/Dockerfile`
- `src/Cena.Actors.Host/docker-compose.test.yml` — PostgreSQL testcontainer for integration tests

**Acceptance:**
- [ ] Trigger: `push` to `main` and `pull_request` to `main` (paths: `src/Cena.Actors/**`, `src/Cena.Domain/**`, `src/Cena.Infrastructure/**`)
- [ ] Steps match `docs/operations.md` Section 4.2.1:
  1. Checkout
  2. `dotnet restore`
  3. `dotnet build --configuration Release --no-restore`
  4. `dotnet test` with PostgreSQL testcontainer (unit + integration)
  5. `docker build` tagged with git SHA (`${{ github.sha }}`) and `latest`
  6. `docker push` to ECR (`cena/actor-cluster`)
  7. **[staging]** ECS rolling update: `aws ecs update-service --force-new-deployment`
  8. **[prod]** Blue-green deploy via CodeDeploy:
     - Deploy to green target group
     - Wait 5 min for health checks (`/health/ready`)
     - Shift ALB traffic blue -> green
     - Monitor 10 min (automated rollback on 5xx spike via CloudWatch alarm)
     - Drain blue target group
- [ ] Marten schema change detection: compare schema hash between deployments; if changed, trigger async projection rebuild via ECS exec command
- [ ] Treat warnings as errors in CI: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- [ ] Test results published as GitHub check annotations
- [ ] Docker image scanned with Trivy (fail on CRITICAL/HIGH CVEs)

**Test:**
```bash
# Local pipeline simulation
cd src/Cena.Actors.Host
dotnet restore && dotnet build --configuration Release --no-restore
dotnet test --logger "trx;LogFileName=results.trx" --filter "Category!=E2E"
docker build -t cena/actor-cluster:test .
docker run --rm cena/actor-cluster:test dotnet --info
# Verify image runs and .NET runtime is available

# Trivy scan
trivy image --exit-code 1 --severity CRITICAL,HIGH cena/actor-cluster:test
```

**Edge cases:**
- PostgreSQL testcontainer port conflict in parallel CI jobs — use random port allocation
- Docker build cache invalidation on `dotnet restore` — use multi-stage Dockerfile with dependency layer caching
- ECR push fails due to token expiry — use `aws ecr get-login-password` with fresh credentials per run
- Blue-green deploy rollback triggered — CloudWatch alarm on `HTTPCode_Target_5XX_Count > 10` in 1 minute

---

### INF-007.2: Python FastAPI (LLM ACL) Pipeline

**Files to create/modify:**
- `.github/workflows/llm-acl.yml`
- `services/llm-acl/Dockerfile`
- `services/llm-acl/pytest.ini`

**Acceptance:**
- [ ] Trigger: `push` to `main` (paths: `services/llm-acl/**`)
- [ ] Steps match `docs/operations.md` Section 4.2.2:
  1. Checkout
  2. `pip install -r requirements.txt`
  3. `pytest` with mocked LLM responses (no real API calls in CI)
  4. `ruff check` for linting, `mypy` for type checking
  5. `docker build` tagged with git SHA
  6. `docker push` to ECR (`cena/llm-acl`)
  7. **[staging]** App Runner auto-deploy (triggered by ECR image push via `ImageIdentifier`)
  8. **[prod]** App Runner deploy:
     - Push image to prod ECR
     - App Runner detects new image, deploys new revision
     - Monitor error rate for 5 min via CloudWatch
- [ ] Test coverage report uploaded as artifact (minimum 80% line coverage)
- [ ] Security: `pip-audit` checks for known vulnerabilities in dependencies
- [ ] SymPy sidecar included in Docker image (CAS evaluator)

**Test:**
```bash
cd services/llm-acl
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt -r requirements-dev.txt
pytest --cov=app --cov-report=term-missing --cov-fail-under=80
ruff check app/
mypy app/ --strict
docker build -t cena/llm-acl:test .
docker run --rm cena/llm-acl:test python -c "import app; print('OK')"
```

**Edge cases:**
- SymPy import takes 3+ seconds cold start — pre-import in FastAPI startup event
- App Runner health check path must be configured (`/health`)
- Mock LLM responses must cover all model tiers (Kimi, Sonnet, Opus) and fallback chains

---

### INF-007.3: React Native Mobile App Pipeline

**Files to create/modify:**
- `.github/workflows/mobile.yml`
- `apps/mobile/Fastfile` — Fastlane configuration
- `apps/mobile/Matchfile` — code signing (iOS)
- `.github/workflows/reusable/mobile-build.yml` — reusable build matrix

**Acceptance:**
- [ ] Trigger: `push` to `main` (paths: `apps/mobile/**`)
- [ ] Steps match `docs/operations.md` Section 4.2.4:
  1. Checkout
  2. `npm ci`
  3. `jest` (unit tests)
  4. **[staging]** Detox E2E tests on iOS and Android simulators (macOS runner required)
  5. Fastlane iOS: build -> upload to TestFlight
  6. Fastlane Android: build -> upload to Play Console internal testing track
  7. **[prod]** Manual dispatch workflow:
     - `workflow_dispatch` with `promote_to_prod: true`
     - iOS: submit to App Store Review (24-48h lead time)
     - Android: promote to production track
- [ ] iOS code signing via Fastlane Match (certificates stored in private GitHub repo)
- [ ] Android signing key stored in GitHub Secrets (base64-encoded keystore)
- [ ] Build matrix: iOS (macOS-latest) + Android (ubuntu-latest) in parallel
- [ ] OTA updates: EAS Updates for JS-only changes (bypass App Store review)
- [ ] Build number auto-incremented from GitHub run number

**Test:**
```bash
cd apps/mobile
npm ci
npx jest --ci --coverage --coverageReporters="text-summary"
# Detox (requires macOS)
npx detox build --configuration ios.sim.release
npx detox test --configuration ios.sim.release --headless
```

**Edge cases:**
- iOS build fails due to expired provisioning profile — Fastlane Match auto-renews; verify Match repo access in CI
- Android keystore password in secrets — use `ANDROID_KEYSTORE_PASSWORD` GitHub secret
- Detox flaky tests — retry failed tests once (`--retries 1`); quarantine persistently flaky tests
- App Store review rejection — plan 48-hour buffer for iOS deployments; have expedited review option ready
- React Native native module changes require full build; JS-only changes use EAS Updates

---

### INF-007.4: React PWA Pipeline

**Files to create/modify:**
- `.github/workflows/web.yml`
- `apps/web/Dockerfile` — optional, for preview deployments

**Acceptance:**
- [ ] Trigger: `push` to `main` (paths: `apps/web/**`)
- [ ] Steps match `docs/operations.md` Section 4.2.5:
  1. Checkout
  2. `npm ci`
  3. `npm run build` (production build)
  4. `npm test` (unit + component tests via Vitest or Jest)
  5. **[staging]** Deploy to staging S3 bucket (`cena-web-staging`)
  6. CloudFront invalidation (`/*`)
  7. Verify: `curl` health check on staging CloudFront URL
  8. **[prod]** Deploy to production S3 bucket (`cena-web-prod`) + CloudFront invalidation
- [ ] Lighthouse CI check: performance >90, accessibility >90, best-practices >90
- [ ] Bundle size check: fail if main bundle >500KB gzipped (alert at >400KB)
- [ ] PWA manifest validation: offline capability verified
- [ ] S3 bucket configured with website hosting, versioning enabled for rollback

**Test:**
```bash
cd apps/web
npm ci
npm run build
npm test -- --ci --coverage
# Lighthouse CI
npx lhci autorun --config=lighthouserc.json
# Bundle size
du -sh build/static/js/*.js | sort -rh | head -5
# Verify build output
ls -la build/index.html build/manifest.json build/service-worker.js
```

**Edge cases:**
- CloudFront cache not invalidating — use `/*` invalidation; verify with `curl -I` checking `x-cache` header
- S3 sync deleting old assets while users have cached HTML pointing to them — use content-hashed filenames for JS/CSS
- Service worker caching stale content — version the service worker; force update on deploy

---

### INF-007.5: Shared Pipeline Components + Secrets Management

**Files to create/modify:**
- `.github/workflows/reusable/docker-build-push.yml` — reusable Docker build/push
- `.github/workflows/reusable/notify-slack.yml` — Slack notification on failure
- `.github/actions/setup-aws/action.yml` — composite action for AWS credential setup
- `.github/workflows/deploy-prod.yml` — manual production promotion (all services)

**Acceptance:**
- [ ] Reusable Docker build/push workflow: accepts `service_name`, `dockerfile_path`, `ecr_repo` as inputs
- [ ] AWS credentials via OIDC (GitHub Actions -> IAM role) — no long-lived access keys in GitHub Secrets
- [ ] GitHub OIDC provider configured in AWS IAM with trust policy scoped to `repo:org/cena:*`
- [ ] Slack notification on pipeline failure to `#cena-alerts` channel
- [ ] Production promotion workflow (`deploy-prod.yml`):
  - `workflow_dispatch` with service selector (dropdown: actor-cluster, llm-acl, web, mobile)
  - Requires approval from `cena-leads` team (GitHub Environment protection rule)
  - Deploys the staging-verified image (same SHA) to prod
- [ ] ECR lifecycle policy: retain last 20 images per repo, delete untagged images after 1 day
- [ ] Pipeline secrets documented (not in code):
  - `AWS_OIDC_ROLE_ARN` — IAM role for GitHub Actions
  - `SLACK_WEBHOOK_URL` — failure notifications
  - `APPLE_CONNECT_API_KEY` — iOS App Store Connect
  - `ANDROID_KEYSTORE_BASE64` — Android signing
  - `MATCH_GIT_PRIVATE_KEY` — Fastlane Match repo access

**Test:**
```bash
# Verify OIDC trust policy
aws iam get-role --role-name github-actions-cena --query 'Role.AssumeRolePolicyDocument' | jq .
# Expect: Condition with token.actions.githubusercontent.com

# Verify ECR repos exist
for repo in actor-cluster llm-acl remotion-worker outreach-service; do
  aws ecr describe-repositories --repository-names "cena/$repo" \
    --query 'repositories[0].repositoryUri' --output text
done

# Verify ECR lifecycle policy
aws ecr get-lifecycle-policy --repository-name "cena/actor-cluster" \
  --query 'lifecyclePolicyText' | jq -r . | jq '.rules[].selection'

# Test Slack notification (dry run)
curl -X POST "$SLACK_WEBHOOK_URL" \
  -H 'Content-type: application/json' \
  -d '{"text":"[TEST] CI/CD pipeline notification test"}'
```

**Edge cases:**
- GitHub OIDC token expiry during long builds (>1 hour) — split long builds into separate jobs with fresh tokens
- ECR cross-account access for staging -> prod promotion — use ECR replication or push to both accounts
- Slack webhook rate limiting — batch failure notifications; don't notify on every retry
- Production deploy blocked by missing approval — timeout after 24 hours, notify in Slack

---

## Integration Test (all subtasks combined)

```bash
#!/bin/bash
set -euo pipefail

echo "=== INF-007 Integration Test ==="

# 1. Verify all workflow files exist and are valid YAML
for wf in actor-cluster llm-acl mobile web deploy-prod; do
  FILE=".github/workflows/${wf}.yml"
  [ -f "$FILE" ] && echo "PASS: $FILE exists" || echo "FAIL: $FILE missing"
  python -c "import yaml; yaml.safe_load(open('$FILE'))" 2>/dev/null && echo "PASS: valid YAML" || echo "FAIL: invalid YAML"
done

# 2. Verify reusable workflows
for wf in docker-build-push notify-slack dotnet-build mobile-build; do
  FILE=".github/workflows/reusable/${wf}.yml"
  [ -f "$FILE" ] && echo "PASS: $FILE exists" || echo "FAIL: $FILE missing"
done

# 3. Verify ECR repos
REPOS=$(aws ecr describe-repositories --query 'repositories[*].repositoryName' --output text | tr '\t' '\n' | grep "^cena/" | wc -l | tr -d ' ')
[ "$REPOS" -ge 4 ] && echo "PASS: $REPOS ECR repos" || echo "FAIL: expected >=4 repos, got $REPOS"

# 4. Verify OIDC provider
aws iam list-open-id-connect-providers --query 'OpenIDConnectProviderList[*].Arn' --output text \
  | grep -q "token.actions.githubusercontent.com" && echo "PASS: OIDC provider" || echo "FAIL: no OIDC provider"

# 5. Dry-run: trigger a workflow (actor-cluster) on a test branch
# gh workflow run actor-cluster.yml --ref test-ci-dry-run
echo "Manual: trigger a workflow run and verify it passes"

echo "=== INF-007 Integration Test Complete ==="
```

## Rollback Criteria

If a pipeline deploys a broken service:
- **Actor cluster:** shift ALB traffic back to blue target group (<1 minute — `docs/operations.md` Section 4.6)
- **LLM ACL:** App Runner revert to previous revision (<2 minutes)
- **Mobile:** EAS Updates rollback for JS changes (minutes); App Store revert for native (24-48h)
- **Web PWA:** redeploy previous build from S3 versioning (<5 minutes)
- **Pipeline itself broken:** revert the workflow YAML change in git; previous pipeline version takes effect on next push

## Definition of Done

- [ ] All 5 service pipelines defined and validated
- [ ] Staging auto-deploys on merge to `main`
- [ ] Production requires manual promotion with team approval
- [ ] AWS credentials use OIDC — no long-lived keys in GitHub Secrets
- [ ] Docker images scanned for vulnerabilities (Trivy)
- [ ] Slack notifications on failure
- [ ] ECR lifecycle policies prevent image accumulation
- [ ] Rollback procedures documented and tested
- [ ] All Dockerfiles build successfully locally
- [ ] PR reviewed by architect
