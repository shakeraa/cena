# RDY-009: OpenAPI/Swagger on Both API Hosts

- **Priority**: High — blocks developer experience and third-party integration
- **Complexity**: Mid engineer
- **Source**: Expert panel audit — Oren (API)
- **Tier**: 2
- **Effort**: 2-3 days

## Problem

Neither `Cena.Student.Api.Host` nor `Cena.Admin.Api.Host` registers SwaggerGen or OpenAPI services. No auto-generated API documentation exists. Frontend developers, mobile team, and future integrators have no discoverable contract.

## Scope

### 1. Add NSwag/Swashbuckle to both hosts

- Register OpenAPI document generation in Program.cs
- Configure endpoint metadata (tags, descriptions, response types)
- Expose `/swagger` UI in Development and Staging environments
- Generate `openapi.json` as build artifact for CI

### 2. Annotate endpoints with response types

- Add `[ProducesResponseType]` or `.Produces<T>()` on all minimal API endpoints
- Include error response shapes (`CenaError`) in documentation
- Document pagination envelope pattern

### 3. Environment gating

- Swagger UI available in Development and Staging only
- OpenAPI JSON available in all environments (for tooling)

## Files to Modify

- `src/api/Cena.Student.Api.Host/Program.cs` — add OpenAPI registration
- `src/api/Cena.Admin.Api/Program.cs` — add OpenAPI registration
- Endpoint files — add response type annotations where missing

## Acceptance Criteria

- [ ] `/swagger` UI accessible in Development
- [ ] `openapi.json` generated as build artifact
- [ ] All endpoints have typed response annotations
- [ ] Error response shape (`CenaError`) documented in schema
- [ ] Swagger UI not accessible in Production
