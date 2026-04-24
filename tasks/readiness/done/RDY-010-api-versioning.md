# RDY-010: REST API Versioning

- **Priority**: High — blocks safe mobile app updates
- **Complexity**: Mid engineer
- **Source**: Expert panel audit — Oren (API)
- **Tier**: 2
- **Effort**: 3-5 days

## Problem

API contracts document specifies `/api/v1/` route prefixes with 6-month overlap for mobile backward compatibility. Reality: all routes are `/api/` with no version prefix. If a breaking change is needed, old mobile clients will break immediately.

## Scope

### 1. Add version prefix routing

- All current endpoints become `/api/v1/...`
- Add backward-compatible redirect from `/api/...` → `/api/v1/...` (temporary, 6-month sunset)
- Use ASP.NET API versioning package (`Asp.Versioning.Http`)

### 2. Deprecation headers

- Emit `Sunset` header on deprecated endpoints
- Emit `Deprecation` header with date
- Log usage of deprecated endpoints for analytics

### 3. Document version lifecycle

- Version 1 is current and active
- New versions created only for breaking changes
- Minimum 6-month overlap between versions

## Files to Modify

- `src/api/Cena.Student.Api.Host/Program.cs` — add versioning middleware
- `src/api/Cena.Admin.Api/Program.cs` — add versioning middleware
- All endpoint mapping files — add version group prefix
- `docs/api-contracts.md` — update to reflect implemented versioning

## Acceptance Criteria

- [ ] All endpoints accessible at `/api/v1/...`
- [ ] `/api/...` redirects to `/api/v1/...` with deprecation header
- [ ] `Sunset` header emitted on deprecated routes
- [ ] API versioning package integrated
- [ ] Version lifecycle documented
