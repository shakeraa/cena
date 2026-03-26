# INF-005: S3 Buckets + CloudFront — Diagrams, Exports, Web App, CORS

**Priority:** P1 — blocks diagram delivery, analytics exports
**Blocked by:** INF-001 (VPC), INF-010 (Domain)
**Estimated effort:** 2 days
**Contract:** `contracts/data/s3-export-schema.json`, `contracts/llm/diagram-generation-pipeline.py`

---

## Context

Three S3 buckets: (1) diagram assets served via CloudFront CDN, (2) anonymized Parquet exports for ML training, (3) static web app hosting. CloudFront provides global CDN with CORS for the mobile app.

## Subtasks

### INF-005.1: S3 Bucket Definitions

**Files to create/modify:**
- `infra/terraform/modules/s3/main.tf`

**Acceptance:**
- [ ] `cena-diagrams-{env}`: public-read via CloudFront, versioning enabled, lifecycle: delete old versions after 90 days
- [ ] `cena-exports-{env}`: private, server-side encryption (SSE-S3), lifecycle: transition to Glacier after 365 days
- [ ] `cena-webapp-{env}`: static website hosting, CloudFront origin
- [ ] All buckets: block public access (except via CloudFront OAI), access logging enabled

**Test:**
```bash
aws s3api get-bucket-encryption --bucket cena-exports-prod   | jq '.ServerSideEncryptionConfiguration'
# Assert: SSE-S3 enabled
```

---

### INF-005.2: CloudFront Distribution

**Files to create/modify:**
- `infra/terraform/modules/cloudfront/main.tf`

**Acceptance:**
- [ ] CloudFront distribution with custom domain `cdn.cena.edu`
- [ ] Origin: `cena-diagrams` bucket via OAI
- [ ] CORS headers: allow `*.cena.edu` and mobile app origins
- [ ] Cache policy: 24h TTL for diagrams, 1h for web app
- [ ] Compress: gzip + brotli for SVG/JS/CSS
- [ ] Custom error page: `/index.html` for SPA routing (webapp)

**Test:**
```bash
curl -I https://cdn.cena.edu/diagrams/math-addition/v1/recall.svg
# Assert: 200, Content-Type: image/svg+xml, Cache-Control: max-age=86400
```

---

### INF-005.3: CORS Configuration

**Files to create/modify:**
- `infra/terraform/modules/s3/cors.tf`

**Acceptance:**
- [ ] CORS allows: `GET`, `HEAD` from `*.cena.edu` and `capacitor://localhost` (mobile)
- [ ] Preflight cached for 3600s
- [ ] No wildcard `*` origin in production

**Test:**
```bash
curl -H "Origin: https://app.cena.edu" -H "Access-Control-Request-Method: GET"   -X OPTIONS https://cdn.cena.edu/diagrams/test.svg
# Assert: Access-Control-Allow-Origin: https://app.cena.edu
```

---

## Rollback Criteria
- Serve diagrams directly from S3 without CloudFront (slower, higher cost)

## Definition of Done
- [ ] 3 buckets created with correct policies
- [ ] CloudFront serving diagrams with correct CORS
- [ ] PR reviewed by architect
