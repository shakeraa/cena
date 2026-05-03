// =============================================================================
// Cena Platform — Parent Console: Time Budget Endpoint (RDY-077 Phase 1B)
//
// Backend for parent-set time controls: weekly minutes budget,
// time-of-day quiet hours, optional topic allow-list. Every change
// emits a ParentalControlsConfiguredV1 event with the parent's
// consent signature.
//
// All caps are SOFT — the endpoint's job is to persist the
// parent's preference; the SESSION pipeline consults the current
// settings + renders a calm banner or gauge when the student
// approaches / exceeds a cap. There is no lockout path on the
// server side either.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.ParentalControls;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Security;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.ParentConsole;

// ---- Wire DTOs --------------------------------------------------------------

public sealed record TimeBudgetDto(
    string StudentAnonId,
    int WeeklyMinutes,
    TimeOfDayRestrictionDto? TimeOfDayRestriction,
    IReadOnlyList<string> TopicAllowList,
    DateTimeOffset ConfiguredAtUtc);

public sealed record TimeOfDayRestrictionDto(
    string NotBefore,   // "HH:mm"
    string NotAfter,    // "HH:mm"
    string Timezone);

public sealed record SetTimeBudgetRequest(
    int WeeklyMinutes,
    TimeOfDayRestrictionDto? TimeOfDayRestriction,
    IReadOnlyList<string>? TopicAllowList,
    string? ConsentSignature);

// ---- Endpoint ---------------------------------------------------------------

public static class TimeBudgetEndpoint
{
    public const string Route = "/api/v1/parent/minors/{studentAnonId}/time-budget";

