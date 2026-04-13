# PP-001: Wire CSAM Hash Detection to PhotoDNA (CRITICAL)

- **Priority**: Critical — legal blocker for any image upload feature
- **Complexity**: Architect level — requires service integration, key management, incident reporting pipeline
- **Blocks**: PHOTO-001 (student capture), PHOTO-002 (PDF ingestion), any photo upload going live
- **Source**: Expert panel review § Assessment Security (Ran)

## Problem

`ContentModerationPipeline.CheckCsamHashAsync` in `src/shared/Cena.Infrastructure/Moderation/ContentModerationPipeline.cs:132-137` returns `Task.FromResult(false)` unconditionally. The AI classification stage (`ClassifyContentAsync` at line 140) returns `Task.FromResult(0.98)` (always safe). Both stages are non-functional placeholders.

Any platform that allows minors to upload images has a legal obligation to detect CSAM material. In most jurisdictions this is a mandatory reporting requirement, not an optional feature.

## Scope

### Stage 1: CSAM Hash Detection
1. Integrate with Microsoft PhotoDNA Cloud Service (or NCMEC hash list if PhotoDNA is unavailable in Israel)
2. Register for a PhotoDNA API key via the Microsoft Cloud for Nonprofits/Education program
3. Implement `CheckCsamHashAsync` to:
   - Compute the perceptual hash of the uploaded image
   - Compare against the PhotoDNA hash database
   - On match: immediately block upload, log critical event, file incident report to NCMEC CyberTipline
   - Return true (CSAM detected) or false (clean)
4. Add a circuit breaker around the PhotoDNA call — if the service is unavailable, all image uploads must be BLOCKED (fail-closed, not fail-open)
5. Add OTel counter `cena.moderation.csam.checks.total` with tags for result (clean/detected/error)

### Stage 2: AI Safety Classification
1. Integrate with Azure AI Content Safety (or Google Cloud Vision SafeSearch if Azure is unavailable)
2. Implement `ClassifyContentAsync` to return a real safety score [0.0, 1.0]
3. The existing threshold logic in `ModerateAsync` (auto-approve at 0.95 for minors, auto-block at 0.30, human review in between) is correct and should remain

### Stage 3: Incident Reporting
1. When CSAM is detected, automatically generate an incident report containing: content hash, upload timestamp, uploader ID, IP address
2. Store the incident report in a sealed audit log (append-only, tamper-evident)
3. The actual NCMEC CyberTipline submission requires a human-in-the-loop — the system queues the report for the designated safety officer

## Files to Modify

- `src/shared/Cena.Infrastructure/Moderation/ContentModerationPipeline.cs` — replace placeholder implementations
- `src/shared/Cena.Infrastructure/Moderation/PhotoDnaClient.cs` — NEW: HTTP client for PhotoDNA API
- `src/shared/Cena.Infrastructure/Moderation/ContentSafetyClient.cs` — NEW: HTTP client for Azure AI Content Safety
- `src/shared/Cena.Infrastructure/Moderation/IncidentReportService.cs` — NEW: sealed audit log + NCMEC queue
- Configuration: API keys via environment variables, never in source

## Acceptance Criteria

- [ ] `CheckCsamHashAsync` calls a real hash-matching service (PhotoDNA or equivalent)
- [ ] Service unavailability causes fail-closed behavior (uploads blocked, not allowed)
- [ ] `ClassifyContentAsync` calls a real AI classification service
- [ ] CSAM detection triggers: block upload, critical log, incident report queued
- [ ] OTel metrics for moderation pipeline operational
- [ ] Integration test with known-safe test images (do NOT use real CSAM material in tests — use PhotoDNA's test hash set)
- [ ] Circuit breaker wraps external service calls

## Out of Scope

- Human review UI for NeedsReview verdicts (separate task)
- NCMEC CyberTipline API integration (requires organization registration — manual process)
