# REV-016: Admin API Restructure (Feature Folders, Error Handling, Duplicate Registration)

**Priority:** P2 -- MEDIUM (50+ files flat in one directory, no global error handler, duplicate host registration)
**Blocked by:** None
**Blocks:** None (improves maintainability)
**Estimated effort:** 2 days
**Source:** System Review 2026-03-28 -- Lead Architect (API Layer, Architecture P2/P5), Backend Senior (I4, I5)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Three structural issues in the Admin API:

1. **Flat file structure**: 50+ `.cs` files in `Cena.Admin.Api/` with no subdirectories. DTOs, services, endpoints, and quality gate scorers are siblings. Finding files requires searching, not browsing.
2. **Duplicate service registration**: Both `Cena.Actors.Host/Program.cs` and `Cena.Api.Host/Program.cs` register the same 15+ admin services and map the same endpoints. Changes must be synchronized across two files.
3. **No global error handler**: Unhandled exceptions produce default .NET responses that may leak stack traces. Actor Host has no error-handling middleware at all.

## Architect's Decision

1. **Feature folders**: Organize by domain concern, not by type. Each folder contains its service, endpoints, and DTOs together.
2. **Shared registration**: Extract `builder.Services.AddCenaAdminServices()` and `app.MapCenaAdminEndpoints()` into extension methods in the Admin API project. Both hosts call these methods.
3. **Global error handler**: Add `UseExceptionHandler` with structured JSON error responses.

## Subtasks

### REV-016.1: Reorganize into Feature Folders

**Target structure:**
```
src/api/Cena.Admin.Api/
  Questions/
    QuestionBankService.cs
    QuestionBankEndpoints.cs
    QuestionDtos.cs
    QualityGate/
      QualityGateService.cs
      BloomAlignmentScorer.cs
      DistractorQualityScorer.cs
      StemClarityScorer.cs
      StructuralValidator.cs
  Users/
    AdminUserService.cs
    AdminUserEndpoints.cs
  Roles/
    AdminRoleService.cs
    AdminRoleEndpoints.cs
  Dashboard/
    AdminDashboardService.cs
    AdminDashboardEndpoints.cs
  Mastery/
    MasteryTrackingService.cs
    MasteryTrackingEndpoints.cs
  Focus/
    FocusAnalyticsService.cs
    FocusAnalyticsEndpoints.cs
  Moderation/
    ContentModerationService.cs
    ContentModerationEndpoints.cs
  Tutoring/
    TutoringAdminService.cs
    TutoringAdminEndpoints.cs
  System/
    SystemMonitoringService.cs
    SystemMonitoringEndpoints.cs
  AI/
    AiGenerationService.cs
    AiGenerationEndpoints.cs
  Ingestion/
    IngestionPipelineService.cs
    IngestionPipelineEndpoints.cs
  Pedagogy/
    MethodologyAnalyticsService.cs
    MethodologyAnalyticsEndpoints.cs
  Registration/
    CenaAdminServiceRegistration.cs   -- shared DI extension
    CenaAdminEndpointRegistration.cs  -- shared endpoint mapping
```

**Acceptance:**
- [ ] No `.cs` files remain directly in `Cena.Admin.Api/` root (except `Program.cs` if it exists)
- [ ] Each feature folder is self-contained (service + endpoints + DTOs)
- [ ] `AdminApiEndpoints.cs` (960 lines) is split into per-domain endpoint files
- [ ] All namespaces updated to match folder structure
- [ ] Build succeeds, all 224 tests pass

### REV-016.2: Extract Shared Service Registration

**File to create:** `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs`

```csharp
public static class CenaAdminServiceRegistration
{
    public static IServiceCollection AddCenaAdminServices(this IServiceCollection services)
    {
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        // ... all 15+ services
        return services;
    }

    public static WebApplication MapCenaAdminEndpoints(this WebApplication app)
    {
        AdminDashboardEndpoints.Map(app);
        AdminUserEndpoints.Map(app);
        // ... all endpoint groups
        return app;
    }
}
```

**Files to modify:**
- `src/actors/Cena.Actors.Host/Program.cs` -- replace 30+ lines of service registration with `builder.Services.AddCenaAdminServices()`
- `src/api/Cena.Api.Host/Program.cs` -- replace with same call

**Acceptance:**
- [ ] Service registration exists in ONE place
- [ ] Both hosts call `AddCenaAdminServices()` and `MapCenaAdminEndpoints()`
- [ ] Adding a new admin service requires editing ONE file
- [ ] Both hosts serve identical admin endpoints

### REV-016.3: Add Global Exception Handler

**Files to modify:**
- `src/api/Cena.Api.Host/Program.cs`
- `src/actors/Cena.Actors.Host/Program.cs`

```csharp
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";
        var error = context.Features.Get<IExceptionHandlerFeature>();

        var (statusCode, message) = error?.Error switch
        {
            BadHttpRequestException e => (400, e.Message),
            UnauthorizedAccessException => (401, "Unauthorized"),
            KeyNotFoundException => (404, "Resource not found"),
            _ => (500, app.Environment.IsDevelopment()
                ? error?.Error?.Message ?? "Internal server error"
                : "Internal server error")
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { error = message, status = statusCode });
    });
});
```

**Acceptance:**
- [ ] Unhandled exceptions return structured JSON `{ error, status }`
- [ ] Stack traces never leak in non-Development environments
- [ ] 400/401/404 errors return appropriate status codes
- [ ] Both hosts have the exception handler registered before endpoint mapping
