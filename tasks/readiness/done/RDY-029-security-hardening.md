# RDY-029: Security Hardening Bundle

- **Priority**: High — multiple security gaps identified across stack
- **Complexity**: Senior DevOps + Security engineer
- **Source**: Cross-review — Ran (Security)
- **Tier**: 2 (blocks production, not pilot)
- **Effort**: 3-4 weeks (parallelizable sub-tasks)

## Problem

Cross-persona security review identified 12 categories of missing security controls that exist in no other task. These are not design gaps — the platform's security architecture is sound — but implementation gaps that must be closed before production with real student data.

## Sub-Tasks

### 1. SBOM (Software Bill of Materials) — 2 days
- Generate SBOM for all .NET projects (CycloneDX or SPDX format)
- Add to CI: `dotnet CycloneDX` on every build
- Publish SBOM artifact with each release

### 2. Security headers — 1 day
- Add `X-Content-Type-Options: nosniff`
- Add `X-Frame-Options: DENY`
- Add `Strict-Transport-Security` (HSTS)
- Add `Referrer-Policy: strict-origin-when-cross-origin`
- Verify headers via automated test

### 3. Content Security Policy (CSP) — 2 days
- Define CSP for student app (allow KaTeX, MathLive, Firebase, Anthropic)
- Define CSP for admin app
- Report-only mode first, then enforce

### 4. Secrets rotation plan — 1 day
- Document rotation schedule for: Firebase SA key, NATS credentials, PostgreSQL passwords, Redis auth, Anthropic API key
- Add monitoring: alert when secret age > rotation threshold

### 5. Audit logging — 3 days
- Log all admin actions (CRUD on questions, user management)
- Log all authentication events (login, logout, failed attempts)
- Structured format: `{ actor, action, resource, timestamp, ip, result }`
- Ship to external log aggregator (not just local files)

### 6. Vendor security assessment — 2 days
- Document security posture of each vendor: Anthropic, Firebase, NATS (Synadia), Redis, Neo4j
- Review each vendor's SOC 2 / ISO 27001 status
- Document data residency for each vendor

### 7. Encryption at rest — 2 days
- Verify PostgreSQL TDE (Transparent Data Encryption) is enabled
- Verify Redis persistence encryption
- Document encryption status for all data stores

### 8. Data subject rights portal — 3 days
- Implement "Download my data" endpoint (GDPR Art. 15)
- Implement "Delete my data" endpoint (GDPR Art. 17)
- Parent-initiated only (minors' data, parental consent required)

### 9. Rate limiting on auth endpoints — 1 day
- Add rate limiting to login, registration, password reset
- Threshold: 5 attempts per minute per IP
- Return 429 with `Retry-After` header

### 10. Incident response runbook — 2 days
- Step-by-step runbook for: data breach, DDoS, compromised credentials, service outage
- Contact list: legal, engineering, management
- Communication templates for parents, schools, authorities

### 11. Compliance monitoring dashboard — 3 days
- Dashboard showing: data retention compliance, consent collection rates, DPA status
- Alert on: retention period violations, missing consents, expiring DPAs

### 12. Penetration test plan — 1 day
- Define scope for pre-production pen test
- Document what's in-scope (APIs, web apps) and out-of-scope
- Schedule with external security firm

## Files to Modify

- New: `deploy/security/security-headers.middleware.cs` or nginx config
- New: `deploy/security/csp-policy.json`
- New: `docs/security/secrets-rotation-plan.md`
- New: `docs/security/vendor-assessment.md`
- New: `docs/security/incident-runbook.md`
- New: `docs/security/pen-test-plan.md`
- `src/api/Cena.Student.Api.Host/Program.cs` — security headers middleware
- `src/api/Cena.Admin.Api.Host/Program.cs` — audit logging middleware
- New: `src/api/Cena.Student.Api.Host/Endpoints/DataRightsEndpoints.cs`

## Acceptance Criteria

- [ ] SBOM generated in CI for every build
- [ ] All security headers present on API responses
- [ ] CSP deployed in report-only mode
- [ ] Secrets rotation schedule documented with monitoring
- [ ] Admin audit log captures all CRUD + auth events
- [ ] Vendor security assessment documented for all 5 vendors
- [ ] Data encryption at rest verified for all stores
- [ ] Data rights endpoints functional (download + delete)
- [ ] Rate limiting on auth endpoints (5/min/IP)
- [ ] Incident response runbook reviewed by team
- [ ] Compliance monitoring dashboard operational
- [ ] Pen test scope documented and scheduled
