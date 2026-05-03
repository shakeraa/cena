# RDY-001: CSAM Detection + Content Moderation Fail-Closed

- **Priority**: SHIP-BLOCKER — blocks all image features and pilot with minors
- **Complexity**: Senior engineer — external API integration + circuit breaker wiring
- **Source**: Expert panel audit — Ran (Security), Dina (Architecture)
- **Tier**: 0 (blocks ALL deployment)
- **Effort**: 2-3 weeks (revised from 1 week per Rami's adversarial review)

> **Rami's challenge**: This task duplicates [PP-001](../pre-pilot/TASK-PP-001-csam-detection-wire.md) almost word-for-word. PP-001 has MORE detail (NCMEC CyberTipline, test hash set). Clarify: is PP-001 completed or is RDY-001 starting over? Also: PhotoDNA registration with Microsoft Cloud for Nonprofits may add 1-2 days + approval lag. "1 week" assumes integration is trivial — realistic estimate is 2-3 weeks including registration, integration, incident pipeline, and testing.
>
> **Immediate action**: Contact PhotoDNA THIS WEEK to confirm API availability in Israel and get test hash set.

## Problem

### 1. CSAM detection is a stub

`ContentModerationPipeline.CheckCsamHashAsync()` in `src/shared/Cena.Infrastructure/Moderation/ContentModerationPipeline.cs` returns `false` unconditionally. The code comment explicitly states: "Production: integrate with PhotoDNA or similar hash-matching service. This MUST be implemented before any image upload feature goes live."

Image upload endpoints (`/api/photo/capture`, `/api/photo/upload`) exist and are reachable. A platform serving minors cannot operate with placeholder child safety detection.

### 2. Content moderation fails open

`ClassifyContentAsync()` returns `0.98` (safe) unconditionally when the AI classification service is unavailable. This means all content is auto-approved when the service is down — a fail-open vulnerability for child safety.

### 3. Cost circuit breaker fails open (mismatched comment)

`RedisCostCircuitBreaker.cs` line 74: comment says "failing closed (allowing requests)" but `return false` actually allows all requests through. Redis outage = unlimited LLM spend.

## Scope

### 1. Integrate real CSAM detection

- Integrate with Microsoft PhotoDNA or Google Cloud Vision SafeSearch
- Replace stub with actual hash-matching API call
- If external service is unavailable, **block the upload** (fail-closed)
- Log all CSAM detection results with `[SIEM]` structured tag

### 2. Make content moderation fail-closed

- When AI classification service is unavailable, return `ModerationVerdict.NeedsReview` (not Safe)
- Add circuit breaker on the classification HTTP client (Polly)
- Queue blocked content for manual review when service recovers

### 3. Disable image uploads until CSAM is operational

- Add feature flag `CENA_IMAGE_UPLOAD_ENABLED` (default: false)
- Gate `/api/photo/*` endpoints behind this flag
- Only enable after CSAM integration passes E2E test

### 4. Fix cost circuit breaker

- `RedisCostCircuitBreaker.cs` line 74: change `return false` to `return true` (block requests when Redis is down)
- Update comment to match behavior

## Files to Modify

- `src/shared/Cena.Infrastructure/Moderation/ContentModerationPipeline.cs` — replace stubs
- `src/actors/Cena.Actors/RateLimit/RedisCostCircuitBreaker.cs` — fix fail-open bug (line 74)
- `src/api/Cena.Student.Api.Host/Program.cs` — add feature flag gate on photo endpoints
- New: `src/shared/Cena.Infrastructure/Moderation/PhotoDnaClient.cs` (or CloudVisionClient)

## Acceptance Criteria

- [ ] `CheckCsamHashAsync` calls real external API (not stub)
- [ ] CSAM detection failure (API down) → upload blocked, logged as `[SIEM]` event
- [ ] `ClassifyContentAsync` failure → `ModerationVerdict.NeedsReview` (not Safe)
- [ ] Image upload endpoints return 503 when `CENA_IMAGE_UPLOAD_ENABLED=false`
- [ ] Cost circuit breaker blocks requests when Redis is unavailable
- [ ] Comment on line 74 matches actual behavior
- [ ] E2E test: upload with known CSAM hash → blocked
- [ ] E2E test: upload with AI service down → queued for review
