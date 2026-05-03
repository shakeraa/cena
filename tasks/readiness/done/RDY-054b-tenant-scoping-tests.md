# RDY-054b: Tenant-Scoping Tests Repair

- **Priority**: High — multi-tenant safety
- **Complexity**: Mid engineer
- **Effort**: 3-5 hours

## Failing tests (baseline 2026-04-15)

- `Cena.Admin.Api.Tests.QueryAllRawEventsTenantTests.AdminDashboardService_TopicsMastery_HasTenantScoping`
- `Cena.Admin.Api.Tests.QueryAllRawEventsTenantTests.EventStreamService_GetRecentEvents_HasTenantScoping`
- `Cena.Admin.Api.Tests.AdminUserServiceTenantScopingTests.Implementation_LogsCrossTenantAccessAttempts`

## Hypothesis

These scan service source code for tenant-scope patterns. After the RDY-037 layering refactor and RDY-038/039/041 ship-blocker fixes, either the pattern strings drifted or the files moved.

## Acceptance

- [ ] Each test either pins the updated pattern or the service gains the missing scoping
- [ ] 3 tests green
