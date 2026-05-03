# TASK-STB-09: Analytics Extensions + Exports

**Priority**: MEDIUM
**Effort**: 3-4 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md)
**UI consumers**: [STU-W-09](../student-web/TASK-STU-W-09-progress-mastery.md)
**Status**: Not Started

---

## Goal

Extend the existing analytics endpoints with the three new dimensions the web progress dashboard needs, add per-concept history, and implement the PDF progress report + share-token generator.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/analytics/time-breakdown?period=30d` | Daily time, subject split, hour-of-day heatmap | `api` | JWT |
| `GET` | `/api/analytics/flow-vs-accuracy?period=30d` | Dual-series trend | `api` | JWT |
| `GET` | `/api/analytics/concepts/{id}/history?period=90d` | Mastery history + last 10 attempts for a concept | `api` | JWT |
| `POST` | `/api/analytics/export/pdf` | Trigger PDF progress report generation | `api` (5/day) | JWT |
| `GET` | `/api/analytics/export/{jobId}` | Poll PDF job status / download URL | `api` | JWT |
| `POST` | `/api/me/share-tokens` | Create a parent/tutor share token | `api` (5/day) | JWT |
| `GET` | `/api/me/share-tokens` | List active share tokens | `api` | JWT |
| `DELETE` | `/api/me/share-tokens/{id}` | Revoke a share token | `api` | JWT |
| `GET` | `/api/shared/progress?token=` | Public read-only progress view via token | `share-token` (30/min) | token-only |

Already exist (unchanged): `/api/analytics/summary`, `/api/analytics/mastery`, `/api/analytics/progress`.

## Data Access

- **Reads**:
  - `ConceptAttemptedProjection` — aggregated for time breakdown
  - `FlowScoreProjection` (new async) — aggregated per day with accuracy join
  - `ConceptMasteryHistoryProjection` (new async) — per-student per-concept mastery over time
  - `ShareTokenDocument` (new)
- **Writes**: share token generation; append `ShareTokenCreated_V1`, `ShareTokenRevoked_V1`
- **Async projections** again carry the weight — no endpoint runs a live aggregate over raw events

## PDF Export

- `POST /api/analytics/export/pdf` queues a job (simple background worker, not a full BullMQ / Hangfire install — a single-threaded in-process worker is fine for v1)
- Job renders a styled PDF server-side using existing tooling (QuestPDF is already in the .NET ecosystem and commercially license-friendly for non-commercial use; confirm license before committing)
- PDF includes: student display name, date range, KPIs, charts (pre-rendered as PNG via QuestPDF chart helpers or SkiaSharp), mastery breakdown, recent sessions
- Client polls `GET /api/analytics/export/{jobId}` until `status = ready` and then downloads the signed URL
- Alternative: fire a `ExportReady` hub event (cleaner, reuse the hub pattern; confirm on the day whether polling or pushing is simpler)

## Share Tokens

- Token: 32-byte random, URL-safe base64
- Scopes (v1): `{ progress: boolean, mastery: boolean, sessions: boolean }`
- Expiry: up to 30 days, client picks within that cap
- `GET /api/shared/progress?token=` validates the token, enforces expiry + scope, and returns a limited read-only projection (no PII beyond display name)

## Contracts

Add to `Cena.Api.Contracts/Dtos/Analytics/`:

- `TimeBreakdownDto`, `DailyTimeDto`, `SubjectTimeDto`, `HourHeatmapEntryDto`
- `FlowVsAccuracyDto`, `FlowVsAccuracyPointDto`
- `ConceptHistoryDto`, `ConceptAttemptSummaryDto`
- `PdfExportRequestDto`, `PdfExportJobDto`
- `ShareTokenCreateRequestDto`, `ShareTokenDto`, `SharedProgressDto`

## Auth & Authorization

- Firebase JWT on the student-owned endpoints
- `ResourceOwnershipGuard` on all student reads
- `/api/shared/progress?token=` has **no JWT requirement**; the token itself is the auth
- Share token reads log to `StudentRecordAccessLog` for FERPA compliance

## Cross-Cutting

- Analytics endpoints cacheable privately with ETag + 5-min TTL
- Share token endpoint rate-limited per token
- PDF generation capped at 10 MB per file
- Handler logs with `correlationId`; share token reads log with `tokenId`
- Every new async projection is registered and has a rebuild test

## Definition of Done

- [ ] All 9 endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts/Dtos/Analytics/`
- [ ] New async projections enabled and tested against seeded events
- [ ] PDF export generates a valid, styled PDF for a 30-day dataset
- [ ] Share token creation enforces expiry cap and scope
- [ ] Share token revocation invalidates the token immediately
- [ ] `/api/shared/progress` works without JWT and returns the scoped subset
- [ ] FERPA access log entries created on every share token read
- [ ] Integration tests cover: time breakdown 30d, flow-vs-accuracy, concept history, PDF export job lifecycle, share token create/revoke/read
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated
- [ ] Mobile lead review: mobile will eventually adopt the share token feature

## Out of Scope

- Teacher gradebook PDFs — admin surface
- Export format other than PDF for progress reports — use PDF only
- Sharing individual session replays — future
- Parent account system — the share token model intentionally avoids needing real parent accounts for v1