    public static IEndpointRouteBuilder MapTimeBudgetEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleGetAsync)
            .WithName("GetMinorTimeBudget")
            .WithTags("Parent Console", "Time Budget")
            .RequireAuthorization();

        app.MapPut(Route, HandleSetAsync)
            .WithName("SetMinorTimeBudget")
            .WithTags("Parent Console", "Time Budget")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleGetAsync(
        string studentAnonId,
        HttpContext http,
        IDocumentStore store,
        ILogger<TimeBudgetEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });

        // prr-009 / ADR-0041: PARENT callers MUST go through
        // ParentAuthorizationGuard. ADMIN / SUPER_ADMIN rely on the
        // institute-scope gate below + Marten's TenantScope at query time.
        var authResult = await AuthorizeParentOrAdminAsync(http, studentAnonId, ct).ConfigureAwait(false);
        if (authResult is not null) return authResult;

        using var session = store.QuerySession();
        var latest = await session.Events
            .QueryRawEventDataOnly<ParentalControlsConfiguredV1>()
            .Where(e => e.StudentAnonId == studentAnonId)
            .OrderByDescending(e => e.ConfiguredAtUtc)
            .Take(1)
            .ToListAsync(ct);

        if (latest.Count == 0)
        {
            return Results.Ok(new TimeBudgetDto(
                StudentAnonId: studentAnonId,
                WeeklyMinutes: 0,
                TimeOfDayRestriction: null,
                TopicAllowList: Array.Empty<string>(),
                ConfiguredAtUtc: DateTimeOffset.MinValue));
        }

        var ev = latest[0];
        TimeOfDayRestrictionDto? tod = null;
        if (ev.NotBefore.HasValue && ev.NotAfter.HasValue && !string.IsNullOrEmpty(ev.Timezone))
        {
            tod = new TimeOfDayRestrictionDto(
                NotBefore: ev.NotBefore.Value.ToString("HH:mm"),
                NotAfter: ev.NotAfter.Value.ToString("HH:mm"),
                Timezone: ev.Timezone);
        }

        return Results.Ok(new TimeBudgetDto(
            StudentAnonId: studentAnonId,
            WeeklyMinutes: ev.WeeklyBudgetMinutes ?? 0,
            TimeOfDayRestriction: tod,
            TopicAllowList: ev.TopicAllowList?.ToList() ?? new List<string>(),
            ConfiguredAtUtc: ev.ConfiguredAtUtc));
    }

    private static async Task<IResult> HandleSetAsync(
        string studentAnonId,
        SetTimeBudgetRequest request,
        HttpContext http,
        IDocumentStore store,
        ILogger<TimeBudgetEndpointMarker> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            return Results.BadRequest(new { error = "missing-studentAnonId" });
        if (request is null)
            return Results.BadRequest(new { error = "missing-body" });

        // prr-009 / ADR-0041: writes are PARENT-only. The guard enforces
        // (parentActorId, studentAnonId, instituteId) binding — any
        // mismatch throws ForbiddenException which we render as 403.
        if (!http.User.IsInRole("PARENT"))
            return Results.Forbid();

        ParentChildBindingResolution binding;
        try
        {
            binding = await RequireParentBindingAsync(http, studentAnonId, ct).ConfigureAwait(false);
        }
        catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
        {
            return Results.Forbid();
        }

        var parentAnonId = binding.ParentActorId;

        if (request.WeeklyMinutes < 0)
            return Results.BadRequest(new { error = "negative-weekly-minutes" });

        // Parse time-of-day restriction (HH:mm strings) if provided.
        TimeOnly? notBefore = null;
        TimeOnly? notAfter = null;
        string? timezone = null;
        if (request.TimeOfDayRestriction is not null)
        {
            if (!TimeOnly.TryParse(request.TimeOfDayRestriction.NotBefore, out var nb)
                || !TimeOnly.TryParse(request.TimeOfDayRestriction.NotAfter, out var na))
            {
                return Results.BadRequest(new
                {
                    error = "invalid-time-format",
                    message = "Expected HH:mm for NotBefore and NotAfter.",
                });
            }
            notBefore = nb;
            notAfter = na;
            timezone = request.TimeOfDayRestriction.Timezone;
            if (string.IsNullOrWhiteSpace(timezone))
                return Results.BadRequest(new { error = "missing-timezone" });
        }

        var now = DateTimeOffset.UtcNow;
        var ev = new ParentalControlsConfiguredV1(
            StudentAnonId: studentAnonId,
            WeeklyBudgetMinutes: request.WeeklyMinutes > 0 ? request.WeeklyMinutes : null,
            NotBefore: notBefore,
            NotAfter: notAfter,
            Timezone: timezone,
            TopicAllowList: request.TopicAllowList?.ToList() ?? new List<string>(),
            ParentAnonId: parentAnonId,
            ConsentSignature: request.ConsentSignature ?? "parent-console-ui",
            ConfiguredAtUtc: now);

        await using var session = store.LightweightSession();
        session.Events.Append(studentAnonId, ev);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "[RDY-077] TimeBudget configured for student={StudentAnonId} by "
            + "parent={ParentAnonId} weekly={Weekly} topics={Topics}",
            studentAnonId,
            parentAnonId,
            request.WeeklyMinutes,
            request.TopicAllowList?.Count ?? 0);

        return Results.Ok(new TimeBudgetDto(
            StudentAnonId: studentAnonId,
            WeeklyMinutes: ev.WeeklyBudgetMinutes ?? 0,
            TimeOfDayRestriction: request.TimeOfDayRestriction,
            TopicAllowList: ev.TopicAllowList?.ToList() ?? new List<string>(),
            ConfiguredAtUtc: now));
    }

    private static bool CallerCanReadParentalControls(HttpContext http)
        => http.User.IsInRole("PARENT")
           || http.User.IsInRole("ADMIN")
           || http.User.IsInRole("SUPER_ADMIN");

    /// <summary>
    /// Reads the institute that the guard should check the binding
    /// against. Source: the caller's JWT <c>institute_id</c> claim
    /// (populated at login / session-refresh). A parent with multi-institute
    /// visibility uses separate sessions per institute — switching
    /// institutes for the same session is out of scope for prr-009 and
    /// tracked in ADR-0041 "multi-institute parent visibility".
    /// </summary>
    internal static string ResolveInstituteId(HttpContext http)
    {
        var claims = TenantScope.GetInstituteFilter(http.User, defaultInstituteId: null);
        return claims.Count == 0 ? string.Empty : claims[0];
    }

    /// <summary>
    /// prr-009: call ParentAuthorizationGuard on a PARENT caller; return
    /// null on success; return a 403 IResult on any IDOR-violation throw.
    /// Non-PARENT callers that legitimately read the route (ADMIN /
    /// SUPER_ADMIN) fall through the <c>CallerCanReadParentalControls</c>
    /// role gate. Any other caller gets 403.
    /// </summary>
    internal static async Task<IResult?> AuthorizeParentOrAdminAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        if (http.User.IsInRole("PARENT"))
        {
            try
            {
                await RequireParentBindingAsync(http, studentAnonId, ct).ConfigureAwait(false);
                return null;
            }
            catch (ForbiddenException ex) when (ex.ErrorCode == ErrorCodes.CENA_AUTH_IDOR_VIOLATION)
            {
                return Results.Forbid();
            }
        }
        return CallerCanReadParentalControls(http) ? null : Results.Forbid();
    }

    /// <summary>
    /// Invokes <see cref="ParentAuthorizationGuard.AssertCanAccessAsync"/>
    /// with the per-request DI-resolved service + logger. Callers handle
    /// the thrown <see cref="ForbiddenException"/> themselves. Keeps the
    /// architecture ratchet's regex on exactly one well-known call site.
    /// </summary>
    internal static async Task<ParentChildBindingResolution> RequireParentBindingAsync(
        HttpContext http, string studentAnonId, CancellationToken ct)
    {
        var services = http.RequestServices;
        var bindingService = services.GetRequiredService<IParentChildBindingService>();
        var logger = services.GetRequiredService<ILogger<TimeBudgetEndpointMarker>>();
        var instituteId = ResolveInstituteId(http);
        if (string.IsNullOrWhiteSpace(instituteId))
        {
            throw new ForbiddenException(
                ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                "PARENT caller is missing a required institute_id claim.");
        }
        return await ParentAuthorizationGuard.AssertCanAccessAsync(
            http.User, studentAnonId, instituteId, bindingService, logger, ct)
            .ConfigureAwait(false);
    }

    internal sealed class TimeBudgetEndpointMarker { }
}
