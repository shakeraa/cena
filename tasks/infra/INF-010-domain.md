# INF-010: Domain + SSL — Route53, ACM Certificate, CloudFront

**Priority:** P1 — blocks production URL
**Blocked by:** None
**Estimated effort:** 1 day
**Contract:** None (infrastructure)

---

## Context

Production domain `cena.edu` (or similar) with SSL certificates via AWS ACM. Subdomains: `api.cena.edu` (backend), `app.cena.edu` (web app), `cdn.cena.edu` (CloudFront diagrams).

## Subtasks

### INF-010.1: Route53 Hosted Zone + DNS Records

**Files to create/modify:**
- `infra/terraform/modules/domain/main.tf`

**Acceptance:**
- [ ] Route53 hosted zone for `cena.edu`
- [ ] A record: `api.cena.edu` -> ALB
- [ ] A record: `app.cena.edu` -> CloudFront (webapp)
- [ ] CNAME: `cdn.cena.edu` -> CloudFront (diagrams)
- [ ] MX records for email (if needed)
- [ ] TTL: 300 seconds for all records

**Test:**
```bash
dig api.cena.edu +short
# Assert: resolves to ALB IP
```

---

### INF-010.2: ACM Certificate + CloudFront Association

**Files to create/modify:**
- `infra/terraform/modules/domain/acm.tf`

**Acceptance:**
- [ ] Wildcard certificate: `*.cena.edu` via ACM (DNS validation)
- [ ] Certificate in `us-east-1` (required for CloudFront)
- [ ] Auto-renewal enabled (ACM default)
- [ ] CloudFront distributions use the ACM certificate
- [ ] ALB listener on 443 uses the ACM certificate
- [ ] HTTP -> HTTPS redirect on ALB and CloudFront

**Test:**
```bash
curl -I https://api.cena.edu/health/ready
# Assert: 200, valid SSL certificate
```

---

## Rollback Criteria
- DNS changes propagate globally; keep old records as fallback

## Definition of Done
- [ ] Domain resolves to correct services
- [ ] SSL/TLS working on all subdomains
- [ ] PR reviewed by architect
