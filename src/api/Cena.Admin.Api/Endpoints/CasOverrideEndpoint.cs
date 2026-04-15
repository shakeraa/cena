// =============================================================================
// Cena Platform — CAS Override Endpoint (RDY-036 §14, ADR-0002)
//
// POST /api/admin/questions/{id}/cas-override
//
// Lets a SUPER_ADMIN manually override a Failed/Unverifiable CAS binding
// when the engine is wrong (rare) or when a question is genuinely
// non-CAS-evaluable but pedagogically valuable.
//
// Heavy audit trail required:
//   - Operator user id (from Firebase auth claim "sub")
//   - Reason ≥ 20 chars
//   - External ticket reference (Jira/Linear/etc.)
//   - QuestionCasBindingOverridden_V1 event appended
//   - Binding doc updated with Status=OverriddenByOperator + Override* fields
//   - cena_cas_override_total counter incremented
//
// Gated by env CENA_CAS_OVERRIDE_ENABLED. When unset/false the endpoint
// returns 403 — overrides are intentionally hard to enable in production.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Endpoints;

/// <summary>RDY-036 §14: Request body for a CAS override.</summary>
public sealed record CasOverrideRequest(string Reason, string Ticket);

/// <summary>RDY-036 §14: Response body for a successful override.</summary>
public sealed record CasOverrideResponse(
    string QuestionId,
    string PreviousStatus,
    string NewStatus,
    string OperatorUserId,
    DateTimeOffset OverriddenAt);

public static class CasOverrideEndpoint
{
    private static readonly Meter Meter = new("Cena.Cas.Gate", "1.0");
    private static readonly Counter<long> OverrideCounter = Meter.CreateCounter<long>(
        "cena_cas_override_total",
        description: "Number of CAS bindings overridden by super-admin operators");

    public const string EnvFlag = "CENA_CAS_OVERRIDE_ENABLED";
    public const int MinReasonLength = 20;

    public static IEndpointRouteBuilder MapCasOverrideEndpoint(this IEndpointRouteBuilder app)
    {
        // Mounted as a top-level route (separate from the questions group) so
        // we can require the SuperAdminOnly policy independently and preserve
        // the principle that operator overrides are a different surface from
        // normal moderator workflows.
        app.MapPost("/api/admin/questions/{id}/cas-override", HandleAsync)
            .WithName("OverrideCasBinding")
            .WithTags("Question Bank", "CAS")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api")
            .Produces<CasOverrideResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        string id,
        CasOverrideRequest request,
        HttpContext ctx,
        IDocumentStore store,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("CasOverride");

        // Hard env gate — overrides are off by default in any environment.
        var enabled = Environment.GetEnvironmentVariable(EnvFlag);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("[CAS_OVERRIDE_DISABLED] {EnvFlag} not set", EnvFlag);
            return Results.Json(
                new CenaError("CAS_OVERRIDE_DISABLED",
                    $"CAS overrides are disabled in this environment. Set {EnvFlag}=true to enable.",
                    ErrorCategory.Authorization, null, null),
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Input validation at boundary.
        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new CenaError(
                "INVALID_QUESTION_ID", "Question id is required.",
                ErrorCategory.Validation, null, null));

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < MinReasonLength)
            return Results.BadRequest(new CenaError(
                "INVALID_OVERRIDE_REASON",
                $"Override reason must be at least {MinReasonLength} characters.",
                ErrorCategory.Validation, null, null));

        if (string.IsNullOrWhiteSpace(request.Ticket))
            return Results.BadRequest(new CenaError(
                "INVALID_OVERRIDE_TICKET",
                "Override ticket reference is required (e.g. JIRA-123).",
                ErrorCategory.Validation, null, null));

        var operatorUserId = ctx.User.FindFirstValue("sub")
                             ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? "unknown";

        await using var session = store.LightweightSession();
        var binding = await session.LoadAsync<QuestionCasBinding>(id);
        if (binding is null)
            return Results.NotFound(new CenaError(
                "CAS_BINDING_NOT_FOUND",
                $"No CAS binding exists for question {id}.",
                ErrorCategory.NotFound, null, null));

        var previous = binding.Status;
        var now = DateTimeOffset.UtcNow;

        binding.Status = CasBindingStatus.OverriddenByOperator;
        binding.OverrideOperator = operatorUserId;
        binding.OverrideReason = request.Reason.Trim();
        binding.OverrideTicket = request.Ticket.Trim();
        binding.VerifiedAt = now;

        session.Store(binding);
        session.Events.Append(id, new QuestionCasBindingOverridden_V1(
            id, operatorUserId, request.Reason.Trim(), request.Ticket.Trim(), previous, now));

        await session.SaveChangesAsync();

        OverrideCounter.Add(1,
            new KeyValuePair<string, object?>("previous_status", previous.ToString()),
            new KeyValuePair<string, object?>("operator", operatorUserId));

        logger.LogWarning(
            "[CAS_OVERRIDE_APPLIED] questionId={Qid} previous={Previous} operator={Operator} ticket={Ticket}",
            id, previous, operatorUserId, request.Ticket);

        return Results.Ok(new CasOverrideResponse(
            id, previous.ToString(), nameof(CasBindingStatus.OverriddenByOperator),
            operatorUserId, now));
    }
}

