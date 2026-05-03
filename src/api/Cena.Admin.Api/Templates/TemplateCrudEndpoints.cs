// =============================================================================
// Cena Platform — Parametric Template CRUD + Preview Endpoints (prr-202)
//
// REST surface for admin template authoring. Group: /api/admin/templates.
// Policy: AdminOnly (ADMIN + SUPER_ADMIN). Student-role / teacher-role → 403.
// Rate-limit: "api" bucket for list/get/update/delete, "ai" bucket for preview
// (preview fans out through the CAS sidecar).
//
// Routes:
//   GET    /api/admin/templates                 → list
//   GET    /api/admin/templates/{id}            → detail
//   POST   /api/admin/templates                 → create
//   PUT    /api/admin/templates/{id}            → update (versioned)
//   DELETE /api/admin/templates/{id}            → soft-delete
//   POST   /api/admin/templates/{id}/preview    → CAS-verified preview
//
// The POST /api/admin/templates/generate route from prr-200 (dry-run, transient
// template) stays as-is — it's wired by MapParametricTemplateEndpoints in the
// same /api/admin/templates group.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Templates;

public static class TemplateCrudEndpoints
{
    public static IEndpointRouteBuilder MapTemplateCrudEndpoints(this IEndpointRouteBuilder app)
    {
        var crud = app.MapGroup("/api/admin/templates")
            .WithTags("Parametric Template Authoring")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        // GET list
        crud.MapGet("", async (
            HttpContext http,
            IParametricTemplateAuthoringService service,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var filter = ParseFilter(http);
            try
            {
                var response = await service.ListAsync(filter, user, ct);
                return Results.Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        })
        .WithName("ListParametricTemplates")
        .Produces<TemplateListResponseDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        // GET detail
        crud.MapGet("{id}", async (
            string id,
            IParametricTemplateAuthoringService service,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            try
            {
                var detail = await service.GetAsync(id, user, ct);
                return detail is null ? NotFound(id) : Results.Ok(detail);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        })
        .WithName("GetParametricTemplate")
        .Produces<TemplateDetailDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound);

        // POST create
        crud.MapPost("", async (
            TemplateCreateRequestDto body,
            IParametricTemplateAuthoringService service,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (body is null)
                return BadRequest("request body is required");

            try
            {
                var detail = await service.CreateAsync(body, user, ct);
                return Results.Created($"/api/admin/templates/{detail.Id}", detail);
            }
            catch (ArgumentException ex)
            {
                loggerFactory.CreateLogger("Cena.Admin.Api.Templates.Crud")
                    .LogInformation(ex, "[TEMPLATE_CREATE_VALIDATION] id={Tid}", body.Id);
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new CenaError(
                    "template_conflict", ex.Message, ErrorCategory.Conflict, null, null));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        })
        .WithName("CreateParametricTemplate")
        .Produces<TemplateDetailDto>(StatusCodes.Status201Created)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status409Conflict);

        // PUT update
        crud.MapPut("{id}", async (
            string id,
            TemplateUpdateRequestDto body,
            IParametricTemplateAuthoringService service,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (body is null)
                return BadRequest("request body is required");

            try
            {
                var detail = await service.UpdateAsync(id, body, user, ct);
                return detail is null ? NotFound(id) : Results.Ok(detail);
            }
            catch (ArgumentException ex)
            {
                loggerFactory.CreateLogger("Cena.Admin.Api.Templates.Crud")
                    .LogInformation(ex, "[TEMPLATE_UPDATE_VALIDATION] id={Tid}", id);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        })
        .WithName("UpdateParametricTemplate")
        .Produces<TemplateDetailDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound);

        // DELETE soft-delete
        crud.MapDelete("{id}", async (
            string id,
            IParametricTemplateAuthoringService service,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            try
            {
                var reason = http.Request.Query.TryGetValue("reason", out var r) ? r.ToString() : null;
                var deleted = await service.SoftDeleteAsync(id, reason, user, ct);
                return deleted ? Results.NoContent() : NotFound(id);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        })
        .WithName("DeleteParametricTemplate")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound);

        // POST preview — CAS-verified live preview. Fan-outs through the CAS
        // sidecar so it lives in the "ai" rate-limit bucket.
        var preview = app.MapGroup("/api/admin/templates/{id}/preview")
            .WithTags("Parametric Template Authoring")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("ai");

        preview.MapPost("", async (
            string id,
            TemplatePreviewRequestDto? body,
            IParametricTemplateAuthoringService service,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            body ??= new TemplatePreviewRequestDto(BaseSeed: 1, SampleCount: 5);
            try
            {
                var response = await service.PreviewAsync(id, body, user, ct);
                return response is null ? NotFound(id) : Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                loggerFactory.CreateLogger("Cena.Admin.Api.Templates.Crud")
                    .LogInformation(ex, "[TEMPLATE_PREVIEW_VALIDATION] id={Tid}", id);
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        })
        .WithName("PreviewParametricTemplate")
        .Produces<TemplatePreviewResponseDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    // ── Request parsing + error helpers ───────────────────────────────────

    private static TemplateListFilterDto ParseFilter(HttpContext http)
    {
        var q = http.Request.Query;
        int Int(string key, int fallback) =>
            int.TryParse(q[key], out var v) ? v : fallback;
        bool Bool(string key, bool fallback) =>
            bool.TryParse(q[key], out var v) ? v : fallback;
        string? Str(string key) => q.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;

        return new TemplateListFilterDto(
            Subject: Str("subject"),
            Topic: Str("topic"),
            Track: Str("track"),
            Difficulty: Str("difficulty"),
            Methodology: Str("methodology"),
            Status: Str("status"),
            IncludeInactive: Bool("includeInactive", false),
            Page: Int("page", 1),
            PageSize: Int("pageSize", 25));
    }

    private static IResult BadRequest(string message) =>
        Results.BadRequest(new CenaError("invalid_request", message, ErrorCategory.Validation, null, null));

    private static IResult NotFound(string id) =>
        Results.NotFound(new CenaError(
            "template_not_found", $"Template '{id}' not found (or inactive).",
            ErrorCategory.NotFound, null, null));

    private static IResult Unauthorized(string message) =>
        Results.Json(new CenaError("unauthorized", message, ErrorCategory.Authentication, null, null),
                     statusCode: StatusCodes.Status401Unauthorized);
}
