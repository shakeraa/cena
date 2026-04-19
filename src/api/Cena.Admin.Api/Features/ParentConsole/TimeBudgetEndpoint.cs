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
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

        if (!CallerCanReadParentalControls(http))
            return Results.Forbid();

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

        var parentAnonId = http.User.FindFirst("parentAnonId")?.Value
            ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(parentAnonId))
            return Results.Unauthorized();
        if (!http.User.IsInRole("PARENT"))
            return Results.Forbid();

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

    private sealed class TimeBudgetEndpointMarker { }
}
